namespace StarXelem.Models;

/// <summary>
/// Représente un profil utilisateur retourné par l'API Alliance Orbital.
/// </summary>
public class ProfilItem
{
    /// <summary>Identifiant unique du profil (GUID).</summary>
    public string Guid { get; set; } = "";

    /// <summary>Nom d'affichage du profil.</summary>
    public string Handle { get; set; } = "";

    /// <summary>URL de l'avatar, nullable si aucun n'est défini.</summary>
    public string? Avatar { get; set; }
}
