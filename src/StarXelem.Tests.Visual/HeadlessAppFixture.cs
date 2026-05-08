using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Design;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.Tests.Visual.Services;
using StarXelem.ViewModels;
using StarXelem.ViewModels.Popup;

namespace StarXelem.Tests;

/// <summary>
/// Fixture xUnit qui initialise le DI container en mode test.
/// Permet d'instancier des ViewModels puis de les rendre headless via Avalonia.Headless.
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
    /// Enregistre tous les services mock nécessaires aux tests headless.
    /// </summary>
    internal static void RegisterTestServices(ServiceCollection services)
    {
        services.AddLogging(b =>
        {
            // Disable logs
            b.SetMinimumLevel(LogLevel.None);
        });

        
        // Mock IGrpcClientService avec données prévisibles (du projet principal)
        services.AddSingleton<IGrpcClientService, TestGrpcClientService>();

        // Mock P4kService
        services.AddSingleton<IP4kService, DesignP4kService>();

        // Location service mock
        services.AddSingleton<ILocationService, DesignLocationService>();

        // Entity class definition
        services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();

        services.AddSingleton<ISettingsService, DesignSettingService>();

        // ViewModels nécessaires pour tous les onglets de l'application
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<FriendListTabViewModel>();
        services.AddTransient<BlueprintListTabViewModel>();
        services.AddTransient<P4kShipTabViewModel>();
        services.AddTransient<ShipTabViewModel>();
        services.AddTransient<ItemsTabViewModel>();
        services.AddTransient<ContainerTabViewModel>();
        services.AddTransient<ExtractionTabViewModel>();
        services.AddTransient<MissionsTabViewModel>();
        services.AddTransient<SettingsTabViewModel>();
        services.AddTransient<PopupViewModel>();
    }

    public void Dispose() { }
}
