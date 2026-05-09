using Avalonia.Headless.XUnit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StarXelem.VisualJudge;

namespace StarXelem.Tests.Visual;

public class PixelDiffEngineTests : IDisposable
{
    private readonly string _tempDir;

    public PixelDiffEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stx_visual_tests_{Guid.NewGuid()}");
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
    public async Task Identical_Images_Return_100_Percent_Similarity()
    {
        string path = await SaveImage("solid_red.png", new Rgba32(255, 0, 0));

        double similarity = await PixelDiffEngine.SimilarityPercentAsync(path, path);

        Assert.True(Math.Abs(similarity - 100.0) < 0.01, $"Expected 100% but got {similarity:F4}%");
    }

    [AvaloniaFact]
    public async Task Completely_Different_Colors_Return_Near_Zero_Similarity()
    {
        string pathA = await SaveImage("solid_red.png", new Rgba32(255, 0, 0));
        string pathB = await SaveImage("solid_blue.png", new Rgba32(0, 0, 255));

        double similarity = await PixelDiffEngine.SimilarityPercentAsync(pathA, pathB);

        Assert.True(similarity < 0.1, $"Expected near-zero but got {similarity:F4}%");
    }

    [AvaloniaFact]
    public async Task Slightly_Different_Colors_Return_High_Similarity()
    {
        string pathA = await SaveImage("dark_red.png", new Rgba32(255, 0, 0));
        string pathB = await SaveImage("slightly_lighter_red.png", new Rgba32(248, 3, 3));

        double similarity = await PixelDiffEngine.SimilarityPercentAsync(pathA, pathB);

        Assert.True(similarity >= 95.0, $"Expected high similarity but got {similarity:F2}%");
    }

    [AvaloniaFact]
    public async Task GenerateHeatmap_Produces_Valid_Image()
    {
        string pathA = await SaveImage("solid_red.png", new Rgba32(255, 0, 0));
        string pathB = await SaveImage("solid_blue.png", new Rgba32(0, 0, 255));
        string outputPath = Path.Combine(_tempDir, "heatmap.png");

        string resultPath = await PixelDiffEngine.GenerateHeatmapAsync(pathA, pathB, outputPath);

        Assert.True(File.Exists(resultPath));
        Assert.Equal(outputPath.ToLowerInvariant(), resultPath.ToLowerInvariant());

        using var heatmap = await Image.LoadAsync<Rgba32>(resultPath);
        Assert.Equal(100, heatmap.Width);
        Assert.Equal(100, heatmap.Height);
    }

    [AvaloniaFact]
    public async Task Heatmap_On_Identical_Images_Has_No_Red_Pixels()
    {
        string path = await SaveImage("solid_green.png", new Rgba32(0, 255, 0));
        string outputPath = Path.Combine(_tempDir, "heatmap_identical.png");

        await PixelDiffEngine.GenerateHeatmapAsync(path, path, outputPath);

        using var heatmap = await Image.LoadAsync<Rgba32>(outputPath);
        Rgba32[] pixels = new Rgba32[100 * 100];
        heatmap.CopyPixelDataTo(pixels);

        for (int i = 0; i < pixels.Length; i++)
        {
            bool isRedDiff = pixels[i].R == 255 && pixels[i].G == 0 && pixels[i].B == 0 && pixels[i].A == 128;
            Assert.False(isRedDiff, $"Pixel {i} should not be a red diff marker");
        }
    }

    [AvaloniaFact]
    public async Task Different_Dimensions_Throw_ArgumentException()
    {
        string pathA = await SaveImage("small.png", new Rgba32(255, 0, 0), width: 50, height: 50);
        string pathB = await SaveImage("large.png", new Rgba32(0, 0, 255), width: 100, height: 100);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            PixelDiffEngine.SimilarityPercentAsync(pathA, pathB));

        Assert.True(exception.Message.ToLowerInvariant().Contains("dimensions"), $"Expected 'dimensions' in message: {exception.Message}");
    }
}
