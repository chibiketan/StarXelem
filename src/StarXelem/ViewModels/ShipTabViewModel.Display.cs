using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StarXelem.Extensions;
using StarXelem.Models;

namespace StarXelem.ViewModels;

/// <summary>
/// Affichage de l'écran Mon hangar : recherche, puces de statut, tri et filtres par colonne.
/// </summary>
public partial class ShipTabViewModel
{
    private const string NoLocationKey = "";
    private const string NoLocationLabel = "—";

    private static readonly SpaceshipState[] StatusOrder =
        [SpaceshipState.STOWED, SpaceshipState.UNSTOWED, SpaceshipState.UNCLAIMED, SpaceshipState.DESTROYED, SpaceshipState.UNKNOWN];

    private IReadOnlyList<SpaceshipModel> _allShips = Array.Empty<SpaceshipModel>();
    private ColumnHeaderViewModel? _sortColumn;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))]
    private IReadOnlyList<SpaceshipModel> _displayedShips = Array.Empty<SpaceshipModel>();

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

    /// <summary>Nombre de vaisseaux affichés sur le total.</summary>
    public string ResultSummary => HasLoadedShips ? $"{DisplayedShips.Count} sur {_allShips.Count} vaisseaux" : string.Empty;

    /// <summary>Vrai une fois la liste des vaisseaux chargée.</summary>
    public bool HasLoadedShips => Spaceships is { IsCompletedSuccessfully: true };

    /// <summary>Vrai tant qu'aucun chargement n'a eu lieu.</summary>
    public bool HasNotLoadedShips => !HasLoadedShips && !IsLoading;

    /// <summary>Vrai si les filtres ne laissent aucun vaisseau.</summary>
    public bool ShowNoMatch => HasLoadedShips && _allShips.Count > 0 && DisplayedShips.Count == 0;

    private void InitializeDisplay()
    {
        var statusFilter = new ColumnFilterViewModel();
        statusFilter.SelectionChanged += (_, _) => ApplyFilters();
        var locationFilter = new ColumnFilterViewModel();
        locationFilter.SelectionChanged += (_, _) => ApplyFilters();

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
        ApplyFilters();
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNotLoadedShips));

    partial void OnSearchTextChanged(string value) => ApplyFilters();

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
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = SearchText.Trim();
        var searched = _allShips.Where(ship => search.Length == 0 || MatchesSearch(ship, search)).ToList();

        // Les compteurs d'une colonne tiennent compte de la recherche et des autres colonnes, pas de la sienne.
        var statusKeys = StatusColumn.Filter!.GetSelectedKeys();
        var locationKeys = LocationColumn.Filter!.GetSelectedKeys();
        bool MatchesStatus(SpaceshipModel ship) => statusKeys == null || statusKeys.Contains(ship.State.ToString());
        bool MatchesLocation(SpaceshipModel ship) => locationKeys == null || locationKeys.Contains(GetLocationKey(ship));

        var statusCounts = searched.Where(MatchesLocation)
            .GroupBy(ship => ship.State.ToString())
            .ToDictionary(group => group.Key, group => group.Count());
        StatusColumn.Filter.UpdateCounts(statusCounts);
        LocationColumn.Filter.UpdateCounts(searched.Where(MatchesStatus)
            .GroupBy(GetLocationKey)
            .ToDictionary(group => group.Key, group => group.Count()));

        RefreshStatusChips(statusCounts);
        DisplayedShips = Sort(searched.Where(ship => MatchesStatus(ship) && MatchesLocation(ship))).ToList();
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

    private IEnumerable<SpaceshipModel> Sort(IEnumerable<SpaceshipModel> ships)
    {
        if (_sortColumn?.SortDirection is not { } direction)
        {
            return ships;
        }

        Func<SpaceshipModel, object> key = _sortColumn switch
        {
            _ when _sortColumn == StatusColumn => ship => Array.IndexOf(StatusOrder, ship.State),
            _ when _sortColumn == LocationColumn => ship => GetLocationLabel(ship),
            _ when _sortColumn == StoreColumn => ship => ship.IsRealPurchase,
            _ => ship => ship.Shipname
        };
        var comparer = Comparer<object>.Create((left, right) => left is string a && right is string b
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a, b)
            : Comparer<object>.Default.Compare(left, right));

        return direction == ListSortDirection.Ascending ? ships.OrderBy(key, comparer) : ships.OrderByDescending(key, comparer);
    }
}
