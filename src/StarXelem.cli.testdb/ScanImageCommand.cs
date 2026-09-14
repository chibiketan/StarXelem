using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using StarXelem.Models;
using StarXelem.Services.Scan;

namespace StarXelem.Cli.TestDb;

/// <summary>
/// Banc de test OCR : exécute le pipeline <see cref="ISignatureOcrService"/> sur des fichiers image (captures du jeu)
/// et affiche les candidats lus. Sert de jeu de non-régression : chaque capture ratée est à ajouter à
/// <c>private/debug/</c> et à repasser ici après tout ajustement du pré-traitement ou des seuils.
/// Usage : <c>scan-image &lt;image|dossier&gt;… [--expect 19425,16900,…] [--learn]</c> (ou un <c>expected.txt</c> dans le dossier).
/// </summary>
public static class ScanImageCommand
{
    public static async Task<int> RunAsync(string[] args, ILoggerFactory loggerFactory)
    {
        var expected = new List<int>();
        var paths = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--expect" && i + 1 < args.Length)
            {
                expected.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse));
            }
            else
            {
                paths.Add(args[i]);
            }
        }

        var files = paths
            .SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                : [p])
            .OrderBy(f => f)
            .ToList();

        // Sans --expect, un fichier expected.txt (valeurs séparées par des virgules, dans l'ordre alphabétique
        // des captures) placé dans le dossier sert de vérité terrain.
        if (expected.Count == 0 && paths.Count == 1 && Directory.Exists(paths[0]))
        {
            var expectedFile = Path.Combine(paths[0], "expected.txt");
            if (File.Exists(expectedFile))
            {
                expected.AddRange(File.ReadAllText(expectedFile).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse));
            }
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine("Usage : scan-image <image|dossier>… [--expect v1,v2,…]");
            return 2;
        }

        // Base de glyphes figée (graine embarquée, ou STARXELEM_SCAN_GLYPHS) : le banc doit rester reproductible,
        // sauf avec --learn qui rejoue l'auto-apprentissage de l'application dans le fichier STARXELEM_SCAN_GLYPHS.
        var learn = paths.Remove("--learn");
        var glyphStore = new DigitGlyphStore(Environment.GetEnvironmentVariable("STARXELEM_SCAN_GLYPHS"), autoLearnEnabled: learn, loggerFactory.CreateLogger<DigitGlyphStore>());
        var ocr = new WindowsSignatureOcrService(glyphStore, loggerFactory.CreateLogger<WindowsSignatureOcrService>());
        Console.WriteLine($"Base de glyphes : {glyphStore.Glyphs.Count} glyphes.");
        if (!ocr.IsAvailable)
        {
            Console.Error.WriteLine($"OCR indisponible : {ocr.UnavailableReason}");
            return 3;
        }

        var success = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            using var bitmap = SKBitmap.Decode(file);
            if (bitmap == null)
            {
                Console.WriteLine($"[{Path.GetFileName(file)}] image illisible");
                continue;
            }

            // Le décodeur peut renvoyer un autre format que BGRA8888 (JPEG) : on normalise comme la capture GDI.
            using var frameBitmap = bitmap.ColorType == SKColorType.Bgra8888 && bitmap.AlphaType == SKAlphaType.Opaque
                ? bitmap.Copy()
                : bitmap.Copy(SKColorType.Bgra8888);
            using var frame = new CapturedFrame(frameBitmap, 0, 0);

            var sw = Stopwatch.StartNew();
            var candidates = await ocr.FindSignatureCandidatesAsync(frame);
            sw.Stop();

            var best = candidates.FirstOrDefault();
            var expect = i < expected.Count ? expected[i] : (int?)null;
            var verdict = expect == null ? "" : best?.Value == expect ? "  ✓" : $"  ✗ (attendu {expect})";
            if (expect != null && best?.Value == expect) success++;

            Console.WriteLine($"[{Path.GetFileName(file)}] {bitmap.Width}x{bitmap.Height} {sw.ElapsedMilliseconds}ms → " +
                              (best == null ? "aucune lecture" : $"{best.Value} ({best.Votes}/{best.ValidPasses} votes, '{best.RawText}' à {best.ScreenBounds})") + verdict);
            foreach (var other in candidates.Skip(1))
            {
                var mark = expect != null && other.Value == expect ? "  ✓ (arbitrage possible)" : "";
                Console.WriteLine($"    autre : {other.Value} ({other.Votes}/{other.ValidPasses}, '{other.RawText}'){mark}");
            }
        }

        await glyphStore.FlushAsync();
        if (learn) Console.WriteLine($"Base de glyphes après apprentissage : {glyphStore.Glyphs.Count} glyphes.");

        if (expected.Count > 0)
        {
            Console.WriteLine($"\n{success}/{expected.Count} corrects.");
            return success == expected.Count ? 0 : 1;
        }
        return 0;
    }
}
