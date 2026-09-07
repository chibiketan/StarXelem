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
/// (<see cref="OcrEngine"/>). Un pré-traitement SkiaSharp agrandit l'image (lissée) puis isole les pixels
/// HUD (texte ambré ou blanc avec frange chromatique) et les binarise, pour fiabiliser la lecture d'un
/// texte de petite taille.
/// </summary>
public partial class WindowsSignatureOcrService : ISignatureOcrService
{
    // Teinte HSL du HUD jaune/ambre de Star Citizen (35°-60°), seuils de saturation/luminosité ajustables.
    private const float HueMin = 30f;
    private const float HueMax = 65f;
    private const float SaturationMin = 0.30f;
    private const float LightnessMin = 0.35f;

    /// <summary>Seuil de luminosité (indépendant de la teinte) pour capturer le texte HUD blanc avec frange chromatique.</summary>
    private const float BrightTextLightnessMin = 0.55f;

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
        // L'OCR Windows refuse toute image dont un côté dépasse OcrEngine.MaxImageDimension (10000px) :
        // sans le jeu détecté, la capture peut retomber sur le moniteur sous le curseur voire l'écran
        // virtuel entier (plusieurs moniteurs), plus grand qu'un HUD — un upscale ×3 aveugle ferait alors
        // planter RecognizeAsync.
        var maxDim = (int)OcrEngine.MaxImageDimension;
        var longestSide = Math.Max(src.Width, src.Height);
        scale = longestSide > 0 && longestSide * (double)ScanConstants.OcrUpscaleFactor > maxDim
            ? maxDim / (double)longestSide
            : ScanConstants.OcrUpscaleFactor;

        if (scale < 1)
        {
            _logger.LogWarning("Capture {Width}x{Height} trop grande pour l'agrandissement OCR habituel : facteur réduit à {Scale:F2} (max {Max}px).", src.Width, src.Height, scale, maxDim);
        }

        // Agrandir l'image COULEUR (lissée, filtre bicubique) AVANT de binariser : le texte HUD ne fait
        // souvent que 1-2px de trait à la résolution native. Binariser d'abord (contours nets mais à très
        // basse résolution) puis agrandir en plus-proche-voisin fige ces contours en blocs grossiers et
        // rend les glyphes illisibles pour l'OCR. Agrandir l'anti-aliasing d'origine préserve la forme
        // réelle du glyphe, que le seuillage capture ensuite bien plus fidèlement.
        var scaledInfo = new SKImageInfo(
            Math.Max(1, (int)(src.Width * scale)),
            Math.Max(1, (int)(src.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var scaledColor = src.Resize(scaledInfo, SKFilterQuality.High) ?? src.Copy();

        // Binarisation via le buffer de pixels brut (GetPixelSpan + tableau managé) plutôt que
        // GetPixel/SetPixel : chaque appel GetPixel/SetPixel fait un aller-retour natif par pixel, ce qui
        // rend le traitement très lent sur une grande image.
        var width = scaledColor.Width;
        var height = scaledColor.Height;
        var rowBytes = scaledColor.RowBytes;
        var span = scaledColor.GetPixelSpan(); // Bgra8888

        var dstRowBytes = width * 4;
        var dst = new byte[dstRowBytes * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * rowBytes;
            var dstRow = y * dstRowBytes;
            for (var x = 0; x < width; x++)
            {
                var si = row + x * 4;
                var b = span[si];
                var g = span[si + 1];
                var r = span[si + 2];
                var isHud = IsHudPixel(new SKColor(r, g, b));

                var di = dstRow + x * 4;
                var v = isHud ? (byte)0 : (byte)255;
                dst[di] = v;
                dst[di + 1] = v;
                dst[di + 2] = v;
                dst[di + 3] = 255;
            }
        }

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(dst, System.Runtime.InteropServices.GCHandleType.Pinned);
        var binarized = new SKBitmap();
        var installed = binarized.InstallPixels(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque),
            handle.AddrOfPinnedObject(),
            dstRowBytes,
            (_, ctx) => ((System.Runtime.InteropServices.GCHandle)ctx!).Free(),
            handle);
        if (!installed) handle.Free();

        return binarized;
    }

    private static bool IsHudPixel(SKColor c)
    {
        var r = c.Red / 255f;
        var g = c.Green / 255f;
        var b = c.Blue / 255f;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2f;

        // Beaucoup de texte HUD (lectures de distance/signature, repères de boussole) est en fait blanc,
        // avec une frange chromatique (aberration rouge/cyan) plutôt qu'une vraie teinte ambrée : un simple
        // filtre de teinte les manque entièrement. On les capture via un seuil de luminosité indépendant de
        // la teinte, en plus du filtre ambré ci-dessous (qui reste utile pour les libellés de boutons ambrés :
        // PWR, WPN, COOL...).
        if (lightness >= BrightTextLightnessMin)
            return true;

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
