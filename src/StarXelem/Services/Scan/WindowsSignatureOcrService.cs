using System.Text.RegularExpressions;
using Avalonia;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using StarXelem.Constants;
using StarXelem.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace StarXelem.Services.Scan;

/// <summary>
/// Reconnaissance de la signature radar affichée sur le HUD via le moteur OCR intégré à Windows
/// (<see cref="OcrEngine"/>). Un léger agrandissement SkiaSharp est appliqué si nécessaire, mais le texte
/// HUD (anti-aliasé) est transmis directement en couleur à l'OCR : un banc de test dédié a montré que la
/// binarisation par teinte/luminosité dégradait la lisibilité au lieu de l'améliorer.
/// </summary>
public partial class WindowsSignatureOcrService : ISignatureOcrService
{
    private const int MinPlausibleSignature = 2500;
    // Doit couvrir le pire cas des clusters larges FPS/GroundVehicle (ScanConstants.MaxLargeClusterSize
    // × signature générique 4000 = 120 000), en plus des clusters vaisseau/surface (max 43 000).
    private const int MaxPlausibleSignature = 120000;

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+$|^\d{4,6}$")]
    private static partial Regex SignatureNumberRegex();

    private readonly ILogger<WindowsSignatureOcrService> _logger;
    private readonly OcrEngine? _engine;

    public bool IsAvailable => _engine != null;
    public string? UnavailableReason { get; }

    public WindowsSignatureOcrService(ILogger<WindowsSignatureOcrService> logger)
    {
        _logger = logger;

        try
        {
            var hasEnglish = OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (!hasEnglish)
            {
                UnavailableReason = "Le pack de langue anglaise pour la reconnaissance de texte (OCR) n'est pas installé sur cette machine. " +
                                     "Installez-le via Paramètres Windows > Heure et langue > Langue > Ajouter une langue > Anglais (paquet de reconnaissance optique).";
                return;
            }

            _engine = OcrEngine.TryCreateFromLanguage(new Language("en"));
            if (_engine == null)
            {
                UnavailableReason = "Le moteur de reconnaissance de texte Windows n'a pas pu être initialisé pour l'anglais.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR Windows indisponible sur cette machine.");
            UnavailableReason = "La reconnaissance de texte Windows n'est pas disponible sur cette machine.";
        }
    }

    public async Task<IReadOnlyList<SignatureCandidate>> FindSignatureCandidatesAsync(CapturedFrame frame, CancellationToken ct = default)
    {
        if (_engine == null)
        {
            _logger.LogWarning("Tentative de scan alors que l'OCR n'est pas disponible : {Reason}", UnavailableReason);
            return Array.Empty<SignatureCandidate>();
        }

        using var processed = Preprocess(frame.Bitmap, out var scale);
        SaveDebugImage(processed, "preprocessed.png");

        using var softwareBitmap = ToSoftwareBitmap(processed);
        var result = await _engine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);

        var candidates = new List<SignatureCandidate>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                ct.ThrowIfCancellationRequested();

                var cleaned = word.Text.Trim()
                    .Replace("O", "0", StringComparison.Ordinal)
                    .Replace("o", "0", StringComparison.Ordinal)
                    .Replace("l", "1", StringComparison.Ordinal)
                    .Replace("I", "1", StringComparison.Ordinal)
                    .Replace(" ", "");

                if (!SignatureNumberRegex().IsMatch(cleaned))
                    continue;

                var numeric = cleaned.Replace(",", "");
                if (!int.TryParse(numeric, out var value))
                    continue;

                if (value < MinPlausibleSignature || value > MaxPlausibleSignature)
                    continue;

                var rect = word.BoundingRect; // coordonnées dans l'image agrandie/pré-traitée
                var screenRect = new PixelRect(
                    frame.ScreenX + (int)(rect.X / scale),
                    frame.ScreenY + (int)(rect.Y / scale),
                    (int)(rect.Width / scale),
                    (int)(rect.Height / scale));

                candidates.Add(new SignatureCandidate(value, word.Text, screenRect));
            }
        }

        // Trie par proximité du centre de l'écran capturé : le marqueur de scan est en général proche du centre du HUD.
        var centerX = frame.Bitmap.Width / 2 + frame.ScreenX;
        var centerY = frame.Bitmap.Height / 2 + frame.ScreenY;
        candidates.Sort((a, b) => DistanceSquared(a, centerX, centerY).CompareTo(DistanceSquared(b, centerX, centerY)));

        _logger.LogInformation("OCR : {Count} candidat(s) de signature détecté(s).", candidates.Count);
        return candidates;
    }

    private static double DistanceSquared(SignatureCandidate c, int centerX, int centerY)
    {
        var dx = c.ScreenBounds.X + c.ScreenBounds.Width / 2 - centerX;
        var dy = c.ScreenBounds.Y + c.ScreenBounds.Height / 2 - centerY;
        return (double)dx * dx + (double)dy * dy;
    }

    private SKBitmap Preprocess(SKBitmap src, out double scale)
    {
        // Vérifié via un banc de test dédié (ocrprobe) sur une vraie capture : le moteur OCR Windows lit
        // très bien le texte du HUD directement en couleur (police avec anti-aliasing), y compris à
        // résolution native — "80,000" est reconnu tel quel, sans aucun traitement. Binariser par
        // teinte/luminosité (approche précédente) s'est avéré CONTRE-PRODUCTIF : le seuillage fige des
        // contours nets mais grossiers, moins lisibles par l'OCR que l'anti-aliasing d'origine.
        var maxDim = (int)OcrEngine.MaxImageDimension;
        var longestSide = Math.Max(src.Width, src.Height);

        if (longestSide > 0 && longestSide < ScanConstants.OcrUpscaleThreshold)
        {
            // Petite fenêtre de jeu (résolution réduite) : un agrandissement aide la lisibilité du texte HUD.
            scale = Math.Min(ScanConstants.OcrUpscaleFactor, maxDim / (double)longestSide);
        }
        else
        {
            // Résolution déjà suffisante : agrandir coûterait cher (temps OCR + encodage de l'image de debug,
            // mesuré à plusieurs secondes sur une capture 4K agrandie ×3) sans gain de lisibilité. On ne
            // redimensionne que pour respecter OcrEngine.MaxImageDimension (fallback multi-écrans sans le jeu détecté).
            scale = longestSide > maxDim ? maxDim / (double)longestSide : 1.0;
        }

        if (scale < 1)
        {
            _logger.LogWarning("Capture {Width}x{Height} trop grande pour l'OCR : réduite d'un facteur {Scale:F2} (max {Max}px).", src.Width, src.Height, scale, maxDim);
        }

        if (Math.Abs(scale - 1.0) < 0.001)
        {
            return src.Copy();
        }

        var scaledInfo = new SKImageInfo(
            Math.Max(1, (int)(src.Width * scale)),
            Math.Max(1, (int)(src.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        return src.Resize(scaledInfo, SKFilterQuality.High) ?? src.Copy();
    }

    private static SoftwareBitmap ToSoftwareBitmap(SKBitmap bitmap)
    {
        var bytes = bitmap.Bytes; // BGRA8888, correspond à BitmapPixelFormat.Bgra8
        var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
    }

    private void SaveDebugImage(SKBitmap bitmap, string fileName)
    {
        var debugDir = Environment.GetEnvironmentVariable(ScanConstants.DebugDirEnvVar);
        if (string.IsNullOrWhiteSpace(debugDir)) return;

        try
        {
            Directory.CreateDirectory(debugDir);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(Path.Combine(debugDir, fileName));
            data.SaveTo(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'écrire l'image de debug {FileName}.", fileName);
        }
    }
}
