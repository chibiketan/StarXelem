using System.Diagnostics;
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
    // Plus petite signature de base connue : 3000 (FPS) / 3170 (Quantainium). En dessous, ce sont des lectures
    // parasites du HUD (cap « 258° » lu 2582, etc.).
    private const int MinPlausibleSignature = 3000;
    // Doit couvrir le pire cas des clusters larges FPS/GroundVehicle (ScanConstants.MaxLargeClusterSize
    // × signature générique 4000 = 120 000), en plus des clusters vaisseau/surface (max 43 000).
    private const int MaxPlausibleSignature = 120000;

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+$|^\d{4,6}$")]
    private static partial Regex SignatureNumberRegex();

    private readonly ILogger<WindowsSignatureOcrService> _logger;
    private readonly IDigitGlyphStore _glyphStore;
    private readonly OcrEngine? _engine;

    public bool IsAvailable => _engine != null;
    public string? UnavailableReason { get; }

    public WindowsSignatureOcrService(IDigitGlyphStore glyphStore, ILogger<WindowsSignatureOcrService> logger)
    {
        _logger = logger;
        _glyphStore = glyphStore;

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
        var stages = new List<(SKRectI Zone, double Scale, string Name)> { (roi, ScanConstants.OcrLocalizationScales[0], "roi") };
        stages.Add((PriorZone(source), ScanConstants.OcrPriorZoneScale, "prior"));
        stages.AddRange(ScanConstants.OcrLocalizationScales.Skip(1).Select(s => (roi, s, "roi")));
        stages.AddRange(ScanConstants.OcrFullFrameLocalizationScales.Select(s => (full, s, "full")));

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
                candidates.AddRange(await ReadByVoteAsync(source, box, frame, readBoxes.Count - 1, ct).ConfigureAwait(false));
            }

            _logger.LogDebug("OCR étage {Stage} ({Name} x{Scale}) : {Boxes} boîte(s) en {LocMs}ms, {Found} candidat(s), {StageMs}ms.",
                stageIndex++, stage.Name, stage.Scale, boxes.Count, localizationMs, candidates.Count, stageSw.ElapsedMilliseconds);
            // Seule une lecture OCR arrête l'escalade : un candidat « gabarits » seul peut venir d'un autre nombre du HUD.
            if (candidates.Any(c => !c.FromTemplates)) break;
        }

        // OCR d'abord (par votes puis proximité du centre), gabarits ensuite : ces derniers ne servent que d'arbitre
        // quand l'orchestrateur ne trouve pas la valeur OCR en base.
        candidates.Sort((a, b) =>
        {
            var bySource = a.FromTemplates.CompareTo(b.FromTemplates);
            if (bySource != 0) return bySource;
            var byVotes = b.Votes.CompareTo(a.Votes);
            if (byVotes != 0) return byVotes;
            return DistanceSquared(a, frame, centerX, centerY).CompareTo(DistanceSquared(b, frame, centerX, centerY));
        });

        _logger.LogInformation("OCR : {Count} candidat(s) en {TotalMs}ms ({Stages} étage(s)) : {Values}.",
            candidates.Count, sw.ElapsedMilliseconds, stageIndex,
            string.Join(", ", candidates.Select(c => $"{c.Value} ({c.Votes}/{c.ValidPasses})")));
        return candidates;
    }

    /// <summary>
    /// Boîtes (coordonnées de la capture) des mots ressemblant à un nombre, issues des deux premiers étages de
    /// localisation (zone centrale ×1 et zone a priori ×2), triées par proximité du centre de la zone a priori.
    /// Sert de point d'entrée à la reconnaissance par gabarits (<see cref="DigitTemplateReader"/>).
    /// </summary>
    public async Task<IReadOnlyList<SKRectI>> LocalizeBoxesAsync(CapturedFrame frame, CancellationToken ct = default)
    {
        if (_engine == null) return Array.Empty<SKRectI>();

        var source = frame.Bitmap;
        var prior = PriorZone(source);
        var boxes = await LocalizeAsync(source, CenterRoi(source), 1.0, "roi_x1", ct).ConfigureAwait(false);
        foreach (var box in await LocalizeAsync(source, prior, ScanConstants.OcrPriorZoneScale, "prior_x2", ct).ConfigureAwait(false))
        {
            var overlapping = boxes.FindIndex(b => b.IntersectsWith(box));
            if (overlapping >= 0) boxes[overlapping] = SKRectI.Union(boxes[overlapping], box);
            else boxes.Add(box);
        }

        return boxes.OrderBy(b => DistanceSquared(b, prior.MidX, prior.MidY)).ToList();
    }

    private static SKRectI CenterRoi(SKBitmap source)
    {
        var w = (int)(source.Width * ScanConstants.OcrRoiWidthRatio);
        var h = (int)(source.Height * ScanConstants.OcrRoiHeightRatio);
        var x = (source.Width - w) / 2;
        var y = (source.Height - h) / 2;
        return new SKRectI(x, y, x + w, y + h);
    }

    private static SKRectI PriorZone(SKBitmap source)
    {
        var w = (int)(source.Width * ScanConstants.OcrPriorZoneWidthRatio);
        var h = (int)(source.Height * ScanConstants.OcrPriorZoneHeightRatio);
        var x = (source.Width - w) / 2;
        var y = Math.Max(0, (int)(source.Height * ScanConstants.OcrPriorZoneCenterYRatio) - h / 2);
        return new SKRectI(x, y, x + w, Math.Min(source.Height, y + h));
    }

    /// <summary>Un mot « ressemble » à un nombre s'il contient au moins OcrLocalizationMinDigits caractères
    /// chiffre-ou-confondus (0/O, 1/l/I, 4/A) et au plus un séparateur (virgule ou lecture bruitée . / *).</summary>
    // Ponctuation parasite que l'OCR colle parfois en bordure du nombre ("15,300!", "'19,425").
    private static readonly char[] EdgeNoise = [' ', '!', '\'', '"', '`', '.', ',', ':', ';', '(', ')', '[', ']', '{', '}', '-', '_', '—', '*', '/', '\\', '|', '°', 'º'];

    private static string TrimEdgeNoise(string text) => text.Trim(EdgeNoise);

    private static bool LooksLikeNumber(string text)
    {
        var digits = 0;
        var separators = 0;
        foreach (var c in TrimEdgeNoise(text))
        {
            if (char.IsDigit(c) || c is 'O' or 'o' or 'l' or 'I' or 'A') digits++;
            else if (c is ',' or '.' or '/' or '*') separators++;
            else return false;
        }
        return digits >= ScanConstants.OcrLocalizationMinDigits && digits <= 6 && separators <= 1;
    }

    /// <summary>Retourne les boîtes (coordonnées de la capture) des mots ressemblant à un nombre dans la zone donnée.</summary>
    private async Task<List<SKRectI>> LocalizeAsync(SKBitmap source, SKRectI roi, double scale, string debugName, CancellationToken ct)
    {
        using var region = ScanImageOps.Crop(source, roi);
        using var scaled = ScanImageOps.Scale(region, scale);
        using var green = ScanImageOps.GreenChannel(scaled);
        using var normalized = ScanImageOps.NormalizeContrast(green, (int)(ScanConstants.OcrNormalizeTileSize * scale));
        SaveDebugImage(region, $"{debugName}.png");
        SaveDebugImage(normalized, $"{debugName}_norm.png");

        // Couleur, canal vert et vert normalisé par tuiles : dans l'espace (fond sombre) la couleur ou le vert
        // suffisent, au sol (fond clair) seule la normalisation locale fait ressortir le badge (mesuré 1/18 → 14/18).
        var boxes = new List<SKRectI>();
        foreach (var (image, kind) in new[] { (scaled, "color"), (green, "green"), (normalized, "norm") })
        {
            ct.ThrowIfCancellationRequested();
            var result = await RecognizeAsync(image, ct).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("OCR localisation {Name} [{Kind}] : {Words}", debugName, kind, string.Join(" | ", result.Lines.Select(l => l.Text)));
            }
            foreach (var word in result.Lines.SelectMany(l => l.Words))
            {
                if (!LooksLikeNumber(word.Text)) continue;

                var r = word.BoundingRect;
                var box = new SKRectI(
                    roi.Left + (int)(r.X / scale),
                    roi.Top + (int)(r.Y / scale),
                    roi.Left + (int)((r.X + r.Width) / scale),
                    roi.Top + (int)((r.Y + r.Height) / scale));

                // Le même mot est en général retrouvé dans plusieurs variantes : on fusionne les boîtes qui se recouvrent.
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
    private async Task<IReadOnlyList<SignatureCandidate>> ReadByVoteAsync(SKBitmap source, SKRectI box, CapturedFrame frame, int index, CancellationToken ct)
    {
        var marginX = (int)(box.Height * ScanConstants.OcrCropMarginHorizontal);
        var marginY = (int)(box.Height * ScanConstants.OcrCropMarginVertical);
        var crop = new SKRectI(
            Math.Max(0, box.Left - marginX),
            Math.Max(0, box.Top - marginY),
            Math.Min(source.Width, box.Right + marginX),
            Math.Min(source.Height, box.Bottom + marginY));

        using var color = ScanImageOps.Crop(source, crop);
        SaveDebugImage(color, $"badge_{index}.png");

        var readings = new List<(int Value, string Raw, Windows.Foundation.Rect Rect, double Scale)>();
        foreach (var scale in ScanConstants.OcrReadingScales)
        {
            // Agrandir AVANT de normaliser : mesuré nettement plus lisible que l'inverse (×3 : 15/18 contre 9/18),
            // l'anti-aliasing du texte agrandi survit mieux au gamma.
            using var scaled = ScanImageOps.Scale(color, scale);
            using var green = ScanImageOps.GreenChannel(scaled);
            using var normalized = ScanImageOps.NormalizeContrast(green, tileSize: 0);
            // Deuxième filtre d'agrandissement : bicubique et Lanczos ne se trompent pas sur les mêmes captures
            // (confusions 6/8), leurs lectures se complètent dans le vote. Le bilinéaire, lui, est nettement pire.
            using var lanczos = ScanImageOps.ScaleLanczos(color, scale);
            using var lanczosNormalized = ScanImageOps.NormalizeContrast(ScanImageOps.GreenChannel(lanczos), tileSize: 0);
            if (Math.Abs(scale - ScanConstants.OcrReadingScales[^1]) < 0.001) SaveDebugImage(normalized, $"badge_{index}_norm.png");

            foreach (var (image, kind) in new[] { (scaled, "color"), (green, "green"), (normalized, "norm"), (lanczosNormalized, "lanczos-norm") })
            {
                ct.ThrowIfCancellationRequested();
                var result = await RecognizeAsync(image, ct).ConfigureAwait(false);
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

        var candidates = new List<SignatureCandidate>();
        var boxScreenRect = new PixelRect(frame.ScreenX + box.Left, frame.ScreenY + box.Top, box.Width, box.Height);

        // Lecture par gabarits sur un recadrage serré du nombre : candidat supplémentaire (poids faible) qui sert
        // d'arbitre quand la valeur OCR n'existe pas en base (l'orchestrateur affiche le premier candidat connu).
        var templateReading = ReadByTemplates(source, box, index);

        if (readings.Count > 0)
        {
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

            var ocrCandidate = new SignatureCandidate(winner.Value, winner.First.Raw, screenRect, winner.Votes, readings.Count);
            candidates.Add(ocrCandidate);
            TryLearn(source, box, ocrCandidate, templateReading);
        }
        else
        {
            _logger.LogDebug("OCR : aucune lecture plausible pour la boîte {Index} ({Box}).", index, box);
        }

        if (templateReading != null && candidates.All(c => c.Value != templateReading.Value))
        {
            candidates.Add(new SignatureCandidate(templateReading.Value, $"gabarits {templateReading.Digits}", boxScreenRect,
                ScanConstants.TemplateVoteWeight, Math.Max(readings.Count, ScanConstants.TemplateVoteWeight)) { FromTemplates = true });
        }
        return candidates;
    }

    private static SKRectI TemplateCrop(SKBitmap source, SKRectI box)
    {
        var mx = (int)(box.Height * ScanConstants.TemplateCropMarginX);
        var my = (int)(box.Height * ScanConstants.TemplateCropMarginY);
        return new SKRectI(
            Math.Max(0, box.Left - mx),
            Math.Max(0, box.Top - my),
            Math.Min(source.Width, box.Right + mx),
            Math.Min(source.Height, box.Bottom + my));
    }

    private DigitTemplateReader.Reading? ReadByTemplates(SKBitmap source, SKRectI box, int index)
    {
        var glyphs = _glyphStore.Glyphs;
        if (glyphs.Count == 0) return null;

        try
        {
            using var crop = ScanImageOps.Crop(source, TemplateCrop(source, box));
            var reading = new DigitTemplateReader(glyphs).Read(crop);
            if (reading == null) return null;

            _logger.LogDebug("Gabarits [boîte {Index}] : {Digits} (score moyen {Mean:F3}, min {Min:F3}, base {Count} glyphes).",
                index, reading.Digits, reading.MeanScore, reading.MinScore, glyphs.Count);
            var good = reading.MinScore >= ScanConstants.TemplateGoodMinScore && reading.MeanScore >= ScanConstants.TemplateGoodMeanScore
                       && reading.Value >= MinPlausibleSignature && reading.Value <= MaxPlausibleSignature;
            return good ? reading : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lecture par gabarits impossible sur la boîte {Index}.", index);
            return null;
        }
    }

    /// <summary>
    /// Auto-apprentissage : un vote OCR fort étiquette les glyphes du badge pour la reconnaissance par gabarits.
    /// Pas d'apprentissage si une lecture par gabarits de qualité contredit l'OCR : c'est le signe d'une confusion
    /// OCR (18,960 unanime pour 16,960) et apprendre ici empoisonnerait la base avec la mauvaise étiquette.
    /// </summary>
    private void TryLearn(SKBitmap source, SKRectI box, SignatureCandidate ocr, DigitTemplateReader.Reading? templateReading)
    {
        if (!_glyphStore.AutoLearnEnabled) return;
        if (ocr.Votes < ScanConstants.AutoLearnMinVotes || ocr.Confidence < ScanConstants.AutoLearnMinConfidence) return;
        if (templateReading != null && templateReading.Value != ocr.Value)
        {
            _logger.LogInformation("Auto-apprentissage : OCR {Ocr} contredit par les gabarits {Template}, glyphes non appris.", ocr.Value, templateReading.Value);
            return;
        }

        try
        {
            using var crop = ScanImageOps.Crop(source, TemplateCrop(source, box));
            var labeled = DigitTemplateReader.Label(crop, ocr.Value.ToString());
            if (labeled == null)
            {
                _logger.LogDebug("Auto-apprentissage : segmentation incompatible avec {Value}, glyphes ignorés.", ocr.Value);
                return;
            }
            var result = _glyphStore.Learn(labeled);
            if (result.Conflict != null)
            {
                _logger.LogWarning("Auto-apprentissage : {Value} refusé, {Conflict} — l'étiquetage OCR contredit la base de glyphes.", ocr.Value, result.Conflict);
            }
            else
            {
                _logger.LogInformation("Auto-apprentissage : {Added} glyphes ajoutés ({Duplicates} doublons) pour {Value} (vote {Votes}/{Passes}).",
                    result.Added, result.Duplicates, ocr.Value, ocr.Votes, ocr.ValidPasses);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Auto-apprentissage impossible pour {Value}.", ocr.Value);
        }
    }

    private static bool TryParseSignature(string text, out int value)
    {
        value = 0;
        var cleaned = TrimEdgeNoise(text)
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

    private async Task<OcrResult> RecognizeAsync(SKBitmap image, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        SKBitmap? fitted = null;
        try
        {
            var target = image;
            var maxDim = (int)OcrEngine.MaxImageDimension;
            if (target.Width > maxDim || target.Height > maxDim)
            {
                var fit = Math.Min(maxDim / (double)target.Width, maxDim / (double)target.Height);
                var info = new SKImageInfo(Math.Max(1, (int)(target.Width * fit)), Math.Max(1, (int)(target.Height * fit)), SKColorType.Bgra8888, SKAlphaType.Opaque);
                fitted = target.Resize(info, SKFilterQuality.High);
                target = fitted ?? image;
            }

            using var softwareBitmap = ToSoftwareBitmap(target);
            var convertMs = sw.ElapsedMilliseconds;
            var result = await _engine!.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
            _logger.LogDebug("OCR pass {Width}x{Height}: conversion {ConvertMs}ms, reconnaissance {RecognizeMs}ms.",
                target.Width, target.Height, convertMs, sw.ElapsedMilliseconds - convertMs);
            return result;
        }
        finally
        {
            fitted?.Dispose();
        }
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
