using Avalonia.Headless.XUnit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StarXelem.VisualJudge;

namespace StarXelem.Tests.Visual;

public class OllamaVisualJudgeTests : IDisposable
{
    private readonly string _tempDir;

    public OllamaVisualJudgeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stx_judge_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    async Task<string> SaveImage(string fileName, Rgba32 color, int width = 100, int height = 100)
    {
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = color;
            }
        });

        var path = Path.Combine(_tempDir, fileName);
        await img.SaveAsync(path);
        return path;
    }

    [AvaloniaFact]
    public async Task Ollama_Not_Available_Returns_Skipped_Result()
    {
        string pathA = await SaveImage("red.png", new Rgba32(255, 0, 0));
        string pathB = await SaveImage("blue.png", new Rgba32(0, 0, 255));

        var result = await OllamaVisualJudge.CompareAsync(pathA, pathB, "TestPage");

        Assert.NotNull(result);
        Assert.True(result.IsSkipped, "Expected a skipped result when Ollama is not running.");
    }

    [AvaloniaFact]
    public async Task Skipped_Result_Has_Zero_Score()
    {
        string pathA = await SaveImage("a.png", new Rgba32(100, 50, 25));
        string pathB = await SaveImage("b.png", new Rgba32(25, 50, 100));

        var result = await OllamaVisualJudge.CompareAsync(pathA, pathB, "EmptyPage");

        Assert.True(result.IsSkipped);
        Assert.Equal(0.0, result.Score);
    }

    [AvaloniaFact]
    public async Task Skipped_Result_Contains_Reason()
    {
        string path = await SaveImage("single.png", new Rgba32(128, 128, 128));

        var result = await OllamaVisualJudge.CompareAsync(path, path, "SamePage");

        Assert.True(result.IsSkipped);
        Assert.False(string.IsNullOrEmpty(result.Summary), "A skipped result should contain a reason.");
    }

    [AvaloniaFact]
    public async Task Skipped_Result_Has_No_Gaps()
    {
        string path = await SaveImage("gray.png", new Rgba32(64, 64, 64));

        var result = await OllamaVisualJudge.CompareAsync(path, path, "GrayPage");

        Assert.True(result.IsSkipped);
        Assert.Empty(result.Gaps);
    }
}
