using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public interface ILocationService
{
    Task<string?> ResolveEntityLocation(string? entityLocation);
    Task<string?> ResolveLocation(EntityItemQueryResult entity);
}