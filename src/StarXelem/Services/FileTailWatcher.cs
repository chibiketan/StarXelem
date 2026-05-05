namespace StarXelem.Services;

/// <summary>
/// Surveille un fichier texte et émet des événements pour chaque ligne complète (terminée par \n).
/// Gère l'attente de création, le suivi en temps réel, et la détection de troncation.
/// </summary>
public class FileTailWatcher : IFileTailService
{
    private const int PollDelayMs = 500;
    private const int BufferSize = 4096;

    private FileSystemWatcher? _directoryWatcher;
    private FileStream? _fileStream;
    private StreamReader? _reader;
    private string _partialLine = string.Empty;
    private long _lastKnownSize;

    public FileState State { get; private set; } = FileState.Missing;

    public event EventHandler<FileTailEventArgs>? LineReceived;
    public event EventHandler<FileState>? StateChanged;

    public void Dispose()
    {
        Stop();
    }

    public async Task StartAsync(string filePath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!File.Exists(filePath))
            {
                SetState(FileState.Missing);
                await WaitForFileCreationAsync(filePath, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    return;
                continue;
            }

            SetState(FileState.Watching);
            _partialLine = string.Empty;

            if (!TryOpenFile(filePath))
            {
                await Task.Delay(PollDelayMs, ct).ConfigureAwait(false);
                continue;
            }

            await TailAsync(filePath, ct).ConfigureAwait(false);
            CleanupStream();
        }
    }

    /// <summary>
    /// Boucle principale : lit les nouvelles données et détecte la troncation.
    /// Sort uniquement si le fichier disparaît, est tronqué (après traitement), ou si le token est annulé.
    /// </summary>
    private async Task TailAsync(string filePath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int charsRead = ReadAvailableChars(out string text);

                if (charsRead > 0)
                {
                    _lastKnownSize = _fileStream!.Position;
                    EmitCompleteLines(text);
                    continue;
                }

                // Rien de nouveau — vérifier la troncation ou la suppression
                if (!File.Exists(filePath))
                    return;

                long currentFileSize = new FileInfo(filePath).Length;

                if (currentFileSize < _lastKnownSize)
                {
                    // Fichier tronqué : on signale et on repart depuis le début
                    SetState(FileState.Truncated);
                    CleanupStream();
                    _partialLine = string.Empty;

                    await Task.Delay(200, ct).ConfigureAwait(false);

                    if (!TryOpenFile(filePath))
                        return;

                    SetState(FileState.Watching);
                    continue;
                }

                await Task.Delay(PollDelayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private int ReadAvailableChars(out string text)
    {
        var buffer = new char[BufferSize];
        int charsRead = _reader!.Read(buffer, 0, buffer.Length);
        text = charsRead > 0 ? new string(buffer, 0, charsRead) : string.Empty;
        return charsRead;
    }

    /// <summary>
    /// Découpe le texte en lignes. Seules les lignes terminées par \n sont émises.
    /// Le reste est conservé dans _partialLine pour le prochain appel.
    /// </summary>
    private void EmitCompleteLines(string text)
    {
        string combined = _partialLine + text;
        var lines = combined.Split('\n');

        // Tous les segments sauf le dernier sont des lignes complètes
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string line = lines[i].TrimEnd('\r');
            LineReceived?.Invoke(this, new FileTailEventArgs(line));
        }

        // Le dernier segment est soit vide (texte finissait par \n), soit une ligne incomplète
        _partialLine = lines[^1];
    }

    private bool TryOpenFile(string filePath)
    {
        try
        {
            _fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: BufferSize,
                FileOptions.SequentialScan);

            _reader = new StreamReader(_fileStream);
            _lastKnownSize = 0;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attend la création du fichier via FileSystemWatcher sur le dossier parent.
    /// </summary>
    private async Task WaitForFileCreationAsync(string filePath, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return;

        var tcs = new TaskCompletionSource<bool>();

        _directoryWatcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = Path.GetFileName(filePath)
        };

        _directoryWatcher.Created += (_, _) => tcs.TrySetResult(true);
        _directoryWatcher.EnableRaisingEvents = true;

        try
        {
            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Annulé — OK
        }
        finally
        {
            CleanupDirectoryWatcher();
        }
    }

    private void SetState(FileState newState)
    {
        if (State == newState)
            return;

        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    private void CleanupStream()
    {
        _reader?.Dispose();
        _fileStream?.Dispose();
        _reader = null;
        _fileStream = null;
    }

    private void CleanupDirectoryWatcher()
    {
        if (_directoryWatcher != null)
        {
            _directoryWatcher.Dispose();
            _directoryWatcher = null;
        }
    }

    public void Stop()
    {
        CleanupStream();
        CleanupDirectoryWatcher();
    }
}