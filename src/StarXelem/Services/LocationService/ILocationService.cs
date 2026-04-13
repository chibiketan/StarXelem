using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public interface ILocationService
{
    Task<string?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null);
    Task<string?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null);
    void ClearCache();
}