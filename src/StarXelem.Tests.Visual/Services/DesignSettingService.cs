using StarXelem.Services;

namespace StarXelem.Tests.Visual.Services;

public class DesignSettingService : ISettingsService
{
    public Task<string?> GetAsync(string key)
    {
        return Task.FromResult($"{key}Result");
    }

    public Task SetAsync(string key, string value)
    {
        return Task.CompletedTask;
    }

    public Task ClearAsync(string key)
    {
        return Task.CompletedTask;
    }
}