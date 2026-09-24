using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class FpsWeaponRepository : IFpsWeaponRepository
{
    private readonly IDbContextFactory _factory;

    public FpsWeaponRepository(IDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<FpsWeaponEntity>> GetAllAsync()
    {
        if (!File.Exists(_factory.DbPath))
            return [];

        await using var db = await _factory.CreateDbContextAsync();
        return await db.FpsWeapons
            .AsNoTracking()
            .Include(w => w.Manufacturer)
            .OrderBy(w => w.LocalizedName)
            .ToListAsync();
    }
}
