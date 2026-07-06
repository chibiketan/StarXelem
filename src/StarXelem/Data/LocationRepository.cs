using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class LocationRepository : ILocationRepository
{
    private static string GetDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "StarXelem");
        return Path.Combine(folder, "database.db");
    }

    private DbContextOptions<StarXelemDbContext> GetOptions()
    {
        return new DbContextOptionsBuilder<StarXelemDbContext>()
            .UseSqlite($"Data Source={GetDbPath()}")
            .Options;
    }

    public async Task<LocationEntity?> GetByCrcAsync(uint crc)
    {
        await using var db = new StarXelemDbContext(GetOptions());
        return await db.Locations.FirstOrDefaultAsync(l => l.Crc == crc);
    }

    public async Task<List<LocationEntity>> GetAllAsync()
    {
        await using var db = new StarXelemDbContext(GetOptions());
        return await db.Locations.ToListAsync();
    }
}
