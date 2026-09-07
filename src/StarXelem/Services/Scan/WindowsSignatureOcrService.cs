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
/// (<see cref="OcrEngine"/>). Un pré-traitement SkiaSharp isole les pixels jaune/ambre du HUD, binarise
/// l'image et l'agrandit avant OCR, pour fiabiliser la lecture d'un texte de petite taille.
/// </summary>
public partial class WindowsSignatureOcrService : ISignatureOcrService
{
    // Teinte HSL du HUD jaune/ambre de Star Citizen (35°-60°), seuils de saturation/luminosité ajustables.
    private const float HueMin = 30f;
    private const float HueMax = 65f;
    private const float SaturationMin = 0.30f;
    private const float LightnessMin = 0.35f;

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

        using var processed = Preprocess(frame.Bitmap);
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
                    frame.ScreenX + (int)(rect.X / ScanConstants.OcrUpscaleFactor),
                    frame.ScreenY + (int)(rect.Y / ScanConstants.OcrUpscaleFactor),
                    (int)(rect.Width / ScanConstants.OcrUpscaleFactor),
                    (int)(rect.Height / ScanConstants.OcrUpscaleFactor));

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

    private static SKBitmap Preprocess(SKBitmap src)
    {
        using var binarized = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var py = 0; py < src.Height; py++)
        {
            for (var px = 0; px < src.Width; px++)
            {
                var color = src.GetPixel(px, py);
                var isHud = IsHudYellow(color);
                binarized.SetPixel(px, py, isHud ? SKColors.Black : SKColors.White);
            }
        }

        var scaledInfo = new SKImageInfo(src.Width * ScanConstants.OcrUpscaleFactor, src.Height * ScanConstants.OcrUpscaleFactor, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var scaled = binarized.Resize(scaledInfo, SKFilterQuality.High);
        return scaled ?? binarized.Copy();
    }

    private static bool IsHudYellow(SKColor c)
    {
        var r = c.Red / 255f;
        var g = c.Green / 255f;
        var b = c.Blue / 255f;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2f;

        if (max == min)
            return false; // gris pur, jamais du HUD ambre

        var delta = max - min;
        var saturation = lightness > 0.5f ? delta / (2f - max - min) : delta / (max + min);

        float hue;
        if (max == r) hue = 60f * (((g - b) / delta) % 6f);
        else if (max == g) hue = 60f * ((b - r) / delta + 2f);
        else hue = 60f * ((r - g) / delta + 4f);
        if (hue < 0) hue += 360f;

        return hue is >= HueMin and <= HueMax && saturation >= SaturationMin && lightness >= LightnessMin;
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
