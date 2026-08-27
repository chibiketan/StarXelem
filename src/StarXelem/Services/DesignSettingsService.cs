using System.Collections.Concurrent;

namespace StarXelem.Services;

/// <summary>
/// Implémentation en mémoire de <see cref="ISettingsService"/> utilisée uniquement
/// en mode design (Avalonia <c>Design.IsDesignMode</c>).
/// Ne touche jamais au registre Windows : les valeurs vivent le temps de la session design.
/// </summary>
public class DesignSettingsService : ISettingsService
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<string?> GetAsync(string key)
        => Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);

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
