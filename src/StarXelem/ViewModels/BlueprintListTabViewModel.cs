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

public partial class BlueprintListTabViewModel : PageViewModelBase
{
    private readonly ILogger<BlueprintListTabViewModel> _logger;

    private readonly IGrpcClientService _clientService;
    private readonly IBlueprintMappingService _mappingService;
    public override string Name => "Blueprints";
    public override string Icon => nameof(Symbol.Copy);
    [ObservableProperty] public IList<BlueprintViewModel>? _blueprintList;
    [ObservableProperty] public BlueprintViewModel? _selectedBluePrint;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private string _search = "";

    // Stocke la liste complète pour le filtrage
    private List<BlueprintViewModel>? _allBlueprints;

    public BlueprintListTabViewModel(
        ILogger<BlueprintListTabViewModel> logger,
        IGrpcClientService clientService,
        IBlueprintMappingService mappingService)
    {
        _logger = logger;
        _clientService = clientService;
        _mappingService = mappingService;

        _clientService.OnStatusChanged += (sender, status) => { OnConnectedStatusChanged(status); };
    }

    private void OnConnectedStatusChanged(GrpcConnectionStatus status)
    {
        LoadItemListCommand.NotifyCanExecuteChanged();
    }

    public bool CanLoadItemList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        IsLoading = true;

        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Appel RSI");

        var bpDbList = await _clientService.GetBlueprintList().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement de la liste des objets");

        var result = await _mappingService.TransformBlueprintsAsync(bpDbList);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TreatmentStatus = "Terminé";
            IsLoading = false;
            _allBlueprints = result;
            ApplyFilter();
        });
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
        return _allBlueprints is { Count: > 0 };
    }

    partial void OnSearchChanged(string value)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var source = _allBlueprints ?? new List<BlueprintViewModel>();

        if (string.IsNullOrWhiteSpace(Search))
        {
            BlueprintList = source.ToList();
        }
        else
        {
            var term = Search.Trim();
            BlueprintList = source
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

        vm.BlueprintsToSend = _allBlueprints;
        WeakReferenceMessenger.Default.Send(new Popup.ShowPopupMessage(
            showCloseButton: true,
            onClose: null,
            viewModel: vm
        ));
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
    /// <summary>Identifiant unique du blueprint (CUID RSI). Utilisé pour la synchronisation API.</summary>
    public required string BlueprintId { get; set; } = "";
    public required string Name { get; set; }
    public required uint TierLevel { get; set; }
    public required int RemainingUse { get; set; }
    public required TimeSpan CraftDuration { get; set; }
    public required List<BlueprintCategoryModel> CategoryList { get; set; }
    public EItemType Type { get; set; }
    public EItemSubType Subtype { get; set; }

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
