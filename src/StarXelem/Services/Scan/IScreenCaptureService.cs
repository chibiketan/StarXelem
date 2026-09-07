using StarXelem.Models;

namespace StarXelem.Services.Scan;

public interface IScreenCaptureService
{
    /// <summary>Capture la zone client de la fenêtre Star Citizen ; à défaut, le moniteur sous le curseur. Null si rien n'est capturable.</summary>
    Task<CapturedFrame?> CaptureGameWindowAsync(CancellationToken ct = default);
}
