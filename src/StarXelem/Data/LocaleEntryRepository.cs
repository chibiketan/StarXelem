using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class LocaleEntryRepository : ILocaleEntryRepository
{
    private readonly IDbContextFactory _factory;

    public LocaleEntryRepository(IDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<string?> GetValueByKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (!File.Exists(_factory.DbPath)) return null;

        await using var db = await _factory.CreateDbContextAsync();
        var entry = await db.LocaleEntries.FirstOrDefaultAsync(l => l.Key == key);
        return entry?.Value;
    }
}
