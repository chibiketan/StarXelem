using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace StarXelem.Services;

/// <summary>
/// Windows Registry-based implementation of <see cref="ISettingsService"/>.
/// </summary>
public class RegistrySettingsService : ISettingsService
{
    private const string RegistryBasePath = "Software\\StarXelem";
    private readonly ILogger<RegistrySettingsService> _logger;

    public RegistrySettingsService(ILogger<RegistrySettingsService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(RegistryBasePath);
            var value = regKey?.GetValue(key) as string;
            return await Task.FromResult(value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de lire la clé de registre \"{Key}\"", key);
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            using var regKey = Registry.CurrentUser.CreateSubKey(RegistryBasePath);
            regKey?.SetValue(key, value, RegistryValueKind.String);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'écrire la clé de registre \"{Key}\"", key);
        }
    }

    public async Task ClearAsync(string key)
    {
        try
        {
            using var regKey = Registry.CurrentUser.CreateSubKey(RegistryBasePath);
            regKey?.DeleteValue(key, false);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de supprimer la clé de registre \"{Key}\"", key);
        }
    }
}
