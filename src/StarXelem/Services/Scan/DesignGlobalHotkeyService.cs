namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignGlobalHotkeyService : IGlobalHotkeyService
{
    public event EventHandler? HotkeyPressed { add { } remove { } }

    public bool TryApply(ScanHotkeySettings settings, out string? error)
    {
        error = null;
        return true;
    }

    public void Stop() { }
    public void Dispose() { }
}
