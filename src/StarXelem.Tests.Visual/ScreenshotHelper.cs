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
    /// Dimensions standard pour tous les screenshots, garantissant une comparaison pixel-perfect.
    /// Modifiable via la variable d'environnement SCREENSHOT_SIZE (ex: "1920x1080").
    /// </summary>
    public static Size ScreenshotSize { get; } = ParseSizeFromEnv();

    /// <summary>
    /// Capture le rendu d'une fenêtre et l'enregistre.
    /// Retourne le chemin du fichier PNG créé.
    /// Force les dimensions à ScreenshotSize pour une comparaison pixel-perfect.
    /// </summary>
    public static string CaptureWindow(Window window, string fileName)
    {
        Directory.CreateDirectory(_outputDir);

        window.Show();
        window.Measure(ScreenshotSize);
        window.Arrange(new Rect(default, ScreenshotSize));

        var bitmap = window.CaptureRenderedFrame()!;
        var filePath = Path.Combine(_outputDir, fileName);

        bitmap.Save(filePath);
        bitmap.Dispose();

        return filePath;
    }

    /// <summary>
    /// Capture le rendu d'un UserControl en l'encapsulant dans une fenêtre headless.
    /// Retourne le chemin du fichier PNG créé. Force les dimensions à ScreenshotSize.
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

        control.Measure(ScreenshotSize);
        control.Arrange(new Rect(default, ScreenshotSize));

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

    private static Size ParseSizeFromEnv()
    {
        string? env = Environment.GetEnvironmentVariable("SCREENSHOT_SIZE");
        if (string.IsNullOrWhiteSpace(env)) return new(1920, 1080);

        var parts = env.Split('x');
        if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
            return new Size(w, h);

        throw new InvalidOperationException($"Invalid SCREENSHOT_SIZE format: '{env}'. Expected 'widthxheight' (e.g. '1920x1080').");
    }
}
