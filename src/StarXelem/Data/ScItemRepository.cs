using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class ScItemRepository : IScItemRepository
{
    private readonly IDbContextFactory _factory;

    public ScItemRepository(IDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<ScItemEntity?> GetByCrc32Async(uint crc32)
    {
        if (!File.Exists(_factory.DbPath)) return null;

        await using var db = await _factory.CreateDbContextAsync();
        return await db.ScItems.AsNoTracking().FirstOrDefaultAsync(s => s.Crc32 == crc32);
    }

    public async Task<IReadOnlyDictionary<string, uint>> GetCrcByShortNameAsync()
    {
        if (!File.Exists(_factory.DbPath)) return new Dictionary<string, uint>();

        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.ScItems.AsNoTracking().Select(s => new { s.TechnicalName, s.Crc32 }).ToListAsync();

        // TechnicalName vaut « Type.NomCourt » : la clé est la partie après le premier point, comparée sans tenir compte de la casse.
        var map = new Dictionary<string, uint>(items.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            map.TryAdd(item.TechnicalName[(item.TechnicalName.IndexOf('.') + 1)..], item.Crc32);
        }

        return map;
    }
}
