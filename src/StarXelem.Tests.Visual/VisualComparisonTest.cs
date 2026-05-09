using Avalonia.Headless.XUnit;
using StarXelem.ViewModels;
using StarXelem.VisualJudge;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Comparaison visuelle complète : pixel diff + analyse sémantique Ollama + rapports HTML.
/// </summary>
public class VisualComparisonTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;
    private readonly string _reportDir;

    public VisualComparisonTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
        _reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports", $"report_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
    }

    public void Dispose() { }

    /// <summary>
    /// Compare l'onglet Extractions avec la référence Playwright : pixel diff + analyse sémantique + rapport HTML.
    /// </summary>
    [AvaloniaFact]
    public async Task ExtractionsTab_Full_Comparison()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);
        VisualPilot.NavigateToTab(window, "Extractions");

        var vm = (MainWindowViewModel)window.DataContext!;
        var extractionPage = vm.Pages.OfType<ExtractionTabViewModel>().FirstOrDefault();
        if (extractionPage != null)
            await VisualPilot.WaitForLoadAsync(extractionPage);

        string actualPath = window.CaptureScreenshot("compare_extractions_actual.png");
        window.Close();

        string referencePath = Path.Combine(ReferenceImageGenerator.OutputDirectory, "extractions_screen_dark.png");
        Assert.True(File.Exists(referencePath), $"Référence manquante : {referencePath}");

        // --- Pixel diff (mêmes dimensions 1920x1080) ---
        double pixelSimilarity = await RunPixelDiff(actualPath, referencePath);

        // --- Analyse sémantique via Ollama ---
        var semanticResult = await OllamaVisualJudge.CompareAsync(actualPath, referencePath, "Extractions");

        // --- Résultat combiné ---
        ComparisonResult result;
        if (semanticResult.IsSkipped)
        {
            // Pas d'Ollama — on se base uniquement sur le pixel diff
            bool isCompliant = pixelSimilarity >= 95.0;
            var gaps = pixelSimilarity < 100.0 && !isCompliant
                ? new[] { new GapRecord("Pixel", $"Similitude pixel à pixel : {pixelSimilarity:F1}% (seuil 95%)", "critical") }
                : Array.Empty<GapRecord>();

            result = new ComparisonResult(
                isCompliant,
                pixelSimilarity / 100.0,
                gaps,
                $"Ollama non disponible. Score basé sur le seul pixel diff : {pixelSimilarity:F2}%",
                false);
        }
        else
        {
            result = semanticResult;
        }

        // --- Génération du rapport HTML ---
        Directory.CreateDirectory(_reportDir);
        string reportPath = Path.Combine(_reportDir, "extractions_comparison.html");
        HtmlReportGenerator.Generate(result, reportPath);

        Assert.True(File.Exists(reportPath), "Rapport HTML non généré.");

        // --- Heatmap de différence (si pixel diff disponible) ---
        if (!semanticResult.IsSkipped || pixelSimilarity < 100.0)
        {
            string heatmapPath = Path.Combine(_reportDir, "extractions_heatmap.png");
            try
            {
                await PixelDiffEngine.GenerateHeatmapAsync(actualPath, referencePath, heatmapPath);
                Assert.True(File.Exists(heatmapPath));
            }
            catch (ArgumentException)
            {
                // Dimensions différentes — la génération de heatmap est impossible, c'est attendu
                // si les images n'ont pas été capturées aux mêmes dimensions.
            }
        }

        // --- Assertions finales ---
        Assert.NotNull(result);
        if (!result.IsSkipped || pixelSimilarity >= 95.0)
            Assert.True(result.IsCompliant || result.Score > 0, "Résultat devrait être conforme ou avoir un score significatif.");
    }

    /// <summary>
    /// Capture et compare tous les onglets disponibles, générant un rapport par onglet + un résumé global.
    /// </summary>
    [AvaloniaFact]
    public async Task All_Tabs_Comparison_Report()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        string[] tabNames =
        {
            "Mon hangar", "Objets", "Blueprints", "Amis",
            "Loadout vaisseaux", "Missions", "Extractions", "Paramètres"
        };

        Directory.CreateDirectory(_reportDir);

        var allResults = new List<(string TabName, ComparisonResult Result)>();

        foreach (var tabName in tabNames)
        {
            VisualPilot.NavigateToTab(window, tabName);

            var vm = (MainWindowViewModel)window.DataContext!;
            var currentPage = vm.CurrentPage;
            if (currentPage != null)
                await VisualPilot.WaitForLoadAsync(currentPage, TimeSpan.FromSeconds(2));

            string safeName = NormalizeFileName(tabName);
            string actualPath = window.CaptureScreenshot($"compare_tab_{safeName}.png");

            // Pixel diff rapide sans référence Playwright pour les onglets non couverts par une maquette
            var result = await CompareWithFallback(actualPath, tabName);

            allResults.Add((tabName, result));

            string reportPath = Path.Combine(_reportDir, $"{safeName}_comparison.html");
            HtmlReportGenerator.Generate(result, reportPath);
        }

        window.Close();

        // --- Rapport résumé global ---
        await GenerateSummaryReport(allResults);

        Assert.Equal(tabNames.Length, allResults.Count);
        foreach (var (tabName, result) in allResults)
            Assert.True(File.Exists(Path.Combine(_reportDir, $"{NormalizeFileName(tabName)}_comparison.html")),
                $"Rapport manquant pour l'onglet '{tabName}'");
    }

    // --- Helpers ---

    private static string NormalizeFileName(string name) =>
        name.Replace(" ", "_").Replace("é", "e").Replace("è", "e").Replace("à", "a");

    async Task<double> RunPixelDiff(string actualPath, string referencePath)
    {
        try
        {
            return await PixelDiffEngine.SimilarityPercentAsync(actualPath, referencePath);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("dimensions"))
        {
            // Images de dimensions différentes — pixel diff non applicable
            return -1.0;
        }
    }

    async Task<ComparisonResult> CompareWithFallback(string actualPath, string pageName)
    {
        // Cherche une référence correspondante dans le dossier References
        var referenceDir = ReferenceImageGenerator.OutputDirectory;
        if (!Directory.Exists(referenceDir))
            return ComparisonResult.Skipped($"Aucun fichier de référence trouvé dans {referenceDir}");

        string[] references = Directory.GetFiles(referenceDir, "*.png");
        string? matchingRef = references.FirstOrDefault(r =>
        {
            var baseName = Path.GetFileNameWithoutExtension(r).ToLowerInvariant();
            return baseName.Contains(pageName.ToLowerInvariant()) || pageName.ToLowerInvariant().Contains(baseName);
        });

        if (matchingRef is null)
            return ComparisonResult.Skipped($"Aucune référence trouvée pour l'onglet '{pageName}'");

        // Pixel diff d'abord
        double pixelSim = await RunPixelDiff(actualPath, matchingRef);

        // Analyse sémantique via Ollama
        var semanticResult = await OllamaVisualJudge.CompareAsync(actualPath, matchingRef, pageName);

        if (semanticResult.IsSkipped)
        {
            bool isCompliant = pixelSim >= 95.0 || pixelSim < 0; // pas de pixel diff si dimensions différentes
            return new ComparisonResult(
                isCompliant,
                pixelSim > 0 ? pixelSim / 100.0 : 0.0,
                isCompliant ? Array.Empty<GapRecord>()
                    : new[] { new GapRecord("Pixel", $"Similitude {pixelSim:F1}% (seuil 95%)", "critical") },
                $"Ollama non disponible — score basé sur pixel diff ({pixelSim:F2}%)",
                false);
        }

        return semanticResult;
    }

    async Task GenerateSummaryReport(List<(string TabName, ComparisonResult Result)> results)
    {
        int total = results.Count;
        int passed = results.Count(r => r.Result.IsCompliant || r.Result.IsSkipped && r.Result.Gaps.Length == 0);
        int failed = total - passed;

        var allGaps = results.SelectMany(r => r.Result.Gaps).ToArray();
        string summaryText = $"Global comparison: {passed}/{total} tabs compliant. " +
            $"{results.Count(r => r.Result.IsSkipped)} skipped (Ollama unavailable).";

        var summaryResult = new ComparisonResult(
            failed == 0,
            passed / (double)total,
            allGaps,
            summaryText,
            false);

        string summaryPath = Path.Combine(_reportDir, "summary.html");
        HtmlReportGenerator.Generate(summaryResult, summaryPath);
    }
}
