using System.ComponentModel;
using System.Text;
using Avalonia.Threading;
using Cig.Protocols.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Sc.Internal.Services.UniverseHierarchy.V1;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;

namespace StarXelem.ViewModels;

public partial class FriendListTabViewModel : PageViewModelBase
{
    private readonly IGrpcClientService  _clientService;
    public override string Name => "Amis";
    public override string Icon => nameof(Symbol.People);
    public Task<IList<FriendViewModel>>? _spaceships;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private bool _onlyConnected = true;
    [ObservableProperty] private List<FriendViewModel>? _filteredFriendList;

    public FriendListTabViewModel(IGrpcClientService clientService)
    {
        _clientService = clientService;

        _clientService.OnConnectedChanged += (sender, b) => loadShipListCommand?.NotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanLoadShipList))]
    public async Task LoadShipList()
    {
        IsLoading = true;
        TreatmentStatus = "Appel RSI";
        var spaceships = await _clientService.GetFriendList();
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        _spaceships = Task.FromResult<IList<FriendViewModel>>(spaceships.Select(f => new FriendViewModel(f, _clientService)).ToList());
        IsLoading = false;
        AppliFilterOnSearchresult();
    }

    public bool CanLoadShipList()
    {
        return _clientService.IsConnected && !IsLoading;
    }

    private async Task<IList<FriendViewModel>> GetFriendListSafe()
    {
        if (null == _spaceships)
        {
            return new List<FriendViewModel>();
        }
        
        var list = await _spaceships;
        return list ?? new List<FriendViewModel>();
    }

    private async void AppliFilterOnSearchresult()
    {
        var list = (await GetFriendListSafe()).AsQueryable();

        if (OnlyConnected)
        {
            list = list.Where(s => s.IsConnected);
        }
        
        FilteredFriendList = list.ToList();
    }

    partial void OnOnlyConnectedChanged(bool onlyConnected)
    {
        AppliFilterOnSearchresult();
    }
}