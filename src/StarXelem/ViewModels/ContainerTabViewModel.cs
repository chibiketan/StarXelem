using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Views;

namespace StarXelem.ViewModels;

public partial class ContainerTabViewModel : PageViewModelBase
{
    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    public override string Name => "Conteneurs";
    public override string Icon => nameof(Symbol.Folder);
    [ObservableProperty] public Task<IList<InventoryViewModel>>? _inventoryList;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";

    public ContainerTabViewModel(IGrpcClientService clientService, IP4kService p4kService)
    {
        _clientService = clientService;
        _p4KService = p4kService;

        _p4KService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;
        _clientService.OnConnectedChanged += (sender, b) => loadShipListCommand?.NotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanLoadShipList))]
    public async Task LoadShipList()
    {
        IsLoading = true;
        TreatmentStatus = "Appel RSI";
        //await _clientService.TestRequest();
        var spaceships = await _clientService.QueryInventories();
        
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        InventoryList = Task.FromResult<IList<InventoryViewModel>>(spaceships.Select(i => new InventoryViewModel(i)).ToList());
        IsLoading = false;
    }

    public bool CanLoadShipList()
    {
        return _clientService.IsConnected && !IsLoading;
    }

    private void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, on change tout, reconnexion en prime
        LoadShipListCommand.NotifyCanExecuteChanged();
    }
}