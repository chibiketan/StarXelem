namespace StarXelem.Models;

/// <summary>
/// Emplacements résolus des objets d'un chargement, enregistrés dans le cache pour être réaffichés jeu fermé.
/// </summary>
/// <param name="ItemsFetchedAt">Date de récupération du chargement d'objets auquel ces emplacements correspondent.</param>
/// <param name="Locations">Emplacement résolu, par identifiant d'objet.</param>
public sealed record CachedItemLocations(DateTimeOffset ItemsFetchedAt, Dictionary<ulong, ResolvedLocation> Locations);
