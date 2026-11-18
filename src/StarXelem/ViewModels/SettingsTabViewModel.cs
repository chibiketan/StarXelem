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
    protected override string NavIconData => "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z";
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
            var savedKey = await _settingsService.GetAsync(ApiKeySettingName);
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
            await _settingsService.SetAsync(ApiKeySettingName, ApiKey);
            Saved = true;
            await Task.Delay(2000);
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
