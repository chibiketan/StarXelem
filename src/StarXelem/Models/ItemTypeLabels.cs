using StarBreaker.DataCoreGenerated;

namespace StarXelem.Models;

/// <summary>
/// Libellés français des types d'objets. Un type sans traduction garde son nom technique.
/// </summary>
public static class ItemTypeLabels
{
    private static readonly Dictionary<EItemType, string> Labels = new()
    {
        [EItemType.WeaponPersonal] = "Armes personnelles",
        [EItemType.WeaponAttachment] = "Accessoires",
        [EItemType.WeaponGun] = "Armes de vaisseau",
        [EItemType.Char_Armor_Helmet] = "Casques",
        [EItemType.Misc] = "Divers"
    };

    /// <summary>Retourne le libellé à afficher pour un type d'objet.</summary>
    /// <param name="type">Type de l'objet.</param>
    public static string Get(EItemType type)
    {
        return Labels.GetValueOrDefault(type, type.ToString());
    }
}
