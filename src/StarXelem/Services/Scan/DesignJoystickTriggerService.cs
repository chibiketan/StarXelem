namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignJoystickTriggerService : IJoystickTriggerService
{
    public event EventHandler? Triggered { add { } remove { } }
    public event EventHandler? StatusChanged { add { } remove { } }

    public ScanTriggerStatus Status => ScanTriggerStatus.Disabled;

    public bool TryApply(ScanTriggerSettings settings, out string? error)
    {
        error = null;
        return true;
    }

    public Task<JoystickBinding?> CaptureNextButtonAsync(CancellationToken cancellationToken) => Task.FromResult<JoystickBinding?>(null);

    public void Stop() { }
    public void Dispose() { }
}
