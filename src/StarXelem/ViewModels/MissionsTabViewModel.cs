using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public sealed partial class MissionsTabViewModel : PageViewModelBase
{
    private readonly ILogger<P4kService> _logger;
    private readonly IP4kService _p4kService;
    public override string Name => "Missions";
    public override string Icon => nameof(Symbol.Target);

    [ObservableProperty]
    private bool _isLoading;


    [ObservableProperty] private List<MissionContractorItemViewModel> _contractorList = [];
    [ObservableProperty] private MissionContractorItemViewModel selectedContractor;
    [ObservableProperty] private MissionItemViewModel selectedMission;
    [ObservableProperty] private List<MissionCategoryItemViewModel> _categoryList = [];
    [ObservableProperty] private MissionCategoryItemViewModel selectedCategory;


    public MissionsTabViewModel(IP4kService p4KService, ILogger<P4kService> logger)
    {
        _p4kService = p4KService;
        _logger = logger;
    }

    protected override Task OnFirstShowAsync()
    {
        // Précharger si nécessaire plus tard
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return Task.Run(async () =>
        {

            if (IsLoading)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
            });

            try
            {
                // Placeholder: vider/remplir avec quelques éléments factices pour l'instant
                await _p4kService.OpenP4k(_p4kService.SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
                var contractorMap = new Dictionary<string, MissionContractorItemViewModel>(20);
                var categoryMap = new Dictionary<string, MissionCategoryItemViewModel>(20);

                var contractGeneratorListTmp = await _p4kService.GetAllContractGenerator().ConfigureAwait(false);
                var sw = Stopwatch.StartNew();
                
                
                var contractGeneratorList = contractGeneratorListTmp.AsParallel().Select(s => _p4kService.GetRecordWithFullHistory(s.RecordId).Result).ToList();
                
                
                sw.Stop();
                _logger.LogTrace("All contract records loaded in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
                foreach (var dataRecordTemp in contractGeneratorList)
                {
                    if (dataRecordTemp.Data is not ContractGenerator)
                    {
                        // Normalement impossible
                        continue;
                    }

                    // TODO Load data record with full history
                    // var dataRecord = await _p4kService.GetRecordWithFullHistory(dataRecordTemp.RecordId).ConfigureAwait(false);
                    var contractGenerator = dataRecordTemp.Data as ContractGenerator;

                    // Pour chaque generateur de contrat
                    foreach (var contractGeneratorBase in contractGenerator.generators)
                    {
                        if (contractGeneratorBase is null || contractGeneratorBase.notForRelease || contractGeneratorBase.workInProgress)
                        {
                            // Si le générateur n'est pas prêt, on passe à la suite
                            continue;
                        }

                        var correctContractGenerator = contractGeneratorBase as ContractGeneratorHandler_List;
                        // TODO On ne gère que les listes ici, mais il faudrait tout gérer...
                        switch (contractGeneratorBase)
                        {
                            case ContractGeneratorHandler_Career contractGeneratorHandlerCareer:
                                await HandleContractGenerator(contractGeneratorHandlerCareer, contractorMap, categoryMap).ConfigureAwait(false);
                                break;
                            case ContractGeneratorHandler_Legacy contractGeneratorHandlerLegacy:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                            case ContractGeneratorHandler_TutorialSeriesDef contractGeneratorHandlerTutorialSeriesDef:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                            case ContractGeneratorHandler_LinearSeries contractGeneratorHandlerLinearSeries:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                            case ContractGeneratorHandler_List contractGeneratorHandlerList:
                                await HandleContractGenerator(contractGeneratorHandlerList, contractorMap, categoryMap).ConfigureAwait(false);
                                break;
                            case ContractGeneratorHandler_PVPBountyDef contractGeneratorHandlerPvpBountyDef:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                            case ContractGeneratorHandler_ServiceBeacon contractGeneratorHandlerServiceBeacon:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                            default:
                                _logger.LogDebug("Unknown contract generator type : {}", contractGeneratorBase.GetType().FullName);
                                break;
                        }
                    }
                }

                // On trie les contrats par nom de contrat
                foreach (var missionContractorItemViewModel in contractorMap.Values)
                {
                    // On trie toutes les fonctions par titre
                    missionContractorItemViewModel.MissionList.Sort((a, b) => String.Compare(a.Title, b.Title, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase));
                }

                foreach (var missionContractorItemViewModel in categoryMap.Values)
                {
                    // On trie toutes les fonctions par titre
                    missionContractorItemViewModel.MissionList.Sort((a, b) =>
                    {
                        var contractorCompare = String.Compare(a.Contractor.Name, b.Contractor.Name, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase);

                        if (0 != contractorCompare)
                        {
                            return contractorCompare;
                        }
                        
                        return String.Compare(a.Title, b.Title, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase);
                    });
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // On affecte pour l'affichage
                    ContractorList = contractorMap.Values.OrderBy(c => c.Name).ToList();
                    CategoryList = categoryMap.Values.OrderBy(c => c.Name).ToList();
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                });
            }
        });
    }

    private async Task<MissionContractorItemViewModel> GetContractorItem(ContractGeneratorHandlerBase contractGenerator, Dictionary<string, MissionContractorItemViewModel> contractorMap)
    {
        var contractorKey = contractGenerator.contractParams.stringParamOverrides.FirstOrDefault(c => c.param == ContractStringParamType.Contractor)?.value ?? "Inconnu";

        if (!contractorMap.TryGetValue(contractorKey, out var contractorItem))
        {
            contractorItem = new MissionContractorItemViewModel
            {
                NameKey = contractorKey,
                Name = await _p4kService.GetLocaleValue(contractorKey).ConfigureAwait(false) ?? "Inconnu",
                MissionList = new List<MissionItemViewModel>(20)
            };

            contractorMap.Add(contractorKey, contractorItem);
        }

        return contractorItem;
    }

    private async Task HandleContractGenerator(ContractGeneratorHandler_Career contractGenerator, Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        // On regarde si un type de remplacement n'existe pas
        var missionTypeOverride = contractGenerator.contractParams.missionTypeOverride;
        var contractorItem = await GetContractorItem(contractGenerator, contractorMap).ConfigureAwait(false);
        
        foreach (var introContract in contractGenerator.introContracts)
        {
            _ = await HandleContract(introContract, missionTypeOverride, contractorItem, categoryMap).ConfigureAwait(false);
        }

        foreach (var contract in contractGenerator.contracts)
        {
            _ = await HandleContract(contract, missionTypeOverride, contractorItem, categoryMap, contractGenerator.reputationScope).ConfigureAwait(false);
        }
    }
    
    private async Task HandleContractGenerator(ContractGeneratorHandler_List contractGenerator, Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        // On regarde si un type de remplacement n'existe pas
        var missionTypeOverride = contractGenerator.contractParams.missionTypeOverride;
        var contractorItem = await GetContractorItem(contractGenerator, contractorMap).ConfigureAwait(false);

        foreach (var contract1 in contractGenerator.contracts)
        {
            _ = await HandleContract(contract1, missionTypeOverride, contractorItem, categoryMap).ConfigureAwait(false);
        }
    }

    private async Task<MissionItemViewModel?> HandleContract(CareerContract contract, MissionType? missionTypeOverride, MissionContractorItemViewModel contractorItem,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap, SReputationScopeParams? reputationScopeParams)
    {
        // On fait le traitement général
        var contractVm = await HandleContract((ContractBase)contract, missionTypeOverride, contractorItem, categoryMap).ConfigureAwait(false);
        // TODO faire le traitement spécifique

        if (contractVm is not null && contract.minStanding is not null)
        {
            contractVm.MinStanding = $"{contract.minStanding.minReputation} {await _p4kService.GetLocaleValue(contract.minStanding.displayName)}";
            
        }

        if (contractVm is not null && contract.maxStanding is not null)
        {
            // Pour avoir la valeur max du palier max, il faut aller chercher la valeur min du palier suivant dans l'ordre
            var maxRep = contract.maxStanding.minReputation;
            if (reputationScopeParams is not null)
            {
                var size = reputationScopeParams.standingMap.standings.Length;
                var foundIndex = -1;
                
                foreach (var valueTuple in reputationScopeParams.standingMap.standings.Index())
                {
                    if (valueTuple.Item?.name == contract.maxStanding.name)
                    {
                        foundIndex = valueTuple.Index;
                        break;
                    }
                }

                if (foundIndex > -1)
                {
                    if (foundIndex < size - 1)
                    {
                        // Le palier n'est pas le dernier, on va chercher la valeur min du suivant
                        maxRep = reputationScopeParams.standingMap.standings[foundIndex + 1].minReputation - 1;
                    }
                    else
                    {
                        // On est sur le dernier palier, on utilise le plafond
                        maxRep = reputationScopeParams.standingMap.reputationCeiling;
                    }
                }
            }
            contractVm.MaxStanding = $"{maxRep} {await _p4kService.GetLocaleValue(contract.maxStanding.displayName)}";
            
        }

        return contractVm;
    }
    
    /**
     * Retourne un VM pour le contrat si ce dernier est éligible
     */
    private async Task<MissionItemViewModel?> HandleContract(ContractBase contract, MissionType? missionTypeOverride, MissionContractorItemViewModel contractorItem, Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        if (contract.notForRelease || contract.workInProgress)
        {
            // Contrat pas prêt pour la live
            return null;
        }

        MissionCategoryItemViewModel? categoryItem = null;
        var template = contract.template;
        var type = template?.contractDisplayInfo?.type;
        var localMissionTypeOverride = contract.paramOverrides.missionTypeOverride;

        if (localMissionTypeOverride is not null)
        {
            // Le override directement sur le contrat est prioritaire
            if (!categoryMap.TryGetValue(localMissionTypeOverride.LocalisedTypeName, out categoryItem))
            {
                categoryItem = new MissionCategoryItemViewModel
                {
                    Name = await _p4kService.GetLocaleValue(localMissionTypeOverride.LocalisedTypeName) ?? "Unknown"
                };
                categoryMap.Add(localMissionTypeOverride.LocalisedTypeName, categoryItem);
            }
            
        }
        else if (missionTypeOverride is not null)
        {
            // Le override sur le generateur est prioritaire
            if (!categoryMap.TryGetValue(missionTypeOverride.LocalisedTypeName, out categoryItem))
            {
                categoryItem = new MissionCategoryItemViewModel
                {
                    Name = await _p4kService.GetLocaleValue(missionTypeOverride.LocalisedTypeName) ?? "Unknown"
                };
                categoryMap.Add(missionTypeOverride.LocalisedTypeName, categoryItem);
            }
            
        }
        else if (type is not null)
        {
            // le type existe, on l'utilise
            if (!categoryMap.TryGetValue(type.LocalisedTypeName, out categoryItem))
            {
                categoryItem = new MissionCategoryItemViewModel
                {
                    Name = await _p4kService.GetLocaleValue(type.LocalisedTypeName) ?? "Unknown"
                };
                categoryMap.Add(type.LocalisedTypeName, categoryItem);
            }
        }
        else
        {
            // Le type n'existe pas, on crée une catégorie inconnue
            var key = "@Unknown";
            
            if (!categoryMap.TryGetValue(key, out categoryItem))
            {
                categoryItem = new MissionCategoryItemViewModel
                {
                    Name = "Inconnue"
                };
                categoryMap.Add(key, categoryItem);
            }
        }
        
        var titleKey = contract.paramOverrides?.stringParamOverrides?.FirstOrDefault(c => c.param == ContractStringParamType.Title)?.value;
        if (titleKey is null)
        {
            // l'objet à l'index 0 semble être le titre du contrat
            titleKey = contract.template?.contractDisplayInfo?.displayString[0];
        }
        var title = await _p4kService.GetLocaleValue(titleKey).ConfigureAwait(false) ?? "Inconnu";
        
        var descKey = contract.paramOverrides?.stringParamOverrides?.FirstOrDefault(c => c.param == ContractStringParamType.Description)?.value;
        if (descKey is null)
        {
            // l'objet à l'index 2 semble être la description du contrat
            descKey = contract.template?.contractDisplayInfo?.displayString[2];
        }
        var description = await _p4kService.GetLocaleValue(descKey).ConfigureAwait(false);
        // TODO récupérer le loot
        var rewardList = await ExtractRewardFromContract(contract).ConfigureAwait(false);

        // TODO récupérer les objectifs
        var objectives = await ExtractObjectivesFromContract(contract).ConfigureAwait(false);
        
        // TODO récupérer les pré-requis

        var missionVm = new MissionItemViewModel
        {
            Title = title,
            Description = description,
            DebugName = contract.debugName,
            RewardList = rewardList,
            Contractor = contractorItem,
            ObjectiveList = objectives,
        };
        contractorItem.MissionList.Add(missionVm);
        categoryItem.MissionList.Add(missionVm);
        
        return missionVm;
    }

    private async Task<List<MissionObjectiveViewModel>> ExtractObjectivesFromContract(ContractBase contract)
    {
        var result = new List<MissionObjectiveViewModel>(10);
        foreach (var templateObjectiveToken in contract.template.objectiveTokens)
        {
            List<MissionObjectiveViewModel> stepResult;
            if (templateObjectiveToken.childMissionPhases.Length > 0)
            {
                _logger.LogDebug("Contrat {contractName} avec des sous objectifs pour l'objectif {objectifName}", contract.debugName, templateObjectiveToken.debugName);
            }

            switch (templateObjectiveToken.objectiveHandler)
            {
                case null:
                    // Sérieux, du null ?
                    stepResult = [];
                    break;
                case ObjectiveHandler_Hauling objectiveHandlerHauling:
                    stepResult = await TransformOvjectiveHandlerToVM(contract, templateObjectiveToken, objectiveHandlerHauling).ConfigureAwait(false);
                    break;
                // case ObjectiveHandler_EventModule objectiveHandlerEventModule:
                //     break;
                // case ObjectiveHandler_EntityAttached objectiveHandlerEntityAttached:
                //     break;
                // case ObjectiveHandler_Local objectiveHandlerLocal:
                //     break;
                // case ObjectiveHandler_NearLocation objectiveHandlerNearLocation:
                //     break;
                // case ObjectiveHandler_PlayerAttached objectiveHandlerPlayerAttached:
                //     break;
                // case ObjectiveHandler_WithModule objectiveHandlerWithModule:
                //     break;
                default:
                    _logger.LogDebug("Unknown objective handler type : {type}", templateObjectiveToken.objectiveHandler.GetType().FullName);
                    stepResult = [];
                    break;
            }
            
            result.AddRange(stepResult);
        }
        
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ContractBase contract, ObjectiveToken objectiveToken, ObjectiveHandler_Hauling objectiveHandler)
    {
        // 3 sources potentielles pour les objectifs :
        // - contrat => MissionProperty
        // - template => MissionProperty
        // - template => ObjectiveHandler_Hauling
        var result = new List<MissionObjectiveViewModel>(10);

        foreach (var haulingOrder in objectiveHandler.haulingOrders)
        {
            // TODO à compléter
            List<MissionObjectiveViewModel> stepResult;
            switch (haulingOrder)
            {
                case null:
                    stepResult = [];
                    break;
                // case HaulingOrder_DropOff haulingOrderDropOff:
                //     break;
                case HaulingOrder_EntityClass haulingOrderEntityClass:
                    stepResult = await TransformObjectiveHandlerToVM(contract, objectiveToken, objectiveHandler, haulingOrderEntityClass).ConfigureAwait(false);
                    break;
                case HaulingOrder_EntityClasses haulingOrderEntityClasses:
                    stepResult = await TransformObjectiveHandlerToVM(contract, objectiveToken, objectiveHandler, haulingOrderEntityClasses).ConfigureAwait(false);
                    break;
                // case HaulingOrder_MissionItem haulingOrderMissionItem:
                //     break;
                // case HaulingOrder_MissionItemDropOff haulingOrderMissionItemDropOff:
                //     break;
                // case HaulingOrder_Or haulingOrderOr:
                //     break;
                case HaulingOrder_Property haulingOrderProperty:
                    stepResult = await TransformOvjectiveHandlerToVM(contract, objectiveToken, objectiveHandler, haulingOrderProperty).ConfigureAwait(false);
                    break;
                // case HaulingOrder_PropertyDropOff haulingOrderPropertyDropOff:
                //     break;
                // case HaulingOrder_PropertyBase haulingOrderPropertyBase:
                //     break;
                // case HaulingOrder_Resource haulingOrderResource:
                //     break;
                // case HaulingOrder_ResourceUnlimitedDropOff haulingOrderResourceUnlimitedDropOff:
                //     break;
                // case HaulingOrder_ResourceBase haulingOrderResourceBase:
                //     break;
                default:
                    _logger.LogDebug("Unknown hauling order type : {type} for contract {contract}", haulingOrder.GetType().FullName, contract.debugName);
                    stepResult = [];
                    break;
            }
            
            result.AddRange(stepResult);
        }
        
        return result;
    }
    
    private async Task<List<MissionObjectiveViewModel>> TransformObjectiveHandlerToVM(ContractBase contract, ObjectiveToken objectiveToken, ObjectiveHandler_Hauling objectiveHandler, HaulingOrder_EntityClasses haulingOrder)
    {
        // Représente une catégorie de composant avec une liste de composant acceptés (par exemple poweplant S1)
        var result = new List<MissionObjectiveViewModel>(1);
        
        var name = await _p4kService.GetLocaleValue(haulingOrder.haulingEntityClasses?.orderDisplayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrder.minAmount, haulingOrder.maxAmount).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformObjectiveHandlerToVM(ContractBase contract, ObjectiveToken objectiveToken, ObjectiveHandler_Hauling objectiveHandler, HaulingOrder_EntityClass haulingOrder)
    {
        var result = new List<MissionObjectiveViewModel>(1);
        
        var name = await _p4kService.GetEntityClassName(haulingOrder.entityClass).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrder.minAmount, haulingOrder.maxAmount).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ContractBase contract, ObjectiveToken objectiveToken, ObjectiveHandler_Hauling objectiveHandler, HaulingOrder_Property haulingOrder)
    {
        // 3 sources potentielles pour les objectifs :
        // - contrat => MissionProperty
        // - template => MissionProperty
        // - template => ObjectiveHandler_Hauling
        var templateHaulingProperty = contract.template?.contractProperties.Where(m => m.value is MissionPropertyValue_HaulingOrders).FirstOrDefault();
        var result = new List<MissionObjectiveViewModel>(10);
        
        // TODO comment trouver l'élément qui correspond au Datapointer haulingOrder.haulingOrdersProperty ?

        if (templateHaulingProperty is null)
        {
            // Configuration étrange
            _logger.LogWarning("Objectif de hauling sans propriété de hauling dans le template..., token {tokenName} pour le contrat {contratName}", objectiveToken.debugName, contract.debugName);
            return result;
        }

        var propertyKey = templateHaulingProperty.missionVariableName;
        var propery = contract.paramOverrides.propertyOverrides.Where(p => p.missionVariableName == propertyKey).FirstOrDefault();

        var haulingOrderList = (templateHaulingProperty.value as MissionPropertyValue_HaulingOrders)?.haulingOrderContent;

        if (propery?.value is not MissionPropertyValue_HaulingOrders { haulingOrderContent.Length: > 0 } haulingOrders2)
        {
            // Le contrat ne défini pas des hauling orders ou alors ce dernier est vide
            if (propery is null)
            {
                _logger.LogWarning("Propriété de hauling non trouvé ou alors pas du bon type..., propriété {propertyKey} pour le contrat {contratName}", propertyKey, contract.debugName);
            }
            else
            {
                _logger.LogWarning("Propriété de hauling trouvé mais value du mauvais type ou liste vide..., propriété {propertyKey} pour le contrat {contratName}", propertyKey, contract.debugName);
            }
        }
        else
        {
            haulingOrderList = haulingOrders2.haulingOrderContent;
        }

        if (haulingOrderList is null)
        {
            // garde fou ultime
            return result;
        }
        
        foreach (var haulingOrderContentBase in haulingOrderList)
        {
            List<MissionObjectiveViewModel> stepResult;

            switch (haulingOrderContentBase)
            {
                case null:
                    stepResult = [];
                    break;
                case HaulingOrderContent_EntityClass haulingOrderContentEntityClass:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClass).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_EntityClasses haulingOrderContentEntityClasses:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClasses).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_MissionItem haulingOrderContentMissionItem:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentMissionItem).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_Or haulingOrderContentOr:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentOr).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_Resource haulingOrderContentResource:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentResource).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_ResourceUnlimitedDropOff haulingOrderContentResourceUnlimitedDropOff:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentResourceUnlimitedDropOff).ConfigureAwait(false);
                    break;
                default:
                    _logger.LogDebug("Unknown hauling order content type : {type}", haulingOrderContentBase.GetType().FullName);
                    stepResult = [];
                    break;
            }
            
            result.AddRange(stepResult);
        }
        
        return result;
    }
    
    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_ResourceUnlimitedDropOff haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(1);

        // TODO manque de détail ?
        var name = await _p4kService.GetLocaleValue(haulingOrderContent.resource?.displayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.resourceDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            //{ objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrderContent.minSCU, haulingOrderContent.maxSCU).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_MissionItem haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(1);

        //TODO comment le remonter celui la ???
        // haulingOrderContent.item
        // var name = await _p4kService.GetLocaleValue(haulingOrderContent.resource?.displayName).ConfigureAwait(false);
        // var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.resourceDeliverObjective.shortDescription).ConfigureAwait(false);
        // var map = new Dictionary<string, string>
        // {
        //     { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
        //     { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
        //     { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrderContent.minSCU, haulingOrderContent.maxSCU).ToString(CultureInfo.CurrentCulture) },
        //     { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        // };
        //
        // result.Add(new MissionObjectiveViewModel
        // {
        //     Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        // });
        result.Add(new MissionObjectiveViewModel
        {
            Title = $"{Math.Max(haulingOrderContent.minAmount, haulingOrderContent.maxAmount)} item HaulingOrderContent_MissionItem"
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_Resource haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(1);

        var name = await _p4kService.GetLocaleValue(haulingOrderContent.resource?.displayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.resourceDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrderContent.minSCU, haulingOrderContent.maxSCU).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_EntityClass haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(1);
        
        var name = await _p4kService.GetEntityClassName(haulingOrderContent.entityClass).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrderContent.minAmount, haulingOrderContent.maxAmount).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_EntityClasses haulingOrderContent)
    {
        // Représente une catégorie de composant avec une liste de composant acceptés (par exemple poweplant S1)
        var result = new List<MissionObjectiveViewModel>(1);
        
        var name = await _p4kService.GetLocaleValue(haulingOrderContent.haulingEntityClasses?.orderDisplayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(haulingOrderContent.minAmount, haulingOrderContent.maxAmount).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_Or haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(5);
        var label = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.orObjective.longDescription) ?? "Inconnu";
        var map = new Dictionary<string, string>
        {
            {objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]"}
        };
        var item = new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(label, map)
        };
        
        
        foreach (var haulingOrderOrOptionBase in haulingOrderContent.options)
        {
            List<MissionObjectiveViewModel> stepResult;
            switch (haulingOrderOrOptionBase)
            {
                case null:
                    _logger.LogDebug("Hauling order content Or avec null option");
                    stepResult = [];
                    break;
                case HaulingOrder_OrOption_And haulingOrderOrOptionAnd:
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderOrOptionAnd);
                    break;
                default:
                    _logger.LogDebug("Unknown hauling order Or option type : {type}", haulingOrderOrOptionBase.GetType().FullName);
                    stepResult = [];
                    break;
            }
            
            // TODO imbriquer les sous objectifs
            result.AddRange(stepResult);
        }
        
        item.ObjectiveList = result;
        return [item];
    }
    
    private async Task<List<MissionObjectiveViewModel>> TransformObjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrder_OrOption_And haulingOrderOrOptionAnd)
    {
        var result = new List<MissionObjectiveViewModel>(5);
        
        foreach (var haulingOrderContentBase in haulingOrderOrOptionAnd.orders)
        {
            List<MissionObjectiveViewModel> stepResult;
            
            switch (haulingOrderContentBase)
            {
                case null:
                    _logger.LogDebug("Hauling order Or avec null option");
                    stepResult = [];
                    break;
                case HaulingOrderContent_EntityClass haulingOrderContentEntityClass:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClass);
                    break;
                case HaulingOrderContent_EntityClasses haulingOrderContentEntityClasses:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClasses);
                    break;
                case HaulingOrderContent_MissionItem haulingOrderContentMissionItem:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentMissionItem);
                    break;
                case HaulingOrderContent_Or haulingOrderContentOr:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentOr);
                    break;
                case HaulingOrderContent_Resource haulingOrderContentResource:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentResource);
                    break;
                case HaulingOrderContent_ResourceUnlimitedDropOff haulingOrderContentResourceUnlimitedDropOff:
                    stepResult = await TransformOvjectiveHandlerToVM(objectiveHandler, haulingOrderContentResourceUnlimitedDropOff);
                    break;
                default:
                    _logger.LogDebug("Unknown hauling order content type : {type}", haulingOrderContentBase.GetType().FullName);
                    stepResult = [];
                    break;
            }
            
            // TODO imbriquer les sous objectifs
            result.AddRange(stepResult);
        }
        
        return result;
    }

    private string ReplaceMissionToken(string message, Dictionary<string, string> tokenMap)
    {
        foreach (var (key, value) in tokenMap)
        {
            var regex = new Regex($"~mission\\({key}(\\|[a-z]+)*\\)", RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
            message = regex.Replace(message, value, 1);
        }
        
        return message;
    }

    private async Task<List<MissionRewardItemViewModel>> ExtractRewardFromContract(ContractBase contract)
    {
        var result = new List<MissionRewardItemViewModel>();
        
        foreach (var contractResult in contract.contractResults.contractResults)
        {
            List<MissionRewardItemViewModel> stepResult;

            switch (contractResult)
            {
                case ContractResult_ItemsWeighting contractRewardItem:
                    stepResult = await TransformContractResultToRewardVM(contractRewardItem).ConfigureAwait(false);
                    break;
                case ContractResult_Item contractResultItem:
                    stepResult = await TransformContractResultToRewardVM(contractResultItem).ConfigureAwait(false);
                    break;
                case ContractResult_Reward contractResultReward:
                    stepResult = await TransformContractResultToRewardVM(contractResultReward).ConfigureAwait(false);
                    break;
                case ContractResult_LegacyReputation contractResultLegacyReputation:
                    stepResult = await TransformContractResultToRewardVM(contractResultLegacyReputation).ConfigureAwait(false);
                    break;
                case ContractResult_BadgeAward contractResultBadgeAward:
                    stepResult = await TransformContractResultToRewardVM(contractResultBadgeAward).ConfigureAwait(false);
                    break;
                case ContractResult_CompletionTags contractResultCompletionTags:
                    stepResult = await TransformContractResultToRewardVM(contractResultCompletionTags).ConfigureAwait(false);
                    break;
                case ContractResult_CompletionBounty contractResultCompletionBounty:
                    stepResult = await TransformContractResultToRewardVM(contractResultCompletionBounty).ConfigureAwait(false);
                    break;
                case ContractResult_CalculatedReward contractResultCalculatedReward:
                    stepResult = await TransformContractResultToRewardVM(contractResultCalculatedReward).ConfigureAwait(false);
                    break;
                case ContractResult_CalculatedReputation contractResultCalculatedReputation:
                    stepResult = await TransformContractResultToRewardVM(contractResultCalculatedReputation).ConfigureAwait(false);
                    break;
                case ContractResult_JournalEntry contractResultJournalEntry:
                    stepResult = await TransformContractResultToRewardVM(contractResultJournalEntry).ConfigureAwait(false);
                    break;
                case ContractResult_ScenarioProgress contractResultScenarioProgress:
                    stepResult = await TransformContractResultToRewardVM(contractResultScenarioProgress).ConfigureAwait(false);
                    break;
                case null:
                    // Franchement, pas de données ??
                    stepResult = [];
                    break;
                default:
                    stepResult = [];
                    _logger.LogDebug("Unknown contract result type : {type} for contract : {contract_name}", contractResult!.GetType().FullName, contract.debugName);
                    break;
            }
            
            result.AddRange(stepResult);
        }

        return result;
    }
    
    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_ScenarioProgress contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // TODO comment avoir la valeur ?
        var factionName = await _p4kService.GetLocaleValue(contractResult.scenarioProgressPlugin.faction?.name);
        result.Add(new MissionRewardItemViewModel
        {
            Count = contractResult.PointsToAward,
            Name = $"Progres de scénario pour la faction {factionName}",
            OnlyToMissionOwner = false,
            SendToHomeLocation = false
        });
        
        return result;
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_JournalEntry contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // TODO comment avoir la valeur ?
        foreach (var journalEntry in contractResult.journalEntriesToAdd)
        {
            var title = await _p4kService.GetLocaleValue(journalEntry?.Title);
            var type = journalEntry?.type;

            result.Add(new MissionRewardItemViewModel
            {
                Count = 1,
                Name = $"Ajout journal [{type}] : {title}",
                OnlyToMissionOwner = false,
                SendToHomeLocation = false
            });
            
        }

        foreach (var journalEntry in contractResult.journalEntriesToRemove)
        {
            var title = await _p4kService.GetLocaleValue(journalEntry?.Title);
            var type = journalEntry?.type;

            result.Add(new MissionRewardItemViewModel
            {
                Count = 1,
                Name = $"Suppression journal [{type}] : {title}",
                OnlyToMissionOwner = false,
                SendToHomeLocation = false
            });
            
        }

        return result;
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_CompletionBounty contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // TODO comment avoir la valeur ?
        result.Add(new MissionRewardItemViewModel
        {
            Count = 1,
            Name = $"Résultat de bounty calculé",
            OnlyToMissionOwner = false,
            SendToHomeLocation = false
        });

        return Task.FromResult(result);
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_CalculatedReputation contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // TODO comment avoir la valeur ?
        var scopeName = await _p4kService.GetLocaleValue(contractResult.reputationScope?.displayName);
        var factionName = await _p4kService.GetLocaleValue(contractResult.factionReputation?.displayName);
        result.Add(new MissionRewardItemViewModel
        {
            Count = 1,
            Name = $"Réputation calculée de {scopeName} pour {factionName}",
            OnlyToMissionOwner = false,
            SendToHomeLocation = false
        });

        return result;
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_CalculatedReward contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // On fait quoi de ça ? XD
        result.Add(new MissionRewardItemViewModel
        {
            Count = 1,
            Name = "Récompense calculée",
            OnlyToMissionOwner = false,
            SendToHomeLocation = false
        });

        return Task.FromResult(result);
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_ItemsWeighting contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // La récompense du contrat est un ensemble d'objets
        foreach (var itemAwardWeightingsBase in contractResult.itemAwardStructure)
        {
            // TODO comment gérer les autres types ?
            if (itemAwardWeightingsBase is ItemAwardWeightings itemAwardWeightings)
            {
                foreach (var itemAwardBase in itemAwardWeightings.awards)
                {
                    // TODO comment gérer les autres types ?
                    if (itemAwardBase is ItemAwardEntityClass itemAwardEntity)
                    {
                        var name = "Inconnu";
                        // L'objet est la !!
                        var c = itemAwardEntity.entityClass?.Components.OfType<SAttachableComponentParams>().FirstOrDefault();
                        if (null != c)
                        {
                            name = await _p4kService.GetLocaleValue(c.AttachDef.Localization.Name).ConfigureAwait(false);
                        }

                        result.Add(new MissionRewardItemViewModel
                        {
                            Count = itemAwardEntity.amountToAward,
                            Name = name!,
                            OnlyToMissionOwner = contractResult.awardOnlyToMissionOwner,
                            // [CHECK] Besoin de vérifier le target dans le ContractResult ?
                            SendToHomeLocation = false
                        });
                    }
                    else
                    {
                        _logger.LogDebug("Unknown item award type : {}", itemAwardBase!.GetType().FullName);
                    }

                }
            }
        }

        return result;
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_Item contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // La récompense du contrat est unique type d'objet 
        result.Add(new MissionRewardItemViewModel
        {
            Count = contractResult.amount,
            Name = await _p4kService.GetEntityClassName(contractResult.entityClass).ConfigureAwait(false) ?? "Inconnu",
            OnlyToMissionOwner = contractResult.awardOnlyToMissionOwner,
            SendToHomeLocation = contractResult.sendToPlayerHomeLocation
        });

        return result;
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_Reward contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        result.Add(new MissionRewardItemViewModel
        {
            Count = contractResult.contractReward.reward,
            Name = contractResult.contractReward.currencyType.ToString(),
            SendToHomeLocation = false,
            OnlyToMissionOwner = true
        });
        if (contractResult.contractReward.plusBonuses)
        {
            result.Add(new MissionRewardItemViewModel
            {
                Count = 1,
                Name = "+ Bonus"
            });
        }

        return Task.FromResult(result);
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_LegacyReputation contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        // TODO gérer correctement les labels
        var scopeName = await _p4kService.GetLocaleValue(contractResult.contractResultReputationAmounts.reputationScope?.displayName);
        var factionName = await _p4kService.GetLocaleValue(contractResult.contractResultReputationAmounts.factionReputation?.displayName);
        result.Add(new MissionRewardItemViewModel
        {
            Count = contractResult.contractResultReputationAmounts.reward?.reputationAmount ?? -1,
            Name = $"points de réputation {scopeName} pour {factionName}",
            SendToHomeLocation = false,
            OnlyToMissionOwner = false
        });

        return result;
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_BadgeAward contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);
        result.Add(new MissionRewardItemViewModel
        {
            Count = 1,
            Name = $"badge '{contractResult.badgeToAward}'",
            SendToHomeLocation = false,
            OnlyToMissionOwner = false
        });

        return Task.FromResult(result);
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_CompletionTags contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        foreach (var contractResultCompletionTag in contractResult.completionTags)
        {
            result.Add(new MissionRewardItemViewModel
            {
                Count = contractResultCompletionTag.count,
                Name = $"tag '{contractResultCompletionTag.tag?.tagName}'",
                SendToHomeLocation = false,
                OnlyToMissionOwner = false
            });
        }

        return Task.FromResult(result);
    }
}

public sealed class MissionCategoryItemViewModel
{
    public required string Name { get; set; }
    public List<MissionItemViewModel> MissionList { get; } = new List<MissionItemViewModel>(20);
}

public sealed class MissionContractorItemViewModel
{
    public required string Name { get; set; }
    public required string NameKey { get; set; }
    public required List<MissionItemViewModel> MissionList { get; set; }
}

public sealed class MissionRewardItemViewModel
{
    public required string Name { get; set; }
    public required int Count { get; set; }
    public bool? OnlyToMissionOwner { get; set; }
    public bool? SendToHomeLocation { get; set; }
}

public sealed class MissionObjectiveViewModel
{
    public required string Title { get; set; }
    public List<MissionObjectiveViewModel>? ObjectiveList { get; set; }
}

public sealed class MissionItemViewModel
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string DebugName { get; set; }
    public MissionContractorItemViewModel Contractor { get; set; }
    public List<MissionRewardItemViewModel> RewardList { get; set; }
    public List<MissionObjectiveViewModel> ObjectiveList { get; set; }
    public string? MinStanding { get; set; }
    public string? MaxStanding { get; set; }
}
