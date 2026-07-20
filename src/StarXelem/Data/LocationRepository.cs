using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class LocationRepository : ILocationRepository
{
    private readonly IDbContextFactory _factory;

    public LocationRepository(IDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<LocationEntity?> GetByCrcAsync(uint crc)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Locations.FirstOrDefaultAsync(l => l.Crc == crc);
    }

    public async Task<List<LocationEntity>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Locations.ToListAsync();
    }
}
