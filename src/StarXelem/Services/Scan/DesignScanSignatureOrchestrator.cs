namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignScanSignatureOrchestrator : IScanSignatureOrchestrator
{
    public string? LastError => null;

    public Task StartAsync() => Task.CompletedTask;

    public Task<(bool Success, string? Error)> ApplySettingsAsync(ScanHotkeySettings settings)
        => Task.FromResult<(bool, string?)>((true, null));

    public Task RunOnceAsync() => Task.CompletedTask;
}
