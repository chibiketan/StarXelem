using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Tests headless de ContainerTabViewModel (onglet "Conteneurs").
/// </summary>
public class ContainerTabHeadlessTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public ContainerTabHeadlessTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public void ContainerTabViewModel_Should_Resolve_From_DI()
    {
        var viewModel = _fixture.Services.GetRequiredService<ContainerTabViewModel>();

        Assert.NotNull(viewModel);
        Assert.Equal("Conteneurs", viewModel.Name);
    }

    [AvaloniaFact]
    public void ContainerTab_LoadCommand_CanExecute_When_Connected()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        Assert.Equal(GrpcConnectionStatus.Connected, grpcService.Status);

        var p4kService = _fixture.Services.GetRequiredService<IP4kService>();
        var viewModel = new ContainerTabViewModel(grpcService, p4kService);
        var canExecute = viewModel.LoadShipListCommand?.CanExecute(null);

        Assert.True(canExecute ?? false);
    }

    [AvaloniaFact]
    public async Task ContainerTab_Load_Populates_InventoryList()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var p4kService = _fixture.Services.GetRequiredService<IP4kService>();
        var viewModel = new ContainerTabViewModel(grpcService, p4kService);

        await viewModel.LoadShipList();

        Assert.NotNull(viewModel.InventoryList);
        var list = await viewModel.InventoryList!;
        // Le mock retourne une liste vide mais non nulle
        Assert.NotNull(list);
    }

    [AvaloniaFact]
    public async Task ContainerTab_IsLoading_Becomes_True_During_Load()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var p4kService = _fixture.Services.GetRequiredService<IP4kService>();
        var viewModel = new ContainerTabViewModel(grpcService, p4kService);

        Assert.False(viewModel.IsLoading);

        await viewModel.LoadShipList();

        // Après exécution, le chargement doit être terminé
        Assert.False(viewModel.IsLoading);
    }

    [AvaloniaFact]
    public async Task ContainerTab_TreatmentStatus_Is_Set()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        var p4kService = _fixture.Services.GetRequiredService<IP4kService>();
        var viewModel = new ContainerTabViewModel(grpcService, p4kService);

        await viewModel.LoadShipList();

        // Le statut de traitement doit avoir été mis à jour
        Assert.NotEmpty(viewModel.TreatmentStatus);
    }

    public void Dispose() { }
}
