namespace StarXelem.Services.Scan;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;

    /// <summary>(Ré)enregistre le raccourci global. Retourne false + message d'erreur si l'OS le refuse (déjà pris par une autre application).</summary>
    bool TryApply(ScanHotkeySettings settings, out string? error);

    /// <summary>Désenregistre le raccourci et arrête le thread d'écoute.</summary>
    void Stop();
}
