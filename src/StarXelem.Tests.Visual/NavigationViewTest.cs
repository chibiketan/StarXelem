using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Tests;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Tests de navigation entre tous les onglets principaux via la NavigationView.
/// Vérifie que chaque page est accessible et que le ViewModel correspondant est chargé.
/// </summary>
public class NavigationViewTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public NavigationViewTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public void MainWindowViewModel_Has_All_Tabs_Registered()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();

        // Vérifier que les onglets attendus sont présents
        Assert.Contains(viewModel.Pages, p => p.Name == "Amis");
        Assert.Contains(viewModel.Pages, p => p.Name == "Blueprints");
        Assert.Contains(viewModel.Pages, p => p.Name == "Mon hangar");
        Assert.Contains(viewModel.Pages, p => p.Name == "Paramètres");
    }

    [AvaloniaFact]
    public void MainWindowViewModel_Default_Page_Is_First()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(viewModel.CurrentPage);
        Assert.Equal(viewModel.Pages.First().Name, viewModel.CurrentPage.Name);
    }

    [AvaloniaFact]
    public async Task MainWindow_Navigate_To_FriendList()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var friendTab = viewModel.Pages.FirstOrDefault(p => p.Name == "Amis");

        Assert.NotNull(friendTab);
        viewModel.CurrentPage = friendTab!;

        Assert.IsType<FriendListTabViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public async Task MainWindow_Navigate_To_ItemsTab()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var itemsTab = viewModel.Pages.FirstOrDefault(p => p.Name == "Objets");

        Assert.NotNull(itemsTab);
        viewModel.CurrentPage = itemsTab!;

        Assert.IsType<ItemsTabViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public async Task MainWindow_Navigate_To_Settings()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var settingsTab = viewModel.Pages.FirstOrDefault(p => p.Name == "Paramètres");

        Assert.NotNull(settingsTab);
        viewModel.CurrentPage = settingsTab!;

        Assert.IsType<SettingsTabViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public async Task MainWindow_Navigate_To_BlueprintList()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var blueprintTab = viewModel.Pages.FirstOrDefault(p => p.Name == "Blueprints");

        Assert.NotNull(blueprintTab);
        viewModel.CurrentPage = blueprintTab!;

        Assert.IsType<BlueprintListTabViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void MainWindow_Page_Switching_Is_Reversible()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var initialPage = viewModel.CurrentPage;

        // Naviguer vers un autre onglet
        var settingsTab = viewModel.Pages.First(p => p.Name == "Paramètres");
        viewModel.CurrentPage = settingsTab;
        Assert.Equal("Paramètres", viewModel.CurrentPage.Name);

        // Revenir à l'onglet initial
        viewModel.CurrentPage = initialPage;
        Assert.Equal(initialPage.Name, viewModel.CurrentPage.Name);
    }

    [AvaloniaFact]
    public async Task MainWindow_All_Tabs_Are_Loadable()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();

        foreach (var page in viewModel.Pages)
        {
            Assert.NotNull(page);
            await page.LoadAsync();
            Assert.True(page.IsLoaded, $"Page '{page.Name}' should be loaded after LoadAsync()");
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_View_Contains_NavigationView()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var view = new StarXelem.Views.MainWindow { DataContext = viewModel };
        view.Show();

        var navigationView = view.FindLogicalDescendantOfType<NavigationView>();
        Assert.NotNull(navigationView);
    }

    [AvaloniaFact]
    public async Task MainWindow_Screenshot_On_Different_Tabs()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var view = new StarXelem.Views.MainWindow { DataContext = viewModel };
        view.Show();
        view.Measure(Size.Infinity);
        view.Arrange(new Rect(default, view.DesiredSize));

        Dispatcher.UIThread.RunJobs();

        // Capture sur l'onglet par défaut
        var defaultShot = ScreenshotHelper.CaptureWindow(view, "mainwindow_default.png");
        Assert.True(File.Exists(defaultShot), "Screenshot par défaut doit exister");

        // Naviguer vers Paramètres et capturer
        var settingsTab = viewModel.Pages.First(p => p.Name == "Paramètres");
        viewModel.CurrentPage = settingsTab;
        Dispatcher.UIThread.RunJobs();

        var settingsShot = ScreenshotHelper.CaptureWindow(view, "mainwindow_settings.png");
        Assert.True(File.Exists(settingsShot), "Screenshot Paramètres doit exister");
    }

    public void Dispose() { }
}
