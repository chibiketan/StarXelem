namespace StarXelem.Tests.Visual;

public class ReferenceGenerationTest : IAsyncDisposable
{
    private readonly ReferenceImageGenerator _generator;

    private static string MockupPath(string fileName) => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "StarXelem", "maquettes", fileName);

    public ReferenceGenerationTest()
    {
        _generator = new ReferenceImageGenerator();
    }

    [Fact]
    public async Task Can_Generate_Extractions_Dark_Reference()
    {
        var path = MockupPath("extractions_screen.html");
        Assert.True(File.Exists(path), $"Maquette introuvable : {path}");

        var screenshot = await _generator.CaptureFromHtml(path, "_dark");
        Assert.True(File.Exists(screenshot));
    }

    [Fact]
    public async Task Can_Generate_Extractions_Light_Reference()
    {
        var path = MockupPath("extractions_screen_light.html");
        Assert.True(File.Exists(path), $"Maquette introuvable : {path}");

        var screenshot = await _generator.CaptureFromHtml(path);
        Assert.True(File.Exists(screenshot));
    }

    [Fact]
    public async Task Can_Generate_ConnectionBar_Reference()
    {
        var path = MockupPath("connection_status_bar.html");
        Assert.True(File.Exists(path), $"Maquette introuvable : {path}");

        var screenshot = await _generator.CaptureFromHtml(path);
        Assert.True(File.Exists(screenshot));
    }

    public async ValueTask DisposeAsync() => await _generator.DisposeAsync();
}
