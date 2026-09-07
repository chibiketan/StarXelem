using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class MineralSignatureRepository : IMineralSignatureRepository
{
    private readonly IDbContextFactory _factory;

    public MineralSignatureRepository(IDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<MineralSignatureEntity>> FindBySignatureAsync(int value, int tolerance, CancellationToken ct = default)
    {
        if (!File.Exists(_factory.DbPath)) return Array.Empty<MineralSignatureEntity>();

        await using var db = await _factory.CreateDbContextAsync();
        var min = value - tolerance;
        var max = value + tolerance;

        return await db.MineralSignatures
            .AsNoTracking()
            .Where(m => m.Signature >= min && m.Signature <= max)
            .OrderBy(m => m.Signature > value ? m.Signature - value : value - m.Signature)
            .ThenBy(m => m.ClusterSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MineralSignatureEntity>> GetAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_factory.DbPath)) return Array.Empty<MineralSignatureEntity>();

        await using var db = await _factory.CreateDbContextAsync();
        return await db.MineralSignatures.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
    }
}
