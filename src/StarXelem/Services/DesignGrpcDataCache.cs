using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Cache vide utilisé par le designer Avalonia.
/// </summary>
public class DesignGrpcDataCache : IGrpcDataCache
{
    /// <inheritdoc />
    public Task<CachedDataset<byte[]>?> LoadAsync(string datasetKey, CancellationToken token = default)
    {
        return Task.FromResult<CachedDataset<byte[]>?>(null);
    }

    /// <inheritdoc />
    public Task SaveAsync(string datasetKey, byte[] payload, DateTimeOffset fetchedAt, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }
}
