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
    public DbSet<ActorEntity> Actors => Set<ActorEntity>();
    public DbSet<MissionCategoryEntity> MissionCategories => Set<MissionCategoryEntity>();
    public DbSet<ContractGeneratorEntity> ContractGenerators => Set<ContractGeneratorEntity>();
    public DbSet<MissionRewardEntity> MissionRewards => Set<MissionRewardEntity>();
    public DbSet<MissionRequiredTagEntity> MissionRequiredTags => Set<MissionRequiredTagEntity>();
    public DbSet<MissionCompletionTagEntity> MissionCompletionTags => Set<MissionCompletionTagEntity>();
    public DbSet<MissionBlueprintPoolEntity> MissionBlueprintPools => Set<MissionBlueprintPoolEntity>();
    public DbSet<MissionBlueprintEntryEntity> MissionBlueprintEntries => Set<MissionBlueprintEntryEntity>();
    public DbSet<BlueprintEntity> Blueprints => Set<BlueprintEntity>();
    public DbSet<BlueprintRecipeCostEntity> BlueprintRecipeCosts => Set<BlueprintRecipeCostEntity>();

    public DbSet<BlueprintModifierEntity> BlueprintModifiers => Set<BlueprintModifierEntity>();

    public DbSet<ScItemEntity> ScItems => Set<ScItemEntity>();
    public DbSet<ScItemTagEntity> ScItemTags => Set<ScItemTagEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShipEntity>().HasIndex(s => s.Crc32);
        modelBuilder.Entity<ScItemEntity>().HasIndex(s => s.Crc32);
        modelBuilder.Entity<BlueprintEntity>().HasIndex(b => b.Crc32);
        
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

        // Rewards
        modelBuilder.Entity<MissionRewardEntity>()
            .HasOne(r => r.Mission)
            .WithMany()
            .HasForeignKey(r => r.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Blueprint pools
        modelBuilder.Entity<MissionBlueprintPoolEntity>()
            .HasOne(p => p.Mission)
            .WithMany(m => m.BlueprintPools)
            .HasForeignKey(p => p.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionBlueprintEntryEntity>()
            .HasOne(e => e.Pool)
            .WithMany(p => p.Entries)
            .HasForeignKey(e => e.PoolId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionBlueprintEntryEntity>()
            .HasOne(e => e.Blueprint)
            .WithMany()
            .HasForeignKey(e => e.BlueprintId)
            .OnDelete(DeleteBehavior.Restrict);

        // Blueprint costs
        modelBuilder.Entity<BlueprintRecipeCostEntity>()
            .HasOne(c => c.Blueprint)
            .WithMany(b => b.Costs)
            .HasForeignKey(c => c.BlueprintId)
            .OnDelete(DeleteBehavior.Cascade);

        // Blueprint modifiers
        modelBuilder.Entity<BlueprintModifierEntity>()
            .HasOne(m => m.Cost)
            .WithMany(c => c.Modifiers)
            .HasForeignKey(m => m.CostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mission <-> Contractor
        modelBuilder.Entity<MissionEntity>()
            .HasOne(m => m.Contractor)
            .WithMany(a => a.Missions)
            .HasForeignKey(m => m.ContractorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mission <-> Category
        modelBuilder.Entity<MissionEntity>()
            .HasOne(m => m.Category)
            .WithMany(c => c.Missions)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mission -> RequiredTags
        modelBuilder.Entity<MissionRequiredTagEntity>()
            .HasOne(rt => rt.Mission)
            .WithMany(m => m.RequiredTags)
            .HasForeignKey(rt => rt.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionRequiredTagEntity>()
            .HasOne(rt => rt.Tag)
            .WithMany()
            .HasForeignKey(rt => rt.TagSelfId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mission -> CompletionTags
        modelBuilder.Entity<MissionCompletionTagEntity>()
            .HasOne(ct => ct.Mission)
            .WithMany(m => m.CompletionTags)
            .HasForeignKey(ct => ct.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MissionCompletionTagEntity>()
            .HasOne(ct => ct.Tag)
            .WithMany()
            .HasForeignKey(ct => ct.TagSelfId)
            .OnDelete(DeleteBehavior.Cascade);

        // ScItem -> Manufacturer
        modelBuilder.Entity<ScItemEntity>()
            .HasOne(si => si.Manufacturer)
            .WithMany(m => m.ScItems)
            .HasForeignKey(si => si.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict);

        // ScItemTag junction
        modelBuilder.Entity<ScItemTagEntity>()
            .HasKey(st => new { st.ScItemRecordId, st.TagSelfId });

        modelBuilder.Entity<ScItemTagEntity>()
            .HasOne(st => st.ScItem)
            .WithMany(si => si.ScItemTags)
            .HasForeignKey(st => st.ScItemRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScItemTagEntity>()
            .HasOne(st => st.Tag)
            .WithMany()
            .HasForeignKey(st => st.TagSelfId)
            .OnDelete(DeleteBehavior.Cascade);

        // Blueprint -> Output ScItem
        modelBuilder.Entity<BlueprintEntity>()
            .HasOne(b => b.OutputItem)
            .WithMany()
            .HasForeignKey(b => b.OutputEntityClassRef)
            .OnDelete(DeleteBehavior.Restrict);

        // BlueprintRecipeCost -> ScItem
        modelBuilder.Entity<BlueprintRecipeCostEntity>()
            .HasOne(c => c.Item)
            .WithMany(si => si.BlueprintCosts)
            .HasForeignKey(c => c.ItemEntityClassRef)
            .OnDelete(DeleteBehavior.Restrict);

        // Many-to-many: Blueprint <-> ScItem for required items
        modelBuilder.Entity<BlueprintEntity>()
            .HasMany(b => b.RequiredItems)
            .WithMany()
            .UsingEntity(j => j.ToTable("BlueprintScItems"));
    }
}
