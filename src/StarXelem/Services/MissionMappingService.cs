using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OneOf;
using StarBreaker.DataCoreGenerated;
using StarXelem.ViewModels;

namespace StarXelem.Services;

public interface IMissionMappingService
{
    Task ProcessContractGeneratorAsync(ContractGeneratorHandlerBase contractGenerator,
        Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap);
}

internal sealed class MissionMappingService : IMissionMappingService
{
    private readonly ILogger<MissionMappingService> _logger;
    private readonly IP4kService _p4kService;

    public MissionMappingService(IP4kService p4kService, ILogger<MissionMappingService> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
    }

    public Task ProcessContractGeneratorAsync(ContractGeneratorHandlerBase contractGenerator,
        Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        switch (contractGenerator)
        {
            case ContractGeneratorHandler_Career career:
                return HandleContractGenerator(career, contractorMap, categoryMap);
            case ContractGeneratorHandler_List list:
                return HandleContractGenerator(list, contractorMap, categoryMap);
            // case ContractGeneratorHandler_Legacy:
            // case ContractGeneratorHandler_TutorialSeriesDef:
            // case ContractGeneratorHandler_LinearSeries:
            // case ContractGeneratorHandler_PVPBountyDef:
            // case ContractGeneratorHandler_ServiceBeacon:
            //     _logger.LogDebug("Unknown contract generator type : {type}", contractGenerator.GetType().FullName);
            //     return Task.CompletedTask;
            default:
                _logger.LogDebug("Unknown contract generator type : {type} for the generator {debugname}", contractGenerator.GetType().FullName, contractGenerator.debugName);
                return Task.CompletedTask;
        }
    }

    private async Task<MissionContractorItemViewModel> GetContractorItem(ContractGeneratorHandlerBase contractGenerator,
        Dictionary<string, MissionContractorItemViewModel> contractorMap)
    {
        var contractorKey = contractGenerator.contractParams.stringParamOverrides
                               .FirstOrDefault(c => c.param == ContractStringParamType.Contractor)?.value
                           ?? "Inconnu";

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

    private async Task HandleContractGenerator(ContractGeneratorHandler_Career contractGenerator,
        Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        var missionTypeOverride = contractGenerator.contractParams.missionTypeOverride;
        var contractorItem = await GetContractorItem(contractGenerator, contractorMap).ConfigureAwait(false);

        foreach (var introContract in contractGenerator.introContracts)
        {
            // Les contrats d'intro ne sont pas des CareerContract complets
            _ = await HandleContract(introContract, missionTypeOverride, contractorItem, categoryMap).ConfigureAwait(false);
        }

        foreach (var contract in contractGenerator.contracts)
        {
            _ = await HandleContract(contract, missionTypeOverride, contractorItem, categoryMap, contractGenerator.reputationScope).ConfigureAwait(false);
        }
    }

    private async Task HandleContractGenerator(ContractGeneratorHandler_List contractGenerator,
        Dictionary<string, MissionContractorItemViewModel> contractorMap,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        var missionTypeOverride = contractGenerator.contractParams.missionTypeOverride;
        var contractorItem = await GetContractorItem(contractGenerator, contractorMap).ConfigureAwait(false);

        foreach (var contract1 in contractGenerator.contracts)
        {
            _ = await HandleContract(contract1, missionTypeOverride, contractorItem, categoryMap).ConfigureAwait(false);
        }
    }

    private async Task<MissionItemViewModel?> HandleContract(CareerContract contract, MissionType? missionTypeOverride,
        MissionContractorItemViewModel contractorItem,
        Dictionary<string, MissionCategoryItemViewModel> categoryMap, SReputationScopeParams? reputationScopeParams)
    {
        var contractVm = await HandleContract((ContractBase)contract, missionTypeOverride, contractorItem, categoryMap)
            .ConfigureAwait(false);

        if (contractVm is not null && contract.minStanding is not null)
        {
            contractVm.MinStanding = $"{contract.minStanding.minReputation} {await _p4kService.GetLocaleValue(contract.minStanding.displayName)}";
        }

        if (contractVm is not null && contract.maxStanding is not null)
        {
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
                        maxRep = reputationScopeParams.standingMap.standings[foundIndex + 1].minReputation - 1;
                    }
                    else
                    {
                        maxRep = reputationScopeParams.standingMap.reputationCeiling;
                    }
                }
            }

            contractVm.MaxStanding = $"{maxRep} {await _p4kService.GetLocaleValue(contract.maxStanding.displayName)}";
        }

        return contractVm;
    }

    private async Task<MissionItemViewModel?> HandleContract(ContractBase contract, MissionType? missionTypeOverride,
        MissionContractorItemViewModel contractorItem, Dictionary<string, MissionCategoryItemViewModel> categoryMap)
    {
        if (contract.notForRelease || contract.workInProgress)
        {
            return null;
        }

        MissionCategoryItemViewModel? categoryItem = null;
        var type = contract.template?.contractDisplayInfo?.type;
        var localMissionTypeOverride = contract.paramOverrides.missionTypeOverride;

        if (localMissionTypeOverride is not null)
        {
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

        var titleKey = contract.paramOverrides?.stringParamOverrides?
                           .FirstOrDefault(c => c.param == ContractStringParamType.Title)?.value
                       ?? contract.template?.contractDisplayInfo?.displayString[0];
        var title = await _p4kService.GetLocaleValue(titleKey).ConfigureAwait(false) ?? "Inconnu";

        var descKey = contract.paramOverrides?.stringParamOverrides?
                          .FirstOrDefault(c => c.param == ContractStringParamType.Description)?.value
                      ?? contract.template?.contractDisplayInfo?.displayString[2];
        var description = await _p4kService.GetLocaleValue(descKey).ConfigureAwait(false);

        var rewardList = await ExtractRewardFromContract(contract).ConfigureAwait(false);
        var objectives = await ExtractObjectivesFromContract(contract).ConfigureAwait(false);

        title = ReplaceMissionToken(title,  token => ReplaceTokenFromContractCallback(contract, token));
        description = ReplaceMissionToken(description,  token => ReplaceTokenFromContractCallback(contract, token));
        
        // manage prerequisites
        List<MissionPrerequisiteViewModel> prerequisiteList = await ExtractPrerequisiteVMListFromContract(contract);
        
        var missionVm = new MissionItemViewModel
        {
            Title = title,
            Description = description.Replace(@"\n", "\n"),
            DebugName = contract.debugName,
            RewardList = rewardList,
            Contractor = contractorItem,
            ObjectiveList = objectives,
            PrerequisiteList = prerequisiteList
        };
        contractorItem.MissionList.Add(missionVm);
        categoryItem.MissionList.Add(missionVm);

        return missionVm;
    }

    private string ReplaceTokenFromContractCallback(ContractBase contract, string token)
    {
            var split = token.Split('|');
            var ftoken = split.First();
            var param = contract.paramOverrides?.propertyOverrides.FirstOrDefault(p => p.extendedTextToken == ftoken);
            var result = $"[{ftoken}]";

            if (param is not null)
            {
                switch (param.value)
                {
                    case null:
                        break;
                    case MissionPropertyValue_Organization missionPropertyValueOrganization:
                        foreach (var matchCondition in missionPropertyValueOrganization.matchConditions)
                        {
                            switch (matchCondition)
                            {
                                case null:
                                    break;
                                case DataSetMatchCondition_SpecificOrganizationsDef specificOrganizationsDef:
                                    if (split.Length > 1)
                                    {
                                        // on cherche le tag qui correspond au second paramètre
                                        result = specificOrganizationsDef.organizations.FirstOrDefault(o => o.stringVariants.variants.Any(t => t.tag?.tagName == split[1]))
                                            ?.stringVariants.variants.FirstOrDefault(t => t.tag?.tagName == split[1])?.@string ?? "Inconnue";
                                    }
                                    else
                                    {
                                        result = specificOrganizationsDef.organizations.FirstOrDefault()?.factionReputation?.name ?? "Inconnue";
                                    }
                                    break;
                                default:
                                    // TODO creuser
                                    // Debugger.Break();
                                    break;
                            }
                        }
                        break;
                    case MissionPropertyValue_AIName missionPropertyValueAiName:
                        result = "[NPC name]";
                        break;
                    case MissionPropertyValue_Location missionPropertyValueLocation:
                        var tagList = new List<string>(5);

                        tagList.AddRange(missionPropertyValueLocation.resourceTags.Select(t => t?.tagName)!);
                        foreach (var matchCondition in missionPropertyValueLocation.matchConditions)
                        {
                            switch (matchCondition)
                            {
                                case null:
                                    break;
                                case DataSetMatchCondition_TagSearch dataSetMatchConditionTagSearch:
                                    tagList.AddRange(dataSetMatchConditionTagSearch.tagSearch.SelectMany(ts => ts.positiveTags.Select(t => t?.tagName))!);
                                    break;
                                case DataSetMatchCondition_ExcludeProperty:
                                    // On ignore juste
                                    break;
                                case DataSetMatchCondition_ExcludeDistantLocationsDef:
                                    // Supression des plus d'une certaine distance d'un lieu, on ignore
                                    break;
                                default:
                                    // TODO creuser
                                    Debugger.Break();
                                    break;
                            }
                        }

                        result = $"[Lieu avec tags {string.Join(", ", tagList)}]";
                        break;
                    default:
                        // TODO creuser
                        // Debugger.Break();
                        break;
                }
            }
            
            return result;        
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
                    stepResult = [];
                    break;
                case ObjectiveHandler_Hauling objectiveHandlerHauling:
                    stepResult = await TransformOvjectiveHandlerToVM(contract, templateObjectiveToken, objectiveHandlerHauling).ConfigureAwait(false);
                    break;
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
        var result = new List<MissionObjectiveViewModel>(10);

        foreach (var haulingOrder in objectiveHandler.haulingOrders)
        {
            List<MissionObjectiveViewModel> stepResult;
            switch (haulingOrder)
            {
                case null:
                    stepResult = [];
                    break;
                case HaulingOrder_EntityClass haulingOrderEntityClass:
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderEntityClass).ConfigureAwait(false);
                    break;
                case HaulingOrder_EntityClasses haulingOrderEntityClasses:
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderEntityClasses).ConfigureAwait(false);
                    break;
                case HaulingOrder_Property haulingOrderProperty:
                    stepResult = await TransformOvjectiveHandlerToVM(contract, objectiveToken, objectiveHandler, haulingOrderProperty).ConfigureAwait(false);
                    break;
                default:
                    _logger.LogDebug("Unknown hauling order type : {type} for contract {contract}", haulingOrder.GetType().FullName, contract.debugName);
                    stepResult = [];
                    break;
            }

            result.AddRange(stepResult);
        }

        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ContractBase contract, ObjectiveToken objectiveToken, ObjectiveHandler_Hauling objectiveHandler, HaulingOrder_Property haulingOrder)
    {
        var templateHaulingProperty = contract.template?.contractProperties.Where(m => m.value is MissionPropertyValue_HaulingOrders).FirstOrDefault();
        var result = new List<MissionObjectiveViewModel>(10);

        if (templateHaulingProperty is null)
        {
            _logger.LogWarning("Objectif de hauling sans propriété de hauling dans le template..., token {tokenName} pour le contrat {contratName}", objectiveToken.debugName, contract.debugName);
            return result;
        }

        var propertyKey = templateHaulingProperty.missionVariableName;
        var propery = contract.paramOverrides.propertyOverrides.Where(p => p.missionVariableName == propertyKey).FirstOrDefault();

        var haulingOrderList = (templateHaulingProperty.value as MissionPropertyValue_HaulingOrders)?.haulingOrderContent;

        if (propery?.value is not MissionPropertyValue_HaulingOrders { haulingOrderContent.Length: > 0 } haulingOrders2)
        {
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
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClass).ConfigureAwait(false);
                    break;
                case HaulingOrderContent_EntityClasses haulingOrderContentEntityClasses:
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClasses).ConfigureAwait(false);
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

        var name = await _p4kService.GetLocaleValue(haulingOrderContent.resource?.displayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.resourceDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private Task<List<MissionObjectiveViewModel>> TransformOvjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, HaulingOrderContent_MissionItem haulingOrderContent)
    {
        var result = new List<MissionObjectiveViewModel>(1)
        {
            new MissionObjectiveViewModel
            {
                Title = $"{Math.Max(haulingOrderContent.minAmount, haulingOrderContent.maxAmount)} item HaulingOrderContent_MissionItem"
            }
        };
        return Task.FromResult(result);
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

    private async Task<List<MissionObjectiveViewModel>> TransformObjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, OneOf<HaulingOrder_EntityClass, HaulingOrderContent_EntityClass> haulingOrder)
    {
        var result = new List<MissionObjectiveViewModel>(1);
        var entityClass = haulingOrder.Match(a => a.entityClass, a => a.entityClass);
        var minAmount = haulingOrder.Match(a => a.minAmount, a => a.minAmount);
        var maxAmount = haulingOrder.Match(a => a.maxAmount, a => a.maxAmount);

        var name = await _p4kService.GetEntityClassName(entityClass).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(minAmount, maxAmount).ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.dropOffLocationExtendedTextToken, "[Destination]" }
        };

        result.Add(new MissionObjectiveViewModel
        {
            Title = ReplaceMissionToken(objectiveDescription ?? "Inconnu", map)
        });
        return result;
    }

    private async Task<List<MissionObjectiveViewModel>> TransformObjectiveHandlerToVM(ObjectiveHandler_Hauling objectiveHandler, OneOf<HaulingOrder_EntityClasses, HaulingOrderContent_EntityClasses> haulingOrder)
    {
        var result = new List<MissionObjectiveViewModel>(1);
        var haulingEntityClasses = haulingOrder.Match(a => a.haulingEntityClasses, a => a.haulingEntityClasses);
        var minAmount = haulingOrder.Match(a => a.minAmount, a => a.minAmount);
        var maxAmount = haulingOrder.Match(a => a.maxAmount, a => a.maxAmount);

        var name = await _p4kService.GetLocaleValue(haulingEntityClasses?.orderDisplayName).ConfigureAwait(false);
        var objectiveDescription = await _p4kService.GetLocaleValue(objectiveHandler.objectiveSettings.itemDeliverObjective.shortDescription).ConfigureAwait(false);
        var map = new Dictionary<string, string>
        {
            { objectiveHandler.objectiveSettings.amountExtendedTextToken, 0.ToString(CultureInfo.CurrentCulture) },
            { objectiveHandler.objectiveSettings.itemExtendedTextToken, name ?? "Inconnu" },
            { objectiveHandler.objectiveSettings.totalExtendedTextToken, Math.Max(minAmount, maxAmount).ToString(CultureInfo.CurrentCulture) },
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
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClass);
                    break;
                case HaulingOrderContent_EntityClasses haulingOrderContentEntityClasses:
                    stepResult = await TransformObjectiveHandlerToVM(objectiveHandler, haulingOrderContentEntityClasses);
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

            result.AddRange(stepResult);
        }

        return result;
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
                    stepResult = [];
                    break;
                case BlueprintRewards blueprintRewards:
                    stepResult = await TransformContractResultToRewardVM(blueprintRewards).ConfigureAwait(false);
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

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(BlueprintRewards contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(1);
        var content = "";

        foreach (var blueprintPoolBlueprintReward in contractResult.blueprintPool.blueprintRewards)
        {
            switch (blueprintPoolBlueprintReward.blueprintRecord?.blueprint)
            {
                case null:
                    break;
                case CraftingBlueprint craftingBlueprint:
                    // Le Name semble être @LOC_PLACEHOLDER à chaque fois...
                    // craftingBlueprint.blueprintName
                    switch (craftingBlueprint.processSpecificData)
                    {
                        case null:
                            break;
                        case CraftingProcess_Creation craftingProcessCreation:
                            content = $"{content}    - {await _p4kService.GetEntityClassName(craftingProcessCreation.entityClass)}\n";
                            break;
                        default:
                            // TODO break pour analyser plus tard
                            Debugger.Break();
                            break;
                    }

                    break;
                default:
                    // gérer quelque chose ici ?
                    break;
            }
        }
        
        result.Add(new MissionRewardItemViewModel
        {
            Count = 1,
            Name = $"{contractResult.chance*100}% de chance d'un blueprint\n{content}",
            OnlyToMissionOwner = true,
            SendToHomeLocation = true
        });

        return result;
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_ScenarioProgress contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

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
        var result = new List<MissionRewardItemViewModel>(5)
        {
            new MissionRewardItemViewModel
            {
                Count = 1,
                Name = "Résultat de bounty calculé",
                OnlyToMissionOwner = false,
                SendToHomeLocation = false
            }
        };

        return Task.FromResult(result);
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_CalculatedReputation contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

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
        var result = new List<MissionRewardItemViewModel>(5)
        {
            new MissionRewardItemViewModel
            {
                Count = 1,
                Name = "Récompense calculée",
                OnlyToMissionOwner = false,
                SendToHomeLocation = false
            }
        };

        return Task.FromResult(result);
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_ItemsWeighting contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5);

        foreach (var itemAwardWeightingsBase in contractResult.itemAwardStructure)
        {
            if (itemAwardWeightingsBase is ItemAwardWeightings itemAwardWeightings)
            {
                foreach (var itemAwardBase in itemAwardWeightings.awards)
                {
                    if (itemAwardBase is ItemAwardEntityClass itemAwardEntity)
                    {
                        var name = "Inconnu";
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
                            SendToHomeLocation = false
                        });
                    }
                    else
                    {
                        _logger.LogDebug("Unknown item award type : {type}", itemAwardBase!.GetType().FullName);
                    }
                }
            }
        }

        return result;
    }

    private async Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_Item contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5)
        {
            new MissionRewardItemViewModel
            {
                Count = contractResult.amount,
                Name = await _p4kService.GetEntityClassName(contractResult.entityClass).ConfigureAwait(false) ?? "Inconnu",
                OnlyToMissionOwner = contractResult.awardOnlyToMissionOwner,
                SendToHomeLocation = contractResult.sendToPlayerHomeLocation
            }
        };

        return result;
    }

    private Task<List<MissionRewardItemViewModel>> TransformContractResultToRewardVM(ContractResult_Reward contractResult)
    {
        var result = new List<MissionRewardItemViewModel>(5)
        {
            new MissionRewardItemViewModel
            {
                Count = contractResult.contractReward.reward,
                Name = contractResult.contractReward.currencyType.ToString(),
                SendToHomeLocation = false,
                OnlyToMissionOwner = true
            }
        };
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
        var result = new List<MissionRewardItemViewModel>(5)
        {
            new MissionRewardItemViewModel
            {
                Count = 1,
                Name = $"badge '{contractResult.badgeToAward}'",
                SendToHomeLocation = false,
                OnlyToMissionOwner = false
            }
        };

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

    #region contrat - pré-requis
    
    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractBase contract)
    {
        var result = new List<MissionPrerequisiteViewModel>(10);

        foreach (var contractAdditionalPrerequisite in contract.additionalPrerequisites)
        {
            List<MissionPrerequisiteViewModel> stepResult;
            
            switch (contractAdditionalPrerequisite)
            {
                case null:
                    stepResult = [];
                    break;
                case ContractPrerequisite_AreaTags contractPrerequisiteAreaTags:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteAreaTags);
                    break;
                case ContractPrerequisite_CompletedContractTags contractPrerequisiteCompletedContractTags:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteCompletedContractTags);
                    break;
                case ContractPrerequisite_CrimeStat contractPrerequisiteCrimeStat:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteCrimeStat);
                    break;
                case ContractPrerequisite_JournalEntries contractPrerequisiteJournalEntries:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteJournalEntries);
                    break;
                case ContractPrerequisite_Locality contractPrerequisiteLocality:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteLocality);
                    break;
                case ContractPrerequisite_Location contractPrerequisiteLocation:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteLocation);
                    break;
                case ContractPrerequisite_LocationProperty contractPrerequisiteLocationProperty:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contract, contractPrerequisiteLocationProperty);
                    break;
                case ContractPrerequisite_Reputation contractPrerequisiteReputation:
                    stepResult = await ExtractPrerequisiteVMListFromContract(contractPrerequisiteReputation);
                    break;
                default:
                    _logger.LogDebug("Unknown contract prerequisite type : {type} for contract {contractName}", contractAdditionalPrerequisite.GetType().FullName, contract.debugName);
                    Debugger.Break();
                    stepResult = [];
                    break;
            }
            
            result.AddRange(stepResult);
        }
        
        return result;
    }
    
    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractBase contract, ContractPrerequisite_LocationProperty contractPrerequisite)
    {
        var propertyName = contractPrerequisite.propertyVariableName;
        var property = contract.paramOverrides.propertyOverrides.FirstOrDefault(p => p.missionVariableName == propertyName);

        if (property is null)
        {
            // Propriété introuvable dans le paramOverrides
            _logger.LogDebug("Property {propertyName} not found in paramOverrides for contract {contractName}", propertyName, contract.debugName);
            return [];
        }
        
        if (property.value is not MissionPropertyValue_Location locationProperty)
        {
            // Mauvais type de propriété
            _logger.LogDebug("Property {propertyName} of wrong type {propertyType} in paramOverrides for contract {contractName}", propertyName, property.value?.GetType().FullName, contract.debugName);
            return [];
        }
        
        // Petit break, à supprimer mais utile pour savoir pour investiguer
        Debugger.Break();
        
        return
        [
            new MissionPrerequisiteViewModel
            {
                Label = $"[Lieu] Depuis propriété et de type {contractPrerequisite.locationLevelType}"
            }
        ];
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_Reputation contractPrerequisite)
    {
        return
        [
            new MissionPrerequisiteViewModel
            {
                Label =
                    $"[Reputation] comprise entre {contractPrerequisite.minStanding?.minReputation ?? -1} et {contractPrerequisite.maxStanding?.minReputation ?? -1} sur {await _p4kService.GetLocaleValue(contractPrerequisite.scope?.scopeName) ?? "Inconnue"} pour la faction {await _p4kService.GetLocaleValue(contractPrerequisite.factionReputation?.name) ?? "Inconnue"}"
            }
        ];
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_AreaTags contractPrerequisite)
    {
        var result = new List<MissionPrerequisiteViewModel>(10);

        foreach (var tag in contractPrerequisite.requiredAreaTags.tags)
        {
            result.Add(new MissionPrerequisiteViewModel
            {
                Label = $"[Tag zone requis] {await _p4kService.GetLocaleValue(tag?.tagName) ?? "Inconnue"}"
            });
        }

        foreach (var tag in contractPrerequisite.excludedAreaTags.tags)
        {
            result.Add(new MissionPrerequisiteViewModel
            {
                Label = $"[Tag zone exclus] {await _p4kService.GetLocaleValue(tag?.tagName) ?? "Inconnue"}"
            });
        }

        return result;
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_JournalEntries contractPrerequisite)
    {
        var result = new List<MissionPrerequisiteViewModel>(10);

        foreach (var requiredJournalEntry in contractPrerequisite.requiredJournalEntries)
        {
            result.Add(new MissionPrerequisiteViewModel
            {
                Label = $"[Journal entry] {await _p4kService.GetLocaleValue(requiredJournalEntry?.Title) ?? "Inconnue"}"
            });
        }

        return result;
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_CrimeStat contractPrerequisite)
    {
        Debugger.Break();
        return
        [
            new MissionPrerequisiteViewModel
            {
                Label = $"[Crimestat] Entre {contractPrerequisite.minCrimeStat} et {contractPrerequisite.maxCrimeStat} pour {await _p4kService.GetLocaleValue(contractPrerequisite.crimeStatJurisdictionOverride?.name) ?? "Inconnue"}"
            }
        ];
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_Location contractPrerequisite)
    {
        return
        [
            new MissionPrerequisiteViewModel
            {
                Label = $"[Lieu] {await _p4kService.GetLocaleValue(contractPrerequisite.locationAvailable?.name) ?? "Inconnue"}"
            }
        ];
    }

    private async Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_Locality contractPrerequisite)
    {
        var awaitList = contractPrerequisite.localityAvailable?.availableLocations.Select(l => _p4kService.GetLocaleValue(l!.name)).ToList()!;

        return
        [
            new MissionPrerequisiteViewModel
            {
                Label = $"[Localité] {string.Join(" OU ", (await Task.WhenAll(awaitList)).Where(s => !string.IsNullOrEmpty(s)))}"
            }
        ];
    }

    private Task<List<MissionPrerequisiteViewModel>> ExtractPrerequisiteVMListFromContract(ContractPrerequisite_CompletedContractTags contractPrerequisite)
    {
        try
        {
            var result = new List<MissionPrerequisiteViewModel>(10);

            foreach (var completedContractTag in contractPrerequisite.requiredCompletedContractTags.tags)
            {
                result.Add(new MissionPrerequisiteViewModel
                {
                    Label = $"[Tag requis] {completedContractTag?.tagName}"
                });
            }
        
            foreach (var excludedContractTag in contractPrerequisite.excludedCompletedContractTags. tags)
            {
                result.Add(new MissionPrerequisiteViewModel
                {
                    Label = $"[Tag exclus] {excludedContractTag?.tagName}"
                });
            }

            return Task.FromResult(result);
        }
        catch (Exception exception)
        {
            return Task.FromException<List<MissionPrerequisiteViewModel>>(exception);
        }
    }
    
    #endregion
    
    private string ReplaceMissionToken(string message, Dictionary<string, string> tokenMap)
    {
        foreach (var (key, value) in tokenMap)
        {
            var regex = new Regex($"~mission\\({key}(\\|[a-z]+)*\\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            message = regex.Replace(message, value, 1);
        }

        return message;
    }

    private string ReplaceMissionToken(string message, Func<string, string> replaceCallback)
    {
        var regex = new Regex(@"~mission\((?<name>[^)]+)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        var match = regex.Match(message);
        while (match.Success)
        {
            // Tant que la regex match quelque chose, on appelle la fonction callback
            var replaceValue = replaceCallback(match.Groups["name"].Value);
            message = message.Remove(match.Index, match.Length).Insert(match.Index, replaceValue);
            // match suivant
            match = regex.Match(message);
        }

        return message;
    }
}
