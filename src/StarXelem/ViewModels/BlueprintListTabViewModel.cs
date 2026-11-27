using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Extensions;
using StarXelem.Models;
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
    private const string CraftableChipKey = "craftable";

    private readonly ILogger<BlueprintListTabViewModel> _logger;

    private readonly IGrpcClientService _clientService;
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly IOwnedMaterialService _ownedMaterialService;
    private readonly IObtainedBlueprintService _obtainedBlueprintService;
    private CancellationTokenSource? _craftStatusCts;
    private CancellationTokenSource? _filterCts;
    private readonly HashSet<BlueprintFamily> _selectedFamilies = new();

    public override string Name => "Blueprints";
    protected override string NavIconData => "M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h7";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Copy);
    [ObservableProperty] public IList<BlueprintViewModel>? _blueprintList;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] public BlueprintViewModel? _selectedBluePrint;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCraft))] private BlueprintCraftViewModel? _selectedCraft;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowLoadItemsBanner))] private bool _hasOwnedInventory;
    [ObservableProperty] private bool _showOnlyCraftable;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoadItemListCommand)), NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))]
    private bool _isLoading = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BusyMessage))] private string _treatmentStatus = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showOnlyObtained;
    [ObservableProperty] private bool _showOnlyWithMissions;
    [ObservableProperty] private bool _isGrpcConnected;

    /// <summary>Vrai si les blueprints obtenus sont connus, lus auprès du jeu ou relus du cache : la puce « Obtenus » a un sens.</summary>
    [ObservableProperty] private bool _hasObtainedData;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))] private int _filteredCount;

    // Liste complète, remplacée (jamais modifiée) à chaque étape du chargement : le filtrage peut la lire hors du thread d'UI.
    private List<BlueprintViewModel>? _allBlueprints;

    // Blueprint demandé par un autre écran avant la fin du chargement.
    private string? _pendingBlueprintId;

    // Blueprint à sélectionner dès que la liste filtrée qui le contient est affichée.
    private string? _selectAfterFilterId;

    public BlueprintListTabViewModel(
        ILogger<BlueprintListTabViewModel> logger,
        IGrpcClientService clientService,
        ILocalDatabaseService localDatabaseService,
        IOwnedMaterialService ownedMaterialService,
        IObtainedBlueprintService obtainedBlueprintService)
    {
        _logger = logger;
        _clientService = clientService;
        _localDatabaseService = localDatabaseService;
        _ownedMaterialService = ownedMaterialService;
        _obtainedBlueprintService = obtainedBlueprintService;

        ObtainedChip = new FilterChipViewModel(ObtainedChipKey, "Obtenus", ToggleChip);
        MissionChip = new FilterChipViewModel(MissionChipKey, "De mission", ToggleChip);
        CraftableChip = new FilterChipViewModel(CraftableChipKey, "Fabricables", ToggleChip);

        IsGrpcConnected = _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
        _clientService.OnStatusChanged += (sender, status) => { OnConnectedStatusChanged(status); };
        HasOwnedInventory = _ownedMaterialService.Current != null;
        _ownedMaterialService.InventoryChanged += (_, _) => Dispatcher.UIThread.Post(OnInventoryChanged);
    }

    /// <summary>Date des blueprints obtenus et provenance (jeu ou cache).</summary>
    public DataFreshnessViewModel Freshness { get; } = new();

    /// <summary>Puce « Fabricables » : ne garde que les blueprints dont tous les matériaux sont possédés.</summary>
    public FilterChipViewModel CraftableChip { get; }

    /// <summary>Vrai si les matériaux possédés sont confrontés au blueprint sélectionné.</summary>
    public bool HasCraft => SelectedCraft != null;

    /// <summary>Vrai tant que les objets ne sont pas chargés : invite à les charger depuis l'écran Objets.</summary>
    public bool ShowLoadItemsBanner => !HasOwnedInventory;

    /// <summary>Ouvre l'écran Objets et lance le chargement de l'inventaire.</summary>
    [RelayCommand]
    private void OpenItems() => WeakReferenceMessenger.Default.Send(new OpenItemsMessage(true));

    partial void OnSelectedBluePrintChanged(BlueprintViewModel? value) => RebuildSelectedCraft();

    partial void OnShowOnlyCraftableChanged(bool value) => ScheduleFilters(SelectionFilterDelay);

    private void RebuildSelectedCraft()
    {
        var inventory = _ownedMaterialService.Current;
        SelectedCraft = SelectedBluePrint != null && inventory != null && SelectedBluePrint.CategoryList.Any(category => category.MaterialList.Count > 0)
            ? new BlueprintCraftViewModel(SelectedBluePrint, inventory)
            : null;
    }

    private void OnInventoryChanged()
    {
        HasOwnedInventory = _ownedMaterialService.Current != null;
        if (!HasOwnedInventory)
        {
            ShowOnlyCraftable = false;
        }

        RebuildSelectedCraft();
        RefreshCraftStatuses();
    }

    /// <summary>
    /// Recalcule hors du thread d'UI le statut de fabrication de tous les blueprints, puis l'applique et relance le filtrage
    /// pour mettre à jour la puce « Fabricables ». À appeler depuis le thread d'UI.
    /// </summary>
    private void RefreshCraftStatuses()
    {
        _craftStatusCts?.Cancel();
        var cts = new CancellationTokenSource();
        _craftStatusCts = cts;
        var blueprints = _allBlueprints ?? new List<BlueprintViewModel>();
        var inventory = _ownedMaterialService.Current;
        _ = RunCraftStatusesAsync(blueprints, inventory, cts.Token);
    }

    private async Task RunCraftStatusesAsync(List<BlueprintViewModel> blueprints, OwnedMaterialInventory? inventory, CancellationToken token)
    {
        try
        {
            var statuses = await Task.Run(() => blueprints.Select(blueprint => BlueprintStockCalculator.ComputeCraftStatus(inventory, blueprint)).ToArray(), token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                for (var i = 0; i < blueprints.Count; i++)
                {
                    blueprints[i].CraftStatus = statuses[i];
                }

                ScheduleFilters(TimeSpan.Zero);
            });
        }
        catch (OperationCanceledException)
        {
            // Remplacé par un calcul plus récent.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du calcul des blueprints fabricables");
        }
    }

    /// <summary>Puce « Obtenus » : ne garde que les blueprints possédés (nécessite la connexion au jeu).</summary>
    public FilterChipViewModel ObtainedChip { get; }

    /// <summary>Puce « De mission » : ne garde que les blueprints obtenus en récompense de mission.</summary>
    public FilterChipViewModel MissionChip { get; }

    /// <summary>Puces de famille d'objet, cumulables ; rien de coché signifie tout afficher.</summary>
    public ObservableCollection<FilterChipViewModel> FamilyChips { get; } = new();

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

    /// <summary>
    /// Sélectionne un blueprint demandé depuis un autre écran : les filtres sont levés et la recherche porte sur son nom
    /// pour qu'il soit visible dans la liste. Si les blueprints ne sont pas encore chargés, la sélection attend la fin du chargement.
    /// </summary>
    public void ShowBlueprint(string blueprintId)
    {
        _pendingBlueprintId = blueprintId;
        TrySelectPendingBlueprint();
    }

    private void TrySelectPendingBlueprint()
    {
        var blueprint = _pendingBlueprintId == null ? null : _allBlueprints?.FirstOrDefault(b => b.BlueprintId == _pendingBlueprintId);
        if (blueprint == null)
        {
            return;
        }

        // La sélection attend l'affichage de la liste filtrée : la ListBox refuse un élément absent de ses lignes
        // et remettrait la sélection à vide.
        _pendingBlueprintId = null;
        _selectAfterFilterId = blueprint.BlueprintId;
        ShowOnlyObtained = false;
        ShowOnlyWithMissions = false;
        ShowOnlyCraftable = false;
        _selectedFamilies.Clear();
        Search = blueprint.Name;
        ScheduleFilters(TimeSpan.Zero);
    }

    private void OnConnectedStatusChanged(GrpcConnectionStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsGrpcConnected = status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
            if (!IsGrpcConnected && !HasObtainedData)
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

            await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement des BP obtenus…");
            var obtained = await _obtainedBlueprintService.GetObtainedAsync().ConfigureAwait(false);
            var obtainedIds = obtained?.Ids;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasObtainedData = obtained != null;
                if (obtained == null)
                {
                    ShowOnlyObtained = false;
                    Freshness.Clear();
                }
                else
                {
                    Freshness.Update(obtained.FetchedAt, obtained.IsFromCache);
                }
            });

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
                BuildFamilyChips();
                IsLoading = false;
                ScheduleFilters(TimeSpan.Zero);
                RefreshCraftStatuses();
                TrySelectPendingBlueprint();
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
        IReadOnlySet<string>? obtainedIds)
    {
        var categories = await Task.WhenAll(row.Costs.Select(cost => Task.FromResult(new BlueprintCategoryModel
        {
            Name = cost.CostName,
            MaterialList = MapMaterials(cost),
            StatModifierList = MapModifiers(cost.Modifiers)
        })));

        var missionPools = row.MissionPools
            .GroupBy(mp => mp.PoolName)
            .Select(g => new MissionPoolGroup(g.Key,
                // Plusieurs variantes d'une mission portent le même titre : une seule ligne par titre, avec leur nombre.
                g.GroupBy(mp => mp.MissionTitle)
                    .Select(titleGroup => new MissionInfo(titleGroup.Key, titleGroup.First().MissionDebugName, titleGroup.Count()))
                    .OrderBy(mission => mission.MissionTitle, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()))
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
            MissionPools = missionPools,
            Family = BlueprintFamilyInfo.GetFamily(row.Product?.TypeName),
            TypeLabel = BlueprintFamilyInfo.GetTypeLabel(row.Product)
        };
    }

    private static List<BlueprintMaterialModel> MapMaterials(DbBlueprintCostRow cost)
    {
        if (cost.CostType == "Resource" && !string.IsNullOrEmpty(cost.ResourceRef))
        {
            return
            [
                new BlueprintResourceModel
                {
                    Name = cost.ResourceName ?? cost.ResourceRef,
                    QuantityInScu = (float)(cost.ResourceAmount ?? 0m),
                    ResourceRef = cost.ResourceRef,
                    MinQuality = cost.MinQuality
                }
            ];
        }

        if (cost.CostType == "Item" && !string.IsNullOrEmpty(cost.ItemEntityClassRef))
        {
            return
            [
                new BlueprintItemModel
                {
                    Name = cost.ItemName ?? cost.ItemEntityClassRef,
                    QuantityCount = cost.ItemCount ?? 1,
                    ItemClassRef = cost.ItemEntityClassRef,
                    MinQuality = cost.MinQuality
                }
            ];
        }

        return [];
    }

    // Une propriété peut avoir plusieurs plages de qualité : elles forment un seul modificateur.
    private static List<BlueprintStatModelBase> MapModifiers(IEnumerable<DbBlueprintModifierRow> modifiers)
    {
        var result = new List<BlueprintStatModelBase>();
        foreach (var group in modifiers.GroupBy(mod => (mod.PropertyName, mod.RangeType)))
        {
            var ranges = group.OrderBy(mod => mod.StartQuality).ToList();
            if (group.Key.RangeType == "Linear")
            {
                result.Add(new BlueprintStatLinearModel
                {
                    Name = group.Key.PropertyName,
                    Min = (float)ranges[0].ModifierStart,
                    Max = (float)ranges[^1].ModifierEnd,
                    Ranges = ranges
                        .Select(mod => new BlueprintLinearRange(mod.StartQuality, mod.EndQuality, (float)mod.ModifierStart, (float)mod.ModifierEnd))
                        .ToList()
                });
            }
            else if (group.Key.RangeType == "Additive")
            {
                result.Add(new BlueprintStatAdditiveModel
                {
                    Name = group.Key.PropertyName,
                    Bands = ranges
                        .Select(mod => new BlueprintStatBandModel { StartQuality = mod.StartQuality, EndQuality = mod.EndQuality, Value = (int)mod.ModifierStart })
                        .ToList()
                });
            }
        }

        return result;
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
            case ObtainedChipKey when HasObtainedData || ShowOnlyObtained:
                ShowOnlyObtained = !ShowOnlyObtained;
                break;
            case MissionChipKey:
                ShowOnlyWithMissions = !ShowOnlyWithMissions;
                break;
            case CraftableChipKey when HasOwnedInventory || ShowOnlyCraftable:
                ShowOnlyCraftable = !ShowOnlyCraftable;
                break;
        }
    }

    private void BuildFamilyChips()
    {
        FamilyChips.Clear();
        foreach (var family in Enum.GetValues<BlueprintFamily>().Where(family => _allBlueprints!.Any(blueprint => blueprint.Family == family)))
        {
            FamilyChips.Add(new FilterChipViewModel(family.ToString(), family.GetDisplayName() ?? family.ToString(), ToggleFamily));
        }

        _selectedFamilies.RemoveWhere(family => FamilyChips.All(chip => chip.Key != family.ToString()));
    }

    private void ToggleFamily(string? key)
    {
        if (Enum.TryParse<BlueprintFamily>(key, out var family) && !_selectedFamilies.Remove(family))
        {
            _selectedFamilies.Add(family);
        }

        ScheduleFilters(SelectionFilterDelay);
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
            ShowOnlyWithMissions,
            ShowOnlyCraftable,
            _selectedFamilies.Count == 0 ? null : _selectedFamilies.ToHashSet());

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

        bool MatchesObtained(BlueprintViewModel blueprint) => !criteria.OnlyObtained || blueprint.IsObtained;
        bool MatchesMissions(BlueprintViewModel blueprint) => !criteria.OnlyWithMissions || blueprint.HasMissions;
        bool MatchesFamily(BlueprintViewModel blueprint) => criteria.Families == null || criteria.Families.Contains(blueprint.Family);
        bool MatchesCraftable(BlueprintViewModel blueprint) => !criteria.OnlyCraftable || blueprint.CraftStatus == BlueprintCraftStatus.Ready;

        // Chaque puce compte selon les autres filtres, pas le sien.
        var obtainedCount = searched.Count(blueprint => blueprint.IsObtained && MatchesMissions(blueprint) && MatchesFamily(blueprint) && MatchesCraftable(blueprint));
        var missionCount = searched.Count(blueprint => blueprint.HasMissions && MatchesObtained(blueprint) && MatchesFamily(blueprint) && MatchesCraftable(blueprint));
        var craftableCount = searched.Count(blueprint =>
            blueprint.CraftStatus == BlueprintCraftStatus.Ready && MatchesObtained(blueprint) && MatchesMissions(blueprint) && MatchesFamily(blueprint));
        var familyCounts = searched
            .Where(blueprint => MatchesObtained(blueprint) && MatchesMissions(blueprint) && MatchesCraftable(blueprint))
            .GroupBy(blueprint => blueprint.Family)
            .ToDictionary(group => group.Key, group => group.Count());

        var rows = searched
            .Where(blueprint => MatchesObtained(blueprint) && MatchesMissions(blueprint) && MatchesFamily(blueprint) && MatchesCraftable(blueprint))
            .ToList();

        return new BlueprintFilterOutcome(rows, obtainedCount, missionCount, craftableCount, familyCounts);
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
        CraftableChip.Count = outcome.CraftableCount;
        CraftableChip.IsSelected = ShowOnlyCraftable;
        foreach (var chip in FamilyChips)
        {
            var family = Enum.Parse<BlueprintFamily>(chip.Key!);
            chip.Count = outcome.FamilyCounts.GetValueOrDefault(family);
            chip.IsSelected = _selectedFamilies.Contains(family);
        }

        FilteredCount = outcome.Blueprints.Count;

        // La liste est virtualisée : la remplacer d'un bloc reste rapide. On l'évite quand le résultat ne change pas.
        if (BlueprintList == null || !BlueprintList.SequenceEqual(outcome.Blueprints))
        {
            var selected = SelectedBluePrint;
            BlueprintList = outcome.Blueprints;
            SelectedBluePrint = selected != null && outcome.Blueprints.Contains(selected) ? selected : null;
        }

        var requested = _selectAfterFilterId == null ? null : outcome.Blueprints.FirstOrDefault(b => b.BlueprintId == _selectAfterFilterId);
        if (requested != null)
        {
            _selectAfterFilterId = null;
            SelectedBluePrint = requested;
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
    private sealed record BlueprintFilterCriteria(IReadOnlyList<BlueprintViewModel> Blueprints, string? Needle, bool OnlyObtained, bool OnlyWithMissions, bool OnlyCraftable, HashSet<BlueprintFamily>? Families);

    /// <summary>Résultat d'un filtrage : lignes à afficher et compteurs des puces.</summary>
    private sealed record BlueprintFilterOutcome(
        List<BlueprintViewModel> Blueprints,
        int ObtainedCount,
        int MissionCount,
        int CraftableCount,
        IReadOnlyDictionary<BlueprintFamily, int> FamilyCounts);
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

    /// <summary>Nombre de variantes de la mission portant ce titre.</summary>
    public int VariantCount { get; }

    /// <summary>Vrai si plusieurs variantes portent ce titre : affiche le compteur.</summary>
    public bool HasVariants => VariantCount > 1;

    /// <summary>Compteur affiché à côté du titre (ex : « ×14 »).</summary>
    public string VariantCountLabel => $"×{VariantCount}";

    public MissionInfo(string missionTitle, string missionDebugName, int variantCount = 1)
    {
        MissionTitle = missionTitle;
        MissionDebugName = missionDebugName;
        VariantCount = variantCount;
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

    /// <summary>Qualité minimale exigée (0 à 1000) : les lots en dessous ne comptent pas.</summary>
    public int? MinQuality { get; set; }

    /// <summary>Quantité requise, en SCU pour une ressource ou en nombre d'éléments pour un objet.</summary>
    public abstract double RequiredQuantity { get; }

    /// <summary>Vrai pour une ressource mesurée en SCU, faux pour un objet compté à l'unité.</summary>
    public abstract bool IsMeasuredInScu { get; }
}

/// <summary>Ressource brute mesurée en SCU (ex : Fer, Cuivre).</summary>
public class BlueprintResourceModel : BlueprintMaterialModel
{
    public required float QuantityInScu { get; set; }

    /// <summary>selfId du <c>ResourceType</c> demandé.</summary>
    public string? ResourceRef { get; set; }

    public override double RequiredQuantity => QuantityInScu;

    public override bool IsMeasuredInScu => true;
}

/// <summary>Objet spécifique mesuré en quantité physique (ex : minerai Sadaryx x4).</summary>
public class BlueprintItemModel : BlueprintMaterialModel
{
    /// <summary>Nombre d'objets physiques requis (pas de SCU).</summary>
    public required int QuantityCount { get; set; }

    /// <summary>Classe d'objet demandée (RecordId de ScItems).</summary>
    public string? ItemClassRef { get; set; }

    /// <summary>Quantité affichée, accordée au nombre (ex : « 1 unité », « 4 unités »).</summary>
    public string QuantityLabel => QuantityCount > 1 ? $"{QuantityCount} unités" : $"{QuantityCount} unité";

    public override double RequiredQuantity => QuantityCount;

    public override bool IsMeasuredInScu => false;
}

public abstract class BlueprintStatModelBase
{
    public required string Name { get; set; }

    /// <summary>Vrai pour un modificateur additif entier (valeur par plage), faux pour un multiplicateur.</summary>
    public abstract bool IsAdditive { get; }

    /// <summary>Valeur du modificateur à une qualité (0 à 1000).</summary>
    public abstract double ValueAt(int quality);

    /// <summary>Valeur formatée : pourcentage pour un multiplicateur, entier signé pour un additif.</summary>
    public string FormatValue(double value) => IsAdditive
        ? FormatSigned((int)Math.Round(value))
        : (value * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%";

    /// <summary>Écart à la valeur neutre (100 % ou 0), signé : « −9,8 % », « +1 ».</summary>
    public string FormatDelta(double value) => IsAdditive
        ? FormatSigned((int)Math.Round(value))
        : (value >= 1 ? "+" : "−") + Math.Abs((value - 1) * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %";

    protected static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.CurrentCulture);
}

/// <summary>Plage de qualité d'un multiplicateur linéaire : la valeur varie de <see cref="Start"/> à <see cref="End"/>.</summary>
public sealed record BlueprintLinearRange(int StartQuality, int EndQuality, float Start, float End);

public class BlueprintStatLinearModel : BlueprintStatModelBase
{
    public required float Min { get; set; }
    public required float Max { get; set; }

    /// <summary>Plages du multiplicateur ; vide pour un modèle construit à la main (Min à Q0, Max à Q1000).</summary>
    public List<BlueprintLinearRange> Ranges { get; set; } = new();

    /// <summary>Plage du modificateur, de la qualité minimale à la maximale (ex : « 120% – 80% »).</summary>
    public string RangeDisplay => $"{FormatPercent(Min)} – {FormatPercent(Max)}";

    public override bool IsAdditive => false;

    public override double ValueAt(int quality)
    {
        if (Ranges.Count == 0)
        {
            return Min + (Max - Min) * Math.Clamp(quality, 0, 1000) / 1000d;
        }

        // Hors de toute plage, la valeur de la plage la plus proche s'applique.
        var range = Ranges.FirstOrDefault(r => quality >= r.StartQuality && quality <= r.EndQuality)
                    ?? (quality < Ranges.Min(r => r.StartQuality) ? Ranges.MinBy(r => r.StartQuality)! : Ranges.MaxBy(r => r.EndQuality)!);
        var span = range.EndQuality - range.StartQuality;
        var ratio = span <= 0 ? 0 : Math.Clamp((quality - range.StartQuality) / (double)span, 0, 1);
        return range.Start + (range.End - range.Start) * ratio;
    }

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

    public override bool IsAdditive => true;

    public override double ValueAt(int quality)
    {
        var band = Bands.FirstOrDefault(b => quality >= b.StartQuality && quality <= b.EndQuality)
                   ?? (Bands.Count == 0 ? null : quality < Bands.Min(b => b.StartQuality) ? Bands.MinBy(b => b.StartQuality) : Bands.MaxBy(b => b.EndQuality));
        return band?.Value ?? 0;
    }
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

    /// <summary>Possibilité de fabriquer le blueprint avec les objets possédés.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCraftStatus), nameof(CraftStatusLabel), nameof(CraftStatusTone), nameof(IsCraftReady))]
    private BlueprintCraftStatus _craftStatus;

    /// <summary>Vrai si le badge Prêt / Partiel s'affiche dans la liste.</summary>
    public bool HasCraftStatus => CraftStatus is BlueprintCraftStatus.Ready or BlueprintCraftStatus.Partial;

    /// <summary>Badge de la liste : « Prêt » ou « Partiel ».</summary>
    public string CraftStatusLabel => CraftStatus == BlueprintCraftStatus.Ready ? "Prêt" : "Partiel";

    /// <summary>Ton du badge de la liste.</summary>
    public string CraftStatusTone => CraftStatus == BlueprintCraftStatus.Ready ? "success" : "warning";

    /// <summary>Vrai si tous les matériaux sont réunis : badge vert.</summary>
    public bool IsCraftReady => CraftStatus == BlueprintCraftStatus.Ready;

    /// <summary>Liste des pools de mission qui récompensent ce Blueprint.</summary>
    public List<MissionPoolGroup> MissionPools { get; set; } = new();

    /// <summary>Famille de l'objet fabriqué (armure, arme FPS, composant…).</summary>
    public BlueprintFamily Family { get; set; } = BlueprintFamily.Other;

    /// <summary>Type précis de l'objet fabriqué (ex : « Casque · Armure lourde »).</summary>
    public string TypeLabel { get; set; } = string.Empty;

    /// <summary>Nom de la famille, pour l'infobulle de l'icône.</summary>
    public string FamilyLabel => Family.GetDisplayName() ?? Family.ToString();

    /// <summary>Icône de la famille.</summary>
    public Avalonia.Media.Geometry FamilyIcon => BlueprintFamilyInfo.GetIcon(Family);

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
