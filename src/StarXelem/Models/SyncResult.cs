namespace StarXelem.Models;

/// <summary>
/// Résultat de la synchronisation vers Alliance Orbital.
/// </summary>
public class SyncResult
{
    /// <summary>Indique si la synchronisation a réussi.</summary>
    public bool Success { get; set; }

    /// <summary>Nombre total d'items reçus dans la requête.</summary>
    public int Received { get; set; }

    /// <summary>Nombre d'items acceptés après filtrage des types exclus.</summary>
    public int Matched { get; set; }

    /// <summary>Nombre d'items effectivement inserts/mis a jour en base.</summary>
    public int Updated { get; set; }

    /// <summary>Nombre d'items obsoletes supprimes (syncedAt different).</summary>
    public int Removed { get; set; }

    /// <summary>Nombre d'items rejetes car leur type EntityIndex est dans la liste d'exclusion.</summary>
    public int Filtered { get; set; }
}
