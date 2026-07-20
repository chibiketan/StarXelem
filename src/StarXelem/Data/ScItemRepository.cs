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
        return await db.ScItems.FirstOrDefaultAsync(s => s.Crc32 == crc32);
    }
}
