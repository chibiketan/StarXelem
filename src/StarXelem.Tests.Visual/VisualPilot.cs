using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Operations courantes de pilotage headless : ouverture, navigation, capture, interaction.
/// </summary>
public static class VisualPilot
{
    /// <summary>
    /// Ouvre MainWindow avec initialisation complete de l'application (DI + App.Current.Services).
    /// Initialise manuellement les services de test pour que MainWindowViewModel puisse resoudre
    /// ses pages via App.Current.Services.GetRequiredService<T>().
    /// </summary>
    public static async Task<StarXelem.Views.MainWindow> OpenAppAsync(HeadlessAppFixture fixture)
    {
        // [AvaloniaFact] + TestAppBuilder already created App and set Application.Current.
        // But the headless lifetime isn't IClassicDesktopStyleApplicationLifetime, so
        // OnFrameworkInitializationCompleted skipped DI registration and MainWindow creation.
        // We replace App.Services with a test provider containing all mock services.

        var app = App.Current; // safe — TestAppBuilder.Configure<App>() already set it up

        var collection = new ServiceCollection();
        typeof(StarXelem.Extensions.ServiceCollectionExtensions)
            .GetMethod("RegisterServices")!
            .Invoke(null, new object[] { collection, false, true }); // isDesignMode:false, isTestMode:true

        StarXelem.ViewLocator.RegisterViews();

        var serviceProvider = collection.BuildServiceProvider();
        typeof(StarXelem.App).GetProperty("Services")!.SetValue(app, serviceProvider);

        var viewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        var window = new StarXelem.Views.MainWindow { DataContext = viewModel };
        window.Show();
        window.Measure(Size.Infinity);
        window.Arrange(new Rect(default, window.DesiredSize));

        await Dispatcher.UIThread.InvokeAsync(() => { });
        return window;
    }

    /// <summary>
    /// Navigue vers l'onglet nomme via le ViewModel et force un nouveau layout pass.
    /// </summary>
    public static void NavigateToTab(StarXelem.Views.MainWindow window, string tabName)
    {
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var target = viewModel.Pages.FirstOrDefault(p => p.Name == tabName);
        if (target is null)
            throw new ArgumentException($"Onglet '{tabName}' introuvable.");

        viewModel.CurrentPage = target;
        Dispatcher.UIThread.RunJobs();

        // Force un nouveau cycle de layout pour que le ContentControl rende son contenu
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Capture un screenshot de la fenetre et l'enregistre dans le dossier screenshots/.
    /// Retourne le chemin du fichier PNG.
    /// </summary>
    public static string CaptureScreenshot(this StarXelem.Views.MainWindow window, string fileName)
    {
        return ScreenshotHelper.CaptureWindow(window, fileName);
    }

    /// <summary>
    /// Trouve un controle par son attribut Name depuis n'importe quel noeud de l'arbre logique.
    /// </summary>
    public static T? FindLogicalControl<T>(this ILogical root, string name) where T : Control
    {
        foreach (var node in root.GetSelfAndLogicalDescendants())
            if (node is Control c && c.Name == name)
                return node as T;

        return default;
    }

    /// <summary>
    /// Simule un clic sur un bouton trouve par son nom en executant directement sa Command.
    /// Plus fiable que la simulation d'evenements souris en mode headless.
    /// Cherche depuis le controle racine fourni (utile pour les contenus de page).
    /// </summary>
    public static bool ClickButton(this Control root, string buttonName)
    {
        var button = ((ILogical)root).FindLogicalControl<Button>(buttonName);
        if (button is null)
            return false;

        var command = button.Command;
        if (command != null)
        {
            // In headless tests, CanExecute may not reflect actual state due to binding timing.
            // Execute directly — we're in a controlled test environment with known mock state.
            command.Execute(button.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Simule un clic sur un bouton trouve par son nom dans toute la fenetre principale.
    /// </summary>
    public static bool ClickButton(this StarXelem.Views.MainWindow window, string buttonName)
    {
        return ((Control)window).ClickButton(buttonName);
    }

    /// <summary>
    /// Attend que le chargement d'une page soit termine (IsLoading passe a faux).
    /// </summary>
    public static async Task WaitForLoadAsync(this PageViewModelBase page, TimeSpan timeout = default)
    {
        timeout = timeoutOrDefault(timeout);
        var deadline = DateTime.UtcNow + timeout;

        while (page.GetType().GetProperty("IsLoading")?.GetValue(page) is bool loading && loading)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timeout en attendant que la page '{page.Name}' termine son chargement.");

            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Retourne le NavigationView de la fenetre principale.
    /// </summary>
    public static NavigationView? FindNavigationView(this StarXelem.Views.MainWindow window)
    {
        return window.FindLogicalDescendantOfType<NavigationView>(includeSelf: false);
    }

    /// <summary>
    /// Retourne le controle racine de la page active.
    /// Le ContentControl.Content retourne le ViewModel brut (non resolu par DataTemplate),
    /// donc on cherche directement la vue par son type dans l'arbre logique.
    /// </summary>
    public static Control? GetActivePageContent(this StarXelem.Views.MainWindow window)
    {
        var viewModel = window.DataContext as MainWindowViewModel;
        if (viewModel?.CurrentPage is null) return null;

        // Determine le type de vue correspondant au ViewModel actif
        var viewType = ViewTypeFor(viewModel.CurrentPage.GetType());
        if (viewType is null) return null;

        // Cherche la vue dans l'arbre logique
        foreach (var node in ((ILogical)window).GetSelfAndLogicalDescendants())
            if (node is Control c && viewType.IsInstanceOfType(c))
                return c;

        return null;
    }

    /// <summary>
    /// Retourne le type de vue correspondant a un type de ViewModel, selon la convention ViewLocator.
    /// </summary>
    private static Type? ViewTypeFor(Type viewModelType)
    {
        var viewName = viewModelType.FullName?.Replace("ViewModel", "View", StringComparison.Ordinal);
        if (viewName is null) return null;
        return viewModelType.Assembly.GetType(viewName);
    }

    private static TimeSpan timeoutOrDefault(TimeSpan ts) => ts == default ? TimeSpan.FromSeconds(10) : ts;
}
