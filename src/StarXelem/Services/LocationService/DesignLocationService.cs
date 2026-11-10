using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

/// <summary>
/// Implémentation de design-time de <see cref="ILocationService"/> destinée au previewer Avalonia.
/// N'effectue aucun appel réseau ni lecture P4K : retourne des valeurs statiques pour permettre
/// l'affichage des vues en mode conception.
/// </summary>
public class DesignLocationService : ILocationService
{
    /// <inheritdoc/>
    public Task<ResolvedLocation?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult<ResolvedLocation?>(entityLocation == null ? null : new ResolvedLocation(LocationKind.Other, null, entityLocation));
    }

    /// <inheritdoc/>
    public Task<ResolvedLocation?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult<ResolvedLocation?>(new ResolvedLocation(LocationKind.Other, null, "ResolveLocation"));
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
    }

    /// <inheritdoc/>
    public void AddKnownEntities(IEnumerable<EntityItemQueryResult> entities)
    {
    }
}