namespace StarXelem.Models;

/// <summary>
/// Catégorie visuelle d'un emplacement, qui détermine son icône et sa couleur.
/// </summary>
public enum ItemLocationCategory
{
    /// <summary>Emplacement non classé.</summary>
    Other = 0,

    /// <summary>Lieu du jeu.</summary>
    Place,

    /// <summary>Hangar.</summary>
    Hangar,

    /// <summary>Vaisseau.</summary>
    Ship,

    /// <summary>Arme.</summary>
    Weapon,

    /// <summary>Élément d'armure ou casque.</summary>
    Armor,

    /// <summary>Caisse ou conteneur.</summary>
    Container,

    /// <summary>Porté par le joueur.</summary>
    Worn
}
