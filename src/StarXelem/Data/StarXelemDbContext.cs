using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace StarXelem.Data;

public class StarXelemDbContext : DbContext
{
    public StarXelemDbContext(DbContextOptions<StarXelemDbContext> options) : base(options) { }

    public DbSet<ShipEntity> Ships => Set<ShipEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();
    public DbSet<ManufacturerEntity> Manufacturers => Set<ManufacturerEntity>();
    public DbSet<MissionShipRequirementEntity> MissionShipRequirements => Set<MissionShipRequirementEntity>();
    public DbSet<MissionShipSpawnEntity> MissionShipSpawns => Set<MissionShipSpawnEntity>();
    public DbSet<MissionShipSpawnShipEntity> MissionShipSpawnShips => Set<MissionShipSpawnShipEntity>();
    public DbSet<MissionShipSpawnTagEntity> MissionShipSpawnTags => Set<MissionShipSpawnTagEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<ShipTagEntity> ShipTags => Set<ShipTagEntity>();
    public DbSet<ContractGeneratorEntity> ContractGenerators => Set<ContractGeneratorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<MissionShipRequirementEntity>()
            .HasOne(msr => msr.Mission)
            .WithMany(m => m.ShipRequirements)
            .HasForeignKey(msr => msr.MissionId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<MissionShipRequirementEntity>()
            .HasOne(msr => msr.Ship)
            .WithMany(s => s.MissionRequirements)
            .HasForeignKey(msr => msr.ShipGuid)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<MissionShipSpawnEntity>()
            .HasOne(mss => mss.Mission)
            .WithMany(m => m.ShipSpawns)
            .HasForeignKey(mss => mss.MissionId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<MissionShipSpawnShipEntity>()
            .HasOne(msss => msss.SpawnRule)
            .WithMany()
            .HasForeignKey(msss => msss.MissionShipSpawnId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<MissionShipSpawnShipEntity>()
            .HasOne(msss => msss.Ship)
            .WithMany()
            .HasForeignKey(msss => msss.ShipGuid)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<ShipEntity>()
            .HasOne(s => s.Manufacturer)
            .WithMany(m => m.Ships)
            .HasForeignKey(s => s.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<ShipTagEntity>()
            .HasKey(st => new { st.ShipGuid, st.TagSelfId });
        
        modelBuilder.Entity<ShipTagEntity>()
            .HasOne(st => st.Ship)
            .WithMany(s => s.ShipTags)
            .HasForeignKey(st => st.ShipGuid)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<ShipTagEntity>()
            .HasOne(st => st.Tag)
            .WithMany(t => t.ShipTags)
            .HasForeignKey(st => st.TagSelfId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionShipSpawnTagEntity>()
            .HasOne(mst => mst.SpawnRule)
            .WithMany(s => s.Tags)
            .HasForeignKey(mst => mst.MissionShipSpawnId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionShipSpawnTagEntity>()
            .HasOne(mst => mst.Tag)
            .WithMany()
            .HasForeignKey(mst => mst.TagSelfId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionEntity>()
            .HasOne(m => m.Generator)
            .WithMany(cg => cg.Missions)
            .HasForeignKey(m => m.GeneratorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
