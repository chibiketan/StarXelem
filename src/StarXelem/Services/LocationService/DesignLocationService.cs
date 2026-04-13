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
    public Task<string?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult(entityLocation);
    }

    /// <inheritdoc/>
    public Task<string?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null)
    {
        return Task.FromResult<string?>("ResolveLocation");
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
    }
}