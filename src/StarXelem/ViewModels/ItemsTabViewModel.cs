using System.Collections.ObjectModel;
using System.ComponentModel;
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
    [ObservableProperty] private bool _useTreeProjection = false;
    [ObservableProperty] private bool _loadInventoryContent = false;
    [ObservableProperty] private string _ownerId = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private bool _isInDebugMode = false;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private ObservableCollection<FilterTypeOption> _filterTypeList;
    [ObservableProperty] private ObservableCollection<EItemType> _selectedFilterTypes = new ObservableCollection<EItemType>();

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
            
            ScheduleFilters(TypeFilterDelay);
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
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanLoadItemList))]
    public async Task LoadItemList()
    {
        IsLoading = true;
        _locationService.ClearCache();
        TreatmentStatus = "Appel RSI";
     
        var searchQuery = new ItemQueryModel();
        searchQuery.useConnectedUserOwner = _useConnectedProfilAsOwner;
        searchQuery.ownerId = _ownerId;
        searchQuery.Id = _id;
        searchQuery.UseProjection = UseTreeProjection;
        searchQuery.TypeList = SearchTypeList.Where(i => i.IsSelected).Select(i => i.Type).ToList();
        searchQuery.LoadInventoryContent = LoadInventoryContent;

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
            var scItem = await _scItemRepository.GetByCrc32Async(item.EntityNodeProperties.ClassGuidCrc);
            result.Add(new ItemViewModel(_localeEntryRepository, _locationService, _clientService, this, item, scItem));
        }

        _unfilteredItemList = Task.FromResult<IList<ItemViewModel>>(result);
        IsLoading = false;

        // Rattrape la liste filtrée complète avant de rendre la main : la commande reste "en cours"
        // (et le bouton désactivé) jusqu'à l'affichage complet des lignes.
        await ScheduleFilters(TimeSpan.Zero);
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
    private Task ScheduleFilters(TimeSpan delay)
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
            Needle: string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter,
            Types: SelectedFilterTypes is { Count: > 0 } ? SelectedFilterTypes.ToHashSet() : null);

        UpdateIsFiltering(true);
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
            var result = await Task.Run(() => FilterItemsAsync(items, criteria, token), token);

            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(result, token), DispatcherPriority.Background);
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

    private static async Task<List<ItemViewModel>> FilterItemsAsync(IList<ItemViewModel> items, ItemFilterCriteria criteria, CancellationToken token)
    {
        var result = new List<ItemViewModel>();

        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();

            // Le type se lit sans coût : on l'examine avant le nom, dont la résolution interroge la base locale.
            if (criteria.Types != null && !criteria.Types.Contains(item.ItemType))
            {
                continue;
            }

            if (criteria.Needle != null)
            {
                var name = await item.Name.ConfigureAwait(false);
                if (string.IsNullOrEmpty(name) || !name.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }
            }

            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Publie le résultat complet dans <see cref="ItemList"/>, puis l'affiche en alimentant une nouvelle collection
    /// par paquets : construire toutes les lignes d'un coup bloquerait le thread d'UI. S'interrompt dès que la demande
    /// est annulée.
    /// </summary>
    private async Task DisplayResultAsync(List<ItemViewModel> result, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        ItemList = Task.FromResult<IList<ItemViewModel>>(result);

        // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (DisplayedItems.SequenceEqual(result))
        {
            return;
        }

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

    private sealed record ItemFilterCriteria(string? Needle, HashSet<EItemType>? Types);

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
                            var storage = await it.Location ?? string.Empty;
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
                    var storage = await it.Location ?? string.Empty;
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