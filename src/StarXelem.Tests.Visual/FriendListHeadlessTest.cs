using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;
using StarXelem.Views;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Test headless de rendu FriendListTabView.
/// Vérifie la logique ViewModel et la structure UI avec données mockées.
/// </summary>
public class FriendListHeadlessTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;
    private bool _disposed;

    public FriendListHeadlessTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public void FriendListTabViewModel_Should_Resolve_From_DI()
    {
        var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();

        Assert.NotNull(viewModel);
        Assert.Equal("Amis", viewModel.Name);
    }

    [AvaloniaFact]
    public async Task FriendList_Data_Is_Not_Empty()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var friends = await grpcService.GetFriendList();

        Assert.NotNull(friends);
        Assert.NotEmpty(friends);
        Assert.Equal(3, friends.Count);
    }

    [AvaloniaFact]
    public void FriendList_View_Should_Be_Created()
    {
        var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();
        var view = new FriendListTabView { DataContext = viewModel };

        Assert.NotNull(view);
        Assert.Same(viewModel, view.DataContext);
    }

    [AvaloniaFact]
    public async Task FriendList_View_Has_DataGrid()
    {
        var viewModel = _fixture.Services.GetRequiredService<MainWindowViewModel>();
        var view = new MainWindow
        {
            DataContext = viewModel
        };
        view.Show();
        
        var navigationView = view.FindControl<NavigationView>("NavigationView");
        
        navigationView.UpdateLayout();
        var amiButton = navigationView
            .GetVisualDescendants()
            .OfType<NavigationViewItem>()
            .FirstOrDefault(c => c.Content == "Amis");
        
        Assert.NotNull(amiButton);
        
        
        var transform = amiButton.TransformToVisual(view);
        Assert.NotNull(transform);

        var itemCenter = transform.Value.Transform(
            new Point(amiButton.Bounds.Width / 2, amiButton.Bounds.Height / 2)
        );

        view.MouseDown(itemCenter, MouseButton.Left);
        view.MouseUp(itemCenter, MouseButton.Left);
        
        Dispatcher.UIThread.RunJobs();
        // Le view model a bien changé suite au clic sur le bouton
        Assert.True(viewModel.CurrentPage is FriendListTabViewModel);

        // Comment vérifier le contenu ?
        await Task.Delay(1000);
        Dispatcher.UIThread.RunJobs();
        
        DataGrid? dataGrid = ((ContentControl)navigationView.Content!).FindLogicalDescendantOfType<DataGrid>();
        
        // Sans fenêtre affichée, le template n'est pas appliqué — le DataGrid peut être introuvable
        // Ce test vérifie que la logique de recherche fonctionne si les enfants existent
        // if (dataGrid != null)
        Assert.NotNull(dataGrid);
        var bm = view.CaptureRenderedFrame();
        bm.Save("friendlist.png");
    }

    [AvaloniaFact]
    public async Task FriendList_Can_Execute_LoadCommand()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        Assert.Equal(GrpcConnectionStatus.Connected, grpcService.Status);

        var viewModel = new FriendListTabViewModel(grpcService);
        var canExecute = viewModel.LoadFriendListCommand?.CanExecute(null);

        Assert.True(canExecute ?? false);
    }

    [AvaloniaFact]
    public async Task FriendList_Load_Command_Populates_FriendList()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var viewModel = new FriendListTabViewModel(grpcService);

        await viewModel.LoadFriendList();

        Assert.NotNull(viewModel.FriendList);
        var list = await viewModel.FriendList!;
        Assert.NotEmpty(list);
        Assert.Equal(3, list.Count);
    }

    [AvaloniaFact]
    public void OnlyConnected_Filters_FriendList()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var viewModel = new FriendListTabViewModel(grpcService);

        // Appeler directement la méthode async au lieu de passer par Execute (void)
#pragma warning disable VSTHRD002 // Éviter les blocages synchrones dans les tests
        viewModel.LoadFriendListCommand!.Execute(null);
#pragma warning restore VSTHRD002

        Assert.NotNull(viewModel.FriendList);
        var allFriends = viewModel.FriendList!;

        viewModel.OnlyConnected = true;
        var filteredCount = viewModel.FilteredFriendList?.Count ?? 0;

        // Avec le mock, 2 amis ont Presence → connectés
        Assert.Equal(2, filteredCount);
    }

    [AvaloniaFact]
    public void FriendViewModel_Initials_Correctly_Computed()
    {
        var friend = new FriendViewModel(
            displayName: "CommanderVik",
            tokenName: "vik_2847",
            avatarUrl: null,
            isConnected: true,
            isInGame: false,
            activity: "menu");

        Assert.Equal("V2", friend.Initials);
    }

    [AvaloniaFact]
    public void FriendViewModel_ActivityLabel_Translates_PU()
    {
        var friend = new FriendViewModel(
            displayName: "TestUser",
            tokenName: "test_user",
            avatarUrl: null,
            isConnected: true,
            isInGame: true,
            activity: "persistent_universe");

        Assert.Equal("Univers Persistant", friend.ActivityLabel);
        Assert.True(friend.IsInPersistentUniverse);
    }

    [AvaloniaFact]
    public void FriendViewModel_ActivityLabel_Translates_Menu()
    {
        var friend = new FriendViewModel(
            displayName: "TestUser",
            tokenName: "test_user",
            avatarUrl: null,
            isConnected: true,
            isInGame: false,
            activity: "menu");

        Assert.Equal("Menu", friend.ActivityLabel);
        Assert.True(friend.IsInMenu);
    }

    [AvaloniaFact]
    public void FriendViewModel_IsOffline_When_Not_Connected()
    {
        var friend = new FriendViewModel(
            displayName: "TestUser",
            tokenName: "test_user",
            avatarUrl: null,
            isConnected: false,
            isInGame: false,
            activity: "Hors ligne");

        Assert.True(friend.IsOffline);
        Assert.False(friend.IsConnected);
    }

    // [AvaloniaFact]
    // public void FriendList_View_Has_LoadButton()
    // {
    //     var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();
    //     var view = new FriendListTabView { DataContext = viewModel };
    //
    //     Button? loadButton = FindLogicalChild<Button>(view);
    //
    //     Assert.NotNull(loadButton);
    // }
    //
    // private static T? FindLogicalChild<T>(ILogicalRoot root) where T : class
    // {
    //     if (root is ILogical logical)
    //     {
    //         foreach (var child in logical.LogicalChildren)
    //         {
    //             if (child is T found) return found;
    //             var result = FindLogicalChild<T>(child as ILogicalRoot);
    //             if (result != null) return result;
    //         }
    //     }
    //
    //     return null;
    // }

    public void Dispose() => _disposed = true;
}
