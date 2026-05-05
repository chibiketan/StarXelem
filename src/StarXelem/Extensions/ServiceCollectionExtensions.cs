using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;
using StarXelem.ViewModels.Popup;

namespace StarXelem.Extensions;

public static class ServiceCollectionExtensions
{
    public static void RegisterServices(this ServiceCollection services, bool isDesignMode)
    {
        services.AddLogging(b =>
        {
#if DEBUG
            b.SetMinimumLevel(LogLevel.Warning);
            b.AddDebug();
#else
            b.SetMinimumLevel(LogLevel.Warning);
#endif
            b.AddConsole();
        });
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ShipTabViewModel>();
        services.AddTransient<P4kShipTabViewModel>();
        services.AddTransient<ItemsTabViewModel>();
        services.AddTransient<ContainerTabViewModel>();
        services.AddTransient<FriendListTabViewModel>();
        services.AddTransient<ExtractionTabViewModel>();
        services.AddTransient<MissionsTabViewModel>();
        services.AddTransient<SettingsTabViewModel>();
        services.AddTransient<PopupViewModel>();
        services.AddTransient<LoadingPopupContentViewModel>();
        services.AddTransient<ItemComparisonPopupContentViewModel>();
        services.AddTransient<SendToOrbitalAlliancePopupContentViewModel>();
        services.AddTransient<FleetSyncPopupContentViewModel>();
        services.AddTransient<ItemsSyncPopupContentViewModel>();

        if (isDesignMode)
        {
            services.AddSingleton<IP4kService, DesignP4kService>();
            services.AddSingleton<IGrpcClientService, DesignGrpcClientService>();
            services.AddSingleton<ILocationService, DesignLocationService>();
            services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();
            services.AddSingleton<BlueprintListTabViewModel>();
        }
        else
        {
            services.AddSingleton<IP4kService, P4kService>();
            services.AddSingleton<IGrpcClientService, GrpcClientService>();
            services.AddSingleton<ILocationService, LocationService>();
            services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();
            services.AddTransient<BlueprintListTabViewModel>();
        }

        // Les services indépendants du mode design
        services.AddSingleton<IMissionMappingService, MissionMappingService>();
        services.AddSingleton<ISettingsService, RegistrySettingsService>();

        // Service API externe pour la communication avec Alliance Orbital (profil utilisateur)
        services.AddSingleton<IAllianceOrbitalService, AllianceOrbitalService>();
    }
}