namespace StarXelem.Services;

/// <summary>
/// Mock design-time pour IFileTailService.
/// </summary>
public class DesignFileTailWatcher : IFileTailService
{
    public event EventHandler<FileTailEventArgs>? LineReceived;
    public event EventHandler<FileState>? StateChanged;
    public FileState State => FileState.Missing;

    public Task StartAsync(string filePath, CancellationToken ct) => Task.CompletedTask;

    public void Stop() { }

    public void Dispose() { }
}
