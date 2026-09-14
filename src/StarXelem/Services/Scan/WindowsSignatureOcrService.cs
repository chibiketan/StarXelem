using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// Reconnaissance de la signature radar affichée sur le HUD via le moteur OCR intégré à Windows (<see cref="OcrEngine"/>).
/// Pipeline en deux temps, mesuré sur des captures 4K réelles où le badge fait ~15 px de haut (limite du moteur) :
/// 1. localisation : OCR grossier (canal vert, ×1 et ×2) de la zone centrale pour trouver les mots qui ressemblent
///    à un nombre, sans chercher à les lire ;
/// 2. lecture : recadrage serré autour de chaque boîte, OCR à plusieurs échelles en couleur et sur le canal vert
///    (qui supprime la frange chromatique rouge/bleue du HUD), puis vote majoritaire. Aucune passe seule n'est
///    fiable à cette taille de texte, le vote l'est. La binarisation par teinte a été testée et abandonnée (2026-09-07).
/// </summary>
public partial class WindowsSignatureOcrService : ISignatureOcrService
{
    private const int MinPlausibleSignature = 2500;
    // Doit couvrir le pire cas des clusters larges FPS/GroundVehicle (ScanConstants.MaxLargeClusterSize
    // × signature générique 4000 = 120 000), en plus des clusters vaisseau/surface (max 43 000).
    private const int MaxPlausibleSignature = 120000;

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+$|^\d{4,6}$")]
    private static partial Regex SignatureNumberRegex();

    // Passe de localisation : on cherche un mot qui "ressemble" à un nombre bruité (19/05, 16*00, 19A25…),
    // le but étant de trouver où est le badge, pas de le lire.
    [GeneratedRegex(@"^[\dOolIA,./*]{4,7}$")]
    private static partial Regex NoisyNumberRegex();

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

        var sw = Stopwatch.StartNew();
        var source = frame.Bitmap;
        var centerX = source.Width / 2;
        var centerY = source.Height / 2;

        // Escalade : zone centrale ×1 (cas courant, ~200 ms) → zone centrale ×2 (badge trop petit pour être
        // segmenté à l'échelle native, ~+600 ms) → capture entière. On s'arrête dès qu'une valeur est lue :
        // chaque étage supplémentaire coûte surtout le redimensionnement de la zone.
        var roi = CenterRoi(source);
        var full = new SKRectI(0, 0, source.Width, source.Height);
        var stages = ScanConstants.OcrLocalizationScales.Select(s => (Zone: roi, Scale: s, Name: "roi"))
            .Concat(ScanConstants.OcrLocalizationScales.Select(s => (Zone: full, Scale: s, Name: "full")));

        var candidates = new List<SignatureCandidate>();
        var readBoxes = new List<SKRectI>();
        var stageIndex = 0;
        foreach (var stage in stages)
        {
            ct.ThrowIfCancellationRequested();
            var stageSw = Stopwatch.StartNew();
            var boxes = await LocalizeAsync(source, stage.Zone, stage.Scale, $"{stage.Name}_x{stage.Scale}", ct).ConfigureAwait(false);
            boxes = boxes
                .Where(b => !readBoxes.Any(r => r.IntersectsWith(b)))
                .OrderBy(b => DistanceSquared(b, centerX, centerY))
                .Take(ScanConstants.OcrMaxLocalizationBoxes)
                .ToList();
            var localizationMs = stageSw.ElapsedMilliseconds;

            foreach (var box in boxes)
            {
                ct.ThrowIfCancellationRequested();
                readBoxes.Add(box);
                var candidate = await ReadByVoteAsync(source, box, frame, readBoxes.Count - 1, ct).ConfigureAwait(false);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            _logger.LogDebug("OCR étage {Stage} ({Name} x{Scale}) : {Boxes} boîte(s) en {LocMs}ms, {Found} candidat(s), {StageMs}ms.",
                stageIndex++, stage.Name, stage.Scale, boxes.Count, localizationMs, candidates.Count, stageSw.ElapsedMilliseconds);
            if (candidates.Count > 0) break;
        }

        candidates.Sort((a, b) =>
        {
            var byVotes = b.Votes.CompareTo(a.Votes);
            if (byVotes != 0) return byVotes;
            return DistanceSquared(a, frame, centerX, centerY).CompareTo(DistanceSquared(b, frame, centerX, centerY));
        });

        _logger.LogInformation("OCR : {Count} candidat(s) en {TotalMs}ms ({Stages} étage(s)) : {Values}.",
            candidates.Count, sw.ElapsedMilliseconds, stageIndex,
            string.Join(", ", candidates.Select(c => $"{c.Value} ({c.Votes}/{c.ValidPasses})")));
        return candidates;
    }

    private static SKRectI CenterRoi(SKBitmap source)
    {
        var w = (int)(source.Width * ScanConstants.OcrRoiWidthRatio);
        var h = (int)(source.Height * ScanConstants.OcrRoiHeightRatio);
        var x = (source.Width - w) / 2;
        var y = (source.Height - h) / 2;
        return new SKRectI(x, y, x + w, y + h);
    }

    /// <summary>Retourne les boîtes (coordonnées de la capture) des mots ressemblant à un nombre dans la zone donnée.</summary>
    private async Task<List<SKRectI>> LocalizeAsync(SKBitmap source, SKRectI roi, double scale, string debugName, CancellationToken ct)
    {
        using var region = Crop(source, roi);
        using var green = GreenChannel(region);
        SaveDebugImage(region, $"{debugName}.png");

        // Couleur ET canal vert : selon la capture, l'une ou l'autre seule manque le badge (mesuré).
        var boxes = new List<SKRectI>();
        foreach (var image in new[] { region, green })
        {
            ct.ThrowIfCancellationRequested();
            var result = await RecognizeAsync(image, scale, ct).ConfigureAwait(false);
            foreach (var word in result.Lines.SelectMany(l => l.Words))
            {
                if (!NoisyNumberRegex().IsMatch(word.Text.Trim())) continue;

                var r = word.BoundingRect;
                var box = new SKRectI(
                    roi.Left + (int)(r.X / scale),
                    roi.Top + (int)(r.Y / scale),
                    roi.Left + (int)((r.X + r.Width) / scale),
                    roi.Top + (int)((r.Y + r.Height) / scale));

                // Le même mot est en général retrouvé en couleur et en vert : on fusionne les boîtes qui se recouvrent.
                var overlapping = boxes.FindIndex(b => b.IntersectsWith(box));
                if (overlapping >= 0)
                {
                    boxes[overlapping] = SKRectI.Union(boxes[overlapping], box);
                }
                else
                {
                    boxes.Add(box);
                }
            }
        }
        return boxes;
    }

    /// <summary>Recadre serré autour de la boîte, lit à plusieurs échelles (couleur et canal vert) et retient la valeur majoritaire.</summary>
    private async Task<SignatureCandidate?> ReadByVoteAsync(SKBitmap source, SKRectI box, CapturedFrame frame, int index, CancellationToken ct)
    {
        var marginX = (int)(box.Height * ScanConstants.OcrCropMarginHorizontal);
        var marginY = (int)(box.Height * ScanConstants.OcrCropMarginVertical);
        var crop = new SKRectI(
            Math.Max(0, box.Left - marginX),
            Math.Max(0, box.Top - marginY),
            Math.Min(source.Width, box.Right + marginX),
            Math.Min(source.Height, box.Bottom + marginY));

        using var color = Crop(source, crop);
        using var green = GreenChannel(color);
        SaveDebugImage(color, $"badge_{index}.png");

        var readings = new List<(int Value, string Raw, Windows.Foundation.Rect Rect, double Scale)>();
        foreach (var (image, kind) in new[] { (color, "color"), (green, "green") })
        {
            foreach (var scale in ScanConstants.OcrReadingScales)
            {
                ct.ThrowIfCancellationRequested();
                var result = await RecognizeAsync(image, scale, ct).ConfigureAwait(false);
                foreach (var word in result.Lines.SelectMany(l => l.Words))
                {
                    if (TryParseSignature(word.Text, out var value))
                    {
                        readings.Add((value, word.Text.Trim(), word.BoundingRect, scale));
                        _logger.LogDebug("OCR [{Kind} x{Scale}] '{Raw}' → {Value}", kind, scale, word.Text, value);
                    }
                }
            }
        }

        if (readings.Count == 0)
        {
            _logger.LogDebug("OCR : aucune lecture plausible pour la boîte {Index} ({Box}).", index, box);
            return null;
        }

        // Vote : valeur la plus fréquente ; à égalité, celle lue le plus souvent avec séparateur de milliers
        // (la virgule est un indice que les chiffres ont été correctement segmentés).
        var winner = readings
            .GroupBy(r => r.Value)
            .Select(g => new { Value = g.Key, Votes = g.Count(), WithComma = g.Count(r => r.Raw.Contains(',')), First = g.First() })
            .OrderByDescending(g => g.Votes)
            .ThenByDescending(g => g.WithComma)
            .First();

        var rect = winner.First.Rect;
        var scaleUsed = winner.First.Scale;
        var screenRect = new PixelRect(
            frame.ScreenX + crop.Left + (int)(rect.X / scaleUsed),
            frame.ScreenY + crop.Top + (int)(rect.Y / scaleUsed),
            (int)(rect.Width / scaleUsed),
            (int)(rect.Height / scaleUsed));

        return new SignatureCandidate(winner.Value, winner.First.Raw, screenRect, winner.Votes, readings.Count);
    }

    private static bool TryParseSignature(string text, out int value)
    {
        value = 0;
        var cleaned = text.Trim()
            .Replace("O", "0", StringComparison.Ordinal)
            .Replace("o", "0", StringComparison.Ordinal)
            .Replace("l", "1", StringComparison.Ordinal)
            .Replace("I", "1", StringComparison.Ordinal)
            .Replace("A", "4", StringComparison.Ordinal)
            .Replace(" ", "");

        if (!SignatureNumberRegex().IsMatch(cleaned)) return false;
        if (!int.TryParse(cleaned.Replace(",", ""), out value)) return false;
        return value >= MinPlausibleSignature && value <= MaxPlausibleSignature;
    }

    private async Task<OcrResult> RecognizeAsync(SKBitmap image, double scale, CancellationToken ct)
    {
        SKBitmap? scaled = null;
        var sw = Stopwatch.StartNew();
        try
        {
            var target = image;
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                // High (bicubique) : mesuré 2,5× plus rapide côté reconnaissance qu'un bilinéaire (image plus nette), pour +15 ms de resize.
                var info = new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Opaque);
                scaled = image.Resize(info, SKFilterQuality.High);
                target = scaled ?? image;
            }
            var resizeMs = sw.ElapsedMilliseconds;

            var maxDim = (int)OcrEngine.MaxImageDimension;
            if (target.Width > maxDim || target.Height > maxDim)
            {
                var fit = Math.Min(maxDim / (double)target.Width, maxDim / (double)target.Height);
                var info = new SKImageInfo(Math.Max(1, (int)(target.Width * fit)), Math.Max(1, (int)(target.Height * fit)), SKColorType.Bgra8888, SKAlphaType.Opaque);
                var fitted = target.Resize(info, SKFilterQuality.High);
                scaled?.Dispose();
                scaled = fitted;
                target = fitted ?? image;
            }

            using var softwareBitmap = ToSoftwareBitmap(target);
            var convertMs = sw.ElapsedMilliseconds - resizeMs;
            var result = await _engine!.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            _logger.LogDebug("OCR pass {Width}x{Height} x{Scale}: resize {ResizeMs}ms, conversion {ConvertMs}ms, reconnaissance {RecognizeMs}ms.",
                target.Width, target.Height, scale, resizeMs, convertMs, sw.ElapsedMilliseconds - resizeMs - convertMs);
            return result;
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    private static SKBitmap Crop(SKBitmap source, SKRectI rect)
    {
        var dst = new SKBitmap(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!source.ExtractSubset(dst, rect))
        {
            dst.Dispose();
            return source.Copy();
        }
        // ExtractSubset partage les pixels de la source : on copie pour obtenir un bitmap indépendant (et ses Bytes contigus).
        var copy = dst.Copy();
        dst.Dispose();
        return copy;
    }

    /// <summary>Niveaux de gris à partir du seul canal vert : supprime la frange chromatique rouge/bleue du texte HUD.</summary>
    private static SKBitmap GreenChannel(SKBitmap source)
    {
        var dst = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var src = source.Bytes;
        var pixels = new byte[src.Length];
        for (var i = 0; i + 3 < src.Length; i += 4)
        {
            var g = src[i + 1];
            pixels[i] = g;
            pixels[i + 1] = g;
            pixels[i + 2] = g;
            pixels[i + 3] = 255;
        }
        Marshal.Copy(pixels, 0, dst.GetPixels(), pixels.Length);
        return dst;
    }

    private static double DistanceSquared(SKRectI box, int centerX, int centerY)
    {
        var dx = box.MidX - centerX;
        var dy = box.MidY - centerY;
        return (double)dx * dx + (double)dy * dy;
    }

    private static double DistanceSquared(SignatureCandidate c, CapturedFrame frame, int centerX, int centerY)
    {
        var dx = c.ScreenBounds.X - frame.ScreenX + c.ScreenBounds.Width / 2 - centerX;
        var dy = c.ScreenBounds.Y - frame.ScreenY + c.ScreenBounds.Height / 2 - centerY;
        return (double)dx * dx + (double)dy * dy;
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
            using var stream = File.Create(Path.Combine(debugDir, fileName));
            data.SaveTo(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible d'écrire l'image de debug {FileName}.", fileName);
        }
    }
}
