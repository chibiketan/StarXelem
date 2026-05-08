using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Tests headless de BlueprintListTabViewModel (onglet "Blueprints").
/// </summary>
public class BlueprintListHeadlessTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public BlueprintListHeadlessTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public void BlueprintTabViewModel_Should_Resolve_From_DI()
    {
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();

        Assert.NotNull(viewModel);
        Assert.Equal("Blueprints", viewModel.Name);
    }

    [AvaloniaFact]
    public void BlueprintTab_LoadCommand_CanExecute_When_Connected()
    {
        var grpcService = _fixture.Services.GetRequiredService<IGrpcClientService>();
        Assert.Equal(GrpcConnectionStatus.Connected, grpcService.Status);

        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();
        var canExecute = viewModel.LoadItemListCommand?.CanExecute(null);

        Assert.True(canExecute ?? false);
    }

    [AvaloniaFact]
    public void BlueprintTab_Search_Is_Empty_By_Default()
    {
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();

        Assert.Equal(string.Empty, viewModel.Search);
    }

    [AvaloniaFact]
    public void BlueprintTab_ClearSearchCommand_Cannot_Execute_When_Empty()
    {
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();

        var canExecute = viewModel.ClearSearchCommand?.CanExecute(null);
        Assert.False(canExecute ?? false);
    }

    [AvaloniaFact]
    public void BlueprintTab_ClearSearchCommand_Can_Execute_When_Search_Set()
    {
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();
        viewModel.Search = "test";

        var canExecute = viewModel.ClearSearchCommand?.CanExecute(null);
        Assert.True(canExecute ?? false);
    }

    [AvaloniaFact]
    public async Task BlueprintTab_Load_With_Empty_Blueprints()
    {
        // Le mock TestGrpcClientService retourne une liste vide de blueprints
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();

#pragma warning disable VSTHRD002
        await viewModel.LoadItemListCommand!.ExecuteAsync(null);
#pragma warning restore VSTHRD002

        Assert.NotNull(viewModel.BlueprintList);
    }

    [AvaloniaFact]
    public void BlueprintTab_IsLoading_Default_False()
    {
        var viewModel = _fixture.Services.GetRequiredService<BlueprintListTabViewModel>();

        Assert.False(viewModel.IsLoading);
    }

    public void Dispose() { }
}
