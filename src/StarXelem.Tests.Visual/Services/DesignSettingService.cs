using System.Collections.Concurrent;
using StarXelem.Services;

namespace StarXelem.Tests.Visual.Services;

public class DesignSettingService : ISettingsService
{
    private readonly ConcurrentDictionary<string, string?> _store = new();

    public Task<string?> GetAsync(string key)
    {
        return Task.FromResult(_store.TryGetValue(key, out var value) ? value : $"{key}Result");
    }

    public Task SetAsync(string key, string value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
