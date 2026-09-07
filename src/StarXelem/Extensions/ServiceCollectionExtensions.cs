using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Data;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.Services.Mining;
using StarXelem.Services.Scan;
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
            b.SetMinimumLevel(LogLevel.Information);
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
        services.AddTransient<ReputationTabViewModel>();

        if (isDesignMode)
        {
            services.AddSingleton<IP4kService, DesignP4kService>();
            services.AddSingleton<IGrpcClientService, DesignGrpcClientService>();
            services.AddSingleton<ILocationService, DesignLocationService>();
            services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();
            services.AddSingleton<BlueprintListTabViewModel>();
            services.AddSingleton<ILocalDatabaseService, DesignLocalDatabaseService>();
            services.AddSingleton<ISettingsService, DesignSettingsService>();
            services.AddSingleton<IScreenCaptureService, DesignScreenCaptureService>();
            services.AddSingleton<ISignatureOcrService, DesignSignatureOcrService>();
            services.AddSingleton<IOverlayNotificationService, DesignOverlayNotificationService>();
            services.AddSingleton<IGlobalHotkeyService, DesignGlobalHotkeyService>();
            services.AddSingleton<IScanSignatureOrchestrator, DesignScanSignatureOrchestrator>();
        }
        else
        {
            services.AddSingleton<IP4kService, P4kService>();
            services.AddSingleton<IGrpcClientService, GrpcClientService>();
            services.AddSingleton<ILocationService, LocationService>();
            services.AddSingleton<IEntityClassDefinitionService, EntityClassDefinitionService>();
            services.AddTransient<BlueprintListTabViewModel>();
            services.AddSingleton<ILocalDatabaseService>(sp =>
                new LocalDatabaseService(
                    sp.GetRequiredService<IP4kService>(),
                    sp.GetRequiredService<ILogger<LocalDatabaseService>>(),
                    sp.GetRequiredService<ISettingsService>(),
                    sp.GetRequiredService<IDbContextFactory>(),
                    sp.GetRequiredService<IMineralSignatureExtractor>(),
                    autoRebuild: false));
            services.AddSingleton<ISettingsService, RegistrySettingsService>();
            services.AddSingleton<IScreenCaptureService, GdiScreenCaptureService>();
            services.AddSingleton<ISignatureOcrService, WindowsSignatureOcrService>();
            services.AddSingleton<IOverlayNotificationService, OverlayNotificationService>();
            services.AddSingleton<IGlobalHotkeyService, Win32GlobalHotkeyService>();
            services.AddSingleton<IScanSignatureOrchestrator, ScanSignatureOrchestrator>();
        }

        // Les services indépendants du mode design
        services.AddSingleton<IBlueprintMappingService, BlueprintMappingService>();
        services.AddSingleton<IMissionMappingService, MissionMappingService>();
        services.AddSingleton<IReputationService, ReputationService>();
        services.AddSingleton<IDbContextFactory, DbContextFactory>();
        services.AddSingleton<ILocationRepository, LocationRepository>();
        services.AddSingleton<IScItemRepository, ScItemRepository>();
        services.AddSingleton<ILocaleEntryRepository, LocaleEntryRepository>();
        services.AddSingleton<IMineralSignatureExtractor, MineralSignatureExtractor>();
        services.AddSingleton<IMineralSignatureRepository, MineralSignatureRepository>();

        // Service API externe pour la communication avec Alliance Orbital (profil utilisateur)
        services.AddSingleton<IAllianceOrbitalService, AllianceOrbitalService>();
    }
}