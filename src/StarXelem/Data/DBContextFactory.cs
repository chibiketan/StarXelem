using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class DbContextFactory : IDbContextFactory
{
    public string DbPath { get; }

    public DbContextFactory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "StarXelem");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        DbPath = Path.Combine(folder, "database.db");
    }

    private DbContextOptions<StarXelemDbContext> GetOptions()
    {
        return new DbContextOptionsBuilder<StarXelemDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;
    }

    public async Task<StarXelemDbContext> CreateDbContextAsync()
    {
        return new StarXelemDbContext(GetOptions());
    }
}
