namespace StarXelem.Models;

/// <summary>
/// Résultat de la synchronisation des blueprints vers Alliance Orbital.
/// </summary>
public class SyncResult
{
    /// <summary>Indique si la synchronisation a réussi.</summary>
    public bool Success { get; set; }

    /// <summary>Nombre de GUIDs reçus après déduplication par l'API.</summary>
    public int Received { get; set; }

    /// <summary>Nombre de blueprints reconnus en base de données.</summary>
    public int Matched { get; set; }

    /// <summary>Nombre de tiers de blueprint effectivement mis à jour.</summary>
    public int Updated { get; set; }
}
