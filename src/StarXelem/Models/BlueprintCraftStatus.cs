namespace StarXelem.Models;

/// <summary>
/// Possibilité de fabriquer un blueprint avec les objets possédés, affichée dans la liste.
/// </summary>
public enum BlueprintCraftStatus
{
    /// <summary>Objets non chargés, ou blueprint sans matériau : rien à afficher.</summary>
    Unknown,

    /// <summary>Aucun matériau possédé.</summary>
    None,

    /// <summary>Une partie seulement des matériaux est réunie.</summary>
    Partial,

    /// <summary>Tous les matériaux sont réunis (au besoin en mélangeant des lots).</summary>
    Ready
}
