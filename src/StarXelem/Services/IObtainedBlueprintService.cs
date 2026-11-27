namespace StarXelem.Services;

/// <summary>
/// Blueprints obtenus par le joueur, communs aux écrans Blueprints, Missions et Extraction.
/// </summary>
public interface IObtainedBlueprintService
{
    /// <summary>
    /// Lit les blueprints obtenus auprès du jeu s'il est connecté et les enregistre ; sinon, ou si le jeu ne répond pas, relit le dernier
    /// chargement enregistré.
    /// </summary>
    /// <param name="token">Jeton d'annulation.</param>
    /// <returns>Les identifiants des blueprints obtenus, ou <c>null</c> si ni le jeu ni le cache ne les fournissent.</returns>
    Task<ObtainedBlueprints?> GetObtainedAsync(CancellationToken token = default);
}

/// <summary>Identifiants des blueprints obtenus, avec leur date de récupération et leur provenance.</summary>
/// <param name="Ids">Identifiants (GUID) des blueprints obtenus.</param>
/// <param name="FetchedAt">Date de récupération auprès du jeu.</param>
/// <param name="IsFromCache">Vrai si la liste a été relue du cache.</param>
public sealed record ObtainedBlueprints(IReadOnlySet<string> Ids, DateTimeOffset FetchedAt, bool IsFromCache);
