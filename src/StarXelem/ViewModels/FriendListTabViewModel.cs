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
    
    [ObservableProperty] private Task<List<FriendViewModel>?>? _friendList;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";
    [ObservableProperty] private bool _onlyConnected = true;
    [ObservableProperty] private List<FriendViewModel>? _filteredFriendList;

    public FriendListTabViewModel(IGrpcClientService clientService)
    {
        _clientService = clientService;

        _clientService.OnStatusChanged += (sender, _) => LoadFriendNotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanLoadFriendList))]
    public async Task LoadFriendList()
    {
        IsLoading = true;
        await SetTreatmentStatus("Appel RSI").ConfigureAwait(false);
        var friendList = await _clientService.GetFriendList().ConfigureAwait(false);
        await SetTreatmentStatus("Terminé").ConfigureAwait(false);
        FriendList = Task.FromResult(friendList.Select(f => new FriendViewModel(
            displayName: f.Account?.DisplayName ?? "Unknown",
            tokenName: f.Account?.Nickname ?? "Unknown",
            avatarUrl: f.Account?.AvatarUrl,
            isConnected: f.Presence != null,
            isInGame: f.Presence?.Activity?.PlayerId != null,
            activity: f.Presence?.Activity?.State ?? "Hors ligne",
            shardInfoLoader: f.Account?.AccountId != null && f.Presence?.Status != null ? () => _clientService.GetShardInfo((int)f.Account.AccountId)! : null
        )).ToList())!;
        IsLoading = false;
        AppliFilterOnSearchresult();
    }

    private Task SetTreatmentStatus(string treatmentStatus)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            TreatmentStatus = treatmentStatus;
            return Task.CompletedTask;
        }
        
        return Dispatcher.UIThread.InvokeAsync(() => SetTreatmentStatus(treatmentStatus));
    }

    public bool CanLoadFriendList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    private async Task<List<FriendViewModel>> GetFriendListSafe()
    {
        if (null == FriendList)
        {
            return new List<FriendViewModel>();
        }
        
        var list = await FriendList;
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
    
    private void LoadFriendNotifyCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LoadFriendListCommand?.NotifyCanExecuteChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(LoadFriendNotifyCanExecuteChanged);
        }
    }

}