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
    public string DebugName { get; set; } = string.Empty;
    public string GeneratorName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get;set; } = string.Empty;
    public string ContractorName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public bool NotForRelease { get; set; }
    public bool WorkInProgress { get; set; }

    [Required]
    public string GeneratorId { get; set; } = string.Empty;
    [ForeignKey("GeneratorId")]
    public virtual ContractGeneratorEntity? Generator { get; set; }

    public virtual ICollection<MissionShipRequirementEntity> ShipRequirements { get; set; } = new List<MissionShipRequirementEntity>();
    public virtual ICollection<MissionShipSpawnEntity> ShipSpawns { get;set; } = new List<MissionShipSpawnEntity>();
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
