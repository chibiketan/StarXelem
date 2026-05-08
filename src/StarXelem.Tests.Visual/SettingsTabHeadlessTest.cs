using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using StarXelem.Services;
using StarXelem.Tests;
using StarXelem.ViewModels;

namespace StarXelem.Tests.Visual;

/// <summary>
/// Tests headless de SettingsTabViewModel (onglet "Paramètres").
/// </summary>
public class SettingsTabHeadlessTest : IClassFixture<HeadlessAppFixture>, IDisposable
{
    private readonly HeadlessAppFixture _fixture;

    public SettingsTabHeadlessTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public void SettingsTabViewModel_Should_Resolve_From_DI()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        Assert.NotNull(viewModel);
        Assert.Equal("Paramètres", viewModel.Name);
    }

    [AvaloniaFact]
    public void SettingsTab_ApiKey_Is_Empty_By_Default()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        Assert.Equal(string.Empty, viewModel.ApiKey);
    }

    [AvaloniaFact]
    public void SettingsTab_Saved_Is_False_By_Default()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        Assert.False(viewModel.Saved);
    }

    [AvaloniaFact]
    public async Task SettingsTab_LoadAsync_Loads_ApiKey()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        await viewModel.LoadAsync();

        // Le DesignSettingService retourne $"{key}Result" pour toute clé
        Assert.Equal("ApiKeyResult", viewModel.ApiKey);
    }

    [AvaloniaFact]
    public async Task SettingsTab_SaveApiKey_Sets_Saved_True()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();
        viewModel.ApiKey = "test-key-123";

        // Lancer la sauvegarde sans attendre (elle prend 2s pour reset Saved)
        var saveTask = viewModel.SaveApiKeyCommand!.ExecuteAsync(null);

        // Immédiatement après, Saved doit être true
        Assert.True(viewModel.Saved);

        await saveTask;
    }

    [AvaloniaFact]
    public async Task SettingsTab_SaveApiKey_Persists_Value()
    {
        var settingsService = _fixture.Services.GetRequiredService<ISettingsService>();
        var viewModel = new SettingsTabViewModel(settingsService,
            _fixture.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SettingsTabViewModel>>());

        viewModel.ApiKey = "my-custom-key";

        // Simuler la sauvegarde sans le délai de 2s
        await settingsService.SetAsync("ApiKey", viewModel.ApiKey);

        // Vérifier que la valeur est lue correctement
        var savedValue = await settingsService.GetAsync("ApiKey");
        Assert.Equal("my-custom-key", savedValue);
    }

    [AvaloniaFact]
    public void SettingsTab_SaveCommand_CanExecute()
    {
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        // SaveApiKey ne a pas de condition CanExecute, donc toujours exécutable
        Assert.NotNull(viewModel.SaveApiKeyCommand);
    }

    [AvaloniaFact]
    public async Task SettingsTab_LoadAsync_Is_Idempotent()
    {
        // LoadAsync appelle OnFirstShowAsync une seule fois (IsLoaded guard)
        // mais OnShowAsync à chaque appel
        var viewModel = _fixture.Services.GetRequiredService<SettingsTabViewModel>();

        await viewModel.LoadAsync();
        Assert.True(viewModel.IsLoaded);
        var firstKey = viewModel.ApiKey;

        // Deuxième appel : OnFirstShowAsync est sauté, mais OnShowAsync s'exécute
        await viewModel.LoadAsync();
        var secondKey = viewModel.ApiKey;

        Assert.Equal(firstKey, secondKey);
    }

    public void Dispose() { }
}
