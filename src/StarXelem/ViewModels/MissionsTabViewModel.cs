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
    private readonly ILogger<MissionsTabViewModel> _logger;
    private readonly IP4kService _p4kService;
    private readonly IMissionMappingService _missionMappingService;
    public override string Name => "Missions";
    public override string Icon => nameof(Symbol.Target);

    [ObservableProperty]
    private bool _isLoading;


    [ObservableProperty] private List<MissionContractorItemViewModel> _contractorList = [];
    [ObservableProperty] private MissionContractorItemViewModel selectedContractor;
    [ObservableProperty] private MissionItemViewModel selectedMission;
    [ObservableProperty] private List<MissionCategoryItemViewModel> _categoryList = [];
    [ObservableProperty] private MissionCategoryItemViewModel selectedCategory;


    public MissionsTabViewModel(IP4kService p4KService, ILogger<MissionsTabViewModel> logger, IMissionMappingService missionMappingService)
    {
        _p4kService = p4KService;
        _logger = logger;
        // Service responsable de la transformation des données Contrat -> ViewModel (injecté)
        _missionMappingService = missionMappingService;
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

                        await _missionMappingService.ProcessContractGeneratorAsync(contractGeneratorBase, contractorMap, categoryMap).ConfigureAwait(false);
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
    public List<MissionPrerequisiteViewModel> PrerequisiteList { get; set; }
    public string? MinStanding { get; set; }
    public string? MaxStanding { get; set; }
}

public sealed class MissionPrerequisiteViewModel
{
    public required string Label { get; set; }
}
