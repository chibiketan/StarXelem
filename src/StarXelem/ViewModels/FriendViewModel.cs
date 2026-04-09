using System.Linq;
using Sc.External.Common.Shard.V1;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public class FriendViewModel : ViewModelBase
{
    private readonly Lazy<Task<ShardInfo?>> _shardInfo;

    private const string DefaultAvatarUrl = "https://cdn.robertsspaceindustries.com/static/images/account/avatar_default_big.jpg";

    public string? AvatarUrl { get; }
    public string Initials => GetInitials(TokenName);
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
    public Task<string?> ShardRegion => GetShardRegionAsync();
    public Task<bool> IsEuropeRegion => CheckRegionAsync("euw1b");
    public Task<bool> IsUsaRegion => CheckRegionAsync("use1b");
    public Task<bool> IsAsieRegion => CheckRegionAsync("ape1a");
    public Task<bool> IsAustralieRegion => CheckRegionAsync("apse2a");

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
        AvatarUrl = IsAvatarUrlValid(avatarUrl) ? avatarUrl : null;
        IsConnected = isConnected;
        IsInGame = isInGame;
        Activity = activity;
        _shardInfo = new Lazy<Task<ShardInfo?>>(() => shardInfoLoader?.Invoke() ?? Task.FromResult<ShardInfo?>(null));
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "?";

        int spaceIndex = name.IndexOf(' ');
        if (spaceIndex >= 0)
            return $"{GetFirstAlphanumericUpper(name, 0)}{GetFirstAlphanumericUpper(name, spaceIndex + 1)}";

        int underscoreIndex = name.IndexOf('_');
        if (underscoreIndex >= 0)
            return $"{GetFirstAlphanumericUpper(name, 0)}{GetFirstAlphanumericUpper(name, underscoreIndex + 1)}";

        var chars = name.Where(char.IsLetterOrDigit).Take(2).ToArray();
        return new string(chars).ToUpper();
    }

    private static char GetFirstAlphanumericUpper(string name, int startIndex)
    {
        for (int i = startIndex; i < name.Length; i++)
            if (char.IsLetterOrDigit(name[i]))
                return char.ToUpper(name[i]);
        return '?';
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

    private async Task<string?> GetShardRegionAsync()
    {
        var shardInfo = await _shardInfo.Value;
        if (shardInfo?.Id is not { } id)
            return null;

        var parts = id.Split('_');
        if (parts.Length < 2)
            return null;

        return parts[1] switch
        {
            "euw1b"  => "Europe",
            "use1b"  => "USA",
            "apse2a" => "Australie",
            "ape1a"  => "Asie",
            _        => null
        };
    }

    private async Task<bool> CheckRegionAsync(string regionCode)
    {
        var shardInfo = await _shardInfo.Value;
        if (shardInfo?.Id is not { } id)
            return false;
        var parts = id.Split('_');
        return parts.Length >= 2 && parts[1] == regionCode;
    }
}
