namespace StarXelem.Services;

public interface IFileTailService : IDisposable
{
    /// <summary>État actuel du fichier surveillé.</summary>
    FileState State { get; }

    /// <summary>Événement déclenché pour chaque ligne complète lue dans le fichier.</summary>
    event EventHandler<FileTailEventArgs>? LineReceived;

    /// <summary>Événement déclenché lors d'un changement d'état du fichier.</summary>
    event EventHandler<FileState>? StateChanged;

    /// <summary>Démarre la surveillance du fichier spécifié.</summary>
    Task StartAsync(string filePath, CancellationToken ct);

    /// <summary>Arrête la surveillance et libère les ressources.</summary>
    void Stop();
}
