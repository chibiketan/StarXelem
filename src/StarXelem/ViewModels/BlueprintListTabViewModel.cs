using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public partial class BlueprintListTabViewModel : PageViewModelBase
{
    private readonly ILogger<BlueprintListTabViewModel> _logger;

    private readonly IGrpcClientService _clientService;
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly IP4kService _p4kService;
    public override string Name => "Blueprints";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Copy);
    [ObservableProperty] public IList<BlueprintViewModel>? _blueprintList;
    [ObservableProperty] public BlueprintViewModel? _selectedBluePrint;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadItemListCommand))] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showOnlyObtained;
    [ObservableProperty] private bool _showOnlyWithMissions;
    [ObservableProperty] private bool _isGrpcConnected;

    // Stocke la liste complète pour le filtrage
    private List<BlueprintViewModel>? _allBlueprints;

    public BlueprintListTabViewModel(
        ILogger<BlueprintListTabViewModel> logger,
        IGrpcClientService clientService,
        ILocalDatabaseService localDatabaseService,
        IP4kService p4kService)
    {
        _logger = logger;
        _clientService = clientService;
        _localDatabaseService = localDatabaseService;
        _p4kService = p4kService;

        _clientService.OnStatusChanged += (sender, status) => { OnConnectedStatusChanged(status); };
    }

    private void OnConnectedStatusChanged(GrpcConnectionStatus status)
    {
        IsGrpcConnected = status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame;
        if (!IsGrpcConnected)
        {
            ShowOnlyObtained = false;
        }
        LoadItemListCommand.NotifyCanExecuteChanged();
        SendToOrbitalAllianceCommand.NotifyCanExecuteChanged();
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
                TreatmentStatus = "Chargement de la base de données...";
                _allBlueprints = new List<BlueprintViewModel>();
                BlueprintList = new List<BlueprintViewModel>();
            }).GetTask().ConfigureAwait(false);

            HashSet<string>? obtainedIds = null;
            if (IsGrpcConnected)
            {
                await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement des BP obtenus...");
                var grpcBps = await _clientService.GetBlueprintList().ConfigureAwait(false);
                obtainedIds = grpcBps.Select(e => e.BlueprintId).ToHashSet();
            }

            const int BatchSize = 200;
            int totalLoaded = 0;

            await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement des blueprints...").GetTask().ConfigureAwait(false);

            await foreach (var row in _localDatabaseService.GetBlueprintsBatchedAsync(BatchSize).ConfigureAwait(false))
            {
                totalLoaded++;

                var vm = await MapRowToViewModelAsync(row, obtainedIds).ConfigureAwait(false);
                _allBlueprints!.Add(vm);

                if (totalLoaded % BatchSize == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TreatmentStatus = $"Chargement des blueprints… {totalLoaded} traités";
                        ApplyFilter();
                    });
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TreatmentStatus = $"Terminé — {totalLoaded} blueprints chargés";
                IsLoading = false;
                ApplyFilter();
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
                var resourceName = await ResolveResourceNameAsync(cost.ResourceRef).ConfigureAwait(false);
                materials.Add(new BlueprintResourceModel
                {
                    Name = resourceName,
                    QuantityInScu = (float)(cost.ResourceAmount ?? 0m)
                });
            }
            else if (cost.CostType == "Item" && !string.IsNullOrEmpty(cost.ItemEntityClassRef))
            {
                var itemName = await ResolveItemNameAsync(cost.ItemEntityClassRef).ConfigureAwait(false);
                materials.Add(new BlueprintItemModel
                {
                    Name = itemName,
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

    private async Task<string> ResolveResourceNameAsync(string resourceRef)
    {
        try
        {
            var record = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(resourceRef), 0);
            if (record?.RecordName != null)
            {
                var dotIndex = record.RecordName.IndexOf('.');
                if (dotIndex >= 0)
                    return record.RecordName.Substring(dotIndex + 1);
            }
            return resourceRef;
        }
        catch
        {
            return resourceRef;
        }
    }

    private async Task<string> ResolveItemNameAsync(string entityClassRef)
    {
        try
        {
            var record = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(entityClassRef), 1);
            if (record?.Data is EntityClassDefinition entityClass)
            {
                var name = await _p4kService.GetEntityClassName(entityClass);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return entityClassRef;
        }
        catch
        {
            return entityClassRef;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    public void ClearSearch()
    {
        Search = "";
        ApplyFilter();
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
        ApplyFilter();
    }

    partial void OnShowOnlyObtainedChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnShowOnlyWithMissionsChanged(bool value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var source = _allBlueprints ?? new List<BlueprintViewModel>();

        var filtered = source;
        if (ShowOnlyObtained)
        {
            filtered = filtered.Where(b => b.IsObtained).ToList();
        }

        if (ShowOnlyWithMissions)
        {
            filtered = filtered.Where(b => b.MissionPools.Count > 0).ToList();
        }

        if (string.IsNullOrWhiteSpace(Search))
        {
            BlueprintList = filtered.ToList();
        }
        else
        {
            var term = Search.Trim();
            BlueprintList = filtered
                .Where(b => b.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                .ToList();
        }

        if (SelectedBluePrint is not null && (BlueprintList is null || !BlueprintList.Contains(SelectedBluePrint)))
        {
            SelectedBluePrint = null;
        }

        SendToOrbitalAllianceCommand.NotifyCanExecuteChanged();
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
}

public abstract class BlueprintStatModelBase
{
    public required string Name { get; set; }
}

public class BlueprintStatLinearModel : BlueprintStatModelBase
{
    public required float Min { get; set; }
    public required float Max { get; set; }
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
