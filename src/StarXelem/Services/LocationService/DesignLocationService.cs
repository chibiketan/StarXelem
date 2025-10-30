using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public class DesignLocationService : ILocationService
{
    public Task<string?> ResolveEntityLocation(string? entityLocation)
    {
        return Task.FromResult(entityLocation);
    }

    public Task<string?> ResolveLocation(EntityItemQueryResult entity)
    {
        return Task.FromResult<string?>("ResolveLocation");
    }
}