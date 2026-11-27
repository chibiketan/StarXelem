namespace StarXelem.Services;

/// <summary>
/// Blueprints obtenus par le joueur, communs aux écrans Blueprints, Missions et Extraction.
/// </summary>
public interface IObtainedBlueprintService
{
    /// <summary>
    /// Blueprints obtenus : le dernier chargement enregistré s'il existe ; sinon, ou si <paramref name="forceRefresh"/> est vrai, lecture
    /// auprès du jeu s'il est connecté, puis enregistrement. Si le jeu ne répond pas, le cache reste la solution de repli.
    /// </summary>
    /// <param name="forceRefresh">Vrai pour interroger le jeu même si un chargement est enregistré (actualisation demandée).</param>
    /// <param name="token">Jeton d'annulation.</param>
    /// <returns>Les identifiants des blueprints obtenus, ou <c>null</c> si ni le jeu ni le cache ne les fournissent.</returns>
    Task<ObtainedBlueprints?> GetObtainedAsync(bool forceRefresh = false, CancellationToken token = default);
}

/// <summary>Identifiants des blueprints obtenus, avec leur date de récupération et leur provenance.</summary>
/// <param name="Ids">Identifiants (GUID) des blueprints obtenus.</param>
/// <param name="FetchedAt">Date de récupération auprès du jeu.</param>
/// <param name="IsFromCache">Vrai si la liste a été relue du cache.</param>
public sealed record ObtainedBlueprints(IReadOnlySet<string> Ids, DateTimeOffset FetchedAt, bool IsFromCache);
