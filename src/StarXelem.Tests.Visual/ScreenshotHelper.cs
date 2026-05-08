using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Utilitaire pour capturer des rendus headless avec chemins déterministes.
/// </summary>
public static class ScreenshotHelper
{
    private static readonly string _outputDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "screenshots");

    /// <summary>
    /// Capture le rendu d'une fenêtre et l'enregistre.
    /// Retourne le chemin du fichier PNG créé.
    /// </summary>
    public static string CaptureWindow(Window window, string fileName)
    {
        Directory.CreateDirectory(_outputDir);

        window.Show();
        window.Measure(Size.Infinity);
        window.Arrange(new Rect(default, window.DesiredSize));

        var bitmap = window.CaptureRenderedFrame()!;
        var filePath = Path.Combine(_outputDir, fileName);

        bitmap.Save(filePath);
        bitmap.Dispose();

        return filePath;
    }

    /// <summary>
    /// Capture le rendu d'un UserControl en l'encapsulant dans une fenêtre headless.
    /// Retourne le chemin du fichier PNG créé.
    /// </summary>
    public static string CaptureControl(Control control, string fileName)
    {
        Directory.CreateDirectory(_outputDir);

        var wrapper = new Window
        {
            Content = new Border
            {
                Child = control,
                Padding = new Thickness(0),
            }
        };
        wrapper.Show();

        control.Measure(Size.Infinity);
        control.Arrange(new Rect(default, control.DesiredSize));

        var bitmap = wrapper.CaptureRenderedFrame()!;
        var filePath = Path.Combine(_outputDir, fileName);

        bitmap.Save(filePath);
        bitmap.Dispose();

        return filePath;
    }

    /// <summary>
    /// Retourne le chemin complet du dossier de sortie.
    /// </summary>
    public static string OutputDirectory => _outputDir;
}
