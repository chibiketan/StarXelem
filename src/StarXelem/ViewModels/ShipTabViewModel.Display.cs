using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using StarXelem.Extensions;
using StarXelem.Models;

namespace StarXelem.ViewModels;

/// <summary>
/// Affichage de l'écran Mon hangar : recherche, puces de statut, tri et filtres par colonne.
/// Le filtrage reprend le fonctionnement de l'écran Objets : différé, calculé hors du thread d'UI et affiché par paquets.
/// </summary>
public partial class ShipTabViewModel
{
    private const string NoLocationKey = "";
    private const string NoLocationLabel = "—";

    // Délai d'inactivité de la saisie avant de filtrer : une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    // Puces, filtres de colonne et tri : délai réduit, suffisant pour absorber une série de clics rapprochés.
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);
    // Entre deux paquets, le thread d'UI traite la saisie et le rendu : l'indicateur reste animé.
    private const int DisplayBatchSize = 20;

    private static readonly SpaceshipState[] StatusOrder =
        [SpaceshipState.STOWED, SpaceshipState.UNSTOWED, SpaceshipState.UNCLAIMED, SpaceshipState.DESTROYED, SpaceshipState.UNKNOWN];

    private IReadOnlyList<SpaceshipModel> _allShips = Array.Empty<SpaceshipModel>();
    private ColumnHeaderViewModel? _sortColumn;
    private CancellationTokenSource? _filterCts;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private ObservableCollection<SpaceshipModel> _displayedShips = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))]
    private int _filteredCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isFiltering;

    /// <summary>Puces de filtre rapide par statut, synchronisées avec le filtre de la colonne Statut.</summary>
    public ObservableCollection<FilterChipViewModel> StatusChips { get; } = new();

    /// <summary>En-tête de la colonne Modèle.</summary>
    public ColumnHeaderViewModel ModelColumn { get; private set; } = null!;

    /// <summary>En-tête de la colonne Statut.</summary>
    public ColumnHeaderViewModel StatusColumn { get; private set; } = null!;

    /// <summary>En-tête de la colonne Emplacement.</summary>
    public ColumnHeaderViewModel LocationColumn { get; private set; } = null!;

    /// <summary>En-tête de la colonne Store.</summary>
    public ColumnHeaderViewModel StoreColumn { get; private set; } = null!;

    /// <summary>Nombre de vaisseaux retenus par les filtres sur le total.</summary>
    public string ResultSummary => HasLoadedShips ? $"{FilteredCount} sur {_allShips.Count} vaisseaux" : string.Empty;

    /// <summary>Vrai une fois la liste des vaisseaux chargée.</summary>
    public bool HasLoadedShips => Spaceships is { IsCompletedSuccessfully: true };

    /// <summary>Vrai tant qu'aucun chargement n'a eu lieu.</summary>
    public bool HasNotLoadedShips => !HasLoadedShips && !IsLoading;

    /// <summary>Vrai si les filtres ne laissent aucun vaisseau.</summary>
    public bool ShowNoMatch => HasLoadedShips && _allShips.Count > 0 && FilteredCount == 0 && !IsFiltering;

    /// <summary>Vrai pendant un chargement ou un filtrage : affiche l'indicateur superposé au tableau.</summary>
    public bool IsBusy => IsLoading || IsFiltering;

    /// <summary>Texte de l'indicateur superposé au tableau.</summary>
    public string BusyMessage => IsLoading
        ? (string.IsNullOrWhiteSpace(TreatmentStatus) ? "Chargement des vaisseaux…" : TreatmentStatus)
        : "Filtrage en cours…";

    private void InitializeDisplay()
    {
        var statusFilter = new ColumnFilterViewModel();
        statusFilter.SelectionChanged += (_, _) => ScheduleFilters(SelectionFilterDelay);
        var locationFilter = new ColumnFilterViewModel();
        locationFilter.SelectionChanged += (_, _) => ScheduleFilters(SelectionFilterDelay);

        ModelColumn = new ColumnHeaderViewModel("Modèle", OnSortRequested);
        StatusColumn = new ColumnHeaderViewModel("Statut", OnSortRequested, statusFilter);
        LocationColumn = new ColumnHeaderViewModel("Emplacement", OnSortRequested, locationFilter);
        StoreColumn = new ColumnHeaderViewModel("Store", OnSortRequested);
    }

    partial void OnSpaceshipsChanged(Task<IList<SpaceshipModel>>? value)
    {
        _allShips = value is { IsCompletedSuccessfully: true } ? value.Result.ToList() : Array.Empty<SpaceshipModel>();

        foreach (var ship in _allShips)
        {
            ship.LocationDisplay = CreateLocationDisplay(ship);
        }

        StatusColumn.Filter!.SetOptions(StatusOrder
            .Where(state => _allShips.Any(ship => ship.State == state))
            .Select(state => new KeyValuePair<string, string>(state.ToString(), state.GetDisplayName() ?? state.ToString())));
        LocationColumn.Filter!.SetOptions(_allShips
            .Select(ship => new KeyValuePair<string, string>(GetLocationKey(ship), GetLocationLabel(ship)))
            .DistinctBy(option => option.Key));

        OnPropertyChanged(nameof(HasLoadedShips));
        OnPropertyChanged(nameof(HasNotLoadedShips));
        ScheduleFilters(TimeSpan.Zero);
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNotLoadedShips));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyMessage));
    }

    partial void OnTreatmentStatusChanged(string value) => OnPropertyChanged(nameof(BusyMessage));

    partial void OnIsFilteringChanged(bool value) => OnPropertyChanged(nameof(ShowNoMatch));

    partial void OnSearchTextChanged(string value) => ScheduleFilters(SearchFilterDelay);

    private static ItemLocationDisplay? CreateLocationDisplay(SpaceshipModel ship)
    {
        if (ship.ResolvedLocation is not { } location)
        {
            return null;
        }

        // Vu du hangar, un objet porteur est un vaisseau : celui dans lequel ce vaisseau est rangé.
        var category = location.Kind switch
        {
            LocationKind.Location => ItemLocationCategory.Place,
            LocationKind.Hangar => ItemLocationCategory.Hangar,
            LocationKind.Item => ItemLocationCategory.Ship,
            LocationKind.Worn => ItemLocationCategory.Worn,
            _ => ItemLocationCategory.Other
        };
        var name = location.Kind == LocationKind.Worn ? "Logout dedans" : location.DisplayName;
        var detail = string.IsNullOrEmpty(ship.Shard) ? null : $"shard {ship.Shard}";

        return new ItemLocationDisplay(category, name, detail, null);
    }

    private static string GetLocationKey(SpaceshipModel ship) => ship.LocationDisplay?.Name ?? NoLocationKey;

    private static string GetLocationLabel(SpaceshipModel ship) => ship.LocationDisplay?.Name ?? NoLocationLabel;

    private void ToggleStatus(string? key)
    {
        if (key == null)
        {
            StatusColumn.Filter!.Reset();
            return;
        }

        StatusColumn.Filter!.Toggle(key);
    }

    private void OnSortRequested(ColumnHeaderViewModel column)
    {
        var direction = column == _sortColumn && column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var header in new[] { ModelColumn, StatusColumn, LocationColumn, StoreColumn })
        {
            header.SortDirection = header == column ? direction : null;
        }

        _sortColumn = column;
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
        var criteria = new ShipFilterCriteria(
            _allShips,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            StatusColumn.Filter!.GetSelectedKeys(),
            LocationColumn.Filter!.GetSelectedKeys(),
            GetSortColumn(),
            _sortColumn?.SortDirection == ListSortDirection.Descending);

        IsFiltering = true;
        _ = RunFilterAsync(criteria, delay, cts.Token);
    }

    private ShipSortColumn? GetSortColumn()
    {
        if (_sortColumn?.SortDirection == null)
        {
            return null;
        }

        if (_sortColumn == StatusColumn)
        {
            return ShipSortColumn.Status;
        }

        if (_sortColumn == LocationColumn)
        {
            return ShipSortColumn.Location;
        }

        return _sortColumn == StoreColumn ? ShipSortColumn.Store : ShipSortColumn.Model;
    }

    private async Task RunFilterAsync(ShipFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => ComputeFilterOutcome(criteria, token), token);
            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage de la liste des vaisseaux");
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

    /// <summary>
    /// Calcule les lignes à afficher et, pour chaque filtre de colonne, le nombre de lignes de chaque valeur en tenant
    /// compte de la recherche et des autres filtres seulement : une liste de valeurs ne se réduit pas sous ses propres coches.
    /// </summary>
    private static ShipFilterOutcome ComputeFilterOutcome(ShipFilterCriteria criteria, CancellationToken token)
    {
        var searched = criteria.Ships.Where(ship => criteria.Needle == null || MatchesSearch(ship, criteria.Needle)).ToList();
        token.ThrowIfCancellationRequested();

        bool MatchesStatus(SpaceshipModel ship) => criteria.Statuses == null || criteria.Statuses.Contains(ship.State.ToString());
        bool MatchesLocation(SpaceshipModel ship) => criteria.Locations == null || criteria.Locations.Contains(GetLocationKey(ship));

        var statusCounts = searched.Where(MatchesLocation)
            .GroupBy(ship => ship.State.ToString())
            .ToDictionary(group => group.Key, group => group.Count());
        var locationCounts = searched.Where(MatchesStatus)
            .GroupBy(GetLocationKey)
            .ToDictionary(group => group.Key, group => group.Count());
        var rows = Sort(searched.Where(ship => MatchesStatus(ship) && MatchesLocation(ship)), criteria).ToList();

        return new ShipFilterOutcome(rows, statusCounts, locationCounts);
    }

    private async Task DisplayResultAsync(ShipFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        StatusColumn.Filter!.UpdateCounts(outcome.StatusCounts);
        LocationColumn.Filter!.UpdateCounts(outcome.LocationCounts);
        RefreshStatusChips(outcome.StatusCounts);
        FilteredCount = outcome.Ships.Count;

        // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (DisplayedShips.SequenceEqual(outcome.Ships))
        {
            return;
        }

        var target = new ObservableCollection<SpaceshipModel>();
        DisplayedShips = target;

        for (var index = 0; index < outcome.Ships.Count; index += DisplayBatchSize)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var ship in outcome.Ships.Skip(index).Take(DisplayBatchSize))
            {
                target.Add(ship);
            }

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }

    private void RefreshStatusChips(IReadOnlyDictionary<string, int> statusCounts)
    {
        var states = StatusOrder.Where(state => _allShips.Any(ship => ship.State == state)).Select(state => state.ToString()).ToList();
        var expectedKeys = new List<string?> { null };
        expectedKeys.AddRange(states);

        if (!StatusChips.Select(chip => chip.Key).SequenceEqual(expectedKeys))
        {
            StatusChips.Clear();
            StatusChips.Add(new FilterChipViewModel(null, "Tous", ToggleStatus));
            foreach (var key in states)
            {
                StatusChips.Add(new FilterChipViewModel(key, StatusColumn.Filter!.GetLabel(key), ToggleStatus));
            }
        }

        foreach (var chip in StatusChips)
        {
            chip.Count = chip.Key == null ? statusCounts.Values.Sum() : statusCounts.GetValueOrDefault(chip.Key);
            chip.IsSelected = chip.Key == null ? !StatusColumn.Filter!.HasSelection : StatusColumn.Filter!.IsSelected(chip.Key);
        }
    }

    private static bool MatchesSearch(SpaceshipModel ship, string search)
    {
        return new[] { ship.Shipname, ship.Name, ship.TechnicalName, ship.LocationDisplay?.Name, ship.DisplayState, ship.Shard }
            .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private static IEnumerable<SpaceshipModel> Sort(IEnumerable<SpaceshipModel> ships, ShipFilterCriteria criteria)
    {
        if (criteria.SortColumn is not { } column)
        {
            return ships;
        }

        Func<SpaceshipModel, object> key = column switch
        {
            ShipSortColumn.Status => ship => Array.IndexOf(StatusOrder, ship.State),
            ShipSortColumn.Location => ship => GetLocationLabel(ship),
            ShipSortColumn.Store => ship => ship.IsRealPurchase,
            _ => ship => ship.Shipname
        };
        var comparer = Comparer<object>.Create((left, right) => left is string a && right is string b
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a, b)
            : Comparer<object>.Default.Compare(left, right));

        return criteria.SortDescending ? ships.OrderByDescending(key, comparer) : ships.OrderBy(key, comparer);
    }

    private enum ShipSortColumn
    {
        Model,
        Status,
        Location,
        Store
    }

    /// <summary>État des filtres au moment de la demande, lu par le calcul hors du thread d'UI.</summary>
    private sealed record ShipFilterCriteria(
        IReadOnlyList<SpaceshipModel> Ships,
        string? Needle,
        HashSet<string>? Statuses,
        HashSet<string>? Locations,
        ShipSortColumn? SortColumn,
        bool SortDescending);

    /// <summary>Résultat d'un filtrage : lignes à afficher et nombre de lignes par valeur de chaque filtre de colonne.</summary>
    private sealed record ShipFilterOutcome(
        IReadOnlyList<SpaceshipModel> Ships,
        IReadOnlyDictionary<string, int> StatusCounts,
        IReadOnlyDictionary<string, int> LocationCounts);
}
