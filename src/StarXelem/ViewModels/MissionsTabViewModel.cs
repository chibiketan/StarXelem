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
using StarXelem.Data;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public sealed partial class MissionsTabViewModel : PageViewModelBase
{
    private readonly ILogger<MissionsTabViewModel> _logger;
    private readonly ILocalDatabaseService _localDatabaseService;
    public override string Name => "Missions";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Target);

    [ObservableProperty]
    private bool _isLoading;


    [ObservableProperty] private List<MissionContractorItemViewModel> _contractorList = [];
    [ObservableProperty] private MissionContractorItemViewModel selectedContractor;
    [ObservableProperty] private MissionItemViewModel selectedMission;
    [ObservableProperty] private ObservableCollection<ShipEntity> _shipsForSelectedMission = new();
    [ObservableProperty] private List<MissionCategoryItemViewModel> _categoryList = [];
    [ObservableProperty] private MissionCategoryItemViewModel selectedCategory;


    public MissionsTabViewModel(ILogger<MissionsTabViewModel> logger, ILocalDatabaseService localDatabaseService)
    {
        _logger = logger;
        _localDatabaseService = localDatabaseService;
    }

    protected override Task OnFirstShowAsync()
    {
        // Précharger si nécessaire plus tard
        return Task.CompletedTask;
    }

    partial void OnSelectedMissionChanged(MissionItemViewModel? value)
    {
        ShipsForSelectedMission.Clear();
        if (value == null) return;

        _ = Task.Run(async () =>
        {
            var ships = await _localDatabaseService.GetShipsForMissionAsync(value.DebugName).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShipsForSelectedMission.Clear();
                foreach (var ship in ships)
                {
                    ShipsForSelectedMission.Add(ship);
                }
            });
        });
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
                var sw = Stopwatch.StartNew();
                var categoriesWithMissions = await _localDatabaseService.GetAllMissionCategoriesWithMissionsAsync().ConfigureAwait(false);
                sw.Stop();
                _logger.LogTrace("Missions loaded from DB in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

                var contractorMap = new Dictionary<string, MissionContractorItemViewModel>(20);
                var categoryMap = new Dictionary<string, MissionCategoryItemViewModel>(20);

                foreach (var kvp in categoriesWithMissions)
                {
                    var categoryKey = kvp.Key;
                    var missions = kvp.Value;
                    var categoryVm = new MissionCategoryItemViewModel { Name = categoryKey };

                    foreach (var mission in missions)
                    {
                        var missionVm = MapMissionEntity(mission);

                        // Add to contractor map
                        if (mission.Contractor != null)
                        {
                            if (!contractorMap.TryGetValue(mission.Contractor.Id, out var contractorVm))
                            {
                                contractorVm = new MissionContractorItemViewModel
                                {
                                    Name = mission.Contractor.Name,
                                    NameKey = mission.Contractor.Id,
                                    MissionList = new List<MissionItemViewModel>()
                                };
                                contractorMap[mission.Contractor.Id] = contractorVm;
                            }
                            contractorVm.MissionList.Add(missionVm);
                        }

                        // Add to category map
                        categoryVm.MissionList.Add(missionVm);
                    }

                    categoryMap[categoryKey] = categoryVm;
                }

                // Sort missions within each contractor by title
                foreach (var contractorVm in contractorMap.Values)
                {
                    contractorVm.MissionList.Sort((a, b) =>
                        String.Compare(a.Title, b.Title, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase));
                }

                // Sort missions within each category by contractor then title
                foreach (var categoryVm in categoryMap.Values)
                {
                    categoryVm.MissionList.Sort((a, b) =>
                    {
                        var contractorCompare = String.Compare(
                            a.Contractor?.Name ?? "", b.Contractor?.Name ?? "",
                            CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase);
                        if (contractorCompare != 0) return contractorCompare;
                        return String.Compare(a.Title, b.Title, CultureInfo.CurrentUICulture, CompareOptions.IgnoreCase);
                    });
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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

    /// <summary>
    /// Map a MissionEntity from the database to a MissionItemViewModel for display.
    /// </summary>
    private MissionItemViewModel MapMissionEntity(MissionEntity mission)
    {
        return new MissionItemViewModel
        {
            Title = mission.Title,
            Description = mission.Description,
            DebugName = mission.DebugName,
            Contractor = new MissionContractorItemViewModel
            {
                Name = mission.Contractor?.Name ?? "Unknown",
                NameKey = mission.Contractor?.Id ?? "",
                MissionList = new List<MissionItemViewModel>()
            },
            RewardList = mission.Rewards?.Select(r => new MissionRewardItemViewModel
            {
                Name = r.DisplayValue,
                Count = r.Count ?? 0,
                OnlyToMissionOwner = r.OnlyToMissionOwner,
                SendToHomeLocation = r.SendToHomeLocation
            }).ToList() ?? new List<MissionRewardItemViewModel>(),
            ObjectiveList = MapObjectives(mission.Objectives, mission.Tokens),
            PrerequisiteList = mission.Prerequisites?.Select(p => new MissionPrerequisiteViewModel
            {
                Label = FormatPrerequisiteLabel(p)
            }).ToList() ?? new List<MissionPrerequisiteViewModel>(),
            MinStanding = null, // TODO: resolve from tokens
            MaxStanding = mission.MaxStanding?.ToString()
        };
    }

    /// <summary>
    /// Build hierarchical objective list from flat objective entities.
    /// </summary>
    private List<MissionObjectiveViewModel> MapObjectives(ICollection<MissionObjectiveEntity>? objectives, ICollection<MissionTokenEntity>? tokens)
    {
        if (objectives == null || objectives.Count == 0)
            return new List<MissionObjectiveViewModel>();

        // Build dictionary from objective ID to VM for ID-based matching
        var vmById = new Dictionary<int, MissionObjectiveViewModel>();
        foreach (var o in objectives)
        {
            var vm = new MissionObjectiveViewModel
            {
                Title = ResolveMissionTokens(o.Text, tokens),
                ObjectiveList = new List<MissionObjectiveViewModel>()
            };
            vmById[o.Id] = vm;
        }

        // Build hierarchy: link children to parents using IDs
        foreach (var o in objectives)
        {
            var childVm = vmById[o.Id];
            if (o.ParentId != null && vmById.TryGetValue(o.ParentId.Value, out var parentVm))
            {
                parentVm.ObjectiveList.Add(childVm);
            }
        }

        // Return only root objectives (no parent)
        return objectives.Where(o => o.ParentId == null)
            .Select(o => vmById[o.Id])
            .ToList();
    }

    /// <summary>
    /// Format a prerequisite for display.
    /// </summary>
    private string FormatPrerequisiteLabel(MissionPrerequisiteEntity prereq)
    {
        return prereq.PrerequisiteType switch
        {
            "Reputation" => $"Standing: {prereq.FactionNameKey ?? "Unknown"} >= {prereq.MinReputation}",
            "AreaTags" => $"Area tags: {prereq.RequiredTagNames ?? "None"}",
            "CompletedContractTags" => $"Completed contracts: {prereq.RequiredTagNames ?? "None"}",
            "CrimeStat" => $"Crime stat: {prereq.MinCrimeStat} - {prereq.MaxCrimeStat}",
            "JournalEntries" => $"Journal: {prereq.RequiredJournalTitles ?? "None"}",
            "Locality" => $"Locality: {prereq.LocationNameKey ?? "Unknown"}",
            "Location" => $"Location: {prereq.LocationNameKey ?? "Unknown"}",
            "LocationProperty" => $"Location property: {prereq.DisplayLabel ?? "Unknown"}",
            _ => $"{prereq.PrerequisiteType}: {prereq.DisplayLabel ?? "Unknown"}"
        };
    }

    /// <summary>
    /// Resolve ~mission(TokenName) tokens in text to display values from the mission's token collection.
    /// </summary>
    private string ResolveMissionTokens(string text, ICollection<MissionTokenEntity>? tokens)
    {
        if (string.IsNullOrEmpty(text) || tokens == null || tokens.Count == 0)
            return text ?? string.Empty;

        // Replace ~mission(TokenName) with the token's resolved value
        var result = System.Text.RegularExpressions.Regex.Replace(text, "~mission\\(([^)]+)\\)", match =>
        {
            var tokenName = match.Groups[1].Value;
            var token = tokens.FirstOrDefault(t => t.TokenName == tokenName);
            if (token != null)
            {
                // Try locale resolution first, fall back to raw value
                return !string.IsNullOrEmpty(token.ResolvedValue)
                    ? token.ResolvedValue
                    : token.TokenName;
            }
            return match.Value; // Keep original token if not found
        });

        return result;
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
