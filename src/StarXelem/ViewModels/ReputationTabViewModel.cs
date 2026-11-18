using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StarXelem.Extensions;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Écran Réputations : une carte par contractant, filtrable par nom et par état de la relation.
/// Le filtrage reprend le fonctionnement des autres écrans : différé, calculé hors du thread d'UI et affiché par paquets.
/// </summary>
public partial class ReputationTabViewModel : PageViewModelBase
{
    // Délai d'inactivité de la saisie avant de filtrer : une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    // Puces : délai réduit, suffisant pour absorber une série de clics rapprochés.
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);
    // Les cartes sont lourdes à construire : on les ajoute par petits paquets pour garder l'indicateur animé.
    private const int DisplayBatchSize = 8;

    // Ordre des puces d'état : les relations chargées d'abord.
    private static readonly FactionStatus[] StatusOrder =
        [FactionStatus.Friendly, FactionStatus.Neutral, FactionStatus.Hostile, FactionStatus.NotLoaded];

    private readonly IReputationService _reputationService;
    private readonly ILogger<ReputationTabViewModel> _logger;
    private readonly HashSet<FactionStatus> _selectedStatuses = new();
    private List<ContractorModel> _allContractors = new();
    private CancellationTokenSource? _filterCts;

    public override string Name => "Reputations";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Shield);

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))] private int _filteredCount;

    /// <summary>Cartes affichées, remplacées à chaque filtrage.</summary>
    [ObservableProperty] private ObservableCollection<ContractorModel> _filteredContractors = new();

    public ReputationTabViewModel(IReputationService reputationService, ILogger<ReputationTabViewModel> logger)
    {
        _reputationService = reputationService;
        _logger = logger;
    }

    /// <summary>Puces d'état de la relation, cumulables ; « Tous » retire le filtre.</summary>
    public ObservableCollection<FilterChipViewModel> StatusChips { get; } = new();

    /// <summary>Nombre de contractants retenus par les filtres sur le total.</summary>
    public string ResultSummary => $"{FilteredCount} sur {_allContractors.Count} contractants";

    /// <summary>Vrai si les filtres ne laissent aucun contractant.</summary>
    public bool ShowNoMatch => _allContractors.Count > 0 && FilteredCount == 0 && !IsFiltering;

    /// <summary>Vrai pendant un chargement ou un filtrage : affiche l'indicateur superposé aux cartes.</summary>
    public bool IsBusy => IsLoading || IsFiltering;

    /// <summary>Texte de l'indicateur superposé aux cartes.</summary>
    public string BusyMessage => IsLoading ? "Chargement des réputations…" : "Filtrage en cours…";

    partial void OnSearchTextChanged(string value) => ScheduleFilters(SearchFilterDelay);

    protected override async Task OnFirstShowAsync()
    {
        await LoadDataCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadData()
    {
        IsLoading = true;
        try
        {
            var contractors = await _reputationService.GetSynchronizedReputationsAsync();
            _allContractors = contractors ?? new List<ContractorModel>();
            BuildStatusChips();
            ScheduleFilters(TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des réputations");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildStatusChips()
    {
        StatusChips.Clear();
        StatusChips.Add(new FilterChipViewModel(null, "Tous", ToggleStatus));
        foreach (var status in StatusOrder.Where(status => _allContractors.Any(contractor => contractor.FactionStatus == status)))
        {
            StatusChips.Add(new FilterChipViewModel(status.ToString(), status.GetDisplayName() ?? status.ToString(), ToggleStatus));
        }

        _selectedStatuses.RemoveWhere(status => _allContractors.All(contractor => contractor.FactionStatus != status));
    }

    private void ToggleStatus(string? key)
    {
        if (key == null)
        {
            _selectedStatuses.Clear();
        }
        else if (Enum.TryParse<FactionStatus>(key, out var status) && !_selectedStatuses.Remove(status))
        {
            _selectedStatuses.Add(status);
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
        var criteria = new ContractorFilterCriteria(
            _allContractors,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            _selectedStatuses.Count == 0 ? null : _selectedStatuses.ToHashSet());

        IsFiltering = true;
        _ = RunFilterAsync(criteria, delay, cts.Token);
    }

    private async Task RunFilterAsync(ContractorFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => ComputeFilterOutcome(criteria), token);
            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des réputations");
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

    private static ContractorFilterOutcome ComputeFilterOutcome(ContractorFilterCriteria criteria)
    {
        var searched = criteria.Contractors
            .Where(contractor => criteria.Needle == null
                                 || contractor.Name.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase)
                                 || contractor.Reputations.Any(scope => scope.DisplayName.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        var counts = searched.GroupBy(contractor => contractor.FactionStatus).ToDictionary(group => group.Key, group => group.Count());
        var rows = searched.Where(contractor => criteria.Statuses == null || criteria.Statuses.Contains(contractor.FactionStatus)).ToList();

        return new ContractorFilterOutcome(rows, counts, searched.Count);
    }

    private async Task DisplayResultAsync(ContractorFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        foreach (var chip in StatusChips)
        {
            if (chip.Key == null)
            {
                chip.Count = outcome.SearchedCount;
                chip.IsSelected = _selectedStatuses.Count == 0;
                continue;
            }

            var status = Enum.Parse<FactionStatus>(chip.Key);
            chip.Count = outcome.StatusCounts.GetValueOrDefault(status);
            chip.IsSelected = _selectedStatuses.Contains(status);
        }

        FilteredCount = outcome.Contractors.Count;

        // Reconstruire les cartes est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (FilteredContractors.SequenceEqual(outcome.Contractors))
        {
            return;
        }

        var target = new ObservableCollection<ContractorModel>();
        FilteredContractors = target;

        for (var index = 0; index < outcome.Contractors.Count; index += DisplayBatchSize)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var contractor in outcome.Contractors.Skip(index).Take(DisplayBatchSize))
            {
                target.Add(contractor);
            }

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }

    /// <summary>État des filtres au moment de la demande, lu par le calcul hors du thread d'UI.</summary>
    private sealed record ContractorFilterCriteria(IReadOnlyList<ContractorModel> Contractors, string? Needle, HashSet<FactionStatus>? Statuses);

    /// <summary>Résultat d'un filtrage : cartes à afficher et compteurs des puces.</summary>
    private sealed record ContractorFilterOutcome(
        IReadOnlyList<ContractorModel> Contractors,
        IReadOnlyDictionary<FactionStatus, int> StatusCounts,
        int SearchedCount);
}
