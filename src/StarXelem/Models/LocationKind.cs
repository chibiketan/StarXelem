namespace StarXelem.Models;

/// <summary>
/// Nature de l'emplacement où se trouve une entité du jeu.
/// </summary>
public enum LocationKind
{
    /// <summary>Emplacement non classé : mission, donnée absente ou information technique.</summary>
    Other = 0,

    /// <summary>Lieu du jeu, par exemple une ville ou une station.</summary>
    Location,

    /// <summary>Hangar.</summary>
    Hangar,

    /// <summary>Objet existant en jeu, par exemple un vaisseau ou un conteneur.</summary>
    Item,

    /// <summary>Porté par le joueur.</summary>
    Worn
}
