using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace StarXelem.Data;

public class ManufacturerEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;

    public virtual ICollection<ShipEntity> Ships { get; set; } = new List<ShipEntity>();
}

    public class TagEntity
    {
        public string Name { get; set; } = string.Empty;

        [Key]
        public string SelfId { get; set; } = string.Empty;

        public string? ParentName { get; set; }

        public string Path { get; set; } = string.Empty;

        public virtual ICollection<ShipTagEntity> ShipTags { get; set; } = new List<ShipTagEntity>();
    }

public class ShipTagEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string ShipGuid { get; set; } = string.Empty;
    [ForeignKey("ShipGuid")]
    public virtual ShipEntity? Ship { get; set; }

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }
}

public class ShipEntity
{
    [Key]
    public string EntityClassGuid { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public string LocalizedName { get; set; } = string.Empty;
    
    [Required]
    public string ManufacturerId { get; set; } = string.Empty;
    [ForeignKey("ManufacturerId")]
    public virtual ManufacturerEntity? Manufacturer { get; set; }

    public virtual ICollection<ShipTagEntity> ShipTags { get; set; } = new List<ShipTagEntity>();
    public virtual ICollection<MissionShipRequirementEntity> MissionRequirements { get; set; } = new List<MissionShipRequirementEntity>();
}

public class ActorEntity
{
    // PK = selfId (CigGuid string)
    [Key]
    public string Id { get; set; } = string.Empty;

    // Resolved from contractParams.stringParamOverrides (ContractStringParamType.Contractor)
    public string NameKey { get; set; } = string.Empty;

    // DisplayName resolved from P4K
    public string Name { get; set; } = string.Empty;

    public virtual ICollection<MissionEntity> Missions { get; set; } = new List<MissionEntity>();
}

public class MissionCategoryEntity
{
    // PK = locale key (ex: "ContractCategory_Eliminate")
    [Key]
    public string Id { get; set; } = string.Empty;

    // Resolved via GetLocaleValue(localeKey) ou "Inconnue" si null
    public string Name { get; set; } = string.Empty;

    public virtual ICollection<MissionEntity> Missions { get; set; } = new List<MissionEntity>();
}

public class ContractGeneratorEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string DebugName { get; set; } = string.Empty;
    public bool NotForRelease { get; set; }
    public bool WorkInProgress { get; set; }
    public int MaxPlayersPerInstance { get; set; }
    public bool OnceOnly { get; set; }
    public bool AvailableInPrison { get; set; }
    public bool HideInMobiGlas { get; set; }
    public bool CanReacceptAfterAbandoning { get; set; }
    public float AbandonedCooldownTime { get; set; }
    public float AbandonedCooldownTimeVariation { get; set; }
    public bool CanReacceptAfterFailing { get; set; }
    public bool HasPersonalCooldown { get; set; }
    public float PersonalCooldownTime { get; set; }
    public float PersonalCooldownTimeVariation { get; set; }
    public bool NotifyOnAvailable { get; set; }

    public virtual ICollection<MissionEntity> Missions { get; set; } = new List<MissionEntity>();
}

public class MissionEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string DebugName { get;set; } = string.Empty;
    public string GeneratorName { get;set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get;set; } = string.Empty;
    public bool NotForRelease { get; set; }
    public bool WorkInProgress { get; set; }

    [Required]
    public string GeneratorId { get; set; } = string.Empty;
    [ForeignKey("GeneratorId")]
    public virtual ContractGeneratorEntity? Generator { get; set; }

    public string? CategoryId { get; set; }
    [ForeignKey("CategoryId")]
    public virtual MissionCategoryEntity? Category { get; set; }

    public string? ContractorId { get; set; }
    [ForeignKey("ContractorId")]
    public virtual ActorEntity? Contractor { get; set; }

    public decimal AUECReward { get; set; }
    public decimal AUECCost { get; set; }

    public virtual ICollection<MissionShipRequirementEntity> ShipRequirements { get; set; } = new List<MissionShipRequirementEntity>();
    public virtual ICollection<MissionShipSpawnEntity> ShipSpawns { get; set; } = new List<MissionShipSpawnEntity>();
}

public class MissionShipRequirementEntity
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }
    
    [Required]
    public string ShipGuid { get; set; } = string.Empty;
    [ForeignKey("ShipGuid")]
    public virtual ShipEntity? Ship { get; set; }
    
    public string RequirementType { get; set; } = "Objective";
    public int MinAmount { get; set; }
    public int MaxAmount { get; set; }
}

public class MissionShipSpawnEntity
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }
    
    public string GroupName { get; set; } = string.Empty;
    public int Weight { get; set; }

    public virtual ICollection<MissionShipSpawnTagEntity> Tags { get; set; } = new List<MissionShipSpawnTagEntity>();
}

public class MissionShipSpawnTagEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MissionShipSpawnId { get; set; }
    [ForeignKey("MissionShipSpawnId")]
    public virtual MissionShipSpawnEntity? SpawnRule { get; set; }

    [Required]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }

    public bool IsIncluded { get; set; }
}

public class MissionShipSpawnShipEntity
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int MissionShipSpawnId { get; set; }
    [ForeignKey("MissionShipSpawnId")]
    public virtual MissionShipSpawnEntity? SpawnRule { get; set; }
    
    [Required]
    public string ShipGuid { get; set; } = string.Empty;
    [ForeignKey("ShipGuid")]
    public virtual ShipEntity? Ship { get; set; }
}

/* ---- Reward entities ---- */

/// <summary>
/// Polymorphic reward entry for a mission. <c>RewardType</c> is a simple string discriminator
/// (e.g. "CalculatedReward", "LegacyReputation", "BaseReward", "BadgeAward", "CompletionTags", "Item").
/// <c>DisplayValue</c> holds the human-readable value extracted from the game data.
/// </summary>
public class MissionRewardEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    /// <summary>Type discriminator – ContractResult subclass name (e.g. "CalculatedReward", "LegacyReputation").</summary>
    public string RewardType { get; set; } = string.Empty;

    /// <summary>Human-readable display string (e.g. "2500 aUEC", "150 réputation Foxwell").</summary>
    public string DisplayValue { get; set; } = string.Empty;

    /// <summary>True for aUEC (CalculatedReward) where the value is computed, not fixed.</summary>
    public bool IsCalculated { get; set; }
}

/* ---- Blueprint pool linkage — mission → pool → entries ---- */

/// <summary>Represents a BlueprintPoolRecord referenced by a mission contract.</summary>
public class MissionBlueprintPoolEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    /// <summary>BlueprintPoolRecord selfId.</summary>
    public string BlueprintPoolRef { get; set; } = string.Empty;

    public virtual ICollection<MissionBlueprintEntryEntity> Entries { get; set; } = new List<MissionBlueprintEntryEntity>();
}

/// <summary>One BlueprintReward entry inside a BlueprintPoolRecord.</summary>
public class MissionBlueprintEntryEntity
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Pool")]
    public int PoolId { get; set; }
    public virtual MissionBlueprintPoolEntity Pool { get; set; } = new MissionBlueprintPoolEntity();
    public string BlueprintRef { get; set; } = string.Empty;

    public float Weight { get; set; }

    public float ChanceToAppear { get; set; }
}

/* ---- Blueprint detail storage ---- */

/// <summary>Full blueprint definition: recipe, costs, results, and modifiers.</summary>
public class BlueprintEntity
{
    [Key]
    public string SelfId { get; set; } = string.Empty;

    public string BlueprintName { get; set; } = string.Empty;

    /// <summary>BlueprintCategoryRecord selfId.</summary>
    public string CategoryRef { get; set; } = string.Empty;

    /// <summary>Denormalized category name from blueprintcategorydatabase.xml.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Process type: "Creation", "Dismantle", "Upgrade", "Refining", "Repair".</summary>
    public string ProcessType { get; set; } = string.Empty;

    /// <summary>entityClass selfId from CraftingProcess_Creation / _Upgrade (nullable for Dismantle/Refining).</summary>
    public string? OutputEntityClassRef { get; set; }

    public virtual ICollection<BlueprintRecipeCostEntity> Costs { get; set; } = new List<BlueprintRecipeCostEntity>();
    public virtual ICollection<BlueprintModifierEntity> Modifiers { get; set; } = new List<BlueprintModifierEntity>();
}

/// <summary>One cost (resource or item) or a Select-option inside a blueprint recipe.</summary>
public class BlueprintRecipeCostEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string BlueprintId { get; set; } = string.Empty;
    [ForeignKey("BlueprintId")]
    public virtual BlueprintEntity? Blueprint { get; set; }

    /// <summary>"Resource", "Item", or "Select".</summary>
    public string CostType { get; set; } = string.Empty;

    /// <summary>For Select costs: nameInfo (e.g. "FRAME", "ELECTRONICS").</summary>
    public string CostName { get; set; } = string.Empty;

    public string? ResourceRef { get; set; }
    public decimal? ResourceAmount { get; set; }
    public string? ItemEntityClassRef { get; set; }
    public int? ItemCount { get; set; }
    public int? MinQuality { get; set; }
}

/// <summary>Modifier context applied to a cost (quantity multipliers, composition inclusion, gameplay property modifiers).</summary>
public class BlueprintModifierEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string BlueprintId { get; set; } = string.Empty;
    [ForeignKey("BlueprintId")]
    public virtual BlueprintEntity? Blueprint { get; set; }

    /// <summary>Context type: "QuantityMultiplier", "ResultCompositionInclusion", "ResultGameplayPropertyModifiers".</summary>
    public string ContextType { get; set; } = string.Empty;

    /// <summary>Serialized parameter value (e.g. multiplier float, modifier name).</summary>
    public string ParameterValue { get; set; } = string.Empty;
}
