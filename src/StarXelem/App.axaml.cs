using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Extensions;
using StarXelem.ViewModels;
using StarXelem.Views;

namespace StarXelem;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (Design.IsDesignMode)
        {
            RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();

        collection.RegisterServices(Design.IsDesignMode);
        ViewLocator.RegisterViews();

        Services = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Depuis Avalonia 12, la validation DataAnnotations d'Avalonia est désactivée par défaut :
            // plus de doublon avec celle du CommunityToolkit, inutile de retirer le plugin.
            var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowVm,
            };

            // Gérer la configuration CLI (--screen, --screenshot, --close)
            _ = mainWindowVm.HandleLaunchConfigAsync();
        }

        if (Design.IsDesignMode)
        {
            // On trigger les données de design
            Console.WriteLine("NB blueprint au lancement {0}", DesignData.BlueprintListTabViewModel.BlueprintList?.Count);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public new static App Current => Application.Current as App ?? throw new InvalidOperationException("App.Current is null");

    public static IStorageProvider StorageProvider => (Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider ??
                                                      throw new InvalidOperationException("StorageProvider is null");

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

}