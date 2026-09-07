using StarXelem.Models;

namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignScreenCaptureService : IScreenCaptureService
{
    public Task<CapturedFrame?> CaptureGameWindowAsync(CancellationToken ct = default) => Task.FromResult<CapturedFrame?>(null);
}
