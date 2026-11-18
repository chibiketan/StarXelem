using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public sealed partial class MissionsTabViewModel : PageViewModelBase
{
    private readonly ILogger<MissionsTabViewModel> _logger;
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly IGrpcClientService _clientService;
    public override string Name => "Missions";
    protected override string NavIconData => "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Target);

    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);

    private Task? _loadTask;
    private CancellationTokenSource? _filterCts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))]
    private bool _isLoading;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))]
    private bool _isFiltering;

    [ObservableProperty] private List<MissionContractorItemViewModel> _contractorList = [];
    [ObservableProperty] private MissionContractorItemViewModel selectedContractor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] private MissionItemViewModel? selectedMission;
    [ObservableProperty] private ObservableCollection<ShipEntity> _shipsForSelectedMission = new();
    [ObservableProperty] private List<MissionCategoryItemViewModel> _categoryList = [];
    [ObservableProperty] private MissionCategoryItemViewModel? selectedCategory;
    [ObservableProperty] private string _search = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowNoMatch))] private List<MissionItemViewModel> _filteredMissionList = [];

    public MissionsTabViewModel(ILogger<MissionsTabViewModel> logger, ILocalDatabaseService localDatabaseService, IGrpcClientService clientService)
    {
        _logger = logger;
        _localDatabaseService = localDatabaseService;
        _clientService = clientService;
    }

    // Blueprints possédés, lus auprès du jeu s'il est connecté ; null sinon (statut « Obtenu » inconnu).
    private async Task<HashSet<string>?> LoadObtainedBlueprintIdsAsync()
    {
        if (_clientService.Status is not (GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame))
        {
            return null;
        }

        try
        {
            var entries = await _clientService.GetBlueprintList().ConfigureAwait(false);
            return entries.Select(entry => entry.BlueprintId).ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de lire les blueprints obtenus");
            return null;
        }
    }

    public bool HasSelection => SelectedMission != null;

    public bool IsBusy => IsLoading || IsFiltering;

    public string BusyMessage => IsLoading ? "Chargement des missions…" : "Filtrage en cours…";

    public bool ShowNoMatch => SelectedCategory != null && FilteredMissionList.Count == 0 && !IsBusy;

    protected override Task OnFirstShowAsync() => RefreshAsync();

    partial void OnSearchChanged(string value) => ScheduleFilters(SearchFilterDelay);

    partial void OnSelectedCategoryChanged(MissionCategoryItemViewModel? value) => ScheduleFilters(SelectionFilterDelay);

    /// <summary>
    /// Planifie un filtrage différé de la catégorie sélectionnée : toute demande encore en attente ou en cours est annulée.
    /// À appeler depuis le thread d'UI.
    /// </summary>
    private void ScheduleFilters(TimeSpan delay)
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        // Copie de l'état des filtres : le calcul qui suit ne doit lire aucune propriété du ViewModel.
        var missions = SelectedCategory?.MissionList ?? [];
        var needle = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        IsFiltering = true;
        _ = RunFilterAsync(missions, needle, delay, cts.Token);
    }

    private async Task RunFilterAsync(IReadOnlyList<MissionItemViewModel> missions, string? needle, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            // La recherche porte sur le titre, le contractant et le nom technique.
            var rows = await Task.Run(() => missions
                .Where(mission => needle == null
                                  || mission.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                                  || (mission.Contractor?.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false)
                                  || mission.DebugName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList(), token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                FilteredMissionList = rows;
                if (SelectedMission != null && !rows.Contains(SelectedMission))
                {
                    SelectedMission = null;
                }
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des missions");
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

    /// <summary>Recharge les missions ; un appel pendant un chargement en cours attend ce chargement au lieu d'en lancer un second.</summary>
    [RelayCommand]
    private Task RefreshAsync()
    {
        if (_loadTask is { IsCompleted: false })
        {
            return _loadTask;
        }

        _loadTask = LoadMissionsAsync();
        return _loadTask;
    }

    private Task LoadMissionsAsync()
    {
        return Task.Run(async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
            });

            try
            {
                var sw = Stopwatch.StartNew();
                var categoriesWithMissions = await _localDatabaseService.GetAllMissionCategoriesWithMissionsAsync().ConfigureAwait(false);
                var obtainedBlueprintIds = await LoadObtainedBlueprintIdsAsync().ConfigureAwait(false);
                sw.Stop();
                _logger.LogTrace("Missions loaded from DB in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

                var contractorMap = new Dictionary<string, MissionContractorItemViewModel>(20);
                var categoryMap = new Dictionary<string, MissionCategoryItemViewModel>(20);

                foreach (var kvp in categoriesWithMissions)
                {
                    var categoryKey = kvp.Key;
                    var missions = kvp.Value;
                    var categoryName = missions.FirstOrDefault()?.Category?.Name ?? categoryKey;
                    var categoryVm = new MissionCategoryItemViewModel { Name = categoryName };

                    foreach (var mission in missions)
                    {
                        var missionVm = MapMissionEntity(mission, obtainedBlueprintIds);

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

                // Catégorie de tête qui regroupe toutes les missions, triées comme dans chaque catégorie.
                var allCategory = new MissionCategoryItemViewModel { Name = "Toutes les missions" };
                allCategory.MissionList.AddRange(categoryMap.Values
                    .SelectMany(c => c.MissionList)
                    .OrderBy(m => m.Contractor?.Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase));

                var categories = categoryMap.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                categories.Insert(0, allCategory);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var previousCategory = SelectedCategory?.Name;
                    ContractorList = contractorMap.Values.OrderBy(c => c.Name).ToList();
                    CategoryList = categories;
                    SelectedCategory = categories.FirstOrDefault(c => c.Name == previousCategory) ?? allCategory;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du chargement des missions");
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
    // Pools de blueprints de la mission : blueprints triés par nom, chance de tirage calculée sur le poids.
    private static List<MissionBlueprintPoolViewModel> MapBlueprintPools(
        IEnumerable<MissionBlueprintPoolEntity>? pools,
        HashSet<string>? obtainedBlueprintIds)
    {
        var result = new List<MissionBlueprintPoolViewModel>();
        foreach (var pool in pools ?? [])
        {
            var entries = pool.Entries.Where(entry => entry.Blueprint != null).ToList();
            if (entries.Count == 0)
            {
                continue;
            }

            var totalWeight = entries.Sum(entry => entry.Weight);
            var hasEqualChances = entries.All(entry => Math.Abs(entry.Weight - entries[0].Weight) < 0.0001f);
            var blueprints = entries
                .Select(entry =>
                {
                    var item = entry.Blueprint!.OutputItem;
                    var product = item == null ? null : new DbBlueprintProductRow(item.TypeName, item.SubTypeName, item.Size, item.Grade);
                    return new MissionBlueprintViewModel
                    {
                        BlueprintId = entry.BlueprintId,
                        Name = entry.Blueprint.BlueprintName,
                        TypeLabel = BlueprintFamilyInfo.GetTypeLabel(product),
                        Family = BlueprintFamilyInfo.GetFamily(item?.TypeName),
                        IsObtained = obtainedBlueprintIds?.Contains(entry.BlueprintId) ?? false,
                        ChanceLabel = hasEqualChances || totalWeight <= 0 ? null : FormatPercent(entry.Weight / totalWeight)
                    };
                })
                .OrderBy(blueprint => blueprint.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var count = blueprints.Count;
            var chances = hasEqualChances
                ? count == 1 ? "obtenu à coup sûr" : $"chances égales, {FormatPercent(1.0 / count)} chacun"
                : "chances pondérées";
            result.Add(new MissionBlueprintPoolViewModel
            {
                PoolName = pool.PoolName,
                Blueprints = blueprints,
                Summary = $"{count} blueprint{(count > 1 ? "s" : "")} · {chances}"
            });
        }

        foreach (var pool in result)
        {
            pool.ShowHeader = result.Count > 1;
        }

        return result;
    }

    // « 4 blueprints · chances égales, 25 % chacun · 1 déjà obtenu » ; avec plusieurs pools, chaque pool porte son propre résumé.
    private static string FormatBlueprintSummary(List<MissionBlueprintPoolViewModel> pools, bool isObtainedKnown)
    {
        if (pools.Count == 0)
        {
            return "";
        }

        var parts = new List<string>();
        if (pools.Count == 1)
        {
            parts.Add(pools[0].Summary);
        }
        else
        {
            var distinctCount = pools.SelectMany(pool => pool.Blueprints).Select(blueprint => blueprint.BlueprintId).Distinct().Count();
            parts.Add($"{pools.Count} pools · {distinctCount} blueprints");
        }

        if (isObtainedKnown)
        {
            var obtained = pools.SelectMany(pool => pool.Blueprints).Where(blueprint => blueprint.IsObtained)
                .Select(blueprint => blueprint.BlueprintId).Distinct().Count();
            parts.Add(obtained switch
            {
                0 => "aucun obtenu",
                1 => "1 déjà obtenu",
                _ => $"{obtained} déjà obtenus"
            });
        }

        return string.Join(" · ", parts);
    }

    // « 25 % », « 8,3 % » : une décimale seulement quand elle compte.
    private static string FormatPercent(double ratio)
    {
        var percent = Math.Round(ratio * 100, 1);
        return $"{percent.ToString(percent % 1 == 0 ? "0" : "0.0", CultureInfo.CurrentCulture)} %";
    }

    // « Jr. Contractor (800) » : rang traduit et seuil en points ; une clé de traduction non résolue est ignorée.
    private static string? FormatStanding(string? rankName, int? points)
    {
        if (points == null)
        {
            return null;
        }

        var value = points.Value.ToString("N0", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(rankName) || rankName.StartsWith('@') ? value : $"{rankName} ({value})";
    }

    private MissionItemViewModel MapMissionEntity(MissionEntity mission, HashSet<string>? obtainedBlueprintIds)
    {
        var rewards = mission.Rewards?.ToList() ?? [];
        var blueprintPools = MapBlueprintPools(mission.BlueprintPools, obtainedBlueprintIds);
        // Avec un seul pool, la carte Blueprint annonce le nombre de blueprints en jeu.
        var blueprintDetail = blueprintPools.Count == 1 ? $"1 parmi {blueprintPools[0].Blueprints.Count} · liste ci-dessous" : null;
        return new MissionItemViewModel
        {
            Title = mission.Title,
            Description = mission.Description,
            Briefing = FormatBriefing(mission.Description, mission.Tokens),
            RewardCards = rewards
                .OrderBy(MissionRewardCardViewModel.SortRank)
                .Select(reward => MissionRewardCardViewModel.FromEntity(reward, blueprintDetail))
                .OfType<MissionRewardCardViewModel>()
                .ToList(),
            BlueprintPools = blueprintPools,
            BlueprintSummary = FormatBlueprintSummary(blueprintPools, obtainedBlueprintIds != null),
            CompletionTags = string.Join(", ", rewards
                .Where(r => r.RewardType == "ContractResult_CompletionTags")
                .SelectMany(r => Regex.Matches(r.DisplayValue, "'([^']+)'").Select(match => match.Groups[1].Value))
                .Distinct()),
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
            MinStanding = FormatStanding(mission.MinStandingDisplayName, mission.MinStandingRaw),
            MaxStanding = FormatStanding(mission.MaxStandingDisplayName, mission.MaxStanding)
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

        // Return only root objectives (no parent), numbered for display
        var roots = objectives.Where(o => o.ParentId == null)
            .OrderBy(o => o.Order)
            .Select(o => vmById[o.Id])
            .ToList();
        for (var i = 0; i < roots.Count; i++)
        {
            roots[i].Number = $"{i + 1}.";
        }

        return roots;
    }

    /// <summary>
    /// Texte du briefing lisible : jetons de mission résolus, balises de mise en forme du jeu retirées, sauts de ligne
    /// (stockés échappés) rétablis.
    /// </summary>
    private string? FormatBriefing(string? description, ICollection<MissionTokenEntity>? tokens)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = ResolveMissionTokens(description, tokens).Replace("\\n", "\n");
        text = Regex.Replace(text, "</?EM\\d*>", "", RegexOptions.IgnoreCase);
        return text.Trim();
    }

    /// <summary>
    /// Format a prerequisite for display.
    /// </summary>
    private string FormatPrerequisiteLabel(MissionPrerequisiteEntity prereq)
    {
        return prereq.PrerequisiteType switch
        {
            "Reputation" => $"Réputation {prereq.FactionNameKey ?? "inconnue"} ≥ {prereq.MinReputation}",
            "AreaTags" => $"Tags de zone : {prereq.RequiredTagNames ?? "aucun"}",
            "CompletedContractTags" => $"Contrats terminés : {prereq.RequiredTagNames ?? "aucun"}",
            "CrimeStat" => $"CrimeStat entre {prereq.MinCrimeStat} et {prereq.MaxCrimeStat}",
            "JournalEntries" => $"Journal : {prereq.RequiredJournalTitles ?? "aucun"}",
            "Locality" => $"Localité : {prereq.LocationNameKey ?? "inconnue"}",
            "Location" => $"Lieu : {prereq.LocationNameKey ?? "inconnu"}",
            "LocationProperty" => $"Propriété du lieu : {prereq.DisplayLabel ?? "inconnue"}",
            _ => $"{prereq.PrerequisiteType} : {prereq.DisplayLabel ?? "inconnu"}"
        };
    }

    /// <summary>
    /// Resolve ~mission(TokenName) tokens in text to display values from the mission's token collection.
    /// </summary>
    private string ResolveMissionTokens(string text, ICollection<MissionTokenEntity>? tokens)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Replace ~mission(TokenName) with the token's resolved value
        var result = System.Text.RegularExpressions.Regex.Replace(text, "~mission\\(([^)]+)\\)", match =>
        {
            var tokenName = match.Groups[1].Value;
            var token = tokens?.FirstOrDefault(t => t.TokenName == tokenName);
            if (token != null)
            {
                // Try locale resolution first, fall back to raw value
                return !string.IsNullOrEmpty(token.ResolvedValue)
                    ? token.ResolvedValue
                    : token.TokenName;
            }
            // Valeur fixée par le jeu au lancement de la mission : on n'en montre que le nom, entre crochets.
            return $"[{tokenName}]";
        });

        return result;
    }

}

public sealed class MissionCategoryItemViewModel
{
    public required string Name { get; set; }
    public List<MissionItemViewModel> MissionList { get; } = new List<MissionItemViewModel>(20);
    public int Count => MissionList.Count;
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

    /// <summary>Numéro affiché devant un objectif de premier niveau (« 1. »), vide pour un sous-objectif.</summary>
    public string? Number { get; set; }
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

    /// <summary>Description mise en forme pour la lecture (jetons résolus, balises retirées).</summary>
    public string? Briefing { get; set; }

    /// <summary>Récompenses présentées en cartes : valeur en grand, libellé dessous.</summary>
    public List<MissionRewardCardViewModel> RewardCards { get; set; } = [];

    /// <summary>Tags posés à la fin de la mission, information technique.</summary>
    public string CompletionTags { get; set; } = "";

    public bool HasBriefing => !string.IsNullOrWhiteSpace(Briefing);
    public bool HasRewards => RewardCards.Count > 0;
    public bool HasObjectives => ObjectiveList is { Count: > 0 };
    public bool HasPrerequisites => PrerequisiteList is { Count: > 0 };
    /// <summary>Pools de blueprints dont un est tiré en récompense.</summary>
    public List<MissionBlueprintPoolViewModel> BlueprintPools { get; set; } = [];

    /// <summary>Résumé affiché à côté du titre « Blueprints possibles ».</summary>
    public string BlueprintSummary { get; set; } = "";

    public bool HasBlueprintPools => BlueprintPools.Count > 0;
    public bool HasMinStanding => !string.IsNullOrEmpty(MinStanding);
    public bool HasMaxStanding => !string.IsNullOrEmpty(MaxStanding);
    public bool HasCompletionTags => !string.IsNullOrEmpty(CompletionTags);
}

/// <summary>Pool de blueprints d'une mission : un seul blueprint du pool est tiré.</summary>
public sealed class MissionBlueprintPoolViewModel
{
    public required string PoolName { get; init; }
    public required List<MissionBlueprintViewModel> Blueprints { get; init; }

    /// <summary>« 4 blueprints · chances égales, 25 % chacun ».</summary>
    public required string Summary { get; init; }

    /// <summary>Vrai si la mission a plusieurs pools : chacun affiche alors son nom et son résumé.</summary>
    public bool ShowHeader { get; set; }
}

/// <summary>Blueprint possible d'une mission ; un clic l'ouvre dans l'écran Blueprints.</summary>
public sealed class MissionBlueprintViewModel
{
    private RelayCommand? _openCommand;

    public required string BlueprintId { get; init; }
    public required string Name { get; init; }

    /// <summary>Type précis de l'objet fabriqué (« Laser de minage », « Casque · Armure lourde »…).</summary>
    public required string TypeLabel { get; init; }

    public BlueprintFamily Family { get; init; }

    /// <summary>Icône de la famille ; créée au premier affichage, sur le thread d'UI.</summary>
    public Geometry Icon => BlueprintFamilyInfo.GetIcon(Family);

    public bool IsObtained { get; init; }

    /// <summary>Chance de tirage quand les poids diffèrent (« 12,5 % »), sinon <c>null</c>.</summary>
    public string? ChanceLabel { get; init; }

    /// <summary>Ligne sous le nom : type de l'objet, suivi de la chance de tirage si elle diffère des autres.</summary>
    public string Subtitle => ChanceLabel == null ? TypeLabel : $"{TypeLabel} · {ChanceLabel}";

    public string ToolTip => $"Ouvrir {Name} dans l'écran Blueprints";

    public RelayCommand OpenCommand => _openCommand ??= new RelayCommand(
        () => WeakReferenceMessenger.Default.Send(new OpenBlueprintMessage(BlueprintId)));
}

/// <summary>Carte d'une récompense de mission : valeur principale (montant, chance…) et libellé.</summary>
public sealed partial class MissionRewardCardViewModel
{
    /// <summary>Valeur en grand (« 38 000 », « +200 », « 100 % »), ou <c>null</c> pour une récompense sans quantité.</summary>
    public string? Value { get; init; }

    public required string Label { get; init; }

    /// <summary>Précision secondaire (destinataire, lieu de livraison…).</summary>
    public string? Detail { get; init; }

    /// <summary>Texte d'origine, affiché en infobulle.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Carte mise en avant (liseré et précision en accent) : renvoie à la liste des blueprints possibles.</summary>
    public bool IsHighlighted { get; init; }

    public bool HasValue => Value != null;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>Rang d'affichage d'une récompense : aUEC, puis blueprint, puis réputation, puis le reste.</summary>
    public static int SortRank(MissionRewardEntity reward) => reward.RewardType switch
    {
        "ContractResult_CalculatedReward" or "ContractResult_Reward" => 0,
        "BlueprintRewards" => 1,
        "ContractResult_LegacyReputation" or "ContractResult_CalculatedReputation" => 2,
        _ => 3
    };

    /// <summary>Carte d'une récompense enregistrée, ou <c>null</c> si elle ne se montre pas en carte (tags de fin de mission).</summary>
    /// <param name="reward">Récompense enregistrée.</param>
    /// <param name="blueprintDetail">Précision de la carte Blueprint (« 1 parmi 4 · liste ci-dessous »), sinon « Tirage dans un pool ».</param>
    public static MissionRewardCardViewModel? FromEntity(MissionRewardEntity reward, string? blueprintDetail = null)
    {
        var text = reward.DisplayValue.Trim();
        return reward.RewardType switch
        {
            "ContractResult_CompletionTags" => null,
            "ContractResult_CalculatedReward" or "ContractResult_Reward" => AmountCard(text),
            "ContractResult_LegacyReputation" => ReputationCard(text),
            "BlueprintRewards" => ChanceRegex().Match(text) is { Success: true } chance
                ? new MissionRewardCardViewModel
                {
                    Value = $"{chance.Groups[1].Value} %",
                    Label = "Blueprint",
                    Detail = blueprintDetail ?? "Tirage dans un pool",
                    IsHighlighted = blueprintDetail != null,
                    ToolTip = text
                }
                : new MissionRewardCardViewModel { Label = "Blueprint", Detail = blueprintDetail, IsHighlighted = blueprintDetail != null, ToolTip = text },
            "ContractResult_Item" => ItemCard(reward, text),
            "ContractResult_ScenarioProgress" => LeadingNumberRegex().Match(text) is { Success: true } points
                ? new MissionRewardCardViewModel { Value = $"+{points.Groups[1].Value}", Label = "Progression de scénario", ToolTip = text }
                : new MissionRewardCardViewModel { Label = "Progression de scénario", ToolTip = text },
            "ContractResult_CalculatedReputation" => new MissionRewardCardViewModel
            {
                Label = "Réputation calculée",
                Detail = text.Split('→').Last().Trim(),
                ToolTip = text
            },
            "ContractResult_BadgeAward" => new MissionRewardCardViewModel { Label = "Badge", Detail = text, ToolTip = text },
            "ContractResult_ItemsWeighting" => new MissionRewardCardViewModel { Label = "Objets aléatoires", ToolTip = text },
            "ContractResult_JournalEntry" => new MissionRewardCardViewModel { Label = "Entrée de journal", ToolTip = text },
            "ContractResult_RefundBuyIn" => new MissionRewardCardViewModel { Label = "Remboursement de la mise", ToolTip = text },
            "ContractResult_VehicleRental" => new MissionRewardCardViewModel { Label = "Location de véhicule", ToolTip = text },
            _ => new MissionRewardCardViewModel { Label = text, ToolTip = reward.RewardType }
        };
    }

    // « 38 000 aUEC » : le montant en grand, la devise dessous.
    private static MissionRewardCardViewModel AmountCard(string text)
    {
        // Le séparateur de milliers peut être une espace insécable : \s les couvre toutes.
        var match = AmountRegex().Match(text);
        return match.Success
            ? new MissionRewardCardViewModel { Value = match.Groups[1].Value, Label = match.Groups[2].Value, ToolTip = text }
            : new MissionRewardCardViewModel { Value = text, Label = "Récompense", ToolTip = text };
    }

    [GeneratedRegex(@"^(.*\d)\s+(\D+)$")]
    private static partial Regex AmountRegex();

    // « 200 points de réputation Standing pour Recco Battaglia »
    private static MissionRewardCardViewModel ReputationCard(string text)
    {
        var match = ReputationRegex().Match(text);
        return match.Success
            ? new MissionRewardCardViewModel
            {
                Value = $"+{match.Groups[1].Value}",
                Label = $"Réputation {match.Groups[2].Value}",
                Detail = match.Groups[3].Value,
                ToolTip = text
            }
            : new MissionRewardCardViewModel { Label = "Réputation", Detail = text, ToolTip = text };
    }

    // « Council Scrip x 3 » : la quantité en grand, l'objet dessous, puis sa livraison et son partage.
    private static MissionRewardCardViewModel ItemCard(MissionRewardEntity reward, string text)
    {
        var name = ItemCountSuffixRegex().Replace(text, "");
        var delivery = reward.SendToHomeLocation switch
        {
            true => "Livré au domicile",
            false => "Dans l'inventaire",
            null => null
        };
        var sharing = reward.OnlyToMissionOwner switch
        {
            true => "propriétaire seul",
            false => "partagé",
            null => null
        };

        return new MissionRewardCardViewModel
        {
            Value = $"×{reward.Count ?? 1}",
            Label = name,
            Detail = string.Join(" · ", new[] { delivery, sharing }.Where(part => part != null)),
            ToolTip = text
        };
    }

    [GeneratedRegex(@"chance:\s*(\d+)\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex ChanceRegex();

    [GeneratedRegex(@"^(\d[\d\s]*)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"^(\d[\d\s]*?)\s*points de réputation\s+(.+?)\s+pour\s+(.+)$")]
    private static partial Regex ReputationRegex();

    [GeneratedRegex(@"\s*x\s*\d+$")]
    private static partial Regex ItemCountSuffixRegex();
}

public sealed class MissionPrerequisiteViewModel
{
    public required string Label { get; set; }
}
