using Sc.External.Common.Shard.V1;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public class FriendViewModel : ViewModelBase
{
    private readonly Lazy<Task<ShardInfo?>> _shardInfo;

    private const string DefaultAvatarUrl = "https://cdn.robertsspaceindustries.com/static/images/account/avatar_default_big.jpg";

    public string? AvatarUrl { get; }
    public string DisplayName { get; }
    public string TokenName { get; }
    public bool IsConnected { get; }
    public bool IsInGame { get; }
    public string Activity { get; }

    public string ActivityLabel => Activity switch
    {
        "persistent_universe" => "Univers Persistant",
        "menu" => "Menu",
        "arena_commander" => "Arena Commander",
        _ => Activity
    };

    public bool IsOffline => !IsConnected;
    public bool IsInPersistentUniverse => Activity == "persistent_universe";
    public bool IsInMenu => Activity == "menu";
    public bool IsInArenaCommander => Activity == "arena_commander";

    public Task<string?> ShardId => GetShardIdAsync();
    public Task<int?> ShardTotalPlayers => GetShardTotalPlayersAsync();
    public Task<int?> ShardPlayerCount => GetShardPlayerCountAsync();
    public Task<string?> ShardLocation => GetShardLocationAsync();

    public FriendViewModel(
        string displayName,
        string tokenName,
        string? avatarUrl,
        bool isConnected,
        bool isInGame,
        string activity,
        Func<Task<ShardInfo?>>? shardInfoLoader = null)
    {
        DisplayName = displayName;
        TokenName = tokenName;
        AvatarUrl = IsAvatarUrlValid(avatarUrl) ? avatarUrl : DefaultAvatarUrl;
        IsConnected = isConnected;
        IsInGame = isInGame;
        Activity = activity;
        _shardInfo = new Lazy<Task<ShardInfo?>>(() => shardInfoLoader?.Invoke() ?? Task.FromResult<ShardInfo?>(null));
    }

    private static bool IsAvatarUrlValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host == "cdn.robertsspaceindustries.com" || uri.Host == "robertsspaceindustries.com";

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetShardIdAsync()
    {
        var shardInfo = await _shardInfo.Value;
        return shardInfo?.Id;
    }

    private async Task<int?> GetShardTotalPlayersAsync()
    {
        var shardInfo = await _shardInfo.Value;
        if (null == shardInfo || 0 == shardInfo.TotalPlayers)
            return null;
        return shardInfo.TotalPlayers;
    }

    private async Task<int?> GetShardPlayerCountAsync()
    {
        var shardInfo = await _shardInfo.Value;
        if (null == shardInfo || 0 == shardInfo.PlayerCount)
            return null;
        return shardInfo.PlayerCount;
    }

    private async Task<string?> GetShardLocationAsync()
    {
        var shardInfo = await _shardInfo.Value;
        return shardInfo?.Location;
    }
}
