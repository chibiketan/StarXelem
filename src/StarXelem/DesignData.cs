using Microsoft.Extensions.DependencyInjection;
using StarXelem.ViewModels;

namespace StarXelem;

public static class DesignData
{
    public static MainWindowViewModel MainWindowViewModel { get; } = App.Current.Services.GetRequiredService<MainWindowViewModel>();
    public static ShipTabViewModel ShipTabViewModel { get; } = App.Current.Services.GetRequiredService<ShipTabViewModel>();
    public static ItemsTabViewModel ItemsTabViewModel { get; } = App.Current.Services.GetRequiredService<ItemsTabViewModel>();
    public static ContainerTabViewModel ContainerTabViewModel { get; } = App.Current.Services.GetRequiredService<ContainerTabViewModel>();
}