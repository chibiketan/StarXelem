using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StarXelem.Constants;
using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.Services.Scan;

public class ScanSignatureOrchestrator : IScanSignatureOrchestrator
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IJoystickTriggerService _joystickService;
    private ScanTriggerKind _currentKind = ScanTriggerKind.Keyboard;
    private readonly IScreenCaptureService _captureService;
    private readonly ISignatureOcrService _ocrService;
    private readonly IMineralSignatureRepository _signatureRepository;
    private readonly IOverlayNotificationService _overlayService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ScanSignatureOrchestrator> _logger;

    private int _running;
    private ScanTriggerSettings? _lastAppliedSettings;

    public string? LastError { get; private set; }

    public event EventHandler? TriggerStatusChanged;

    public ScanTriggerStatus TriggerStatus => ActiveTrigger.Status;

    private IScanTriggerService ActiveTrigger => _currentKind == ScanTriggerKind.Joystick ? _joystickService : _hotkeyService;

    public ScanSignatureOrchestrator(
        IGlobalHotkeyService hotkeyService,
        IJoystickTriggerService joystickService,
        IScreenCaptureService captureService,
        ISignatureOcrService ocrService,
        IMineralSignatureRepository signatureRepository,
        IOverlayNotificationService overlayService,
        ISettingsService settingsService,
        ILogger<ScanSignatureOrchestrator> logger)
    {
        _hotkeyService = hotkeyService;
        _joystickService = joystickService;
        _captureService = captureService;
        _ocrService = ocrService;
        _signatureRepository = signatureRepository;
        _overlayService = overlayService;
        _settingsService = settingsService;
        _logger = logger;

        _hotkeyService.Triggered += (_, _) => _ = RunOnceAsync();
        _joystickService.Triggered += (_, _) => _ = RunOnceAsync();
        _hotkeyService.StatusChanged += OnTriggerStatusChanged;
        _joystickService.StatusChanged += OnTriggerStatusChanged;
    }

    private void OnTriggerStatusChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActiveTrigger))
        {
            TriggerStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StartAsync()
    {
        var settings = await ScanTriggerSettings.LoadAsync(_settingsService).ConfigureAwait(false);
        var ok = ApplyTriggers(settings, out var error);
        if (!ok)
        {
            _logger.LogWarning("Impossible d'installer le déclencheur de scan au démarrage : {Error}", error);
        }
    }

    public async Task<(bool Success, string? Error)> ApplySettingsAsync(ScanTriggerSettings settings)
    {
        await settings.SaveAsync(_settingsService).ConfigureAwait(false);
        var ok = ApplyTriggers(settings, out var error);
        return (ok, error);
    }

    /// <summary>
    /// Applique les paramètres à tous les déclencheurs : chacun ne s'active que si le type le concerne
    /// et se désactive sinon, ce qui garantit qu'un seul déclencheur est actif à la fois.
    /// </summary>
    private bool ApplyTriggers(ScanTriggerSettings settings, out string? error)
    {
        _currentKind = settings.Kind;
        _lastAppliedSettings = settings;
        var hotkeyOk = _hotkeyService.TryApply(settings, out var hotkeyError);
        var joystickOk = _joystickService.TryApply(settings, out var joystickError);
        error = hotkeyError ?? joystickError;
        LastError = error;
        TriggerStatusChanged?.Invoke(this, EventArgs.Empty);
        return hotkeyOk && joystickOk;
    }

    public void StopTriggers()
    {
        _hotkeyService.Stop();
        _joystickService.Stop();
    }

    /// <summary>
    /// Suspend temporairement le déclencheur actif (ex. pendant la saisie d'un nouveau déclencheur dans les
    /// Paramètres) : sans cela, la combinaison clavier ou le bouton joystick actuellement configurés
    /// resteraient interceptés au niveau OS et ne parviendraient jamais au champ de saisie.
    /// </summary>
    public void PauseTriggers() => StopTriggers();

    /// <summary>Réinstalle le dernier déclencheur appliqué (annule une pause), sans re-sauvegarder ni recharger depuis le registre.</summary>
    public void ResumeTriggers()
    {
        if (_lastAppliedSettings != null)
        {
            ApplyTriggers(_lastAppliedSettings, out _);
        }
    }

    public async Task RunOnceAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            _logger.LogDebug("Scan déjà en cours, requête ignorée.");
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var frame = await _captureService.CaptureGameWindowAsync().ConfigureAwait(false);
            if (frame == null)
            {
                _logger.LogWarning("Scan annulé : aucune capture d'écran disponible.");
                return;
            }

            var candidates = await _ocrService.FindSignatureCandidatesAsync(frame).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                _logger.LogInformation("Scan : aucune signature détectée à l'écran ({Elapsed}ms).", sw.ElapsedMilliseconds);
                return;
            }

            foreach (var candidate in candidates)
            {
                var rows = await _signatureRepository.FindBySignatureAsync(candidate.Value, ScanConstants.SignatureTolerance).ConfigureAwait(false);
                if (rows.Count == 0) continue;

                await _overlayService.ShowAsync(new SignatureMatch(candidate, rows)).ConfigureAwait(false);
                _logger.LogInformation("Scan : signature {Value} → {Count} correspondance(s) affichée(s) en {Elapsed}ms.", candidate.Value, rows.Count, sw.ElapsedMilliseconds);
                return;
            }

            _logger.LogInformation("Scan : aucune signature reconnue en base parmi {Count} candidat(s) ({Elapsed}ms).", candidates.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur pendant le pipeline de scan de signature.");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
