using Spectre.Console;
using Spectre.Console.Cli;
using StarXelem.VisualJudge;

namespace StarXelem.ReportCLI.Commands;

public class CompareCommand : Command<CompareCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ACTUAL>")]
        public required string Actual { get; set; }

        [CommandArgument(1, "<REFERENCE>")]
        public required string Reference { get; set; }

        [CommandOption("-n|--name <NAME>")]
        public string? Name { get; set; } = "Comparison";

        [CommandOption("-o|--output <PATH>")]
        public string? OutputDir { get; set; } = ".";

        [CommandOption("--no-heatmap")]
        public bool NoHeatmap { get; set; }

        [CommandOption("-e|--endpoint <URL>")]
        public string? Endpoint { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        string actualPath = Path.GetFullPath(settings.Actual);
        string referencePath = Path.GetFullPath(settings.Reference);

        if (!File.Exists(actualPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Actual screenshot not found: {actualPath}");
            return 1;
        }

        if (!File.Exists(referencePath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Reference image not found: {referencePath}");
            return 2;
        }

        string outputDir = Path.GetFullPath(settings.OutputDir);
        Directory.CreateDirectory(outputDir);

        // --- Step 1: Pixel diff ---
        AnsiConsole.MarkupLine("[cyan]*[/] Running pixel diff...");
        double pixelSimilarity = 0;
        bool pixelDiffPossible = true;

        try
        {
            pixelSimilarity = PixelDiffEngine.SimilarityPercentAsync(actualPath, referencePath).GetAwaiter().GetResult();
            AnsiConsole.MarkupLine($"[green]Pixel similarity:[/] {pixelSimilarity:F2}%");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("dimensions"))
        {
            pixelDiffPossible = false;
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Images have different dimensions — pixel diff skipped.");
        }

        // --- Step 2: Heatmap ---
        string? heatmapPath = null;
        if (!settings.NoHeatmap && pixelDiffPossible)
        {
            string heatmapTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            heatmapPath = Path.Combine(outputDir, $"heatmap_{heatmapTimestamp}.png");

            AnsiConsole.MarkupLine("[cyan]*[/] Generating heatmap...");
            try
            {
                PixelDiffEngine.GenerateHeatmapAsync(actualPath, referencePath, heatmapPath).GetAwaiter().GetResult();
                AnsiConsole.MarkupLine($"[green]Heatmap saved:[/] {heatmapPath}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Heatmap generation failed: {ex.Message}");
                heatmapPath = null;
            }
        }

        // --- Step 3: Semantic analysis via Ollama ---
        AnsiConsole.MarkupLine("[cyan]*[/] Running semantic analysis...");
        var semanticResult = OllamaVisualJudge.CompareAsync(actualPath, referencePath, settings.Name, settings.Endpoint).GetAwaiter().GetResult();

        if (semanticResult.IsSkipped)
        {
            AnsiConsole.MarkupLine($"[yellow]*[/] {semanticResult.Summary}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Semantic score:[/] {semanticResult.Score:P2}");

            if (semanticResult.Gaps.Length > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Gaps detected:[/]");
                foreach (var gap in semanticResult.Gaps)
                {
                    string severityColor = gap.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
                    AnsiConsole.MarkupLineInterpolated($"  [{severityColor}]•{gap.Severity} {gap.Category}:[/] {gap.Description}");
                }
            }
        }

        // --- Step 4: Combined result for report ---
        ComparisonResult result;
        if (semanticResult.IsSkipped)
        {
            bool isCompliant = pixelSimilarity >= 95.0 || !pixelDiffPossible;
            var gaps = pixelSimilarity < 100.0 && pixelDiffPossible && !isCompliant
                ? new[] { new GapRecord("Pixel", $"Similitude pixel : {pixelSimilarity:F1}% (seuil 95%)", "critical") }
                : Array.Empty<GapRecord>();

            result = new ComparisonResult(
                isCompliant,
                pixelDiffPossible ? pixelSimilarity / 100.0 : 0.0,
                gaps,
                $"Ollama unavailable — score based on pixel diff only ({pixelSimilarity:F2}%)",
                false);
        }
        else
        {
            result = semanticResult;
        }

        // --- Step 5: Generate HTML report ---
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string safeName = settings.Name.Replace(" ", "_").Replace("é", "e").Replace("è", "e").Replace("à", "a");
        string reportPath = Path.Combine(outputDir, $"{safeName}_report_{timestamp}.html");

        AnsiConsole.MarkupLine("[cyan]*[/] Generating HTML report...");
        HtmlReportGenerator.Generate(result, reportPath);
        AnsiConsole.MarkupLine($"[green]Report saved:[/] {reportPath}");

        // --- Summary table ---
        var table = new Table()
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Status", result.IsCompliant ? "[green]PASS[/]" : "[red]FAIL[/]");
        table.AddRow("Score", $"{result.Score:P2}");
        table.AddRow("Pixel Similarity", pixelDiffPossible ? $"{pixelSimilarity:F1}%" : "N/A (different dimensions)");
        table.AddRow("Gaps", $"{(semanticResult.IsSkipped ? result.Gaps.Length : semanticResult.Gaps.Length)}");

        AnsiConsole.Render(table);

        return result.IsCompliant ? 0 : 3;
    }
}
