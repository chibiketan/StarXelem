using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarXelem.Data;

/// <summary>
/// Une arme FPS, regroupée par modèle : toutes les variantes de livrée d'une même arme
/// (Tint01, Store01, Collector01…) partagent une seule ligne, identifiée par le
/// mannequinClassTag. Les statistiques de tir sont dédoublées primaire/secondaire car une
/// arme peut embarquer deux munitions distinctes (l'Arlington tire une balle de 44 mm en
/// primaire et une gerbe de 8 plombs énergétiques en secondaire).
/// </summary>
public class FpsWeaponEntity
{
    /// <summary>mannequinClassTag, ex. « hdgw_rifle_ballistic_01 ».</summary>
    [Key]
    public string Id { get; set; } = string.Empty;

    /// <summary>RecordId de la variante retenue comme représentante du groupe.</summary>
    public string RecordId { get; set; } = string.Empty;

    public string TechnicalName { get; set; } = string.Empty;

    public string LocalizedName { get; set; } = string.Empty;

    public string? LocaleNameKey { get; set; }

    /// <summary>Classe déduite du nom technique : rifle, pistol, smg, shotgun, sniper, lmg, hmg, glauncher, crossbow, special.</summary>
    public string WeaponClass { get; set; } = string.Empty;

    /// <summary>Famille de munition déduite du nom technique : Ballistic ou Energy.</summary>
    public string? AmmoFamily { get; set; }

    public string? SubTypeName { get; set; }

    public int? Size { get; set; }

    public int? Grade { get; set; }

    public string? ManufacturerId { get; set; }

    [ForeignKey("ManufacturerId")]
    public virtual ManufacturerEntity? Manufacturer { get; set; }

    /* ---- Variantes de livrée regroupées ---- */

    public int VariantCount { get; set; }

    /// <summary>Noms localisés des variantes, séparés par un retour à la ligne (affichage en infobulle).</summary>
    public string? VariantNames { get; set; }

    /* ---- Chargeur et réapprovisionnement ---- */

    public int? MagazineSize { get; set; }

    /// <summary>ammoRepoolParams.unstowMagDuration : secondes pour sortir un chargeur du sac à dos.</summary>
    public float? RepoolUnstowDuration { get; set; }

    /// <summary>ammoRepoolParams.bulletsPerSecond : cadence d'extraction des balles.</summary>
    public int? RepoolBulletsPerSecond { get; set; }

    /// <summary>ammoRepoolParams.fullMagMergeDuration : secondes pour fusionner un chargeur plein.</summary>
    public float? RepoolFullMagMergeDuration { get; set; }

    /* ---- Tir principal ---- */

    public string? PrimaryModeName { get; set; }

    /// <summary>Type de dégâts dominant : Physical, Energy, Distortion, Thermal, Biochemical, Stun.</summary>
    public string? PrimaryDamageType { get; set; }

    /// <summary>Dégâts par projectile, avant multiplication par le nombre de plombs.</summary>
    public float? PrimaryDamagePerProjectile { get; set; }

    /// <summary>Dégâts directs réels par tir = dégâts par projectile × plombs × multiplicateur.</summary>
    public float? PrimaryDamagePerShot { get; set; }

    /// <summary>
    /// Dégâts d'explosion par tir, pour les ordonnances explosives (lance-roquettes,
    /// lance-grenades). Null quand la munition n'explose pas. Indépendant des dégâts directs :
    /// le Boomtube inflige 20 à l'impact et 41 000 par son explosion.
    /// </summary>
    public float? PrimaryExplosiveDamage { get; set; }

    public int? PrimaryPelletCount { get; set; }

    public int? PrimaryFireRate { get; set; }

    public float? PrimaryProjectileSpeed { get; set; }

    public float? PrimaryProjectileLifetime { get; set; }

    /// <summary>vitesse × durée de vie : distance maximale que le projectile peut parcourir.</summary>
    public float? PrimaryMaxRange { get; set; }

    /// <summary>damageDropMinDistance : distance jusqu'à laquelle les dégâts restent pleins. Null si la munition n'a pas de chute de dégâts.</summary>
    public float? PrimaryEffectiveRange { get; set; }

    /// <summary>Distance à laquelle les dégâts atteignent leur plancher et cessent de décroître.</summary>
    public float? PrimaryDamageFloorRange { get; set; }

    /* ---- Tir secondaire ---- */

    public bool HasSecondaryFire { get; set; }

    public string? SecondaryModeName { get; set; }

    public string? SecondaryDamageType { get; set; }

    public float? SecondaryDamagePerProjectile { get; set; }

    public float? SecondaryDamagePerShot { get; set; }

    /// <summary>Dégâts d'explosion par tir du mode secondaire. Null quand la munition n'explose pas.</summary>
    public float? SecondaryExplosiveDamage { get; set; }

    public int? SecondaryPelletCount { get; set; }

    public int? SecondaryFireRate { get; set; }

    public float? SecondaryProjectileSpeed { get; set; }

    public float? SecondaryProjectileLifetime { get; set; }

    public float? SecondaryMaxRange { get; set; }

    public float? SecondaryEffectiveRange { get; set; }

    public float? SecondaryDamageFloorRange { get; set; }

    /* ---- Détail ---- */

    /// <summary>Modificateurs non neutres de weaponDegradationModifier.weaponStats, sérialisés en JSON. Null si tous neutres.</summary>
    public string? ModifiersJson { get; set; }
}
