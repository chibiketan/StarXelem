using Microsoft.Extensions.Logging;
using Sc.External.Services.BlueprintLibrary.V1;

namespace StarXelem.Services;

/// <summary>
/// Blueprints obtenus : interroge le jeu quand il est connecté et conserve la réponse dans <see cref="IGrpcDataCache"/> pour les
/// lancements suivants, jeu fermé compris.
/// </summary>
public sealed class ObtainedBlueprintService : IObtainedBlueprintService
{
    private const string DatasetKey = "blueprints";

    private readonly IGrpcClientService _grpcClientService;
    private readonly IGrpcDataCache _dataCache;
    private readonly ILogger<ObtainedBlueprintService> _logger;

    public ObtainedBlueprintService(IGrpcClientService grpcClientService, IGrpcDataCache dataCache, ILogger<ObtainedBlueprintService> logger)
    {
        _grpcClientService = grpcClientService;
        _dataCache = dataCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ObtainedBlueprints?> GetObtainedAsync(bool forceRefresh = false, CancellationToken token = default)
    {
        if (!forceRefresh && await LoadCachedAsync(token).ConfigureAwait(false) is { } cachedFirst)
        {
            return cachedFirst;
        }

        if (_grpcClientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame)
        {
            try
            {
                var entries = await _grpcClientService.GetBlueprintList().ConfigureAwait(false);
                var fetchedAt = DateTimeOffset.Now;
                await _dataCache.SaveAsync(DatasetKey, GrpcCachePayload.WriteMessages(entries), fetchedAt, token).ConfigureAwait(false);
                return new ObtainedBlueprints(ToIdSet(entries), fetchedAt, IsFromCache: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Impossible de lire les blueprints obtenus auprès du jeu, recherche dans le cache");
            }
        }

        // Sans actualisation forcée, le cache a déjà été consulté : il est vide.
        return forceRefresh ? await LoadCachedAsync(token).ConfigureAwait(false) : null;
    }

    private async Task<ObtainedBlueprints?> LoadCachedAsync(CancellationToken token)
    {
        var cached = await _dataCache.LoadAsync(DatasetKey, token).ConfigureAwait(false);
        if (cached == null)
        {
            return null;
        }

        try
        {
            var entries = GrpcCachePayload.ReadMessages(cached.Data, BlueprintEntry.Parser);
            return new ObtainedBlueprints(ToIdSet(entries), cached.FetchedAt, IsFromCache: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blueprints obtenus illisibles dans le cache");
            return null;
        }
    }

    private static HashSet<string> ToIdSet(IEnumerable<BlueprintEntry> entries) => entries.Select(entry => entry.BlueprintId).ToHashSet();
}
