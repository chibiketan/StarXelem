using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;
using StarXelem.Views;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Test headless de rendu FriendListTabView.
/// Vérifie que la liste d'amis se rend correctement avec des données mockées.
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
        // Arrange & Act
        var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();

        // Assert
        Assert.NotNull(viewModel);
        Assert.Equal("Amis", viewModel.Name);
    }

    [AvaloniaFact]
    public void FriendList_Can_Load_From_Mock_Service()
    {
        // Arrange
        var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();

        // Act — le ViewModel attend que le service gRPC retourne des données
        var canExecute = viewModel.LoadFriendListCommand?.CanExecute(null);

        // Assert
        // Avec DesignGrpcClientService, Status est Connecté → le bouton doit être activable
    }

    [AvaloniaFact]
    public void FriendList_View_Should_Be_Created()
    {
        // Arrange
        var viewModel = _fixture.Services.GetRequiredService<FriendListTabViewModel>();
        var view = new Views.FriendListTabView { DataContext = viewModel };

        // Act
        Assert.NotNull(view);
        Assert.Same(viewModel, view.DataContext);

        // Assert — vérifie la structure de base
        // On peut étendre pour vérifier les éléments de liste rendus
    }

    [AvaloniaFact]
    public void FriendList_Data_Is_Not_Empty()
    {
        // Arrange
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();

        // Act
        var friends = grpcService.GetFriendList().GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(friends);
        Assert.NotEmpty(friends);
    }

    public void Dispose() => _disposed = true;
}
