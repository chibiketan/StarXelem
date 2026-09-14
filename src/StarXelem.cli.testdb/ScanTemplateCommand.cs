using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using StarXelem.Models;
using StarXelem.Services.Scan;

namespace StarXelem.Cli.TestDb;

/// <summary>
/// Expérimentation : reconnaissance des chiffres du badge par gabarits (<see cref="DigitTemplateReader"/>),
/// évaluée en validation croisée « leave-one-out » sur des dossiers de captures munis d'un <c>expected.txt</c>.
/// La localisation du badge reste celle de l'OCR (<see cref="WindowsSignatureOcrService.LocalizeBoxesAsync"/>).
/// Usage : <c>scan-template &lt;dossier&gt;… [--export &lt;scan-glyphs.json&gt;]</c>
/// </summary>
public static class ScanTemplateCommand
{
    private const int MaxBoxesPerCapture = 4;
    /// <summary>Marges autour de la boîte OCR du nombre, en fraction de sa hauteur.</summary>
    private const double MarginX = 0.6;
    private const double MarginY = 0.5;
    /// <summary>Seuils de corrélation en dessous desquels une lecture n'est pas jugée fiable.</summary>
    private const double GoodMinScore = 0.7;
    private const double GoodMeanScore = 0.85;

    private sealed record Sample(string File, int Expected, IReadOnlyList<SKBitmap> Crops);

    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        var emptyStore = new DigitGlyphStore(null, autoLearnEnabled: false, loggerFactory.CreateLogger<DigitGlyphStore>());
        var ocr = new WindowsSignatureOcrService(emptyStore, loggerFactory.CreateLogger<WindowsSignatureOcrService>());
        if (!ocr.IsAvailable)
        {
            Console.Error.WriteLine($"OCR indisponible : {ocr.UnavailableReason}");
            return 3;
        }

        string? exportPath = null;
        var exportIndex = Array.IndexOf(args, "--export");
        if (exportIndex >= 0 && exportIndex + 1 < args.Length)
        {
            exportPath = args[exportIndex + 1];
        }

        // 1. Localisation + recadrages pour chaque capture.
        var samples = new List<Sample>();
        foreach (var dir in args.Where(Directory.Exists))
        {
            var expectedFile = Path.Combine(dir, "expected.txt");
            if (!File.Exists(expectedFile))
            {
                Console.Error.WriteLine($"{dir} : expected.txt manquant, dossier ignoré.");
                continue;
            }
            var expected = File.ReadAllText(expectedFile).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
            var files = Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();

            for (var i = 0; i < files.Count && i < expected.Count; i++)
            {
                using var decoded = SKBitmap.Decode(files[i]);
                using var bitmap = decoded.Copy(SKColorType.Bgra8888);
                using var frame = new CapturedFrame(bitmap, 0, 0);
                var boxes = await ocr.LocalizeBoxesAsync(frame);
                var crops = boxes.Take(MaxBoxesPerCapture).Select(b => ScanImageOps.Crop(bitmap, Expand(b, bitmap))).ToList();
                samples.Add(new Sample(Path.GetFileName(files[i]), expected[i], crops));
                Console.WriteLine($"[{Path.GetFileName(files[i])}] {boxes.Count} boîte(s), attendu {expected[i]}, glyphes par boîte : {string.Join(" ", crops.Select(c => DigitTemplateReader.SegmentCrop(c).Count))}");
                SaveDebug(Path.GetFileNameWithoutExtension(files[i]), crops);
            }
        }

        if (samples.Count == 0)
        {
            Console.Error.WriteLine("Usage : scan-template <dossier avec expected.txt>…");
            return 2;
        }

        // 2. Leave-one-out : pour chaque capture, gabarits appris sur toutes les autres.
        Console.WriteLine();
        var correct = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < samples.Count; i++)
        {
            var training = samples.Where((_, j) => j != i).SelectMany(s => s.Crops.Select(c => (Crop: c, Digits: s.Expected.ToString())));
            var (reader, used, skipped) = DigitTemplateReader.Train(training);

            // Première boîte lisible dans l'ordre de proximité de la zone a priori : le score de corrélation ne
            // distingue pas le badge de l'altimètre ou de la boussole (mêmes glyphes), la position si.
            // Une lecture de mauvaise qualité (glyphe mal corrélé) sur la boîte la plus proche cède la place à
            // la suivante ; à défaut de lecture de qualité, la meilleure au score.
            var readings = samples[i].Crops.Select(reader.Read).Where(r => r != null).Select(r => r!).ToList();
            var best = readings.FirstOrDefault(r => r.MinScore >= GoodMinScore && r.MeanScore >= GoodMeanScore)
                       ?? readings.OrderByDescending(r => r.MeanScore).FirstOrDefault();
            var ok = best?.Value == samples[i].Expected;
            if (ok) correct++;

            Console.WriteLine($"[{samples[i].File}] attendu {samples[i].Expected} → " +
                              (best == null ? "aucune lecture" : $"{best.Value} (score moyen {best.MeanScore:F3}, min {best.MinScore:F3})") +
                              (ok ? "  ✓" : "  ✗") +
                              $"  [gabarits : {used} échantillons, {skipped} ignorés, chiffres {string.Concat(reader.KnownDigits.OrderBy(k => k))}]");
        }
        Console.WriteLine($"\n{correct}/{samples.Count} corrects ({sw.ElapsedMilliseconds}ms pour l'apprentissage + lecture LOO).");

        // 3. Export de la graine : gabarits appris sur TOUTES les captures (pas de LOO), à copier dans
        //    src/StarXelem/Resources/scan-glyphs.json.
        if (exportPath != null)
        {
            var (full, used, skipped) = DigitTemplateReader.Train(samples.SelectMany(s => s.Crops.Select(c => (Crop: c, Digits: s.Expected.ToString()))));
            File.WriteAllText(exportPath, DigitGlyphStore.Serialize(full.Samples));
            Console.WriteLine($"Graine exportée : {full.Samples.Count} glyphes ({used} échantillons, {skipped} ignorés) → {exportPath}");
        }

        foreach (var s in samples) foreach (var c in s.Crops) c.Dispose();
        return correct == samples.Count ? 0 : 1;
    }

    /// <summary>Si STARXELEM_SCAN_DEBUG_DIR est défini : image normalisée agrandie de chaque recadrage, avec les boîtes des glyphes en rouge.</summary>
    private static void SaveDebug(string name, IReadOnlyList<SKBitmap> crops)
    {
        var dir = Environment.GetEnvironmentVariable(StarXelem.Constants.ScanConstants.DebugDirEnvVar);
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        for (var b = 0; b < crops.Count; b++)
        {
            using var scaled = ScanImageOps.Scale(crops[b], DigitTemplateReader.UpscaleFactor);
            using var green = ScanImageOps.GreenChannel(scaled);
            using var normalized = ScanImageOps.NormalizeContrast(green, tileSize: 0);
            var glyphs = DigitTemplateReader.Segment(normalized);
            using var canvas = new SKCanvas(normalized);
            using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            foreach (var g in glyphs) canvas.DrawRect(g.Bounds, paint);
            using var image = SKImage.FromBitmap(normalized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(dir, $"tpl_{name}_{b}.png"));
            data.SaveTo(stream);
        }
    }

    private static SKRectI Expand(SKRectI box, SKBitmap bitmap)
    {
        var mx = (int)(box.Height * MarginX);
        var my = (int)(box.Height * MarginY);
        return new SKRectI(
            Math.Max(0, box.Left - mx),
            Math.Max(0, box.Top - my),
            Math.Min(bitmap.Width, box.Right + mx),
            Math.Min(bitmap.Height, box.Bottom + my));
    }
}
