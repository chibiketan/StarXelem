using Avalonia;
using StarXelem.Models;

namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignOverlayNotificationService : IOverlayNotificationService
{
    public Task ShowAsync(SignatureMatch match) => Task.CompletedTask;
    public Task ShowMessageAsync(string message, PixelPoint at) => Task.CompletedTask;
}
