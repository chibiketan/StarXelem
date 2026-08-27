using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public class ScItemEntity
{
    [Key]
    public string RecordId { get; set; } = string.Empty;

    public uint Crc32 { get; set; }

    public string TechnicalName { get; set; } = string.Empty;

    public string LocalizedName { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string SubTypeName { get; set; } = string.Empty;

    public int? Size { get; set; }

    public int? Grade { get; set; }

    public string? LocaleNameKey { get; set; }

    public string? LocaleDescKey { get; set; }

    public string? DisplayLocaleKey { get; set; }

    public string? ManufacturerId { get; set; }

    [ForeignKey("ManufacturerId")]
    public virtual ManufacturerEntity? Manufacturer { get; set; }

    public float? Mass { get; set; }

    public float? Health { get; set; }

    public bool? IsSalvagable { get; set; }

    public bool? IsRepairable { get; set; }

    /* ---- Health damage resistances (flattened from DamageResistance) ---- */

    public float? ResistPhysical { get; set; }

    public float? ResistEnergy { get; set; }

    public float? ResistDistortion { get; set; }

    public float? ResistThermal { get; set; }

    public float? ResistBiochemical { get; set; }

    public float? ResistStun { get; set; }

    public long? InventoryVolumeMicroSCU { get; set; }

    public float? InvDimX { get; set; }

    public float? InvDimY { get; set; }

    public float? InvDimZ { get; set; }

    public string? TagsText { get; set; }

    public string? RequiredTagsText { get; set; }

    /* ---- Resource deltas (flattened from ItemResourceComponentParams) ---- */

    public int? PowerGeneration { get; set; }

    public int? PowerConsumption { get; set; }

    public float? CoolantGeneration { get; set; }

    public float? CoolantConsumption { get; set; }

    public string? ResourceDeltasJson { get; set; }

    /* ---- Distortion (from SDistortionParams) ---- */

    public float? DistortionDecayDelay { get; set; }

    public float? DistortionDecayRate { get; set; }

    public float? DistortionMaximum { get; set; }

    /* ---- Shield (from SCItemShieldGeneratorParams) ---- */

    public float? ShieldHealth { get; set; }

    public float? ShieldRegen { get; set; }

    public float? ShieldDecayRatio { get; set; }

    public float? ShieldDownedRegenDelay { get; set; }

    public float? ShieldDamagedRegenDelay { get; set; }

    public string? ShieldResistancesJson { get; set; }

    /* ---- JumpDrive / QuantumDrive (from SCItemJumpDriveParams) ---- */

    public float? JumpAlignmentRate { get; set; }

    public float? JumpAlignmentDecayRate { get; set; }

    public float? JumpTuningRate { get; set; }

    public float? JumpTuningDecayRate { get; set; }

    public float? JumpFuelUsageEfficiency { get; set; }

    /* ---- Armor (from SCItemVehicleArmorParams) ---- */

    public float? SignalInfrared { get; set; }

    public float? SignalElectromagnetic { get; set; }

    public float? SignalCrossSection { get; set; }

    public float? ArmorMultPhysical { get; set; }

    public float? ArmorMultEnergy { get; set; }

    public float? ArmorMultDistortion { get; set; }

    public float? ArmorMultThermal { get; set; }

    public float? ArmorMultBiochemical { get; set; }

    public float? ArmorMultStun { get; set; }

    /* ---- Weapon (from SCItemWeaponComponentParams) ---- */

    public string? WeaponAmmoRef { get; set; }

    public float? WeaponAccuracyRangeMin { get; set; }

    public float? WeaponAccuracyRangeMax { get; set; }

    /* ---- Weapon/Missile damage (from projectile/explosion DamageInfo) ---- */

    public float? DamagePhysical { get; set; }

    public float? DamageEnergy { get; set; }

    public float? DamageDistortion { get; set; }

    public float? DamageThermal { get; set; }

    public float? DamageBiochemical { get; set; }

    public float? DamageStun { get; set; }

    /* ---- FuelTank ---- */

    public float? FuelCapacity { get; set; }

    /* ---- Thruster ---- */

    public float? Thrust { get; set; }

    public virtual ICollection<ScItemTagEntity> ScItemTags { get; set; } = new List<ScItemTagEntity>();

    public virtual ICollection<BlueprintRecipeCostEntity> BlueprintCosts { get; set; } = new List<BlueprintRecipeCostEntity>();
}

public class ScItemTagEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string ScItemRecordId { get; set; } = string.Empty;

    [ForeignKey("ScItemRecordId")]
    public virtual ScItemEntity? ScItem { get; set; }

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string TagSelfId { get; set; } = string.Empty;

    [ForeignKey("TagSelfId")]
    public virtual TagEntity? Tag { get; set; }
}
