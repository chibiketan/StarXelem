using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System;

namespace StarXelem.Data;

public class LocaleEntry
{
    [Key]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

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
    public virtual ICollection<ScItemEntity> ScItems { get; set; } = new List<ScItemEntity>();
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
    public uint Crc32 { get; set; }
    public string TechnicalName { get; set; } = string.Empty;
    public string LocalizedName { get; set; } = string.Empty;
    public bool IsVisible { get; set; }

    [Required]
    public string ManufacturerId { get; set; } = string.Empty;
    [ForeignKey("ManufacturerId")]
    public virtual ManufacturerEntity? Manufacturer { get; set; }

    public virtual ICollection<ShipTagEntity> ShipTags { get; set; } = new List<ShipTagEntity>();
    public virtual ICollection<MissionShipRequirementEntity> MissionRequirements { get; set; } = new List<MissionShipRequirementEntity>();
    public virtual ICollection<ShipLoadoutEntryEntity> LoadoutEntries { get; set; } = new List<ShipLoadoutEntryEntity>();
}

public class ShipLoadoutEntryEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string ShipGuid { get; set; } = string.Empty;
    [ForeignKey("ShipGuid")]
    public virtual ShipEntity? Ship { get; set; }

    public string PortName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ComponentClass { get; set; } = "Unknown";
    public int Size { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string? WeaponType { get; set; }
    public string? GuidanceType { get; set; }
    public float? AlphaDamage { get; set; }

    public string? ComponentRecordId { get; set; }
    [ForeignKey("ComponentRecordId")]
    public virtual ScItemEntity? Component { get; set; }
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
    public string? TitleKey { get; set; }
    public string Description { get;set; } = string.Empty;
    public string? DescriptionKey { get; set; }
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

    /// <summary>Standing type name from handler propertyOverrides, e.g. "ReputationStanding_General".</summary>
    public string? StandingType { get; set; }

    /// <summary>Standing name extracted from StandingType for display, e.g. "General".</summary>
    public string? StandingName { get; set; }

    /// <summary>Max standing level the mission can reach (last level's minReputation - 1).</summary>
    public int? MaxStanding { get; set; }

    /// <summary>Locale key for the max standing display name, e.g. "StandingName_Rookie".</summary>
    public string? MaxStandingDisplayName { get; set; }

    /// <summary>Min standing raw reputation value from contract.minStanding.minReputation.</summary>
    public int? MinStandingRaw { get; set; }

    /// <summary>Locale key for the min standing display name, e.g. "StandingName_Rookie".</summary>
    public string? MinStandingDisplayName { get; set; }

    public virtual ICollection<MissionShipRequirementEntity> ShipRequirements { get; set; } = new List<MissionShipRequirementEntity>();
    public virtual ICollection<MissionShipSpawnEntity> ShipSpawns { get; set; } = new List<MissionShipSpawnEntity>();
    public virtual ICollection<MissionBlueprintPoolEntity> BlueprintPools { get; set; } = new List<MissionBlueprintPoolEntity>();
    public virtual ICollection<MissionRequiredTagEntity> RequiredTags { get; set; } = new List<MissionRequiredTagEntity>();
    public virtual ICollection<MissionCompletionTagEntity> CompletionTags { get; set; } = new List<MissionCompletionTagEntity>();
    public virtual ICollection<MissionRewardEntity> Rewards { get; set; } = new List<MissionRewardEntity>();
    public virtual ICollection<MissionObjectiveEntity> Objectives { get; set; } = new List<MissionObjectiveEntity>();
    public virtual ICollection<MissionPrerequisiteEntity> Prerequisites { get; set; } = new List<MissionPrerequisiteEntity>();
    public virtual ICollection<MissionTokenEntity> Tokens { get; set; } = new List<MissionTokenEntity>();
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

    /// <summary>Count for Item rewards (amount of the item).</summary>
    public int? Count { get; set; }

    /// <summary>True if reward is only given to mission owner (not split among party).</summary>
    public bool? OnlyToMissionOwner { get; set; }

    /// <summary>True if item reward is sent to home location.</summary>
    public bool? SendToHomeLocation { get; set; }
}

/* ---- Mission objective hierarchy ---- */

/// <summary>
/// Represents one objective in a mission's objective tree.
/// ParentId provides hierarchy (null = root objective).
/// </summary>
public class MissionObjectiveEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    /// <summary>Self-referencing FK for objective hierarchy. Null = root objective.</summary>
    public int? ParentId { get; set; }
    [ForeignKey("ParentId")]
    public virtual MissionObjectiveEntity? Parent { get; set; }

    public virtual ICollection<MissionObjectiveEntity> Children { get; set; } = new List<MissionObjectiveEntity>();

    /// <summary>Objective type, e.g. "All", "Or", "Objective", "Destroy", "Defend", etc.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Display text for the objective (may contain ~mission() tokens).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Locale key if available.</summary>
    public string? TextKey { get; set; }

    /// <summary>Order within parent's children.</summary>
    public int Order { get; set; }

    /// <summary>Tokens extracted from this objective's text.</summary>
    public virtual ICollection<MissionTokenEntity> Tokens { get; set; } = new List<MissionTokenEntity>();
}

/* ---- Mission prerequisites (structured) ---- */

/// <summary>
/// Structured prerequisite from contract.additionalPrerequisites.
/// Each row represents one prerequisite with structured fields per type.
/// </summary>
public class MissionPrerequisiteEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    /// <summary>Prerequisite type: "Reputation", "AreaTags", "CompletedContractTags", "CrimeStat", "JournalEntries", "Locality", "Location", "LocationProperty", etc.</summary>
    public string PrerequisiteType { get; set; } = string.Empty;

    /// <summary>Order among prerequisites.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Display label (final text after locale resolution — no tokens).</summary>
    public string? DisplayLabel { get; set; }

    // --- Reputation ---
    public int? MinReputation { get; set; }
    public int? MaxReputation { get; set; }
    public string? ScopeNameKey { get; set; }
    public string? FactionNameKey { get; set; }

    // --- Location / Locality ---
    public string? LocationNameKey { get; set; }
    public string? LocationLevelType { get; set; }

    // --- CrimeStat ---
    public int? MinCrimeStat { get; set; }
    public int? MaxCrimeStat { get; set; }
    public string? JurisdictionNameKey { get; set; }

    // --- Tags (AreaTags, CompletedContractTags) ---
    public string? RequiredTagNames { get; set; }
    public string? ExcludedTagNames { get; set; }

    // --- JournalEntries ---
    public string? RequiredJournalTitles { get; set; }
    public string? ExcludedJournalTitles { get; set; }
}

/* ---- Mission tokens (~mission() for display resolution) ---- */

/// <summary>
/// Token extracted from ~mission(...) in title, description, or objective text.
/// Linked to either a Mission (title/description tokens) or an Objective (objective tokens).
/// </summary>
public class MissionTokenEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK to mission (for title/description tokens).</summary>
    public string? MissionId { get; set; }
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    /// <summary>FK to objective (for objective tokens).</summary>
    public int? ObjectiveId { get; set; }
    [ForeignKey("ObjectiveId")]
    public virtual MissionObjectiveEntity? Objective { get; set; }

    /// <summary>Token name as it appears in the text, e.g. "factionToken", "locationToken".</summary>
    public string TokenName { get; set; } = string.Empty;

    /// <summary>Value type: "Organization", "AIName", "Location", "HaulingItem", "HaulingAmount", "HaulingTotal", "HaulingDestination".</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>Resolved display value (plain text for basic display).</summary>
    public string ResolvedValue { get; set; } = string.Empty;

    /// <summary>Locale key for the value (if applicable) — allows language switching.</summary>
    public string? ValueKey { get; set; }

    /// <summary>Order among tokens.</summary>
    public int Order { get; set; }
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

    /// <summary>Denormalized pool name from DataCoreTypedRecord.RecordName.</summary>
    public string PoolName { get; set; } = string.Empty;

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

    [Required]
    public string BlueprintId { get; set; } = string.Empty;
    [ForeignKey("BlueprintId")]
    public virtual BlueprintEntity? Blueprint { get; set; }

    public float Weight { get; set; }

    public float ChanceToAppear { get; set; }
}

/* ---- Blueprint detail storage ---- */

/// <summary>Full blueprint definition: recipe, costs, results, and modifiers.</summary>
public class BlueprintEntity
{
    [Key]
    public string SelfId { get; set; } = string.Empty;
    public uint Crc32 { get; set; }

    public string BlueprintName { get; set; } = string.Empty;

    /// <summary>BlueprintCategoryRecord selfId.</summary>
    public string CategoryRef { get; set; } = string.Empty;

    /// <summary>Denormalized category name from blueprintcategorydatabase.xml.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Process type: "Creation", "Dismantle", "Upgrade", "Refining", "Repair".</summary>
    public string ProcessType { get; set; } = string.Empty;

    /// <summary>entityClass selfId from CraftingProcess_Creation / _Upgrade (nullable for Dismantle/Refining).</summary>
    public string? OutputEntityClassRef { get; set; }

    /// <summary>Craft time from CraftingRecipeCosts.craftTime, stored as TimeSpan ticks.</summary>
    public TimeSpan? CraftDuration { get; set; }

    public virtual ICollection<BlueprintRecipeCostEntity> Costs { get; set; } = new List<BlueprintRecipeCostEntity>();

    [ForeignKey("OutputEntityClassRef")]
    public virtual ScItemEntity? OutputItem { get; set; }

    public virtual ICollection<ScItemEntity> RequiredItems { get; set; } = new List<ScItemEntity>();
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

    public string? ResourceName { get; set; }
    public string? ItemName { get; set; }

    public virtual ICollection<BlueprintModifierEntity> Modifiers { get; set; } = new List<BlueprintModifierEntity>();
    public virtual ScItemEntity? Item { get; set; }
}

/// <summary>Modifier context applied to a cost slot (gameplay property modifiers).</summary>
public class BlueprintModifierEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK to the recipe cost this modifier applies to.</summary>
    [Required]
    public int CostId { get; set; }
    [ForeignKey("CostId")]
    public virtual BlueprintRecipeCostEntity? Cost { get; set; }

    /// <summary>Range calculation: "Linear" (multiplicative) or "Additive".</summary>
    public string RangeType { get; set; } = string.Empty;

    /// <summary>Gameplay property name (e.g. "Integrity", "Impact Force", "Power Pips").</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Start of quality range (0 = min quality).</summary>
    public int StartQuality { get; set; }

    /// <summary>End of quality range.</summary>
    public int EndQuality { get; set; }

    /// <summary>Modifier value at start quality.</summary>
    public decimal ModifierStart { get; set; }

    /// <summary>Modifier value at end quality (Linear only, 0 for Additive).</summary>
    public decimal ModifierEnd { get; set; }
}

/// <summary>Raw resource from P4K (e.g. Iron, Copper) — populated during blueprint cost ingestion.</summary>
public class ResourceEntity
{
    [Key]
    public string SelfId { get; set; } = string.Empty;

    public uint? Crc32 { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? NameKey { get; set; }
}

public class MissionRequiredTagEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    [Required]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }

    /// <summary>true = tag requis (inclusion), false = tag exclu (exclusion)</summary>
    public bool IsRequired { get; set; }

    /// <summary>Nombre de tags requis pour satisfaire le prérequis (requiredCountValue ou excludedCountValue)</summary>
    public int RequiredCount { get; set; }
}

public class MissionCompletionTagEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string MissionId { get; set; } = string.Empty;
    [ForeignKey("MissionId")]
    public virtual MissionEntity? Mission { get; set; }

    [Required]
    public string TagSelfId { get; set; } = string.Empty;
    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }
}

public class LocationEntity
{
    [Key]
    public string CigGuid { get; set; } = string.Empty;

    public uint Crc { get; set; }

    public string SelfId { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public string NameLocalized { get; set; } = string.Empty;

    public string? DescriptionKey { get; set; }

    public string? DescriptionLocalized { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Jurisdiction { get; set; }

    public string? Affiliation { get; set; }

    public string? Callout1 { get; set; }

    public string? Callout2 { get; set; }

    public string? Callout3 { get; set; }

    public string? RespawnLocationType { get; set; }

    public string? LocationHierarchyTag { get; set; }

    public string? NavIcon { get; set; }

    public bool IsScannable { get; set; }

    public double Size { get; set; }

    public bool HideInStarmap { get; set; }

    public bool HideInWorld { get; set; }

    public bool HideWhenInAdoptionRadius { get; set; }

    public bool BlockTravel { get; set; }

    public bool OnlyShowWhenParentSelected { get; set; }

    public float MinimumDisplaySize { get; set; }

    public bool OverrideRotationSpeed { get; set; }

    public float OverrideRotationSpeedValue { get; set; }

    public bool ShowOrbitLine { get; set; }

    public bool UseHoloMaterial { get; set; }

    public bool NoAutoBodyRecovery { get; set; }

    public string? StarMapGeomPath { get; set; }

    public string? StarMapMaterialPath { get; set; }

    public string? StarMapShapePath { get; set; }

    public string? LocationImagePath { get; set; }

    public string? LocationMedicalImagePath { get; set; }

    [ForeignKey("ParentCigGuid")]
    public virtual LocationEntity? Parent { get; set; }

    public string? ParentCigGuid { get; set; }

    public virtual ICollection<LocationEntity> Children { get; set; } = new List<LocationEntity>();
}
