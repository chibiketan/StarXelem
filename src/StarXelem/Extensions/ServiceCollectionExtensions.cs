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
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddDebug();
#else
            b.SetMinimumLevel(LogLevel.Warning);
#endif
            b.AddConsole();
        });
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ShipTabViewModel>();
        services.AddTransient<ItemsTabViewModel>();
        services.AddTransient<ContainerTabViewModel>();
        services.AddTransient<FriendListTabViewModel>();
        services.AddTransient<PopupViewModel>();
        services.AddTransient<LoadingPopupContentViewModel>();

        if (isDesignMode)
        {
            services.AddSingleton<IP4kService, DesignP4kService>();
            services.AddSingleton<IGrpcClientService, DesignGrpcClientService>();
            services.AddSingleton<ILocationService, DesignLocationService>();
        }
        else
        {
            services.AddSingleton<IP4kService, P4kService>();
            services.AddSingleton<IGrpcClientService, GrpcClientService>();
            services.AddSingleton<ILocationService, LocationService>();
        }
    }
}