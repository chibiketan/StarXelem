using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

/// <summary>
/// Service de résolution de localisation d'entités Star Citizen.
/// Convertit les identifiants bruts de conteneurs et d'emplacements (tels que retournés
/// par le graphe d'entités gRPC) en chaînes lisibles par l'utilisateur.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Résout une chaîne de localisation brute au format <c>"&lt;entityId&gt;:&lt;type&gt;:&lt;id&gt;"</c>
    /// en un nom d'emplacement lisible.
    /// </summary>
    /// <remarks>
    /// Si le type de l'entité trouvée ne figure pas dans <paramref name="allowedTypes"/>,
    /// la résolution remonte récursivement la chaîne de possession jusqu'à trouver
    /// un ancêtre d'un type autorisé.
    /// </remarks>
    /// <param name="entityLocation">
    /// Chaîne de localisation brute, par exemple <c>"123456:Container:789"</c>.
    /// Retourne <c>null</c> si la valeur est <c>null</c> ou vide.
    /// </param>
    /// <param name="allowedTypes">
    /// Liste optionnelle des types d'items acceptés comme emplacement final.
    /// Si <c>null</c> ou vide, le premier emplacement trouvé est retourné sans filtrage.
    /// </param>
    /// <returns>Le nom localisé de l'emplacement, ou <c>null</c> si aucun emplacement n'est disponible.</returns>
    Task<string?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null);

    /// <summary>
    /// Résout la localisation à partir d'un résultat de requête d'entité déjà obtenu,
    /// en suivant les arêtes du graphe d'entités (<c>AttachedTo</c>, <c>StowedIn</c>).
    /// </summary>
    /// <param name="entity">Résultat d'une requête sur le graphe d'entités du jeu.</param>
    /// <param name="allowedTypes">
    /// Liste optionnelle des types d'items acceptés comme emplacement final.
    /// Si <c>null</c> ou vide, le premier emplacement trouvé est retourné sans filtrage.
    /// </param>
    /// <returns>Le nom lisible de l'emplacement résolu.</returns>
    Task<string?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null);

    /// <summary>
    /// Vide les caches internes des entités et des emplacements résolus.
    /// À appeler lors d'un rechargement de données ou d'une déconnexion.
    /// </summary>
    void ClearCache();
}