namespace StarXelem.Models;

/// <summary>
/// Quantité d'un matériau d'une qualité donnée présente à un emplacement.
/// </summary>
/// <param name="Location">Emplacement, ou null tant qu'il n'est pas résolu.</param>
/// <param name="Quantity">Quantité, en SCU ou en nombre d'éléments selon le matériau.</param>
/// <param name="QuantityLabel">Quantité formatée dans l'unité du matériau.</param>
public sealed record MaterialPlace(ItemLocationDisplay? Location, double Quantity, string QuantityLabel)
{
    /// <summary>Nom affiché : celui de l'emplacement, ou un texte d'attente.</summary>
    public string Name => Location?.Name ?? "Emplacement en cours de résolution";

    /// <summary>Vrai si l'emplacement est connu : affiche son icône.</summary>
    public bool HasLocation => Location != null;
}
