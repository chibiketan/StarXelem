using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StarXelem.Services;
using StarXelem.ViewModels.Popup;
using FluentAvalonia.UI.Controls;

namespace StarXelem.ViewModels;

/// <summary>
/// ViewModel pour la page de paramètres de l'application.
/// </summary>
public partial class SettingsTabViewModel : PageViewModelBase
{
    private const string ApiKeySettingName = "ApiKey";
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsTabViewModel> _logger;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _saved = false;

    public override string Name => "Paramètres";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Settings);

    public SettingsTabViewModel(ISettingsService settingsService, ILogger<SettingsTabViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    protected override async Task OnShowAsync()
    {
        try
        {
            var savedKey = await _settingsService.GetAsync(ApiKeySettingName).ConfigureAwait(false);
            ApiKey = savedKey ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger la clé API depuis le service de paramètres");
        }
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        try
        {
            await _settingsService.SetAsync(ApiKeySettingName, ApiKey).ConfigureAwait(false);
            Saved = true;
            await Task.Delay(2000).ConfigureAwait(false);
            Saved = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de sauvegarder la clé API");
            WeakReferenceMessenger.Default.Send(new ShowPopupMessage(
                showCloseButton: true,
                viewModel: new MessagePopupContentViewModel
                {
                    Title = "Erreur de sauvegarde",
                    Message = "La clé API n'a pas pu être sauvegardée."
                }
            ));
        }
    }
}
