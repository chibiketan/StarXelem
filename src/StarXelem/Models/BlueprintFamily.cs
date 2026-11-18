using System.ComponentModel.DataAnnotations;
using Avalonia.Media;
using StarXelem.Services;

namespace StarXelem.Models;

/// <summary>Famille de l'objet fabriqué par un blueprint : regroupe les types du jeu pour l'icône et le filtre.</summary>
public enum BlueprintFamily
{
    [Display(Name = "Armures")]
    Armor,
    [Display(Name = "Armes FPS")]
    FpsWeapon,
    [Display(Name = "Armes vaisseau")]
    ShipWeapon,
    [Display(Name = "Composants")]
    ShipComponent,
    [Display(Name = "Minage")]
    Mining,
    [Display(Name = "Autres")]
    Other
}

/// <summary>Classement et libellés de l'objet fabriqué par un blueprint.</summary>
public static class BlueprintFamilyInfo
{
    // Classe à part : les géométries ne sont créées qu'au premier affichage, sur le thread d'UI,
    // et non lors du classement, qui tourne hors du thread d'UI.
    private static class IconCache
    {
    public static readonly Dictionary<BlueprintFamily, Geometry> Icons = new()
    {
        [BlueprintFamily.Armor] = StreamGeometry.Parse("M5 14a7 7 0 0 1 14 0v4h-5l-1-3h-2l-1 3H5z"),
        [BlueprintFamily.FpsWeapon] = StreamGeometry.Parse("M3 10h15v5H3zM18 11h3v3h-3M6 15v5h5v-5"),
        [BlueprintFamily.ShipWeapon] = StreamGeometry.Parse("M12 3v4M12 17v4M3 12h4M17 12h4M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8z"),
        [BlueprintFamily.ShipComponent] = StreamGeometry.Parse("M7 7h10v10H7zM10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4"),
        [BlueprintFamily.Mining] = StreamGeometry.Parse("M4 20l9-9M14 4c3 1 5 3 6 6l-3 1-4-4z"),
        [BlueprintFamily.Other] = StreamGeometry.Parse("M21 8l-9-5-9 5v8l9 5 9-5V8zM3 8l9 5 9-5M12 13v8"),
    };
    }

    /// <summary>Icône de la famille, tracée en contour sur une grille de 24 px.</summary>
    public static Geometry GetIcon(BlueprintFamily family) => IconCache.Icons[family];

    /// <summary>Famille d'un type d'objet du jeu (<c>TypeName</c> de ScItems).</summary>
    public static BlueprintFamily GetFamily(string? typeName) => typeName switch
    {
        null or "" => BlueprintFamily.Other,
        _ when typeName.StartsWith("Char_", StringComparison.Ordinal) => BlueprintFamily.Armor,
        "WeaponPersonal" or "WeaponAttachment" => BlueprintFamily.FpsWeapon,
        "WeaponGun" => BlueprintFamily.ShipWeapon,
        "PowerPlant" or "Cooler" or "Shield" or "QuantumDrive" or "Radar" => BlueprintFamily.ShipComponent,
        "WeaponMining" or "MiningModifier" or "SalvageHead" or "SalvageModifier" or "TractorBeam" => BlueprintFamily.Mining,
        _ => BlueprintFamily.Other
    };

    /// <summary>
    /// Type précis de l'objet, lisible seul (ex : « Casque · Armure lourde », « Refroidisseur · S1 · Grade C »,
    /// « Arme vaisseau · Taille 3 »).
    /// </summary>
    public static string GetTypeLabel(DbBlueprintProductRow? product)
    {
        if (product == null)
        {
            return "Type inconnu";
        }

        var size = product.Size is > 0 ? $"Taille {product.Size}" : null;
        var componentSize = product.Size is > 0 ? $"S{product.Size}" : null;
        var grade = product.Grade is >= 1 and <= 5 ? $"Grade {(char)('A' + product.Grade.Value - 1)}" : null;

        return product.TypeName switch
        {
            "Char_Armor_Helmet" => Join("Casque", ArmorWeight(product.SubTypeName)),
            "Char_Armor_Torso" => Join("Torse", ArmorWeight(product.SubTypeName)),
            "Char_Armor_Arms" => Join("Bras", ArmorWeight(product.SubTypeName)),
            "Char_Armor_Legs" => Join("Jambes", ArmorWeight(product.SubTypeName)),
            "Char_Armor_Backpack" => Join("Sac à dos", ArmorWeight(product.SubTypeName)),
            "Char_Armor_Undersuit" => Join("Combinaison", ArmorWeight(product.SubTypeName)),
            _ when product.TypeName.StartsWith("Char_Clothing", StringComparison.Ordinal) => "Vêtement",
            "WeaponPersonal" => Join("Arme FPS", product.SubTypeName switch
            {
                "Small" => "Légère",
                "Medium" => "Moyenne",
                "Large" => "Lourde",
                _ => null
            }),
            "WeaponAttachment" => product.SubTypeName == "Magazine" ? "Chargeur" : "Accessoire d'arme",
            "WeaponGun" => Join("Arme vaisseau", size),
            "PowerPlant" => Join("Générateur", componentSize, grade),
            "Cooler" => Join("Refroidisseur", componentSize, grade),
            "Shield" => Join("Bouclier", componentSize, grade),
            "QuantumDrive" => Join("Moteur quantique", componentSize, grade),
            "Radar" => Join("Radar", componentSize, grade),
            "WeaponMining" => Join("Laser de minage", size),
            "MiningModifier" => "Module de minage",
            "SalvageHead" => "Tête de récupération",
            "SalvageModifier" => "Module de récupération",
            "TractorBeam" => Join("Rayon tracteur", size),
            "DockingCollar" => "Collier d'amarrage",
            "Container" or "Cargo" => Join("Conteneur", size),
            _ => "Divers"
        };
    }

    // Accordé avec « armure », sous-entendu dans chaque libellé de pièce.
    private static string? ArmorWeight(string? subType) => subType switch
    {
        "Light" or "LightArmor" => "Armure légère",
        "Medium" => "Armure moyenne",
        "Heavy" => "Armure lourde",
        _ => null
    };

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));
}
