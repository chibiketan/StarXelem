using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;
using StarBreaker.Common;
using System.Linq;
using System.Collections.Generic;

namespace StarXelem.ViewModels;

public partial class BlueprintListTabViewModel : PageViewModelBase
{
    private readonly ILogger<BlueprintListTabViewModel> _logger;

    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    private readonly IEntityClassDefinitionService _entityClassDefinitionService;
    public override string Name => "Blueprints";
    public override string Icon => nameof(Symbol.Copy);
    [ObservableProperty] public IList<BlueprintViewModel>? _blueprintList;
    [ObservableProperty] public BlueprintViewModel? _selectedBluePrint;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private string _search = "";

    // Stocke la liste complète pour le filtrage
    private List<BlueprintViewModel>? _allBlueprints;

    public BlueprintListTabViewModel(ILogger<BlueprintListTabViewModel> logger, IGrpcClientService clientService, IP4kService p4kService, IEntityClassDefinitionService entityClassDefinitionService)
    {
        _logger = logger;
        _clientService = clientService;
        _p4KService = p4kService;
        _entityClassDefinitionService = entityClassDefinitionService;

        _clientService.OnConnectedChanged += (sender, b) => { OnConnectedStatusChanged(b); };
    }

    private void OnConnectedStatusChanged(bool b)
    {
        LoadItemListCommand.NotifyCanExecuteChanged();
    }

    public bool CanLoadItemList()
    {
        return _clientService.IsConnected && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        IsLoading = true;

        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Appel RSI");



        // TODO call blueprint load/search
        var bpDbList = await _clientService.GetBlueprintList().ConfigureAwait(false);
        var result = new List<BlueprintViewModel>();
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement de la liste des objets");

        foreach (var bp in bpDbList)
        {
            var bpRecord = await _p4KService.GetRecordWithSpecificDepth(new CigGuid(bp.BlueprintId), 1);
            var b = bpRecord?.Data as CraftingBlueprintRecord;

            // On fait quoi si pas de blueprint trouvé ?
            if (b is null ||b.blueprint is not CraftingBlueprint craftingBlueprint)
            {
                _logger.LogWarning("Failed to get blueprint record for {BlueprintId}", bp.BlueprintId);
                continue;
            }

            var craftedItem = (craftingBlueprint.processSpecificData as CraftingProcess_Creation)?.entityClass;

            var craftingRecipe = craftingBlueprint.tiers.OfType<CraftingBlueprintTier>().FirstOrDefault()?.recipe as CraftingRecipe;
            // Comment traiter les specific data ? Est-ce qu'il y en a ?
            // Comment traiter les résultats ? Est-ce qu'il y en a ?
            // On traite les coûts
            var costs = craftingRecipe.costs as CraftingRecipeCosts;
            TimeSpan duration = TimeSpan.Zero;
            switch (costs?.craftTime)
            {
                case null:
                    break;
                case TimeValue_LongSeconds timeValueLongSeconds:
                    duration = TimeSpan.FromSeconds(timeValueLongSeconds.seconds);
                    break;
                case TimeValue_Partitioned timeValuePartitioned:
                    duration = new TimeSpan(timeValuePartitioned.days, timeValuePartitioned.hours, timeValuePartitioned.minutes, (int)timeValuePartitioned.seconds);
                    break;
                default:
                    _logger.LogWarning("Unknown cost type : {type}", costs.craftTime.GetType().FullName);
                    break;
            }

            var categoryList = new List<BlueprintCategoryModel>();
            var craftingCost = costs.mandatoryCost as CraftingCost_Select;
            foreach (var craftingCostOption in craftingCost.options)
            {
                switch (craftingCostOption)
                {
                    case null:
                        break;
                    // case CraftingCost_Item craftingCostItem:
                    //     break;
                    // case CraftingCost_Resource craftingCostResource:
                    //     break;
                    // case CraftingCost_Base_Material craftingCostBaseMaterial:
                    //     break;
                    case CraftingCost_Select craftingCostSelect:
                        var categoryName = await _p4KService.GetLocaleValue(craftingCostSelect.nameInfo.displayName);
                        var materialList = new List<BlueprintMaterialModel>();

                        var ressources = craftingCostSelect.options.OfType<CraftingCost_Resource>().ToList();

                        foreach (var craftingCostResource in ressources)
                        {
                            materialList.Add(new BlueprintMaterialModel
                            {
                                Name = await _p4KService.GetLocaleValue(craftingCostResource.resource?.displayName) ?? "Inconnu",
                                QuantityInScu = (craftingCostResource.quantity as SStandardCargoUnit)?.standardCargoUnits ?? -1.0f
                            });
                        }

                        var statModifierList = new List<BlueprintStatModel>();
                        var dssfsd = craftingCostSelect.context.OfType<CraftingCostContext_ResultGameplayPropertyModifiers>();

                        foreach (var ttt in dssfsd)
                        {
                            foreach (var tttt in (ttt.gameplayPropertyModifiers as CraftingGameplayPropertyModifiers_List).gameplayPropertyModifiers)
                            {
                                var rrrr = (tttt as CraftingGameplayPropertyModifierCommon);

                                var propertyName = await _p4KService.GetLocaleValue(rrrr?.gameplayPropertyRecord?.propertyName);
                                statModifierList.Add(new BlueprintStatModel
                                {
                                    Name = propertyName ?? "Inconnu",
                                    Min = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().FirstOrDefault()?.modifierAtStart ?? -1.0f,
                                    Max = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().FirstOrDefault()?.modifierAtEnd ?? -1.0f
                                });
                            }


                        }


                        categoryList.Add(new BlueprintCategoryModel
                        {
                            Name = categoryName,
                            MaterialList = materialList,
                            StatModifierList = statModifierList
                        });

                        break;
                    // case CraftingCost_Base_NonRef craftingCostBaseNonRef:
                    //     break;
                    // case CraftingCost_RecordRef craftingCostRecordRef:
                    //     break;
                    // case CraftingCost_Ref craftingCostRef:
                    //     break;
                    default:
                        _logger.LogWarning("Unknown cost option type : {type}", craftingCostOption.GetType().FullName);
                        break;
                }
            }
            // TODO fill Category list

            var name = await _p4KService.GetEntityClassName(craftedItem) ?? "Inconnu";
            var types = _entityClassDefinitionService.GetType(craftedItem);

            //
            result.Add(new BlueprintViewModel
            {
                Name = name,
                TierLevel = 1,
                CraftDuration = duration,
                RemainingUse = bp.RemainingUses,
                CategoryList = categoryList,
                Type = types.type,
                Subtype = types.subtype
            });
        }
        // TODO filter ?


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
    public required List<BlueprintStatModel> StatModifierList { get; set; }
}

public class BlueprintMaterialModel
{
    public required string Name { get; set; }
    public required float QuantityInScu { get; set; }
}

public class BlueprintStatModel
{
    public required string Name { get; set; }
    public required float Min { get; set; }
    public required float Max { get; set; }
}

public class BlueprintViewModel : ViewModelBase
{
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
}
