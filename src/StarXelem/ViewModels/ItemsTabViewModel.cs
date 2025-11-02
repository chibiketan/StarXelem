using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;

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
        public Task<String?> Name { get; }
        public string Location { get; }
        [ObservableProperty]
        private bool _isSelected;

        public FilterLocationOptionModel(string location, Task<String?> name)
        {
            Name = name;
            Location = location;
            IsSelected = true;
        }
    }
    
    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    private readonly ILocationService _locationService;
    public override string Name => "Objets";
    public override string Icon => nameof(Symbol.Account);
    [ObservableProperty] public Task<IList<ItemViewModel>>? _itemList;
    [ObservableProperty] public ItemViewModel? _selectedItem;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private bool _useConnectedProfilAsOwner = true;
    [ObservableProperty] private bool _useUserInventoryList = true;
    [ObservableProperty] private bool _useTreeProjection = false;
    [ObservableProperty] private string _ownerId = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isInDebugMode = false;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private ObservableCollection<FilterTypeOption> _filterTypeList;
    [ObservableProperty] private ObservableCollection<EItemType> _selectedFilterTypes;

    [ObservableProperty] private ObservableCollection<FilterTypeOption> _searchTypeList;
    [ObservableProperty] private Task<IList<FilterLocationOptionModel>?>? _locationList = Task.FromResult<IList<FilterLocationOptionModel>?>(null);
    [ObservableProperty] private bool? _selectAllLocation = false;

    // Vue triée: éléments sélectionnés d'abord (A→Z), puis non sélectionnés (A→Z)
    public IEnumerable<FilterTypeOption> SearchTypeListSorted => _searchTypeList
        .OrderByDescending(o => o.IsSelected)
        .ThenBy(o => o.Type.ToString(), StringComparer.CurrentCultureIgnoreCase);
    public IEnumerable<FilterTypeOption> FilterTypeListSorted => _filterTypeList
        .OrderByDescending(o => o.IsSelected)
        .ThenBy(o => o.Type.ToString(), StringComparer.CurrentCultureIgnoreCase);

    // Sorting state for Name column
    [ObservableProperty] private bool _isNameSortAscending = true;
    [ObservableProperty] private string _nameSortLabel = "Trier par Nom A→Z";

    private Task<IList<ItemViewModel>>? _unfilteredItemList;

    public ItemsTabViewModel(IGrpcClientService clientService, IP4kService p4kService, ILocationService locationService)
    {
        _clientService = clientService;
        _p4KService = p4kService;
        _locationService = locationService;

        if (null != _p4KService.SelectedP4KFile)
        {
            _clientService.InitClient(_p4KService.SelectedP4KFile);
        }

        _p4KService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;
        _clientService.OnConnectedChanged += (sender, b) => loadItemListCommand?.NotifyCanExecuteChanged();
        // Initialize filter options for multi-select type filter
        _filterTypeList = new ObservableCollection<FilterTypeOption>(
            Enum.GetValues<EItemType>().Select(t =>
            {
                var opt = new FilterTypeOption(t);
                opt.PropertyChanged += OnFilterTypeOptionPropertyChanged;
                return opt;
            })
        );
        
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
        _filterTypeList.CollectionChanged += (_, __) => OnPropertyChanged(nameof(FilterTypeListSorted));

        
        // TODO for testing purpose
        SearchTypeList.First(t => t.Type == EItemType.Drink).IsSelected = true;
    }
    
    private void OnFilterTypeOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterTypeOption.IsSelected) && sender is FilterTypeOption opt)
        {

            // Keep SelectedFilterTypes in sync
            if (opt.IsSelected)
            {
                if (!SelectedFilterTypes.Contains(opt.Type))
                    SelectedFilterTypes.Add(opt.Type);
            }
            else
            {
                if (SelectedFilterTypes.Contains(opt.Type))
                    SelectedFilterTypes.Remove(opt.Type);
            }
            
            ApplyFilters();
            // Rafraîchir la vue triée quand une sélection change
            OnPropertyChanged(nameof(FilterTypeListSorted));
        }
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
        return _clientService.IsConnected && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        IsLoading = true;
        TreatmentStatus = "Appel RSI";
     
        var searchQuery = new ItemQueryModel();
        searchQuery.useConnectedUserOwner = _useConnectedProfilAsOwner;
        searchQuery.ownerId = _ownerId;
        searchQuery.Id = _id;
        searchQuery.UseProjection = UseTreeProjection;
        searchQuery.TypeList = SearchTypeList.Where(i => i.IsSelected).Select(i => i.Type).ToList();

        var inventoryList = (await LocationList ?? new List<FilterLocationOptionModel>())
            .Where(i => i.IsSelected)
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
        
        // récupérer les instances de vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        var result = new List<ItemViewModel>();
        foreach (var item in itemList)
        {
            var claddType = await _p4KService.GetEntityType(item.EntityNodeProperties.ClassGuidCrc);
            result.Add(new ItemViewModel(_p4KService, _locationService, this, item, claddType));
        }

        _unfilteredItemList = Task.FromResult<IList<ItemViewModel>>(result);
        ApplyFilters();
        IsLoading = false;
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
        return LocationList == null || LocationList.IsCompleted;
    }

    private void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, on change tout, reconnexion en prime
        _clientService.InitClient(e);
    }

    partial void OnNameFilterChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnLocationListChanged(Task<IList<FilterLocationOptionModel>?>? value)
    {
        ReloadLocationListCommand.NotifyCanExecuteChanged();
    }

    private async void ApplyFilters()
    {
        if (_unfilteredItemList == null)
            return;

        var items = await _unfilteredItemList;

        IEnumerable<ItemViewModel> filtered = items;

        // Filter by name if provided (case-insensitive)
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            filtered = filtered.Where(it =>
                !string.IsNullOrEmpty(it.Name.Result) &&
                it.Name.Result!.Contains(NameFilter, StringComparison.CurrentCultureIgnoreCase));
        }

        // Filter by selected types (multi-select). If none selected, do not filter by type
        if (SelectedFilterTypes is { Count: > 0 })
        {
            var selectedSet = SelectedFilterTypes.ToHashSet();
            filtered = filtered.Where(i => selectedSet.Contains(i.ItemType));
        }

        ItemList = Task.FromResult<IList<ItemViewModel>>(filtered.ToList());
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