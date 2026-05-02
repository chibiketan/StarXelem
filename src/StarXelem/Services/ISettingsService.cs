using System.Threading.Tasks;

namespace StarXelem.Services;

/// <summary>
/// Abstracts persistent key/value settings storage, decoupled from the underlying platform mechanism.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Reads a setting value by key. Returns <c>null</c> when the key does not exist.
    /// </summary>
    Task<string?> GetAsync(string key);

    /// <summary>
    /// Saves a string value associated with the specified key.
    /// </summary>
    Task SetAsync(string key, string value);

    /// <summary>
    /// Removes the setting associated with the specified key.
    /// </summary>
    Task ClearAsync(string key);
}
