namespace StarXelem.Models;

/// <summary>
/// Présentation de l'emplacement d'un objet dans l'écran Objets.
/// </summary>
/// <param name="Category">Catégorie visuelle de l'emplacement.</param>
/// <param name="Name">Nom de l'emplacement.</param>
/// <param name="Detail">Chemin qui mène de cet emplacement au lieu final, ou <c>null</c> s'il n'y en a pas.</param>
/// <param name="Destination">Dernière étape de ce chemin, ou <c>null</c> s'il n'y en a pas.</param>
public sealed record ItemLocationDisplay(ItemLocationCategory Category, string Name, string? Detail, string? Destination)
{
    /// <summary>Texte complet, pour l'infobulle quand la colonne tronque l'affichage.</summary>
    public string FullText => Detail == null ? Name : $"{Name} › {Detail}";
}
