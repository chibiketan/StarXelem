using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
                    foreach (var contract in contractGenerator.generators)
                    {
                        if (contract is null || contract.notForRelease || contract.workInProgress)
                        {
                            // Si le générateur n'est pas prêt, on passe à la suite
                            continue;
                        }

                        var correctContractGenerator = contract as ContractGeneratorHandler_List;
                        // TODO On ne gère que les listes ici, mais il faudrait tout gérer...
                        if (correctContractGenerator is null) continue;

                        var contractorKey = contract.contractParams.stringParamOverrides.FirstOrDefault(c => c.param == ContractStringParamType.Contractor)?.value ?? "Inconnu";

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

                        foreach (var contract1 in correctContractGenerator.contracts)
                        {
                            if (contract1.notForRelease || contract1.workInProgress)
                            {
                                // Contrat pas prêt pour la live
                                continue;
                            }

                            var rewardList = new List<MissionRewardItemViewModel>(5);
                            var title = await _p4kService.GetLocaleValue(contract1.paramOverrides?.stringParamOverrides?.FirstOrDefault(c => c.param == ContractStringParamType.Title)?.value).ConfigureAwait(false) ??
                                        "Inconnu";
                            var description = await _p4kService.GetLocaleValue(contract1.paramOverrides?.stringParamOverrides?.FirstOrDefault(c => c.param == ContractStringParamType.Description)
                                ?.value).ConfigureAwait(false);
                            // TODO récupérer le loot
                            foreach (var contractResult in contract1.contractResults.contractResults)
                            {
                                // TODO Comment gérer les autres type ?
                                if (contractResult is ContractResult_ItemsWeighting contractRewardItem)
                                {
                                    // La récompense du contrat est un ensemble d'objets
                                    foreach (var itemAwardWeightingsBase in contractRewardItem.itemAwardStructure)
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

                                                    rewardList.Add(new MissionRewardItemViewModel
                                                    {
                                                        Count = itemAwardEntity.amountToAward,
                                                        Name = name!,
                                                        OnlyToMissionOwner = contractRewardItem.awardOnlyToMissionOwner
                                                    });
                                                }

                                            }
                                        }
                                    }
                                }

                                if (contractResult is ContractResult_Item contractResultItem)
                                {
                                    // La récompense du contrat est unique type d'objet 
                                    //contractResultItem.
                                    rewardList.Add(new MissionRewardItemViewModel
                                    {
                                        Count = contractResultItem.amount,
                                        Name = await _p4kService.GetEntityClassName(contractResultItem.entityClass).ConfigureAwait(false) ?? "Inconnu",
                                        OnlyToMissionOwner = contractResultItem.awardOnlyToMissionOwner,
                                        SendToHomeLocation = contractResultItem.sendToPlayerHomeLocation
                                    });
                                }

                                if (contractResult is ContractResult_Reward contractResultReward)
                                {
                                    rewardList.Add(new MissionRewardItemViewModel
                                    {
                                        Count = contractResultReward.contractReward.reward,
                                        Name = contractResultReward.contractReward.currencyType.ToString()
                                    });
                                    if (contractResultReward.contractReward.plusBonuses)
                                    {
                                        rewardList.Add(new MissionRewardItemViewModel
                                        {
                                            Count = 1,
                                            Name = "+ Bonus"
                                        });
                                    }
                                }
                            }

                            // TODO récupérer les objectifs
                            // TODO récupérer les pré-requis

                            contractorItem.MissionList.Add(new MissionItemViewModel
                            {
                                Title = title,
                                Description = description,
                                DebugName = contract1.debugName,
                                RewardList = rewardList
                            });
                        }
                    }
                }

                foreach (var missionContractorItemViewModel in contractorMap.Values)
                {
                    // On trie toutes les fonctions par titre
                    missionContractorItemViewModel.MissionList.Sort((a, b) => String.Compare(a.Title, b.Title, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase));
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // On affecte pour l'affichage
                    ContractorList = contractorMap.Values.OrderBy(c => c.Name).ToList();
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

public sealed class MissionItemViewModel
{
    public string Title { get; set; }
    public string? Description { get; set; }
    public string DebugName { get; set; }
    public List<MissionRewardItemViewModel> RewardList { get; set; }

}
