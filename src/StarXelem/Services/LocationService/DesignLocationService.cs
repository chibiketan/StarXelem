using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public class DesignLocationService : ILocationService
{
    public Task<string?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult(entityLocation);
    }

    public Task<string?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult<string?>("ResolveLocation");
    }

    public void ClearCache()
    {
    }
}