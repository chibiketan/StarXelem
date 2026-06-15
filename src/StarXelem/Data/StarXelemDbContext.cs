using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class StarXelemDbContext : DbContext
{
    public StarXelemDbContext(DbContextOptions<StarXelemDbContext> options) : base(options) { }

    public DbSet<ShipEntity> Ships => Set<ShipEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();
    public DbSet<ManufacturerEntity> Manufacturers => Set<ManufacturerEntity>();
    public DbSet<MissionShipRequirementEntity> MissionShipRequirements => Set<MissionShipRequirementEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<ShipTagEntity> ShipTags => Set<ShipTagEntity>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MissionShipRequirementEntity>()
            .HasOne(msr => msr.Mission)
            .WithMany(m => m.ShipRequirements)
            .HasForeignKey(msr => msr.MissionDebugName)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionShipRequirementEntity>()
            .HasOne(msr => msr.Ship)
            .WithMany(s => s.MissionRequirements)
            .HasForeignKey(msr => msr.ShipGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShipEntity>()
            .HasOne(s => s.Manufacturer)
            .WithMany(m => m.Ships)
            .HasForeignKey(s => s.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShipTagEntity>()
            .HasKey(st => new { st.ShipGuid, st.TagName });

        modelBuilder.Entity<ShipTagEntity>()
            .HasOne(st => st.Ship)
            .WithMany(s => s.ShipTags)
            .HasForeignKey(st => st.ShipGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShipTagEntity>()
            .HasOne(st => st.Tag)
            .WithMany(t => t.ShipTags)
            .HasForeignKey(st => st.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
