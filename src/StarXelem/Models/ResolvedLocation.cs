namespace StarXelem.Models;

/// <summary>
/// Emplacement d'une entité du jeu, tel que retenu au dernier saut de la résolution.
/// </summary>
/// <param name="Kind">Nature de l'emplacement.</param>
/// <param name="Id">Identifiant lié : CRC32 pour un lieu ou un hangar, identifiant de l'objet pour un objet, <c>null</c> sinon.</param>
/// <param name="Name">Nom résolu de l'emplacement.</param>
public sealed record ResolvedLocation(LocationKind Kind, ulong? Id, string Name)
{
    /// <summary>Emplacement utilisé quand aucune information n'a pu être obtenue.</summary>
    public static ResolvedLocation Unknown { get; } = new(LocationKind.Other, null, "Emplacement inconnu");

    /// <summary>Texte à afficher à l'utilisateur.</summary>
    public string DisplayName => Kind == LocationKind.Hangar ? $"Hangar · {Name}" : Name;

    /// <summary>Clé stable, qui distingue deux emplacements homonymes.</summary>
    public string Key => $"{Kind}:{Id}:{Name}";
}
