using Microsoft.Playwright;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Utilise Playwright pour ouvrir des maquettes HTML dans un navigateur headless
/// et capturer des screenshots de référence pour la comparaison visuelle.
/// </summary>
public class ReferenceImageGenerator : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    public static string OutputDirectory { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "screenshots", "References");

    public ReferenceImageGenerator()
    {
        Directory.CreateDirectory(OutputDirectory);

        _playwright = Microsoft.Playwright.Playwright.CreateAsync().Result;
        _browser = _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        }).Result;
    }

    /// <summary>
    /// Ouvre une maquette HTML et capture un screenshot.
    /// </summary>
    /// <param name="htmlFilePath">Chemin absolu vers le fichier HTML.</param>
    /// <param name="suffix">Suffixe optionnel ajouté au nom de sortie (ex: "_dark", "_light").</param>
    public async Task<string> CaptureFromHtml(string htmlFilePath, string suffix = "")
    {
        var baseName = Path.GetFileNameWithoutExtension(htmlFilePath) + suffix;
        var outputPath = Path.Combine(OutputDirectory, $"{baseName}.png");

        var page = await _browser.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoAsync($"file://{Path.GetFullPath(htmlFilePath).Replace("\\", "/")}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = outputPath, FullPage = false });
        await page.CloseAsync();

        return outputPath;
    }

    /// <summary>
    /// Capture un screenshot d'un élément spécifique identifié par sélecteur CSS.
    /// </summary>
    public async Task<string> CaptureElement(string htmlFilePath, string selector, string elementName)
    {
        var outputPath = Path.Combine(OutputDirectory, $"{elementName}.png");

        var page = await _browser.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1080);
        await page.GotoAsync($"file://{Path.GetFullPath(htmlFilePath).Replace("\\", "/")}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var locator = page.Locator(selector);
        if (await locator.CountAsync() == 0)
            throw new ArgumentException($"Sélecteur '{selector}' non trouvé dans {htmlFilePath}");

        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = outputPath });
        await page.CloseAsync();

        return outputPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();

        if (_playwright is not null)
        {
            _playwright.Dispose();
        }
    }
}
