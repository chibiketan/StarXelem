using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

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
        // Mock IGrpcClientService avec données prévisibles (du projet principal)
        services.AddSingleton<IGrpcClientService, DesignGrpcClientService>();

        // Mock P4kService
        services.AddSingleton<IP4kService, DesignP4kService>();

        // Location service mock
        services.AddSingleton<ILocationService, DesignLocationService>();

        // Entity class definition
        services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();

        // ViewModels nécessaires pour FriendListTabViewModel et ses dépendances transitives
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<FriendListTabViewModel>();
    }

    public void Dispose() { }
}
