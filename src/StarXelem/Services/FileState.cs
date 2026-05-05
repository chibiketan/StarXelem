namespace StarXelem.Services;

/// <summary>
/// État actuel du fichier surveillé.
/// </summary>
public enum FileState
{
    /// <summary>Le fichier n'existe pas — en attente de création.</summary>
    Missing,

    /// <summary>Le fichier existe et est surveillé pour de nouvelles lignes.</summary>
    Watching,

    /// <summary>Le fichier a été tronqué (rotation du log) — le buffer doit être reset.</summary>
    Truncated
}
