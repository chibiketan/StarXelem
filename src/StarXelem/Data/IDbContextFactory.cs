using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public interface IDbContextFactory
{
    string DbPath { get; }
    Task<StarXelemDbContext> CreateDbContextAsync();
}
