namespace StarXelem.Models;

/// <summary>
/// Disponibilité d'un matériau de blueprint dans les objets possédés.
/// </summary>
public enum MaterialStockStatus
{
    /// <summary>Un lot d'une seule qualité couvre le besoin.</summary>
    Enough,

    /// <summary>Le besoin n'est couvert qu'en mélangeant des lots de qualités différentes.</summary>
    NeedsMix,

    /// <summary>Des lots existent mais ne couvrent pas le besoin.</summary>
    Insufficient,

    /// <summary>Aucun lot utilisable.</summary>
    Missing
}
