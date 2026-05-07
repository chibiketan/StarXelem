using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(StarXelem.Tests.Visual.TestAppBuilder))]

namespace StarXelem.Tests.Visual;


public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions()
        {
            UseHeadlessDrawing = false
        });
}