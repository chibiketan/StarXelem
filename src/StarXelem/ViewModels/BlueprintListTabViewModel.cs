using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Écran Blueprints : liste filtrable à gauche, détail du blueprint sélectionné à droite.
/// Le filtrage reprend le fonctionnement des autres écrans : différé, calculé hors du thread d'UI.
/// </summary>
public partial class BlueprintListTabViewModel : PageViewModelBase
{
    // Délai d'inactivité de la saisie avant de filtrer : une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    // Puces : délai réduit, suffisant pour absorber une série de clics rapprochés.
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);

    private const string ObtainedChipKey = "obtained";
    private const string MissionChipKey = "missions";

    private readonly ILogger<BlueprintListTabViewModel> _logger;

    private readonly IGrpcClientService _clientService;
    private readonly ILocalDatabaseService _localDatabaseService;
    private CancellationTokenSource? _filterCts;

    public override string Name => "Blueprints";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Copy);
    [ObservableProperty] public IList<BlueprintViewModel>? _blueprintList;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] public BlueprintViewModel? _selectedBluePrint;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoadItemListCommand)), NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))]
    private bool _isLoading = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BusyMessage))] private string _treatmentStatus = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showOnlyObtained;
    [ObservableProperty] private bool _showOnlyWithMissions;
    [ObservableProperty] private bool _isGrpcConnected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))] private int _filteredCount;

    // Liste complète, remplacée (jamais modifiée) à chaque étape du chargement : le filtrage peut la lire hors du thread d'UI.
    private List<BlueprintViewModel>? _allBlueprints;

    public BlueprintListTabViewModel(
        ILogger<BlueprintListTabViewModel> logger,
        IGrpcClientService clientService,
        ILocalDatabaseService localDatabaseService)
    {
        _logger = logger;
        _clientService = clientService;
        _localDatabaseService = localDatabaseService;

        ObtainedChip = new FilterChipViewModel(ObtainedChipKey, "Obtenus", ToggleChip);
        MissionChip = new FilterChipViewModel(MissionChipKey, "De mission", ToggleChip);

        IsGrpcConnected = _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
        _clientService.OnStatusChanged += (sender, status) => { OnConnectedStatusChanged(status); };
    }

    /// <summary>Puce « Obtenus » : ne garde que les blueprints possédés (nécessite la connexion au jeu).</summary>
    public FilterChipViewModel ObtainedChip { get; }

    /// <summary>Puce « De mission » : ne garde que les blueprints obtenus en récompense de mission.</summary>
    public FilterChipViewModel MissionChip { get; }

    /// <summary>Nombre de blueprints retenus par les filtres sur le total.</summary>
    public string ResultSummary => $"{FilteredCount} sur {_allBlueprints?.Count ?? 0} blueprints";

    /// <summary>Vrai si les filtres ne laissent aucun blueprint.</summary>
    public bool ShowNoMatch => (_allBlueprints?.Count ?? 0) > 0 && FilteredCount == 0 && !IsFiltering;

    /// <summary>Vrai si un blueprint est sélectionné : affiche son détail.</summary>
    public bool HasSelection => SelectedBluePrint != null;

    /// <summary>Vrai pendant un chargement ou un filtrage : affiche l'indicateur superposé à la liste.</summary>
    public bool IsBusy => IsLoading || IsFiltering;

    /// <summary>Texte de l'indicateur superposé à la liste.</summary>
    public string BusyMessage => IsLoading ? TreatmentStatus : "Filtrage en cours…";

    private void OnConnectedStatusChanged(GrpcConnectionStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsGrpcConnected = status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
            if (!IsGrpcConnected)
            {
                ShowOnlyObtained = false;
            }
            LoadItemListCommand.NotifyCanExecuteChanged();
            SendToOrbitalAllianceCommand.NotifyCanExecuteChanged();
        });
    }

    protected override async Task OnFirstShowAsync()
    {
        await LoadItemList().ConfigureAwait(false);
    }

    public bool CanLoadItemList()
    {
        return !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => // Set IsLoading BEFORE any await so the button disables synchronously
            {
                IsLoading = true;
                TreatmentStatus = "Chargement de la base de données…";
                _allBlueprints = new List<BlueprintViewModel>();
                ScheduleFilters(TimeSpan.Zero);
            }).GetTask().ConfigureAwait(false);

            HashSet<string>? obtainedIds = null;
            if (IsGrpcConnected)
            {
                await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement des BP obtenus…");
                var grpcBps = await _clientService.GetBlueprintList().ConfigureAwait(false);
                obtainedIds = grpcBps.Select(e => e.BlueprintId).ToHashSet();
            }

            const int BatchSize = 200;
            var loaded = new List<BlueprintViewModel>();

            await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement des blueprints…").GetTask().ConfigureAwait(false);

            await foreach (var row in _localDatabaseService.GetBlueprintsBatchedAsync(BatchSize).ConfigureAwait(false))
            {
                loaded.Add(await MapRowToViewModelAsync(row, obtainedIds).ConfigureAwait(false));

                if (loaded.Count % BatchSize == 0)
                {
                    // La copie est faite pendant que le chargement attend : la liste publiée n'est plus jamais modifiée.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TreatmentStatus = $"Chargement des blueprints… {loaded.Count} traités";
                        _allBlueprints = loaded.ToList();
                        ScheduleFilters(TimeSpan.Zero);
                    });
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TreatmentStatus = $"Terminé — {loaded.Count} blueprints chargés";
                _allBlueprints = loaded;
                IsLoading = false;
                ScheduleFilters(TimeSpan.Zero);
                SendToOrbitalAllianceCommand.NotifyCanExecuteChanged();
            }).GetTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des blueprints");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TreatmentStatus = $"Erreur : {ex.Message}";
                IsLoading = false;
            }).GetTask().ConfigureAwait(false);
        }
    }

    private async Task<BlueprintViewModel> MapRowToViewModelAsync(
        DbBlueprintRow row,
        HashSet<string>? obtainedIds)
    {
        var categories = await Task.WhenAll(row.Costs.Select(async cost =>
        {
            var materials = new List<BlueprintMaterialModel>();
            var modifiers = new List<BlueprintStatModelBase>();

            if (cost.CostType == "Resource" && !string.IsNullOrEmpty(cost.ResourceRef))
            {
                materials.Add(new BlueprintResourceModel
                {
                    Name = cost.ResourceName ?? cost.ResourceRef,
                    QuantityInScu = (float)(cost.ResourceAmount ?? 0m)
                });
            }
            else if (cost.CostType == "Item" && !string.IsNullOrEmpty(cost.ItemEntityClassRef))
            {
                materials.Add(new BlueprintItemModel
                {
                    Name = cost.ItemName ?? cost.ItemEntityClassRef,
                    QuantityCount = cost.ItemCount ?? 1
                });
            }

            foreach (var mod in cost.Modifiers)
            {
                if (mod.RangeType == "Linear")
                {
                    modifiers.Add(new BlueprintStatLinearModel
                    {
                        Name = mod.PropertyName,
                        Min = (float)mod.ModifierStart,
                        Max = (float)mod.ModifierEnd
                    });
                }
                else if (mod.RangeType == "Additive")
                {
                    modifiers.Add(new BlueprintStatAdditiveModel
                    {
                        Name = mod.PropertyName,
                        Bands = new List<BlueprintStatBandModel>
                        {
                            new BlueprintStatBandModel
                            {
                                StartQuality = mod.StartQuality,
                                EndQuality = mod.EndQuality,
                                Value = (int)mod.ModifierStart
                            }
                        }
                    });
                }
            }

            return new BlueprintCategoryModel
            {
                Name = cost.CostName,
                MaterialList = materials,
                StatModifierList = modifiers
            };
        }));

        var missionPools = row.MissionPools
            .GroupBy(mp => mp.PoolName)
            .Select(g => new MissionPoolGroup(g.Key,
                g.Select(mp => new MissionInfo(mp.MissionTitle, mp.MissionDebugName)).ToList()))
            .ToList();

        return new BlueprintViewModel
        {
            BlueprintId = row.SelfId,
            Name = row.BlueprintName,
            TierLevel = null,
            RemainingUse = null,
            CraftDuration = row.CraftDuration,
            CategoryList = categories.ToList(),
            IsObtained = obtainedIds != null && obtainedIds.Contains(row.SelfId),
            MissionPools = missionPools
        };
    }

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    public void ClearSearch()
    {
        Search = "";
    }

    public bool CanClearSearch()
    {
        return !string.IsNullOrEmpty(Search);
    }

    [RelayCommand(CanExecute = nameof(CanSendToOrbitalAlliance))]
    public void SendToOrbitalAlliance()
    {
        OpenSendPopup();
    }

    private bool CanSendToOrbitalAlliance()
    {
        return IsGrpcConnected && _allBlueprints is { Count: > 0 };
    }

    partial void OnSearchChanged(string value)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
        ScheduleFilters(SearchFilterDelay);
    }

    partial void OnShowOnlyObtainedChanged(bool value) => ScheduleFilters(SelectionFilterDelay);

    partial void OnShowOnlyWithMissionsChanged(bool value) => ScheduleFilters(SelectionFilterDelay);

    private void ToggleChip(string? key)
    {
        switch (key)
        {
            case ObtainedChipKey when IsGrpcConnected || ShowOnlyObtained:
                ShowOnlyObtained = !ShowOnlyObtained;
                break;
            case MissionChipKey:
                ShowOnlyWithMissions = !ShowOnlyWithMissions;
                break;
        }
    }

    /// <summary>
    /// Planifie un filtrage différé : toute demande encore en attente ou en cours est annulée, de sorte qu'une rafale de
    /// modifications ne produit qu'un seul recalcul, après la dernière. À appeler depuis le thread d'UI.
    /// </summary>
    private void ScheduleFilters(TimeSpan delay)
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        // Copie de l'état des filtres : le calcul qui suit ne doit lire aucune propriété du ViewModel.
        var criteria = new BlueprintFilterCriteria(
            _allBlueprints ?? new List<BlueprintViewModel>(),
            string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            ShowOnlyObtained,
            ShowOnlyWithMissions);

        IsFiltering = true;
        _ = RunFilterAsync(criteria, delay, cts.Token);
    }

    private async Task RunFilterAsync(BlueprintFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => ComputeFilterOutcome(criteria), token);
            await Dispatcher.UIThread.InvokeAsync(() => DisplayResult(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des blueprints");
        }
        finally
        {
            // Une demande annulée a forcément une remplaçante, qui reste propriétaire de l'indicateur.
            if (!token.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsFiltering = false, DispatcherPriority.MaxValue);
            }
        }
    }

    private static BlueprintFilterOutcome ComputeFilterOutcome(BlueprintFilterCriteria criteria)
    {
        var searched = criteria.Blueprints
            .Where(blueprint => criteria.Needle == null || blueprint.Name.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        // Chaque puce compte selon l'autre filtre, pas le sien.
        var obtainedCount = searched.Count(blueprint => blueprint.IsObtained && (!criteria.OnlyWithMissions || blueprint.HasMissions));
        var missionCount = searched.Count(blueprint => blueprint.HasMissions && (!criteria.OnlyObtained || blueprint.IsObtained));

        var rows = searched
            .Where(blueprint => (!criteria.OnlyObtained || blueprint.IsObtained) && (!criteria.OnlyWithMissions || blueprint.HasMissions))
            .ToList();

        return new BlueprintFilterOutcome(rows, obtainedCount, missionCount);
    }

    private void DisplayResult(BlueprintFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        ObtainedChip.Count = outcome.ObtainedCount;
        ObtainedChip.IsSelected = ShowOnlyObtained;
        MissionChip.Count = outcome.MissionCount;
        MissionChip.IsSelected = ShowOnlyWithMissions;

        FilteredCount = outcome.Blueprints.Count;

        // La liste est virtualisée : la remplacer d'un bloc reste rapide. On l'évite quand le résultat ne change pas.
        if (BlueprintList == null || !BlueprintList.SequenceEqual(outcome.Blueprints))
        {
            var selected = SelectedBluePrint;
            BlueprintList = outcome.Blueprints;
            SelectedBluePrint = selected != null && outcome.Blueprints.Contains(selected) ? selected : null;
        }
    }

    private void OpenSendPopup()
    {
        var vm = App.Current.Services.GetRequiredService<Popup.SendToOrbitalAlliancePopupContentViewModel>();

        vm.BlueprintsToSend = _allBlueprints?.Where(b => b.IsObtained).ToList();
        WeakReferenceMessenger.Default.Send(new Popup.ShowPopupMessage(
            showCloseButton: true,
            onClose: null,
            viewModel: vm
        ));
    }

    /// <summary>État des filtres au moment de la demande, lu par le calcul hors du thread d'UI.</summary>
    private sealed record BlueprintFilterCriteria(IReadOnlyList<BlueprintViewModel> Blueprints, string? Needle, bool OnlyObtained, bool OnlyWithMissions);

    /// <summary>Résultat d'un filtrage : lignes à afficher et compteurs des puces.</summary>
    private sealed record BlueprintFilterOutcome(List<BlueprintViewModel> Blueprints, int ObtainedCount, int MissionCount);
}

public class MissionPoolGroup
{
    public string PoolName { get; }
    public List<MissionInfo> Missions { get; }

    public MissionPoolGroup(string poolName, List<MissionInfo> missions)
    {
        PoolName = poolName;
        Missions = missions;
    }
}

public class MissionInfo
{
    public string MissionTitle { get; }
    public string MissionDebugName { get; }

    public MissionInfo(string missionTitle, string missionDebugName)
    {
        MissionTitle = missionTitle;
        MissionDebugName = missionDebugName;
    }
}

public class BlueprintCategoryModel
{
    public required string Name { get; set; }
    public required List<BlueprintMaterialModel> MaterialList { get; set; }
    public required List<BlueprintStatModelBase> StatModifierList { get; set; }
}

/// <summary>Classe de base abstraite pour un matériau de blueprint (ressource ou objet).</summary>
public abstract class BlueprintMaterialModel
{
    public required string Name { get; set; }
}

/// <summary>Ressource brute mesurée en SCU (ex : Fer, Cuivre).</summary>
public class BlueprintResourceModel : BlueprintMaterialModel
{
    public required float QuantityInScu { get; set; }
}

/// <summary>Objet spécifique mesuré en quantité physique (ex : minerai Sadaryx x4).</summary>
public class BlueprintItemModel : BlueprintMaterialModel
{
    /// <summary>Nombre d'objets physiques requis (pas de SCU).</summary>
    public required int QuantityCount { get; set; }

    /// <summary>Quantité affichée, accordée au nombre (ex : « 1 unité », « 4 unités »).</summary>
    public string QuantityLabel => QuantityCount > 1 ? $"{QuantityCount} unités" : $"{QuantityCount} unité";
}

public abstract class BlueprintStatModelBase
{
    public required string Name { get; set; }
}

public class BlueprintStatLinearModel : BlueprintStatModelBase
{
    public required float Min { get; set; }
    public required float Max { get; set; }

    /// <summary>Plage du modificateur, de la qualité minimale à la maximale (ex : « 120% – 80% »).</summary>
    public string RangeDisplay => $"{FormatPercent(Min)} – {FormatPercent(Max)}";

    private static string FormatPercent(float ratio) => (ratio * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%";
}

public class BlueprintStatBandModel
{
    public required int StartQuality { get; set; }
    public required int EndQuality { get; set; }
    public required int Value { get; set; }
    public string QualityLabel => $"{StartQuality}-{EndQuality}";
    public string FormattedValue => Value > 0 ? $"+{Value}" : Value.ToString();
}

public class BlueprintStatAdditiveModel : BlueprintStatModelBase
{
    public required List<BlueprintStatBandModel> Bands { get; set; }

    /// <summary>Valeur par plage de qualité (ex : « 0-500 : -1 · 500-1000 : +1 »).</summary>
    public string RangeDisplay => string.Join(" · ", Bands.Select(band => $"{band.QualityLabel} : {band.FormattedValue}"));
}

public partial class BlueprintViewModel : ViewModelBase
{
    /// <summary>Identifiant unique du blueprint (CUID RSI / P4K SelfId). Utilisé pour la synchronisation API.</summary>
    public required string BlueprintId { get; set; } = "";
    public required string Name { get; set; }
    public uint? TierLevel { get; set; }
    public int? RemainingUse { get; set; }
    public TimeSpan? CraftDuration { get; set; }
    public required List<BlueprintCategoryModel> CategoryList { get; set; }
    public EItemType Type { get; set; }
    public EItemSubType Subtype { get; set; }

    /// <summary>True si le joueur possède déjà ce Blueprint (via gRPC).</summary>
    public bool IsObtained { get; set; }

    /// <summary>Liste des pools de mission qui récompensent ce Blueprint.</summary>
    public List<MissionPoolGroup> MissionPools { get; set; } = new();

    /// <summary>Vrai si au moins une mission récompense ce blueprint.</summary>
    public bool HasMissions => MissionPools.Count > 0;

    public bool HasTier => TierLevel.HasValue;
    public string TierLabel => $"Tier {TierLevel}";

    public bool HasRemainingUse => RemainingUse.HasValue;
    public string RemainingUseLabel => RemainingUse == -1 ? "Utilisations illimitées" : $"{RemainingUse} utilisations";

    public bool HasCraftDuration => CraftDuration.HasValue;
    public string CraftDurationLabel => CraftDuration?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    public string ItemIconKey => (Type, Subtype) switch
    {
        (EItemType.WeaponPersonal, EItemSubType.Small) => "Icon.Pistol",
        (EItemType.WeaponPersonal, EItemSubType.Medium) => "Icon.LightWeapon",
        (EItemType.WeaponAttachment, EItemSubType.Magazine) => "Icon.Ammunition",
        (EItemType.Char_Armor_Arms, _) => "Icon.Arms",
        (EItemType.Char_Armor_Legs, _) => "Icon.Legs",
        (EItemType.Char_Armor_Torso, _) => "Icon.Body",
        (EItemType.Char_Armor_Helmet, _) => "Icon.Helmet",
        _ => "Icon.Ammunition"
    };

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(BlueprintId);
    }
}
