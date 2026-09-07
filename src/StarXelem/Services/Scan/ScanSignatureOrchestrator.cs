using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StarXelem.Constants;
using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.Services.Scan;

public class ScanSignatureOrchestrator : IScanSignatureOrchestrator
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IScreenCaptureService _captureService;
    private readonly ISignatureOcrService _ocrService;
    private readonly IMineralSignatureRepository _signatureRepository;
    private readonly IOverlayNotificationService _overlayService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ScanSignatureOrchestrator> _logger;

    private int _running;

    public string? LastError { get; private set; }

    public ScanSignatureOrchestrator(
        IGlobalHotkeyService hotkeyService,
        IScreenCaptureService captureService,
        ISignatureOcrService ocrService,
        IMineralSignatureRepository signatureRepository,
        IOverlayNotificationService overlayService,
        ISettingsService settingsService,
        ILogger<ScanSignatureOrchestrator> logger)
    {
        _hotkeyService = hotkeyService;
        _captureService = captureService;
        _ocrService = ocrService;
        _signatureRepository = signatureRepository;
        _overlayService = overlayService;
        _settingsService = settingsService;
        _logger = logger;

        _hotkeyService.HotkeyPressed += (_, _) => _ = RunOnceAsync();
    }

    public async Task StartAsync()
    {
        var settings = await ScanHotkeySettings.LoadAsync(_settingsService).ConfigureAwait(false);
        var ok = _hotkeyService.TryApply(settings, out var error);
        LastError = error;
        if (!ok)
        {
            _logger.LogWarning("Impossible d'enregistrer le raccourci de scan au démarrage : {Error}", error);
        }
    }

    public async Task<(bool Success, string? Error)> ApplySettingsAsync(ScanHotkeySettings settings)
    {
        await settings.SaveAsync(_settingsService).ConfigureAwait(false);
        var ok = _hotkeyService.TryApply(settings, out var error);
        LastError = error;
        return (ok, error);
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
