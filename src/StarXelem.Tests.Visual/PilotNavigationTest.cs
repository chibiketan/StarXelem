using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

public class PilotNavigationTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public PilotNavigationTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        // Pas de ressource propre à libérer
    }

    [AvaloniaFact]
    public async Task Can_Navigate_And_Capture_Each_Tab()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        string[] tabNames =
        {
            "Mon hangar",
            "Objets",
            "Blueprints",
            "Amis",
            "Loadout vaisseaux",
            "Missions",
            "Extractions",
            "Paramètres"
        };

        foreach (var tabName in tabNames)
        {
            VisualPilot.NavigateToTab(window, tabName);
            var screenshotPath = window.CaptureScreenshot($"tab_{tabName.Replace(" ", "_")}.png");
            Assert.NotNull(screenshotPath);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task Can_Click_LoadButton_On_FriendTab()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        // Navigue vers l'onglet Amis
        VisualPilot.NavigateToTab(window, "Amis");

        // Capture avant chargement
        window.CaptureScreenshot("amis_avant_chargement.png");

        // Clique sur le bouton "Charger" dans le contenu de la page active
        var pageContent = window.GetActivePageContent();
        Assert.NotNull(pageContent);
        var clicked = pageContent!.ClickButton("LoadButton");
        Assert.True(clicked, "Le bouton LoadButton n'a pas été trouvé ou son command n'était pas exécutable.");

        // Attend un peu que les données arrivent (les mocks sont rapides)
        await Task.Delay(300);

        // Récupère le ViewModel pour vérifier que la liste est peuplée
        var viewModel = (MainWindowViewModel)window.DataContext!;
        var friendPage = viewModel.Pages.OfType<FriendListTabViewModel>().FirstOrDefault();
        Assert.NotNull(friendPage);
        var friends = await friendPage.FriendList!;
        Assert.NotNull(friends);
        Assert.True(friends!.Count > 0, "La liste d'amis devrait être peuplée après clic sur LoadButton.");

        window.CaptureScreenshot("amis_apres_chargement.png");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Can_Filter_Friends_By_Connection()
    {
        var window = await VisualPilot.OpenAppAsync(_fixture);

        VisualPilot.NavigateToTab(window, "Amis");

        // Charge d'abord les amis via le contenu de la page active
        var pageContent2 = window.GetActivePageContent();
        Assert.NotNull(pageContent2);
        pageContent2!.ClickButton("LoadButton");
        await Task.Delay(300);

        var viewModel = (MainWindowViewModel)window.DataContext!;
        var friendPage = viewModel.Pages.OfType<FriendListTabViewModel>().FirstOrDefault()!;

        var friends = await friendPage.FriendList!;
        int totalBeforeFilter = friends!.Count;
        Assert.True(totalBeforeFilter > 0, "Il faut des amis chargés pour tester le filtrage.");

        // Active le filtrage "OnlyConnected" directement via le ViewModel
        friendPage.OnlyConnected = !friendPage.OnlyConnected;
        Dispatcher.UIThread.RunJobs();

        int countAfterFilter = friendPage.FilteredFriendList?.Count ?? 0;

        // Le filtrage devrait réduire ou garder la même taille
        Assert.True(countAfterFilter <= totalBeforeFilter, "Le filtrage ne devrait pas augmenter le nombre d'amis.");

        window.CaptureScreenshot("amis_filtres_connectes.png");
        window.Close();
    }
}
