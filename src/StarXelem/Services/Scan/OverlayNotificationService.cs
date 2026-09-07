using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using StarXelem.Constants;
using StarXelem.Models;
using StarXelem.ViewModels.Overlay;
using StarXelem.Views.Overlay;

namespace StarXelem.Services.Scan;

public class OverlayNotificationService : IOverlayNotificationService
{
    private readonly ILogger<OverlayNotificationService> _logger;
    private SignatureOverlayWindow? _currentWindow;
    private DispatcherTimer? _currentTimer;

    public OverlayNotificationService(ILogger<OverlayNotificationService> logger)
    {
        _logger = logger;
    }

    public Task ShowAsync(SignatureMatch match)
    {
        var signatureText = match.Candidate.Value.ToString("N0");
        // Pour les minerais minés à la main (FPS) ou au véhicule terrestre, la signature est générique par
        // catégorie et ne permet pas de distinguer le minéral exact : on l'indique pour éviter toute confiance excessive.
        var lines = match.Rows.Select(r => r.MiningType == nameof(StarXelem.Services.Mining.MiningKind.ShipOrSurface)
            ? $"{r.ClusterSize}× {r.MineralName}"
            : $"{r.ClusterSize}× {r.MineralName} ?");
        var position = new PixelPoint(match.Candidate.ScreenBounds.X, match.Candidate.ScreenBounds.Bottom + 4);
        return ShowInternalAsync(new SignatureOverlayViewModel(signatureText, lines), position);
    }

    public Task ShowMessageAsync(string message, PixelPoint at)
    {
        return ShowInternalAsync(new SignatureOverlayViewModel(string.Empty, new[] { message }), at);
    }

    private async Task ShowInternalAsync(SignatureOverlayViewModel viewModel, PixelPoint position)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CloseCurrent();

            var window = new SignatureOverlayWindow { DataContext = viewModel };
            window.Show();
            window.Position = ClampToScreen(window, position);

            _currentWindow = window;
            _currentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScanConstants.OverlayDurationMs) };
            _currentTimer.Tick += (_, _) => CloseCurrent();
            _currentTimer.Start();
        });
    }

    private void CloseCurrent()
    {
        _currentTimer?.Stop();
        _currentTimer = null;

        if (_currentWindow != null)
        {
            try
            {
                _currentWindow.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la fermeture de l'overlay.");
            }
            _currentWindow = null;
        }
    }

    private static PixelPoint ClampToScreen(Avalonia.Controls.Window window, PixelPoint desired)
    {
        var screen = window.Screens?.ScreenFromPoint(desired) ?? window.Screens?.Primary;
        if (screen == null) return desired;

        var bounds = screen.WorkingArea;
        var width = (int)window.Bounds.Width;
        var height = (int)window.Bounds.Height;

        var x = Math.Clamp(desired.X, bounds.X, Math.Max(bounds.X, bounds.Right - width));
        var y = Math.Clamp(desired.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height));
        return new PixelPoint(x, y);
    }
}
