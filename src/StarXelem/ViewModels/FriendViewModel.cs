using CommunityToolkit.Mvvm.ComponentModel;
using Sc.External.Common.Shard.V1;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Friends.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarBreaker.P4k;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public class FriendViewModel : ViewModelBase
{
    private readonly Friend _friend;
    private readonly IGrpcClientService _grpcClientService;
    private readonly System.Lazy<Task<ShardInfo?>> _shardInfo;

    public string? AvatarUrl => String.IsNullOrEmpty(_friend.Account?.AvatarUrl) ? "https://cdn.robertsspaceindustries.com/static/images/account/avatar_default_big.jpg" : _friend.Account.AvatarUrl;
    public string DisplayName => _friend.Account?.DisplayName ?? "Unknown";
    public string TokenName => _friend.Account?.Nickname ?? "Unknown";
    public bool IsConnected => null != _friend.Presence;
    public bool IsInGame => _friend.Presence?.Activity?.PlayerId != null;
    
    public string Activity => _friend.Presence?.Activity?.State ?? "Hors ligne";
    public Task<string?> ShardId => GetShardIdAsync();
    public Task<int?> ShardTotalPlayers => GetShardTotalPlayersAsync();
    public Task<int?> ShardPlayerCount => GetShardPlayerCountAsync();
    public Task<string?> ShardLocation => GetShardLocationAsync();
    
    public FriendViewModel(Friend friend, IGrpcClientService grpcClientService)
    {
        _friend = friend;
        _grpcClientService = grpcClientService;

        _shardInfo = new Lazy<Task<ShardInfo?>>(LoadShardInfoIfNeeded);
    }

    private Task<ShardInfo?> LoadShardInfoIfNeeded()
    {
        if (null == _friend.Account || null == _friend.Presence)
        {
            return Task.FromResult<ShardInfo?>(null);
        }

        return _grpcClientService.GetShardInfo(_friend.Account.AccountId)!;
    }
    
    private async Task<String?> GetShardIdAsync()
    {
        var shardInfo = await _shardInfo.Value;

        if (null == shardInfo)
        {
            return null;
        }

        return shardInfo.Id;
    }
    
    private async Task<int?> GetShardTotalPlayersAsync()
    {
        var shardInfo = await _shardInfo.Value;

        if (null == shardInfo || 0 == shardInfo.TotalPlayers)
        {
            return null;
        }

        return shardInfo.TotalPlayers;
    }
    
    private async Task<int?> GetShardPlayerCountAsync()
    {
        var shardInfo = await _shardInfo.Value;

        if (null == shardInfo || 0 == shardInfo.PlayerCount)
        {
            return null;
        }

        return shardInfo.PlayerCount;
    }

    private async Task<string?> GetShardLocationAsync()
    {
        var shardInfo = await _shardInfo.Value;

        if (null == shardInfo)
        {
            return null;
        }

        return shardInfo.Location;
    }
}