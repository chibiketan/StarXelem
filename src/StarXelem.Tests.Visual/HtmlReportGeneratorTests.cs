using Avalonia.Headless.XUnit;
using StarXelem.VisualJudge;

namespace StarXelem.Tests.Visual;

public class HtmlReportGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public HtmlReportGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stx_report_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [AvaloniaFact]
    public async Task Generate_Creates_Html_File()
    {
        var result = ComparisonResult.Skipped("Ollama not available");
        string output = Path.Combine(_tempDir, "report.html");

        HtmlReportGenerator.Generate(result, output);

        Assert.True(File.Exists(output));
        string html = File.ReadAllText(output);
        Assert.Contains("<!DOCTYPE html>", html);
    }

    [AvaloniaFact]
    public async Task Generate_Directory_Is_Created_Automatically()
    {
        var result = ComparisonResult.Skipped("Ollama not available");
        string nestedPath = Path.Combine(_tempDir, "sub", "report.html");

        HtmlReportGenerator.Generate(result, nestedPath);

        Assert.True(File.Exists(nestedPath));
    }

    [AvaloniaFact]
    public async Task Skipped_Result_Shows_Skip_Status()
    {
        var result = ComparisonResult.Skipped("Ollama not available");
        string output = Path.Combine(_tempDir, "skip.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("<span class=\"skip\">SKIPPED</span>", html);
    }

    [AvaloniaFact]
    public async Task Skipped_Result_Shows_Reason_Banner()
    {
        var result = ComparisonResult.Skipped("Ollama not available");
        string output = Path.Combine(_tempDir, "skip.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("skipped-banner", html);
        Assert.Contains("Ollama not available", html);
    }

    [AvaloniaFact]
    public async Task Pass_Result_Shows_Pass_Status()
    {
        var result = new ComparisonResult(true, 95.0, [], "All looks good", false);
        string output = Path.Combine(_tempDir, "pass.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("<span class=\"pass\">PASS</span>", html);
    }

    [AvaloniaFact]
    public async Task Fail_Result_Shows_Fail_Status()
    {
        var result = new ComparisonResult(false, 42.5, [], "Major differences detected", false);
        string output = Path.Combine(_tempDir, "fail.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("<span class=\"fail\">FAIL</span>", html);
    }

    [AvaloniaFact]
    public async Task Gaps_Are_Rendered_In_Table()
    {
        var gaps = new[]
        {
            new GapRecord("Layout", "Missing button in toolbar", "critical"),
            new GapRecord("Color", "Slight color shift on sidebar", "minor"),
        };

        var result = new ComparisonResult(false, 78.3, gaps, "Some differences found", false);
        string output = Path.Combine(_tempDir, "gaps.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("Missing button in toolbar", html);
        Assert.Contains("Slight color shift on sidebar", html);
        Assert.Contains("severity-critical", html);
        Assert.Contains("severity-minor", html);
    }

    [AvaloniaFact]
    public async Task Semantic_Summary_Is_Included()
    {
        var summary = "The actual screenshot shows a slightly different layout compared to the reference. The toolbar is shifted 5px to the right.";
        var result = new ComparisonResult(true, 92.0, [], summary, false);
        string output = Path.Combine(_tempDir, "semantic.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("Semantic Analysis", html);
        Assert.Contains(summary, html);
    }

    [AvaloniaFact]
    public async Task Semantic_Summary_Is_Omitted_For_Empty_Results()
    {
        var result = new ComparisonResult(true, 100.0, [], "", false);
        string output = Path.Combine(_tempDir, "empty_summary.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.DoesNotContain("Semantic Analysis", html);
    }

    [AvaloniaFact]
    public async Task Score_Is_Displayed_In_Dashboard()
    {
        var result = new ComparisonResult(true, 87.45, [], "Good match", false);
        string output = Path.Combine(_tempDir, "score.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("87.45", html); // Semantic Score card
    }

    [AvaloniaFact]
    public async Task Gap_Count_Is_Displayed()
    {
        var gaps = new[]
        {
            new GapRecord("Layout", "Gap 1", "critical"),
            new GapRecord("Color", "Gap 2", "minor"),
            new GapRecord("Text", "Gap 3", "critical"),
        };

        var result = new ComparisonResult(false, 60.0, gaps, "Multiple issues", false);
        string output = Path.Combine(_tempDir, "count.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("Gaps Found", html);
    }

    [AvaloniaFact]
    public async Task Special_Characters_Are_Escaped()
    {
        var result = new ComparisonResult(false, 50.0,
            [new GapRecord("XSS", "<script>alert('xss')</script>", "critical")],
            "Summary with <special> & \"chars\"", false);
        string output = Path.Combine(_tempDir, "escape.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.DoesNotContain("<script>alert('xss')</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [AvaloniaFact]
    public async Task Returns_Output_Path()
    {
        var result = ComparisonResult.Skipped("test");
        string expectedPath = Path.Combine(_tempDir, "return_path.html");

        string returned = HtmlReportGenerator.Generate(result, expectedPath);

        Assert.Equal(expectedPath, returned);
    }

    [AvaloniaFact]
    public async Task Footer_Is_Present()
    {
        var result = ComparisonResult.Skipped("test");
        string output = Path.Combine(_tempDir, "footer.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        Assert.Contains("Generated by StarXelem Visual Judge", html);
    }

    [AvaloniaFact]
    public async Task Timestamp_Is_Included()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var result = ComparisonResult.Skipped("test");
        string output = Path.Combine(_tempDir, "timestamp.html");

        HtmlReportGenerator.Generate(result, output);

        string html = File.ReadAllText(output);
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.True(html.Contains(before.ToString("yyyy-MM-dd")), $"HTML should contain a date after {before}");
        Assert.True(html.Contains(after.ToString("yyyy-MM-dd")), $"HTML should contain a date before {after}");
    }
}
