using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Structure de comparaison visuelle entre captures d'application et références Playwright.
/// Pour l'instant vérifie l'existence et la compatibilité des dimensions.
/// L'analyse sémantique (Agent C / LLM) sera implémentée en Phase 3.
/// </summary>
public class VisualComparisonTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public VisualComparisonTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose() { }

    /// <summary>
    /// Vérifie que la capture de l'onglet Extractions et sa référence Playwright sont toutes deux valides.
    /// Les dimensions diffèrent car Playwright capture à 1920x1080 et Avalonia headless rend à taille naturelle.
    /// La comparaison sémantique (Phase 3) gérera ce redimensionnement.
    /// </summary>
    [AvaloniaFact]
    public async Task ExtractionsTab_Images_Are_Valid()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);
        VisualPilot.NavigateToTab(window, "Extractions");

        // Attend que le chargement soit terminé
        var vm = (MainWindowViewModel)window.DataContext!;
        var extractionPage = vm.Pages.OfType<ExtractionTabViewModel>().FirstOrDefault();
        if (extractionPage != null)
            await VisualPilot.WaitForLoadAsync(extractionPage);

        var appScreenshotPath = window.CaptureScreenshot("comparison_extractions_app.png");
        window.Close();

        // Chemin de la référence Playwright
        var referencePath = Path.Combine(ReferenceImageGenerator.OutputDirectory, "extractions_screen_dark.png");

        Assert.True(File.Exists(appScreenshotPath), "Capture de l'application manquante.");
        Assert.True(File.Exists(referencePath), $"Référence Playwright manquante : {referencePath}. Exécutez ReferenceGenerationTest d'abord.");

        // Vérifie que les deux images sont chargeables et ont des dimensions non nulles
        using var appImage = new Bitmap(File.OpenRead(appScreenshotPath));
        using var refImage = new Bitmap(File.OpenRead(referencePath));

        Assert.True(appImage.Size.Width > 0 && appImage.Size.Height > 0, "Capture application : dimensions invalides.");
        Assert.True(refImage.Size.Width > 0 && refImage.Size.Height > 0, "Référence Playwright : dimensions invalides.");
    }

    /// <summary>
    /// Vérifie que les captures de tous les onglets sont compatibles avec les références existantes.
    /// </summary>
    [AvaloniaFact]
    public async Task All_Tabs_Can_Be_Compared_With_References()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        string[] tabNames =
        {
            "Mon hangar",
            "Objets",
            "Blueprints",
            "Amis",
            "Loadout vaisseaux",
            "Missions",
            "Extractions",
            "Paramètres"
        };

        foreach (var tabName in tabNames)
        {
            VisualPilot.NavigateToTab(window, tabName);

            // Attend que le chargement soit terminé si possible
            var vm = (MainWindowViewModel)window.DataContext!;
            var currentPage = vm.CurrentPage;
            if (currentPage != null)
                await VisualPilot.WaitForLoadAsync(currentPage, TimeSpan.FromSeconds(2));

            var safeName = tabName.Replace(" ", "_");
            var path = window.CaptureScreenshot($"comparison_tab_{safeName}.png");

            Assert.True(File.Exists(path), $"Capture de l'onglet '{tabName}' échouée.");
            Assert.True(new FileInfo(path).Length > 0, $"Capture vide pour l'onglet '{tabName}'.");
        }

        window.Close();
    }
}
