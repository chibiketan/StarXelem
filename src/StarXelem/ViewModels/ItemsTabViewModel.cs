using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using DateTime = System.DateTime;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.ViewModels.Popup;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel : PageViewModelBase
{
    public partial class FilterTypeOption : ObservableObject
    {
        public EItemType Type { get; }
        [ObservableProperty]
        private bool _isSelected;

        public FilterTypeOption(EItemType type)
        {
            Type = type;
        }
    }

    public partial class FilterLocationOptionModel : ObservableObject
    {
        public Task<ResolvedLocation?> Name { get; }
        public string Location { get; }
        [ObservableProperty]
        private bool _isSelected;

        public FilterLocationOptionModel(string location, Task<ResolvedLocation?> name)
        {
            Name = name;
            Location = location;
            IsSelected = true;
        }
    }
    
    private readonly IGrpcClientService  _clientService;
    private readonly IScItemRepository _scItemRepository;
    private readonly ILocaleEntryRepository _localeEntryRepository;
    private readonly ILocationService _locationService;
    public override string Name => "Objets";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.PersonAccounts);
    [ObservableProperty] public Task<IList<ItemViewModel>>? _itemList;
    [ObservableProperty] public ItemViewModel? _selectedItem;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isLoading = false;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BusyMessage))] private string _treatmentStatus = "";
    /// <summary>Vrai du moment où un filtre est demandé jusqu'à ce que la liste correspondante soit entièrement affichée.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isFiltering;
    /// <summary>
    /// Lignes affichées par le tableau. Distincte de <see cref="ItemList"/>, qui porte le résultat complet du filtre
    /// pour l'export et la synchronisation : cette collection est alimentée par paquets pour ne pas figer l'UI.
    /// </summary>
    [ObservableProperty] private ObservableCollection<ItemViewModel> _displayedItems = new();
    [ObservableProperty] private bool _useConnectedProfilAsOwner = true;
    [ObservableProperty] private bool _useUserInventoryList = true;
    [ObservableProperty] private string _ownerId = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isInDebugMode = false;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private string _resultSummary = "Aucun objet chargé";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNotLoadedItems))] private bool _hasLoadedItems;
    [ObservableProperty] private bool _showNoMatch;
    [ObservableProperty] private bool _isResolvingLocations;
    [ObservableProperty] private int _resolvedLocationCount;
    [ObservableProperty] private int _locationResolutionTotal = 1;

    [ObservableProperty] private ObservableCollection<FilterTypeOption> _searchTypeList;
    [ObservableProperty] private Task<IList<FilterLocationOptionModel>?>? _locationList = Task.FromResult<IList<FilterLocationOptionModel>?>(null);
    [ObservableProperty] private bool? _selectAllLocation = false;

    // Vue triée: éléments sélectionnés d'abord (A→Z), puis non sélectionnés (A→Z)
    public IEnumerable<FilterTypeOption> SearchTypeListSorted => _searchTypeList
        .OrderByDescending(o => o.IsSelected)
        .ThenBy(o => o.Type.ToString(), StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Filtre par valeurs de la colonne Type, partagé avec les puces de type.</summary>
    public ColumnFilterViewModel TypeFilter { get; } = new();
    /// <summary>Filtre par valeurs de la colonne Emplacement.</summary>
    public ColumnFilterViewModel LocationFilter { get; } = new();
    public ColumnHeaderViewModel NameColumn { get; }
    public ColumnHeaderViewModel TypeColumn { get; }
    public ColumnHeaderViewModel LocationColumn { get; }
    public ColumnHeaderViewModel QuantityColumn { get; }
    public ColumnHeaderViewModel QualityColumn { get; }
    public ObservableCollection<FilterChipViewModel> TypeChips { get; } = new();
    public ObservableCollection<FilterChipViewModel> SubTypeChips { get; } = new();
    public bool HasNotLoadedItems => !HasLoadedItems;

    // Au-delà, les puces prendraient trop de place : les valeurs restantes sont accessibles par le filtre de colonne.
    private const int MaxChipCount = 8;
    private const int LocationResolutionParallelism = 16;
    private static readonly TimeSpan LocationRefreshInterval = TimeSpan.FromMilliseconds(1500);

    private readonly Dictionary<ColumnHeaderViewModel, ItemSortColumn> _sortColumns = new();
    private ColumnHeaderViewModel? _activeSortColumn;
    private EItemSubType? _selectedSubType;
    private List<string> _typesByFrequency = new();
    // Emplacements déjà résolus, par identifiant d'objet. Une ligne absente est encore en attente de résolution.
    private readonly ConcurrentDictionary<ulong, ResolvedLocation> _resolvedLocations = new();
    private CancellationTokenSource? _locationResolutionCts;
    private Task? _locationResolutionTask;

    private Task<IList<ItemViewModel>>? _unfilteredItemList;
    private CancellationTokenSource? _filterCts;

    // Délai d'inactivité de la saisie avant de filtrer : assez court pour rester réactif,
    // assez long pour qu'une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan NameFilterDelay = TimeSpan.FromMilliseconds(300);
    // Cases à cocher des types : délai réduit, suffisant pour absorber une série de clics rapprochés.
    private static readonly TimeSpan TypeFilterDelay = TimeSpan.FromMilliseconds(120);
    // Le tableau est alimenté par paquets : entre deux paquets, le thread d'UI traite la saisie et le rendu,
    // ce qui garde l'indicateur animé et la fenêtre réactive pendant la construction des lignes.
    private const int DisplayBatchSize = 20;

    private readonly ILogger<ItemsTabViewModel> _logger;

    public bool IsBusy => IsLoading || IsFiltering;
    public string BusyMessage => IsLoading
        ? (string.IsNullOrWhiteSpace(TreatmentStatus) ? "Chargement des objets…" : TreatmentStatus)
        : "Filtrage en cours…";

    public ItemsTabViewModel(IGrpcClientService clientService, IScItemRepository scItemRepository, ILocaleEntryRepository localeEntryRepository, ILocationService locationService, ILogger<ItemsTabViewModel> logger)
    {
        _logger = logger;
        _clientService = clientService;
        _scItemRepository = scItemRepository;
        _localeEntryRepository = localeEntryRepository;
        _locationService = locationService;

        _clientService.OnStatusChanged += (sender, status) => { OnConnectedStatusChanged(status); };
        NameColumn = CreateColumn("Nom", ItemSortColumn.Name);
        TypeColumn = CreateColumn("Type", ItemSortColumn.Type, TypeFilter);
        LocationColumn = CreateColumn("Emplacement", ItemSortColumn.Location, LocationFilter);
        QuantityColumn = CreateColumn("Qté", ItemSortColumn.Quantity);
        QualityColumn = CreateColumn("Qualité", ItemSortColumn.Quality);
        TypeFilter.SelectionChanged += (_, _) =>
        {
            // Le sous-type sélectionné n'a plus de sens une fois la famille de types modifiée.
            _selectedSubType = null;
            ScheduleFilters(TypeFilterDelay);
        };
        LocationFilter.SelectionChanged += (_, _) => ScheduleFilters(TypeFilterDelay);

        // Initialize options for multi-select type
        _searchTypeList = new ObservableCollection<FilterTypeOption>(
            Enum.GetValues<EItemType>().Select(t =>
            {
                var opt = new FilterTypeOption(t);
                opt.PropertyChanged += OnTypeOptionPropertyChanged;
                return opt;
            })
        );
        _searchTypeList.CollectionChanged += (_, __) => OnPropertyChanged(nameof(SearchTypeListSorted));

        
        // TODO for testing purpose
        //SearchTypeList.First(t => t.Type == EItemType.Drink).IsSelected = true;

        if (clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame)
        {
            ReloadLocationList();
        }
    }

    private void OnConnectedStatusChanged(GrpcConnectionStatus status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnConnectedStatusChanged(status));
            return;
        }
        ReloadLocationListCommand?.NotifyCanExecuteChanged();
        loadItemListCommand?.NotifyCanExecuteChanged();
        if (status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame)
        {
            ReloadLocationList();
        }
        else
        {
            // Perte de connexion, on efface la liste des conteneurs
            LocationList = Task.FromResult<IList<FilterLocationOptionModel>?>(new List<FilterLocationOptionModel>());
            refreshSelectAllLocation();
        }
    }

    private ColumnHeaderViewModel CreateColumn(string title, ItemSortColumn sortColumn, ColumnFilterViewModel? filter = null)
    {
        var column = new ColumnHeaderViewModel(title, OnSortRequested, filter);
        _sortColumns.Add(column, sortColumn);
        return column;
    }

    // Cycle d'un clic sur un titre : croissant, décroissant, puis retour à l'ordre d'origine.
    private void OnSortRequested(ColumnHeaderViewModel column)
    {
        var next = column.SortDirection switch
        {
            null => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => (ListSortDirection?)null
        };

        foreach (var header in _sortColumns.Keys)
        {
            header.SortDirection = null;
        }

        column.SortDirection = next;
        _activeSortColumn = next == null ? null : column;
        ScheduleFilters(TimeSpan.Zero);
    }

    private void ToggleTypeChip(string? key)
    {
        if (key == null)
        {
            TypeFilter.Reset();
        }
        else
        {
            TypeFilter.Toggle(key);
        }
    }

    private void ToggleSubTypeChip(string? key)
    {
        var requested = key == null ? (EItemSubType?)null : Enum.Parse<EItemSubType>(key);
        _selectedSubType = requested == _selectedSubType ? null : requested;
        ScheduleFilters(TypeFilterDelay);
    }

    private void OnTypeOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ResetSelectedTypesCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(FilterTypeOption.IsSelected))
        {
            // Rafraîchir la vue triée quand une sélection change
            OnPropertyChanged(nameof(SearchTypeListSorted));
        }
    }
    
    public bool CanLoadItemList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        IsLoading = true;
        _locationResolutionCts?.Cancel();
        _resolvedLocations.Clear();
        _locationService.ClearCache();
        TreatmentStatus = "Appel RSI";
     
        // Hors mode debug, on charge toujours tous les objets du compte connecté : les critères de recherche sont ignorés.
        var useSearchCriteria = IsInDebugMode;
        var searchQuery = new ItemQueryModel();
        searchQuery.useConnectedUserOwner = !useSearchCriteria || UseConnectedProfilAsOwner;
        searchQuery.ownerId = useSearchCriteria ? OwnerId : "";
        searchQuery.Id = useSearchCriteria ? Id : "";
        searchQuery.UseProjection = true;
        searchQuery.TypeList = useSearchCriteria
            ? SearchTypeList.Where(i => i.IsSelected).Select(i => i.Type).ToList()
            : new List<EItemType>();

        var inventoryList = (await LocationList ?? new List<FilterLocationOptionModel>())
            .Where(i => !useSearchCriteria || i.IsSelected)
            .Select(i => i.Location)
            .ToList();
        // Si on utilise la liste des conteneurs, on la charge et on désactive le filtrage par owner
        if (inventoryList.Count > 0)
        {
            //searchQuery.useConnectedUserOwner = false;
            searchQuery.ownerId = null;
            searchQuery.InventoryIdList = inventoryList;
        }
        
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement de la liste des objets");

        var itemList = await _clientService.QueryGraphBySearch(searchQuery);
        // La plupart des parents d'objets attachés sont dans ce résultat : autant de requêtes serveur en moins.
        _locationService.AddKnownEntities(itemList);
        
        // récupérer les instances de vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        var result = new List<ItemViewModel>();
        foreach (var item in itemList)
        {
            var scItem = await _scItemRepository.GetByCrc32Async(item.EntityNodeProperties.ClassGuidCrc);
            result.Add(new ItemViewModel(_localeEntryRepository, _locationService, _clientService, this, item, scItem));
        }

        ConfigureColumnFilters(result);

        _unfilteredItemList = Task.FromResult<IList<ItemViewModel>>(result);
        IsLoading = false;

        // Rattrape la liste filtrée complète avant de rendre la main : la commande reste "en cours"
        // (et le bouton désactivé) jusqu'à l'affichage complet des lignes.
        await ScheduleFilters(TimeSpan.Zero);

        // Les emplacements arrivent ensuite, en tâche de fond : le tableau est utilisable sans les attendre.
        var locationCts = new CancellationTokenSource();
        _locationResolutionCts = locationCts;
        _locationResolutionTask = ResolveLocationsAsync(result, locationCts.Token);
    }
    
    private async Task ResolveLocationsAsync(IReadOnlyList<ItemViewModel> items, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        LocationResolutionTotal = Math.Max(1, items.Count);
        ResolvedLocationCount = 0;
        IsResolvingLocations = true;

        try
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = LocationResolutionParallelism, CancellationToken = token };
            var worker = Task.Run(() => Parallel.ForEachAsync(items, options, async (item, cancellation) =>
            {
                var location = await GetLocationAsync(item).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                _resolvedLocations[item.Id] = location;
            }), token);

            while (!worker.IsCompleted && !token.IsCancellationRequested)
            {
                await Task.WhenAny(worker, Task.Delay(LocationRefreshInterval, token));
                if (!worker.IsCompleted && !token.IsCancellationRequested)
                {
                    await RefreshLocationResultsAsync(items, isComplete: false);
                }
            }

            await worker;
            await RefreshLocationResultsAsync(items, isComplete: true);
            _logger.LogInformation("{Count} emplacements résolus en {Elapsed}", items.Count, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Un nouveau chargement a remplacé celui-ci : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la résolution des emplacements");
        }
        finally
        {
            // Une résolution annulée a forcément une remplaçante, qui reste propriétaire de l'indicateur.
            if (!token.IsCancellationRequested)
            {
                IsResolvingLocations = false;
            }
        }
    }

    /// <summary>
    /// Publie les emplacements résolus jusqu'ici. Attend la fin de l'affichage, et cède la place à un filtrage en cours :
    /// un rafraîchissement ne doit jamais interrompre une construction du tableau qu'il devrait ensuite recommencer.
    /// </summary>
    private async Task RefreshLocationResultsAsync(IReadOnlyList<ItemViewModel> items, bool isComplete)
    {
        ResolvedLocationCount = _resolvedLocations.Count;
        LocationFilter.StatusText = isComplete ? null : $"Emplacements résolus : {ResolvedLocationCount} / {items.Count}";
        ApplyLocationDisplays(items);

        // Tant que la résolution continue, une valeur cochée doit rester proposée même si aucune ligne ne l'a encore.
        var options = BuildLocationOptions(_resolvedLocations.Values);
        if (!isComplete)
        {
            foreach (var key in LocationFilter.GetSelectedKeys() ?? new HashSet<string>())
            {
                options.TryAdd(key, LocationFilter.GetLabel(key));
            }
        }

        LocationFilter.SetOptions(options);

        if (isComplete || !IsFiltering)
        {
            await ScheduleFilters(TimeSpan.Zero, showBusyIndicator: false);
        }
    }

    private void ConfigureColumnFilters(IReadOnlyList<ItemViewModel> items)
    {
        TypeFilter.SetOptions(items
            .Select(item => item.ItemType)
            .Distinct()
            .Select(type => KeyValuePair.Create(type.ToString(), ItemTypeLabels.Get(type))));

        _typesByFrequency = items
            .GroupBy(item => item.ItemType)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key.ToString())
            .ToList();
    }

    private async Task<string> GetNameAsync(ItemViewModel item)
    {
        try
        {
            return await item.Name.ConfigureAwait(false) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de résoudre le nom de l'objet {Id}", item.Id);
            return string.Empty;
        }
    }

    // Deux vaisseaux du même modèle portent le même nom : seul l'identifiant les distingue dans le filtre.
    private static Dictionary<string, string> BuildLocationOptions(IEnumerable<ResolvedLocation> locations)
    {
        var distinct = locations.DistinctBy(location => location.Key).ToList();
        var homonyms = distinct
            .GroupBy(location => location.DisplayName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        return distinct.ToDictionary(
            location => location.Key,
            location => location.Id != null && homonyms.Contains(location.DisplayName)
                ? $"{location.DisplayName} ({location.Id})"
                : location.DisplayName);
    }

    private async Task<ResolvedLocation> GetLocationAsync(ItemViewModel item)
    {
        try
        {
            return await item.Location.ConfigureAwait(false) ?? ResolvedLocation.Unknown;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de résoudre l'emplacement de l'objet {Id}", item.Id);
            return ResolvedLocation.Unknown;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetSelectedTypes))]
    public void ResetSelectedTypes()
    {
        foreach (var filterTypeOption in SearchTypeList)
        {
            filterTypeOption.IsSelected = false;
        }
    }

    public bool CanResetSelectedTypes()
    {
        return SearchTypeList.Any(t => t.IsSelected);
    }

    [RelayCommand(CanExecute = nameof(CanReloadLocationList))]
    public void ReloadLocationList()
    {
        LocationList = LoadLocationList().ContinueWith((t) =>
        {
            Dispatcher.UIThread.InvokeAsync(() => ReloadLocationListCommand.NotifyCanExecuteChanged());
            Dispatcher.UIThread.InvokeAsync(() => ExportToExcelCommand.NotifyCanExecuteChanged());
            Dispatcher.UIThread.InvokeAsync(() => CompareDataFromImportCommand.NotifyCanExecuteChanged());
        
            return t.Result;
        })!;
        refreshSelectAllLocation();
    }

    private async Task<IList<FilterLocationOptionModel>> LoadLocationList()
    {
        var locations = await _clientService.QueryInventories();

        return locations.Select(l =>
        {
            var filterModel = new FilterLocationOptionModel(l.Id, _locationService.ResolveEntityLocation(l.Name));
            
            filterModel.PropertyChanged += (_, __) => refreshSelectAllLocation();
            return filterModel;
        }).ToList();
    }

    public bool CanReloadLocationList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && (LocationList == null || LocationList.IsCompleted);
    }

    partial void OnNameFilterChanged(string value) => ScheduleFilters(NameFilterDelay);

    partial void OnLocationListChanged(Task<IList<FilterLocationOptionModel>?>? value)
    {
        ReloadLocationListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Planifie un filtrage différé : toute demande encore en attente ou en cours est annulée,
    /// de sorte qu'une rafale de modifications ne produit qu'un seul recalcul, après la dernière.
    /// Le calcul s'exécute hors du thread d'UI, et l'affichage du résultat se fait par paquets.
    /// La tâche retournée se termine quand la liste est entièrement affichée, ou dès que la demande
    /// est remplacée par une plus récente. À appeler depuis le thread d'UI.
    /// </summary>
    private Task ScheduleFilters(TimeSpan delay, bool showBusyIndicator = true)
    {
        var unfiltered = _unfilteredItemList;
        if (unfiltered == null)
        {
            return Task.CompletedTask;
        }

        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        // Copie de l'état des filtres : le calcul qui suit ne doit lire aucune propriété du ViewModel.
        var criteria = new ItemFilterCriteria(
            Needle: string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
            Types: TypeFilter.GetSelectedKeys(),
            SubType: _selectedSubType,
            Locations: LocationFilter.GetSelectedKeys(),
            SortColumn: _activeSortColumn == null ? null : _sortColumns[_activeSortColumn],
            SortDescending: _activeSortColumn?.IsSortedDescending ?? false);

        if (showBusyIndicator)
        {
            UpdateIsFiltering(true);
        }

        return RunFilterAsync(unfiltered, criteria, delay, cts.Token);
    }

    private async Task RunFilterAsync(Task<IList<ItemViewModel>> unfiltered, ItemFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var items = await unfiltered;
            var outcome = await Task.Run(() => ComputeFilterOutcomeAsync(items, criteria, token), token);

            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage de la liste des objets");
        }
        finally
        {
            // Une demande annulée a forcément une remplaçante, qui reste propriétaire de l'indicateur.
            if (!token.IsCancellationRequested)
            {
                UpdateIsFiltering(false);
            }
        }
    }

    /// <summary>
    /// Calcule les lignes à afficher et, pour chaque filtre, le nombre de lignes de chaque valeur en tenant compte des
    /// autres filtres seulement : une liste de valeurs ne se réduit pas sous l'effet de ses propres coches.
    /// </summary>
    private async Task<ItemFilterOutcome> ComputeFilterOutcomeAsync(IList<ItemViewModel> items, ItemFilterCriteria criteria, CancellationToken token)
    {
        // Le nom passe par la base locale : on ne le résout que si le filtre ou le tri en a besoin.
        var needsName = criteria.Needle != null || criteria.SortColumn == ItemSortColumn.Name;
        var rows = new List<ItemFilterRow>(items.Count);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            var name = needsName ? await GetNameAsync(item).ConfigureAwait(false) : string.Empty;
            rows.Add(new ItemFilterRow(item, name, _resolvedLocations.GetValueOrDefault(item.Id)));
        }

        var matchingText = rows.Where(row => MatchesNeedle(row, criteria.Needle)).ToList();
        bool InTypes(ItemFilterRow row) => criteria.Types == null || criteria.Types.Contains(row.Item.ItemType.ToString());
        bool InSubType(ItemFilterRow row) => criteria.SubType == null || row.Item.ItemSubType == criteria.SubType;
        bool InLocations(ItemFilterRow row) => criteria.Locations == null || (row.Location != null && criteria.Locations.Contains(row.Location.Key));

        var typeBase = matchingText.Where(InLocations).ToList();
        var subTypeBase = typeBase.Where(InTypes).ToList();
        var locationBase = matchingText.Where(row => InTypes(row) && InSubType(row)).ToList();
        var displayed = SortRows(subTypeBase.Where(InSubType), criteria).Select(row => row.Item).ToList();

        return new ItemFilterOutcome(
            displayed,
            items.Count,
            CountByKey(typeBase, row => row.Item.ItemType.ToString()),
            CountByKey(subTypeBase, row => row.Item.ItemSubType.ToString()),
            CountByKey(locationBase.Where(row => row.Location != null), row => row.Location!.Key));
    }

    private static bool MatchesNeedle(ItemFilterRow row, string? needle)
    {
        if (needle == null)
        {
            return true;
        }

        return row.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || row.Item.ItemTypeLabel.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || row.Item.ItemSubType.ToString().Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || (row.Location != null && row.Location.DisplayName.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
    }

    private static Dictionary<string, int> CountByKey(IEnumerable<ItemFilterRow> rows, Func<ItemFilterRow, string> keySelector)
    {
        return rows.CountBy(keySelector).ToDictionary(count => count.Key, count => count.Value);
    }

    private static IEnumerable<ItemFilterRow> SortRows(IEnumerable<ItemFilterRow> rows, ItemFilterCriteria criteria)
    {
        return criteria.SortColumn switch
        {
            ItemSortColumn.Name => SortByText(rows, row => row.Name, criteria.SortDescending),
            ItemSortColumn.Type => SortByText(rows, row => row.Item.ItemTypeLabel, criteria.SortDescending),
            ItemSortColumn.Location => SortByLocation(rows, criteria.SortDescending),
            ItemSortColumn.Quantity => SortByNumber(rows, row => row.Item.StackSize, criteria.SortDescending),
            ItemSortColumn.Quality => SortByNumber(rows, row => (uint?)row.Item.ContentQuality, criteria.SortDescending),
            _ => rows
        };
    }

    private static IEnumerable<ItemFilterRow> SortByText(IEnumerable<ItemFilterRow> rows, Func<ItemFilterRow, string> key, bool descending)
    {
        return descending
            ? rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);
    }

    private static IEnumerable<ItemFilterRow> SortByLocation(IEnumerable<ItemFilterRow> rows, bool descending)
    {
        var pendingLast = rows.OrderBy(row => row.Location == null);
        return descending
            ? pendingLast.ThenByDescending(row => row.Location?.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : pendingLast.ThenBy(row => row.Location?.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    // Une ligne sans valeur reste en fin de tableau, quel que soit le sens du tri.
    private static IEnumerable<ItemFilterRow> SortByNumber(IEnumerable<ItemFilterRow> rows, Func<ItemFilterRow, uint?> key, bool descending)
    {
        var withoutValueLast = rows.OrderBy(row => key(row) == null);
        return descending ? withoutValueLast.ThenByDescending(key) : withoutValueLast.ThenBy(key);
    }

    private void ApplyFilterOutcome(ItemFilterOutcome outcome)
    {
        TypeFilter.UpdateCounts(outcome.TypeCounts);
        LocationFilter.UpdateCounts(outcome.LocationCounts);
        SyncChips(TypeChips, BuildTypeChipStates(outcome.TypeCounts), ToggleTypeChip);
        SyncChips(SubTypeChips, BuildSubTypeChipStates(outcome.SubTypeCounts), ToggleSubTypeChip);

        HasLoadedItems = outcome.TotalCount > 0;
        ShowNoMatch = HasLoadedItems && outcome.Items.Count == 0;
        ResultSummary = $"{outcome.Items.Count} sur {outcome.TotalCount} éléments";
    }

    // Les puces suivent la fréquence au chargement : elles ne changent pas de place pendant que l'utilisateur filtre.
    private List<ChipState> BuildTypeChipStates(IReadOnlyDictionary<string, int> counts)
    {
        var keys = _typesByFrequency
            .Where((key, index) => index < MaxChipCount || TypeFilter.IsSelected(key))
            .ToList();
        var states = new List<ChipState> { new(null, "Tout", counts.Values.Sum(), !TypeFilter.HasSelection) };
        states.AddRange(keys.Select(key => new ChipState(
            key,
            ItemTypeLabels.Get(Enum.Parse<EItemType>(key)),
            counts.GetValueOrDefault(key),
            TypeFilter.IsSelected(key))));
        return states;
    }

    private List<ChipState> BuildSubTypeChipStates(IReadOnlyDictionary<string, int> counts)
    {
        var selectedKey = _selectedSubType?.ToString();
        var keys = counts
            .OrderByDescending(count => count.Value)
            .ThenBy(count => count.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(count => count.Key)
            .Take(MaxChipCount)
            .ToList();
        if (selectedKey != null && !keys.Contains(selectedKey))
        {
            keys.Add(selectedKey);
        }

        var states = new List<ChipState> { new(null, "Tous", counts.Values.Sum(), _selectedSubType == null) };
        states.AddRange(keys.Select(key => new ChipState(key, key, counts.GetValueOrDefault(key), key == selectedKey)));
        return states;
    }

    // Quand les puces sont les mêmes, on les met à jour sur place pour ne pas perdre le focus clavier.
    private static void SyncChips(ObservableCollection<FilterChipViewModel> chips, IReadOnlyList<ChipState> states, Action<string?> toggle)
    {
        if (!chips.Select(chip => chip.Key).SequenceEqual(states.Select(state => state.Key)))
        {
            chips.Clear();
            foreach (var state in states)
            {
                chips.Add(new FilterChipViewModel(state.Key, state.Label, toggle));
            }
        }

        for (var index = 0; index < states.Count; index++)
        {
            chips[index].Count = states[index].Count;
            chips[index].IsSelected = states[index].IsSelected;
        }
    }

    /// <summary>
    /// Publie le résultat complet dans <see cref="ItemList"/>, puis l'affiche en alimentant une nouvelle collection
    /// par paquets : construire toutes les lignes d'un coup bloquerait le thread d'UI. S'interrompt dès que la demande
    /// est annulée.
    /// </summary>
    private async Task DisplayResultAsync(ItemFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        ApplyFilterOutcome(outcome);

        var result = outcome.Items;
        ItemList = Task.FromResult<IList<ItemViewModel>>(result);

        // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (DisplayedItems.SequenceEqual(result))
        {
            return;
        }

        // Le tableau perd sa sélection quand sa collection est remplacée : on la rétablit si l'objet reste affiché.
        var selectedItem = SelectedItem;
        var target = new ObservableCollection<ItemViewModel>();
        DisplayedItems = target;

        for (var index = 0; index < result.Count; index += DisplayBatchSize)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var item in result.Skip(index).Take(DisplayBatchSize))
            {
                target.Add(item);
            }

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        if (selectedItem != null && result.Contains(selectedItem))
        {
            SelectedItem = selectedItem;
        }
    }

    // Écrit depuis RunFilterAsync, qui peut reprendre hors du thread d'UI : IsFiltering ne doit y être écrit que là.
    private void UpdateIsFiltering(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsFiltering = value;
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateIsFiltering(value), DispatcherPriority.MaxValue);
        }
    }

    private enum ItemSortColumn
    {
        Name,
        Type,
        Location,
        Quantity,
        Quality
    }

    private sealed record ItemFilterCriteria(
        string? Needle,
        HashSet<string>? Types,
        EItemSubType? SubType,
        HashSet<string>? Locations,
        ItemSortColumn? SortColumn,
        bool SortDescending);

    private sealed record ItemFilterRow(ItemViewModel Item, string Name, ResolvedLocation? Location);

    private sealed record ItemFilterOutcome(
        List<ItemViewModel> Items,
        int TotalCount,
        Dictionary<string, int> TypeCounts,
        Dictionary<string, int> SubTypeCounts,
        Dictionary<string, int> LocationCounts);

    private sealed record ChipState(string? Key, string Label, int Count, bool IsSelected);

    // Active/désactive le bouton d'export lorsqu'on change la liste
    partial void OnItemListChanged(Task<IList<ItemViewModel>>? value)
    {
        ExportToExcelCommand.NotifyCanExecuteChanged();
        CompareDataFromImportCommand.NotifyCanExecuteChanged();
        SendItemlistToOrbitalAllianceCommand.NotifyCanExecuteChanged();
    }

    public bool CanSendItemlistToOrbitalAlliance()
    {
        try
        {
            return _unfilteredItemList?.IsCompletedSuccessfully == true && _unfilteredItemList.Result.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendItemlistToOrbitalAlliance))]
    private async Task SendItemlistToOrbitalAlliance()
    {
        OpenItemsSyncPopup();
    }

    private void OpenItemsSyncPopup()
    {
        var itemsToSend = _unfilteredItemList?.Result.Select(i => new ItemSyncItem
        {
            Geid = i.Id,
            ClassGuidCrc = i.ClassGuidCrc,
            OwnerId = i.OwnerId,
            ParentUrn = i.ParentUrn ?? "",
            ItemType = (int)i.ItemType,
            ItemSubType = (int)i.ItemSubType,
            StowedIn = i.Edge?.End.InventoryId ??
                       (i.Edge?.End.EntityId != null ? $"{i.Edge?.End.EntityId}:Container:0" : null) 
        }).ToList();

        if (itemsToSend == null || itemsToSend.Count == 0) return;

        var vm = App.Current.Services.GetRequiredService<ItemsSyncPopupContentViewModel>();
        vm.ItemsToSend = itemsToSend;
        WeakReferenceMessenger.Default.Send(new ShowPopupMessage(showCloseButton: true, viewModel: vm));
    }

    public bool CanExportToExcel()
    {
        try
        {
            return !IsLoading && ItemList is { IsCompletedSuccessfully: true, Result.Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportToExcel))]
    public async Task ExportToExcel()
    {
        var vm = App.Current.Services.GetRequiredService<LoadingPopupContentViewModel>();
        vm.ShowLoading = true;
        vm.Message = "Export en cours...";
        WeakReferenceMessenger.Default.Send(new ShowPopupMessage(showCloseButton:false, viewModel: vm));
        try
        {
            var items = ItemList != null ? await ItemList : new List<ItemViewModel>();

            // En-têtes alignés avec les colonnes de la DataGrid
            string[] headers =
            [
                "id",
                "Possesseur",
                "Nom",
                "Type technique",
                "parentUrn",
                "Stockage",
                "Nombre élément",
                "Taille occupée",
                "Possède une entrée Edge",
                "type",
                "sous-type",
                "Location id",
                "Location stockage",
                "location stockage shard",
                "EDGE Type",
                "EDGE Location id",
                "EDGE AttachmentType"
            ];

            // Préparer la boîte de dialogue "Enregistrer sous"
            var suggestedName = $"Items_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string? selectedPath = null;

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var storageProvider = lifetime?.MainWindow?.StorageProvider;

            if (storageProvider != null)
            {
                var fileType = new FilePickerFileType("Classeur Excel")
                {
                    Patterns = new[] { "*.xlsx" },
                    AppleUniformTypeIdentifiers = new[] { "org.openxmlformats.spreadsheetml.sheet" },
                    MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
                };

                var saveResult = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Exporter la liste des objets",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = new List<FilePickerFileType> { fileType }
                });

                if (saveResult == null)
                {
                    TreatmentStatus = "Export annulé";
                    return;
                }

                // Selon la plateforme, TryGetLocalPath peut être null (ex: sandboxed). On utilisera le stream alors.
                selectedPath = saveResult.TryGetLocalPath();

                using (var stream = await saveResult.OpenWriteAsync())
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var ws = workbook.AddWorksheet("Items");
                        // Write headers
                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = ws.Cell(1, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(242, 242, 242);
                        }

                        // Write rows
                        var rowIndex = 2;
                        foreach (var it in items)
                        {
                            var name = await it.Name ?? string.Empty;
                            var storage = (await it.Location)?.DisplayName ?? string.Empty;
                            var owner = await it.Owner;

                            ws.Cell(rowIndex, 1).Value = it.Id.ToString();
                            ws.Cell(rowIndex, 2).Value = owner;
                            ws.Cell(rowIndex, 3).Value = name;
                            ws.Cell(rowIndex, 4).Value = it.LocalTypeName ?? string.Empty;
                            ws.Cell(rowIndex, 5).Value = it.ParentUrn ?? string.Empty;
                            ws.Cell(rowIndex, 6).Value = storage;
                            ws.Cell(rowIndex, 7).Value = it.StackSize?.ToString() ?? string.Empty;
                            ws.Cell(rowIndex, 8).Value = it.EdgeOccupancy?.ToString() ?? string.Empty;
                            ws.Cell(rowIndex, 9).Value = (it.Edge != null).ToString();
                            ws.Cell(rowIndex, 10).Value = it.ItemType.ToString();
                            ws.Cell(rowIndex, 11).Value = it.ItemSubType.ToString();
                            ws.Cell(rowIndex, 12).Value = it.LocationId.ToString();
                            ws.Cell(rowIndex, 13).Value = it.StowLocation ?? string.Empty;
                            ws.Cell(rowIndex, 14).Value = it.StowShard ?? string.Empty;
                            ws.Cell(rowIndex, 15).Value = it.EdgeType?.ToString() ?? string.Empty;
                            ws.Cell(rowIndex, 16).Value = it.EdgeLocation ?? string.Empty;
                            ws.Cell(rowIndex, 17).Value = it.EdgeAttachmentType?.ToString() ?? string.Empty;

                            rowIndex++;
                        }

                        // Format: auto filter + auto fit columns
                        var range = ws.Range(1, 1, Math.Max(1, ws.LastRowUsed()?.RowNumber() ?? 1), headers.Length);
                        range.SetAutoFilter();
                        ws.Columns(1, headers.Length).AdjustToContents();

                        workbook.SaveAs(stream);
                    }
                }

                TreatmentStatus = selectedPath != null ? $"Exporté: {selectedPath}" : "Export terminé";
                return;
            }

            // Fallback: enregistrer automatiquement dans Documents si StorageProvider indisponible (rare)
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path = Path.Combine(documents, suggestedName);

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.AddWorksheet("Items");
                // Write headers
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(242, 242, 242);
                }

                // Write rows
                var rowIndex = 2;
                foreach (var it in items)
                {
                    var name = await it.Name ?? string.Empty;
                    var storage = (await it.Location)?.DisplayName ?? string.Empty;
                    var owner = await it.Owner;

                    ws.Cell(rowIndex, 1).Value = it.Id.ToString();
                    ws.Cell(rowIndex, 2).Value = owner;
                    ws.Cell(rowIndex, 3).Value = name;
                    ws.Cell(rowIndex, 4).Value = it.LocalTypeName ?? string.Empty;
                    ws.Cell(rowIndex, 5).Value = it.ParentUrn ?? string.Empty;
                    ws.Cell(rowIndex, 6).Value = storage;
                    ws.Cell(rowIndex, 7).Value = it.StackSize?.ToString() ?? string.Empty;
                    ws.Cell(rowIndex, 8).Value = it.EdgeOccupancy?.ToString() ?? string.Empty;
                    ws.Cell(rowIndex, 9).Value = (it.Edge != null).ToString();
                    ws.Cell(rowIndex, 10).Value = it.ItemType.ToString();
                    ws.Cell(rowIndex, 11).Value = it.ItemSubType.ToString();
                    ws.Cell(rowIndex, 12).Value = it.LocationId.ToString();
                    ws.Cell(rowIndex, 13).Value = it.StowLocation ?? string.Empty;
                    ws.Cell(rowIndex, 14).Value = it.StowShard ?? string.Empty;
                    ws.Cell(rowIndex, 15).Value = it.EdgeType?.ToString() ?? string.Empty;
                    ws.Cell(rowIndex, 16).Value = it.EdgeLocation ?? string.Empty;
                    ws.Cell(rowIndex, 17).Value = it.EdgeAttachmentType?.ToString() ?? string.Empty;

                    rowIndex++;
                }

                // Format: auto filter + auto fit columns
                var range = ws.Range(1, 1, Math.Max(1, ws.LastRowUsed()?.RowNumber() ?? 1), headers.Length);
                range.SetAutoFilter();
                ws.Columns(1, headers.Length).AdjustToContents();

                workbook.SaveAs(path);
            }

            TreatmentStatus = $"Exporté: {path}";
        }
        catch (Exception ex)
        {
            TreatmentStatus = $"Erreur export: {ex.Message}";
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompareDataFromImport))]
    public async Task CompareDataFromImportAsync()
    {
        var vm = App.Current.Services.GetRequiredService<ItemComparisonPopupContentViewModel>();
        //vm.ShowLoading = false;
        //vm.Message = "Ceci est un test";
        vm.Target = new List<ItemViewModel>(await ItemList);
        WeakReferenceMessenger.Default.Send(new ShowPopupMessage(showCloseButton:true, viewModel: vm));
    }

    public bool CanCompareDataFromImport()
    {
        try
        {
            return !IsLoading && ItemList is { IsCompletedSuccessfully: true, Result.Count: > 0 };
        }
        catch
        {
            return false;
        }

    }

    private async void refreshSelectAllLocation()
    {
        if (null == LocationList || null == await LocationList || (await _locationList).Count == 0)
        {
            _selectAllLocation = false;
        }
        else
        {
            var allFound = false;
            var anyFound = (await LocationList).Any(s => s.IsSelected);

            if (anyFound)
            {
                allFound = (await LocationList).All(s => s.IsSelected);
            }

            if (allFound)
            {
                _selectAllLocation = true;
            }
            else if (anyFound)
            {
                _selectAllLocation = null;
            }
            else
            {
                _selectAllLocation = false;
            }
        }
        
        OnPropertyChanged(nameof(SelectAllLocation));
    }

    partial void OnSelectAllLocationChanged(bool? oldValue, bool? newValue)
    {
        if (null == LocationList || null == LocationList.Result || LocationList.Result.Count == 0)
        {
            _selectAllLocation = false;
            return;
        }
        
        if (!oldValue.HasValue || oldValue.Value == false)
        {
            // go to true
            _selectAllLocation = true;
            foreach (var filterLocationOptionModel in LocationList.Result)
            {
                filterLocationOptionModel.IsSelected = true;
            }
        }
        
        if (oldValue.HasValue && oldValue == true)
        {
            // go to false
            _selectAllLocation = false;
            foreach (var filterLocationOptionModel in LocationList.Result)
            {
                filterLocationOptionModel.IsSelected = false;
            }
        }
    }
}