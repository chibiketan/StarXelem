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
    [ObservableProperty] private ObservableCollection<EItemType> _availableTypeList;
    [ObservableProperty] private ObservableCollection<EItemType> _selectedTypeList = new ObservableCollection<EItemType>();
    [ObservableProperty] private EItemType? _selectedAvailableType;
    [ObservableProperty] private EItemType? _selectedSelectedType;
    [ObservableProperty] private bool _isInDebugMode = false;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private ObservableCollection<FilterTypeOption> _filterTypeList = new ObservableCollection<FilterTypeOption>();
    [ObservableProperty] private ObservableCollection<EItemType> _selectedFilterTypes = new ObservableCollection<EItemType>();

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
        _availableTypeList = new ObservableCollection<EItemType>(Enum.GetValues<EItemType>());
        // Initialize filter options for multi-select type filter
        _filterTypeList = new ObservableCollection<FilterTypeOption>(
            Enum.GetValues<EItemType>().Select(t =>
            {
                var opt = new FilterTypeOption(t);
                opt.PropertyChanged += OnFilterTypeOptionPropertyChanged;
                return opt;
            })
        );
        // TODO remove debug only
        _selectedAvailableType = EItemType.RemovableChip;
        //_selectedAvailableType = EItemType.Char_Armor_Helmet;
        AvailableToSelectedType();
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
        searchQuery.TypeList = _selectedTypeList.ToList();
        
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
        // TODO load item infos from game files
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement p4k");
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement traduction");
        // var globalEntry = _p4KService.P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
        // Dictionary<string, string> lang = new Dictionary<string, string>(500);
        // string iniFile;
        // using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
        // {
        //     while (await sr.ReadLineAsync() is { } line) {
        //
        //         if (!String.IsNullOrEmpty(line))
        //         {
        //             var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        //             var key = parts[0];
        //             var value = parts[1];
        //             
        //             if (key.EndsWith(",P"))
        //                 key = key[..^2];
        //             lang.Add($"@{key}", value);
        //         }
        //     }
        // }
        // await globalEntry.DisposeAsync();
        
        // Chargement des informations de classes sur les vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement classes des vaisseaux");
        // var entry = _p4KService.P4KFileSystem.OpenRead(dataCorePath);
        // var dcb = new DataCoreDatabase(entry);
        // var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        // await entry.DisposeAsync();
        //
        // foreach (var spaceship in spaceships)
        // {
        //     var record = df.GetFromRecord(new CigGuid(spaceship.Entitlement.EntityClassGuid));
        //
        //     if (null != record)
        //     {
        //         spaceship.EntityClassDefinition = record.Data as EntityClassDefinition;
        //         var toto = (record.Data as EntityClassDefinition).Components.FirstOrDefault(t => t is SCItemPurchasableParams) as SCItemPurchasableParams;
        //
        //         if (null != toto)
        //         {
        //             try
        //             {
        //                 spaceship.Shipname = lang[toto.displayName];
        //             }
        //             catch (Exception)
        //             {
        //                 // Ignore for now
        //             }
        //         }
        //     }
        // }
        
        // récupérer les instances de vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Récupération des instances de vaisseaux");
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

    public bool CanLoadItemList()
    {
        return _clientService.IsConnected && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanAvailableToSelectedType))]
    public void AvailableToSelectedType()
    {
        if (null == SelectedAvailableType)
            // On ne fait rien si rien de sélectionné
            return;

        SelectedTypeList.Add(SelectedAvailableType.Value);
        AvailableTypeList.Remove(SelectedAvailableType.Value);
        SelectedAvailableType = null;
    }

    public bool CanAvailableToSelectedType()
    {
        return null != SelectedAvailableType;
    }

    [RelayCommand(CanExecute = nameof(CanSelectedToAvailableType))]
    public void SelectedToAvailableType()
    {
        if (null == SelectedSelectedType)
            // On ne fait rien si rien de sélectionné
            return;

        AvailableTypeList.Add(SelectedSelectedType.Value);
        SelectedTypeList.Remove(SelectedSelectedType.Value);
        SelectedSelectedType = null;
    }

    public bool CanSelectedToAvailableType()
    {
        return null != SelectedSelectedType;
    }

    private void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, on change tout, reconnexion en prime
        LoadItemListCommand.NotifyCanExecuteChanged();
        _clientService.InitClient(e);
    }

    partial void OnSelectedAvailableTypeChanged(EItemType? value)
    {
        AvailableToSelectedTypeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSelectedTypeChanged(EItemType? value)
    {
        SelectedToAvailableTypeCommand.NotifyCanExecuteChanged();
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