using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

namespace StarXelem.Extensions;

public static class ServiceCollectionExtensions
{
    public static void RegisterServices(this ServiceCollection services, bool isDesignMode)
    {
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddDebug();
            b.AddConsole();
        });
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ShipTabViewModel>();
        services.AddTransient<ItemsTabViewModel>();
        services.AddTransient<ContainerTabViewModel>();

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