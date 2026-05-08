using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Services;
using StarXelem.Tests.Visual.Services;

namespace StarXelem.Tests;

/// <summary>
/// Fixture xUnit qui initialise le DI container en mode test.
/// Utilise les memes registres que l'application principale pour garantir la coherence.
/// </summary>
public class HeadlessAppFixture : IDisposable
{
    public IServiceProvider Services { get; }

    public HeadlessAppFixture()
    {
        var collection = new ServiceCollection();
        RegisterTestServices(collection);
        Services = collection.BuildServiceProvider();
    }

    /// <summary>
    /// Enregistre tous les services mock necessaires aux tests headless.
    /// Utilise le meme registre que l'application principale en mode test, avec overrides pour le logging et settings.
    /// </summary>
    internal static void RegisterTestServices(ServiceCollection services)
    {
        // Use the same registration as the main app in test mode — ensures all ViewModels are registered.
        typeof(StarXelem.Extensions.ServiceCollectionExtensions)
            .GetMethod("RegisterServices")!
            .Invoke(null, new object[] { services, false, true });

        // Override: disable logging in tests (main app registers Warning level)
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));

        // Override: use design settings service (registry not available in tests)
        services.AddSingleton<ISettingsService, DesignSettingService>();
    }

    public void Dispose() { }
}
