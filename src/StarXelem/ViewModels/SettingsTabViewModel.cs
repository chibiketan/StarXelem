using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StarXelem.Services;
using StarXelem.Services.Scan;
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
    private readonly IScanSignatureOrchestrator _scanOrchestrator;
    private readonly ISignatureOcrService _ocrService;
    private readonly IOverlayNotificationService _overlayService;
    private readonly IJoystickTriggerService _joystickService;
    private readonly ILogger<SettingsTabViewModel> _logger;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _saved = false;

    [ObservableProperty]
    private bool _scanEnabled = true;

    /// <summary>Déclencheur en cours d'édition (clavier ou joystick). <c>Enabled</c> est porté séparément par <see cref="ScanEnabled"/>.</summary>
    [ObservableProperty]
    private ScanTriggerSettings _scanTrigger = ScanTriggerSettings.Default;

    [ObservableProperty]
    private string? _scanError;

    [ObservableProperty]
    private bool _scanSaved;

    [ObservableProperty]
    private bool _isTriggerDeviceMissing;

    public bool IsOcrAvailable => _ocrService.IsAvailable;
    public string? OcrUnavailableReason => _ocrService.UnavailableReason;

    /// <summary>Fourni au contrôle de saisie pour capturer le prochain bouton de joystick pressé.</summary>
    public Func<CancellationToken, Task<JoystickBinding?>> JoystickCaptureProvider { get; }

    /// <summary>Suspend le déclencheur actif pendant la saisie d'un nouveau déclencheur (cf. TriggerCaptureBox).</summary>
    public Action PauseTriggerAction { get; }

    /// <summary>Réinstalle le déclencheur suspendu par <see cref="PauseTriggerAction"/>.</summary>
    public Action ResumeTriggerAction { get; }

    public override string Name => "Paramètres";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Settings);

    public SettingsTabViewModel(
        ISettingsService settingsService,
        IScanSignatureOrchestrator scanOrchestrator,
        ISignatureOcrService ocrService,
        IOverlayNotificationService overlayService,
        IJoystickTriggerService joystickService,
        ILogger<SettingsTabViewModel> logger)
    {
        _settingsService = settingsService;
        _scanOrchestrator = scanOrchestrator;
        _ocrService = ocrService;
        _overlayService = overlayService;
        _joystickService = joystickService;
        _logger = logger;

        JoystickCaptureProvider = ct => _joystickService.CaptureNextButtonAsync(ct);
        PauseTriggerAction = _scanOrchestrator.PauseTriggers;
        ResumeTriggerAction = _scanOrchestrator.ResumeTriggers;
        _scanOrchestrator.TriggerStatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTriggerStatus);
    }

    private void UpdateTriggerStatus()
    {
        IsTriggerDeviceMissing = _scanOrchestrator.TriggerStatus == ScanTriggerStatus.DeviceNotConnected;
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

        try
        {
            var scanSettings = await ScanTriggerSettings.LoadAsync(_settingsService).ConfigureAwait(false);
            ScanEnabled = scanSettings.Enabled;
            ScanTrigger = scanSettings;
            await Dispatcher.UIThread.InvokeAsync(UpdateTriggerStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger les paramètres du déclencheur de scan");
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

    [RelayCommand]
    private async Task SaveScanHotkeyAsync()
    {
        ScanError = null;

        var settings = ScanTrigger with { Enabled = ScanEnabled };

        if (settings.Kind == ScanTriggerKind.Keyboard && settings.Modifiers == KeyModifiers.None)
        {
            ScanError = "Choisissez au moins un modificateur (Ctrl, Alt ou Shift).";
            return;
        }

        if (settings.Kind == ScanTriggerKind.Joystick && settings.Joystick == null)
        {
            ScanError = "Aucun bouton de joystick sélectionné.";
            return;
        }

        var (success, error) = await _scanOrchestrator.ApplySettingsAsync(settings).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(UpdateTriggerStatus);

        if (!success)
        {
            ScanError = error;
            return;
        }

        ScanSaved = true;
        await Task.Delay(2000).ConfigureAwait(false);
        ScanSaved = false;
    }

    [RelayCommand]
    private async Task TestScanAsync()
    {
        await _scanOrchestrator.RunOnceAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task TestOverlayAsync()
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var bounds = desktop?.MainWindow?.Screens?.Primary?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1920, 1080);
        var center = new Avalonia.PixelRect(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 10, 10);

        var fakeCandidate = new Models.SignatureCandidate(21350, "21,350", center);
        var fakeRow = new Data.MineralSignatureEntity
        {
            MineralName = "Iron", MineralKey = "iron", Rarity = "Common",
            MiningType = nameof(Services.Mining.MiningKind.ShipOrSurface),
            BaseSignature = 4270, ClusterSize = 5, Signature = 21350,
        };

        await _overlayService.ShowAsync(new Models.SignatureMatch(fakeCandidate, new[] { fakeRow })).ConfigureAwait(false);
    }
}
