using System.Text.RegularExpressions;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.ShipLoadout;

/// <summary>
/// Décrit un objet montable d'après sa définition du Data.p4k : nom, classe, taille, grade et, pour une arme ou un missile,
/// type, dégâts par tir et guidage. Partagé par la reconstruction de la base (loadouts d'origine) et la fiche d'un vaisseau possédé.
/// </summary>
public sealed partial class ShipComponentDescriber
{
    private readonly IP4kService _p4kService;

    public ShipComponentDescriber(IP4kService p4kService)
    {
        _p4kService = p4kService;
    }

    /// <summary>
    /// Décrit l'objet, ou renvoie <c>null</c> s'il n'est pas montable (pas de définition d'attache). Les dégâts d'une arme ne sont
    /// connus que si la définition a été chargée avec une profondeur d'au moins 3.
    /// </summary>
    /// <param name="entityClass">Définition de l'objet.</param>
    /// <param name="technicalName">Nom technique, utilisé pour déduire le type d'arme quand les munitions ne le donnent pas.</param>
    /// <param name="fallbackName">Nom affiché si la localisation n'en fournit aucun.</param>
    public async Task<ShipComponentDescription?> DescribeAsync(EntityClassDefinition entityClass, string? technicalName, string? fallbackName)
    {
        var attachDef = entityClass.Components.OfType<SAttachableComponentParams>().FirstOrDefault()?.AttachDef;
        if (attachDef == null)
        {
            return null;
        }

        var displayName = await _p4kService.GetLocaleValue(attachDef.Localization.Name).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith('@'))
        {
            displayName = fallbackName ?? ShortName(technicalName) ?? "Inconnu";
        }

        var componentClass = ComponentClass.Unknown;
        var description = await _p4kService.GetLocaleValue(attachDef.Localization.Description).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(description))
        {
            var match = ClassRegex().Match(description);
            if (match.Success && Enum.TryParse<ComponentClass>(match.Groups[1].Value, out var parsedClass))
            {
                componentClass = parsedClass;
            }
        }

        var grade = attachDef.Grade > 0 ? new string((char)(attachDef.Grade - 1 + 'A'), 1) : string.Empty;
        string? weaponType = null;
        float? alphaDamage = null;
        string? guidanceType = null;

        if (attachDef.Type == EItemType.WeaponGun)
        {
            (weaponType, alphaDamage) = DescribeWeapon(entityClass, technicalName, displayName);
        }
        else if (attachDef.Type == EItemType.Missile)
        {
            guidanceType = DescribeGuidance(entityClass);
        }

        var recordId = entityClass.selfId != default ? entityClass.selfId.ToString() : null;

        return new ShipComponentDescription(attachDef.Type, displayName, componentClass, attachDef.Size, grade, weaponType, guidanceType, alphaDamage,
            technicalName, recordId);
    }

    private static (string? WeaponType, float? AlphaDamage) DescribeWeapon(EntityClassDefinition entityClass, string? technicalName, string displayName)
    {
        string? weaponType = null;
        float? alphaDamage = null;

        // Type de munition et dégâts : lus dans SAmmoContainerComponentParams (profondeur de chargement >= 3).
        var ammoParams = entityClass.Components.OfType<SAmmoContainerComponentParams>().FirstOrDefault()?.ammoParamsRecord;
        if (ammoParams != null)
        {
            var ammoTypeName = ammoParams.ammoCategory switch
            {
                AmmoCategory._5mm or AmmoCategory._7mm or AmmoCategory._10mm or AmmoCategory._50cal or AmmoCategory._50cal_pistol
                    or AmmoCategory._12g or AmmoCategory.Coil => "Ballistic",
                AmmoCategory.Laser => "Laser",
                AmmoCategory.Plasma => "Plasma",
                AmmoCategory.Electron => "Electron",
                _ => null
            };

            var damageBase = ammoParams.projectileParams switch
            {
                BulletProjectileParams bullet => bullet.damage,
                TachyonProjectileParams tachyon => tachyon.damage,
                _ => null
            };
            if (damageBase is DamageInfo damageInfo)
            {
                alphaDamage = Sum(damageInfo);
            }

            // Munitions explosives (Jericho, Suckerpunch) : dégâts de détonation.
            if (alphaDamage is null or <= 1 && ammoParams.projectileParams?.detonationParams?.explosionParams?.damage is DamageInfo detonation)
            {
                alphaDamage = Sum(detonation);
            }

            // Rayon (Supremacy-10T) : dégâts par seconde.
            if (alphaDamage is null or <= 0)
            {
                var beam = entityClass.Components.OfType<SCItemWeaponComponentParams>().FirstOrDefault()?.fireActions?
                    .OfType<SWeaponActionFireBeamParams>()
                    .FirstOrDefault(action => action.damagePerSecond is DamageInfo);
                if (beam?.damagePerSecond is DamageInfo beamDamage)
                {
                    alphaDamage = Sum(beamDamage);
                }
            }

            if (ammoTypeName != null)
            {
                var subtype = InferWeaponSubtypeFromName(technicalName, displayName);
                weaponType = string.IsNullOrEmpty(subtype) ? ammoTypeName : $"{ammoTypeName} {subtype}";
            }
        }

        if (weaponType == null)
        {
            var baseType = InferWeaponTypeFromName(technicalName, displayName);
            var subtype = InferWeaponSubtypeFromName(technicalName, displayName);
            weaponType = !string.IsNullOrEmpty(baseType)
                ? (!string.IsNullOrEmpty(subtype) ? $"{baseType} {subtype}" : baseType)
                : subtype;
        }

        return (weaponType, alphaDamage);
    }

    private static string? DescribeGuidance(EntityClassDefinition entityClass)
    {
        var targeting = entityClass.Components.OfType<SCItemMissileParams>().FirstOrDefault()?.targetingParams;
        return targeting?.trackingSignalType switch
        {
            ESignatureType.Infrared => "IR",
            ESignatureType.Electromagnetic => "EM",
            ESignatureType.CrossSection => "CrossSection",
            ESignatureType.Decibel => "Decibel",
            ESignatureType.Resource => "Resource",
            ESignatureType.Identity => "Identity",
            ESignatureType.CommsSignal => "CommsSignal",
            ESignatureType.Interactable => "Interactable",
            _ => null
        };
    }

    private static float Sum(DamageInfo damage) =>
        damage.DamagePhysical + damage.DamageEnergy + damage.DamageDistortion + damage.DamageThermal + damage.DamageBiochemical + damage.DamageStun;

    /// <summary>Nom court d'un enregistrement (« Type.NomCourt » → « NomCourt »).</summary>
    public static string? ShortName(string? recordName) =>
        string.IsNullOrEmpty(recordName) ? recordName : recordName[(recordName.IndexOf('.') + 1)..];

    /// <summary>Déduit le type d'arme (Laser, Ballistic…) du nom technique ou du nom affiché.</summary>
    public static string? InferWeaponTypeFromName(string? technicalName, string displayName)
    {
        if (!string.IsNullOrEmpty(technicalName))
        {
            var tech = technicalName.ToLowerInvariant();
            if (tech.Contains("laser")) return "Laser";
            if (tech.Contains("ballistic")) return "Ballistic";
            if (tech.Contains("plasma")) return "Plasma";
            if (tech.Contains("distortion")) return "Distortion";
            if (tech.Contains("massdriver")) return "Ballistic";
            if (tech.Contains("neutron")) return "Electron";
            if (tech.Contains("tachyon")) return "Distortion";
            if (tech.Contains("rpod")) return "Ballistic";
        }

        var name = displayName.ToLowerInvariant();
        if (name.Contains("laser") || name.Contains("jericho")) return "Laser";
        if (name.Contains("plasma") || name.Contains("tigerstrike")) return "Plasma";
        if (name.Contains("electron")) return "Electron";
        if (name.Contains("distortion") || name.Contains("tachyon")) return "Distortion";
        if (name.Contains("gatling") || name.Contains("repeater") || name.Contains("cannon") || name.Contains("railgun") || name.Contains("coilgun")
            || name.Contains("scattergun") || name.Contains("mass driver")) return "Ballistic";
        return null;
    }

    /// <summary>Déduit le sous-type d'arme (Repeater, Cannon…) du nom technique ou du nom affiché.</summary>
    public static string? InferWeaponSubtypeFromName(string? technicalName, string displayName)
    {
        var tech = (technicalName ?? string.Empty).ToLowerInvariant();
        var name = displayName.ToLowerInvariant();

        if (tech.Contains("repeater") || name.Contains("repeater")) return "Repeater";
        if (tech.Contains("gatling") || name.Contains("gatling")) return "Gatling";
        if (tech.Contains("cannon") || name.Contains("cannon")) return "Cannon";
        if (tech.Contains("scattergun") || name.Contains("scattergun")) return "Scattergun";
        if (tech.Contains("railgun") || name.Contains("railgun")) return "Railgun";
        if (tech.Contains("coilgun") || name.Contains("coilgun")) return "Coilgun";
        if (tech.Contains("massdriver") || name.Contains("mass driver")) return "Mass Driver";
        return null;
    }

    [GeneratedRegex(@"Class:\s*(\w+)")]
    private static partial Regex ClassRegex();
}

/// <summary>Description d'un objet montable, indépendante de sa provenance (loadout réel ou d'origine).</summary>
public sealed record ShipComponentDescription(
    EItemType Type,
    string DisplayName,
    ComponentClass Class,
    int Size,
    string Grade,
    string? WeaponType,
    string? GuidanceType,
    float? AlphaDamage,
    string? TechnicalName,
    string? RecordId);
