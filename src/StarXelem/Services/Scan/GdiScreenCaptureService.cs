using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using StarXelem.Constants;
using StarXelem.Models;
using StarXelem.Services.Scan.Win32;

namespace StarXelem.Services.Scan;

/// <summary>
/// Capture par GDI <c>BitBlt</c> la zone client de la fenêtre du jeu Star Citizen (mode fenêtré sans bordure
/// requis pour un résultat fiable). À défaut de trouver la fenêtre du jeu, capture le moniteur sous le curseur.
/// </summary>
public class GdiScreenCaptureService : IScreenCaptureService
{
    private readonly ILogger<GdiScreenCaptureService> _logger;

    public GdiScreenCaptureService(ILogger<GdiScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public Task<CapturedFrame?> CaptureGameWindowAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var (hwnd, x, y, width, height) = FindCaptureTarget();
                if (width <= 0 || height <= 0)
                {
                    _logger.LogWarning("Aucune zone capturable trouvée (fenêtre Star Citizen introuvable et pas de moniteur sous le curseur).");
                    return null;
                }

                var frame = Capture(x, y, width, height);
                sw.Stop();
                if (frame != null)
                {
                    _logger.LogInformation("Capture {Width}x{Height} à ({X},{Y}) en {Elapsed}ms (hwnd={Hwnd}).", width, height, x, y, sw.ElapsedMilliseconds, hwnd);
                    SaveDebugImage(frame.Bitmap, "capture.png");
                }
                return frame;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la capture d'écran.");
                return null;
            }
        }, ct);
    }

    private (IntPtr Hwnd, int X, int Y, int Width, int Height) FindCaptureTarget()
    {
        var hwnd = FindGameWindow();
        if (hwnd != IntPtr.Zero && !NativeMethods.IsIconic(hwnd) && NativeMethods.GetClientRect(hwnd, out var clientRect) && clientRect.Width > 0 && clientRect.Height > 0)
        {
            var topLeft = new POINT { X = 0, Y = 0 };
            NativeMethods.ClientToScreen(hwnd, ref topLeft);
            return (hwnd, topLeft.X, topLeft.Y, clientRect.Width, clientRect.Height);
        }

        // Fallback : pas de fenêtre Star Citizen trouvée. On capture le moniteur sous le curseur (nettement
        // plus petit et plus rapide/lisible qu'un fallback sur l'espace virtuel multi-écrans entier).
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var hMonitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                _logger.LogWarning("Fenêtre Star Citizen introuvable : capture du moniteur sous le curseur à la place.");
                return (IntPtr.Zero, monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top, monitorInfo.rcMonitor.Width, monitorInfo.rcMonitor.Height);
            }
        }

        // Dernier recours : écran virtuel multi-écrans entier.
        _logger.LogWarning("Fenêtre Star Citizen introuvable et aucun moniteur détecté sous le curseur : capture de l'espace virtuel entier.");
        return (IntPtr.Zero, NativeMethods.GetSystemMetrics(76 /* SM_XVIRTUALSCREEN */), NativeMethods.GetSystemMetrics(77 /* SM_YVIRTUALSCREEN */),
            NativeMethods.GetSystemMetrics(78 /* SM_CXVIRTUALSCREEN */), NativeMethods.GetSystemMetrics(79 /* SM_CYVIRTUALSCREEN */));
    }

    /// <summary>
    /// Trouve la plus grande fenêtre visible appartenant à un process Star Citizen, en énumérant toutes les
    /// fenêtres de premier niveau plutôt qu'en se fiant à <see cref="Process.MainWindowHandle"/> (peu fiable
    /// pour les jeux plein écran/bordure zéro, qui ne satisfont pas toujours l'heuristique de .NET).
    /// </summary>
    private IntPtr FindGameWindow()
    {
        var pids = new HashSet<uint>(Process.GetProcessesByName(ScanConstants.StarCitizenProcessName).Select(p => (uint)p.Id));
        if (pids.Count == 0) return IntPtr.Zero;

        var best = IntPtr.Zero;
        var bestArea = 0L;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (!pids.Contains(pid) || !NativeMethods.IsWindowVisible(hWnd) || !NativeMethods.GetWindowRect(hWnd, out var rect))
                return true; // continue l'énumération

            var area = (long)rect.Width * rect.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = hWnd;
            }
            return true;
        }, IntPtr.Zero);

        return best;
    }

    private CapturedFrame? Capture(int x, int y, int width, int height)
    {
        var hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return null;

        var hdcMem = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var pixelsPtr = IntPtr.Zero;

        try
        {
            hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
            var oldObj = NativeMethods.SelectObject(hdcMem, hBitmap);

            var ok = NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            if (!ok)
            {
                _logger.LogWarning("BitBlt a échoué (GetLastError={Error}).", Marshal.GetLastPInvokeError());
                return null;
            }

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                },
            };

            var rowBytes = width * 4;
            var bufferSize = rowBytes * height;
            pixelsPtr = Marshal.AllocHGlobal(bufferSize);

            var scanLines = NativeMethods.GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixelsPtr, ref bmi, NativeMethods.DIB_RGB_COLORS);
            NativeMethods.SelectObject(hdcMem, oldObj);

            if (scanLines == 0)
            {
                Marshal.FreeHGlobal(pixelsPtr);
                _logger.LogWarning("GetDIBits a échoué (GetLastError={Error}).", Marshal.GetLastPInvokeError());
                return null;
            }

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap();
            var installed = bitmap.InstallPixels(info, pixelsPtr, rowBytes, (addr, _) => Marshal.FreeHGlobal(addr));
            if (!installed)
            {
                Marshal.FreeHGlobal(pixelsPtr);
                bitmap.Dispose();
                return null;
            }

            return new CapturedFrame(bitmap, x, y);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero) NativeMethods.DeleteDC(hdcMem);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private void SaveDebugImage(SKBitmap bitmap, string fileName)
    {
        var debugDir = Environment.GetEnvironmentVariable(ScanConstants.DebugDirEnvVar);
        if (string.IsNullOrWhiteSpace(debugDir)) return;

        try
        {
            Directory.CreateDirectory(debugDir);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(Path.Combine(debugDir, fileName));
            data.SaveTo(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'écrire l'image de debug {FileName}.", fileName);
        }
    }
}
