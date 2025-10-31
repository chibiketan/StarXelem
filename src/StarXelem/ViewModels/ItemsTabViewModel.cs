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
    [ObservableProperty] private string _ownerId = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isInDebugMode = false;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private ObservableCollection<FilterTypeOption> _filterTypeList = new ObservableCollection<FilterTypeOption>();
    [ObservableProperty] private ObservableCollection<EItemType> _selectedFilterTypes = new ObservableCollection<EItemType>();

    [ObservableProperty] private ObservableCollection<FilterTypeOption> _availableTypeList2 = new ObservableCollection<FilterTypeOption>();

    
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
        _availableTypeList2 = new ObservableCollection<FilterTypeOption>(
            Enum.GetValues<EItemType>().Select(t =>
            {
                var opt = new FilterTypeOption(t);
                opt.PropertyChanged += OnTypeOptionPropertyChanged;
                return opt;
            })
        );
        
        // TODO for testing purpose
        AvailableTypeList2.First(t => t.Type == EItemType.Drink).IsSelected = true;
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
        }
    }

    private void OnTypeOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ResetSelectedTypesCommand.NotifyCanExecuteChanged();
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
        // TODO load item list

        var searchQuery = new ItemQueryModel();
        searchQuery.useConnectedUserOwner = _useConnectedProfilAsOwner;
        searchQuery.ownerId = _ownerId;
        searchQuery.Id = _id;
        searchQuery.TypeList = AvailableTypeList2.Where(i => i.IsSelected).Select(i => i.Type).ToList();
        
        // Si on utilise la liste des conteneurs, on la charge et on désactive le filtrage par owner
        if (UseUserInventoryList)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement de la liste des conteneurs");
            var containerList = await _clientService.QueryInventories();
            
            searchQuery.useConnectedUserOwner = false;
            searchQuery.ownerId = null;
            searchQuery.InventoryIdList = new List<string>(containerList.Select(i => i.Id));
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
        foreach (var filterTypeOption in AvailableTypeList2)
        {
            filterTypeOption.IsSelected = false;
        }
    }

    public bool CanResetSelectedTypes()
    {
        return AvailableTypeList2.Any(t => t.IsSelected);
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

    [RelayCommand]
    private void ToggleNameSort()
    {
        IsNameSortAscending = !IsNameSortAscending;
        NameSortLabel = IsNameSortAscending ? "Trier par Nom A→Z" : "Trier par Nom Z→A";
        ApplyFilters();
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
}