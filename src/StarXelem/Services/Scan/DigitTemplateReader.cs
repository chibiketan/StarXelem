using SkiaSharp;

namespace StarXelem.Services.Scan;

/// <summary>
/// Reconnaissance des chiffres du badge de signature par gabarits : la police du HUD est fixe, un OCR
/// générique n'est pas nécessaire et confond 6/8 à cette taille. Pipeline : recadrage autour du nombre →
/// agrandissement ×<see cref="UpscaleFactor"/> → canal vert normalisé (gamma) → binarisation → segmentation par
/// projection de colonnes → chaque glyphe ramené à <see cref="GlyphWidth"/>×<see cref="GlyphHeight"/> et comparé
/// aux gabarits 0–9 par corrélation normalisée. Les gabarits sont appris (<see cref="Train"/>) sur des captures
/// dont la valeur est connue.
/// </summary>
public sealed class DigitTemplateReader
{
    public const int GlyphWidth = 16;
    public const int GlyphHeight = 24;
    public const double UpscaleFactor = 4.0;
    private const int NearestNeighbours = 3;
    private const int BinarizeThreshold = 100;
    /// <summary>Un glyphe plus bas que cette fraction de la hauteur de ligne est une virgule ou du bruit.</summary>
    private const double MinDigitHeightRatio = 0.6;
    /// <summary>Colonnes vides tolérées à l'intérieur d'un même glyphe (anti-aliasing).</summary>
    private const int MaxGapInsideGlyph = 1;
    /// <summary>Largeur d'un chiffre de la police HUD rapportée à sa hauteur (mesuré sur les captures agrandies).</summary>
    private const double DigitWidthToHeight = 0.65;
    /// <summary>En dessous de cette fraction de la largeur attendue, un segment est un résidu (le « 1 » fait ~0,4).</summary>
    private const double MinWidthRatio = 0.2;

    /// <summary>Glyphe segmenté : boîte dans l'image agrandie, pixels bruts (16×24, 0..255) et vecteur normalisé (moyenne 0, norme 1).</summary>
    public sealed record Glyph(SKRectI Bounds, byte[] Raw, float[] Pixels);

    /// <summary>Résultat de lecture : chiffres, valeur, score moyen et minimal de corrélation (−1..1).</summary>
    public sealed record Reading(string Digits, int Value, double MeanScore, double MinScore, IReadOnlyList<Glyph> Glyphs);

    /// <summary>Glyphe d'apprentissage étiqueté ; <see cref="Raw"/> est la forme persistée, <see cref="Pixels"/> la forme comparée.</summary>
    public sealed record LabeledGlyph(char Digit, byte[] Raw, float[] Pixels)
    {
        public static LabeledGlyph FromRaw(char digit, byte[] raw) => new(digit, raw, NormalizeRaw(raw));
    }

    private readonly IReadOnlyList<LabeledGlyph> _samples;

    public DigitTemplateReader(IReadOnlyList<LabeledGlyph> samples)
    {
        _samples = samples;
    }

    public IReadOnlyList<LabeledGlyph> Samples => _samples;

    public IEnumerable<char> KnownDigits => _samples.Select(s => s.Digit).Distinct();

    /// <summary>Prépare un recadrage source (couleur, échelle native) et en extrait les glyphes.</summary>
    public static IReadOnlyList<Glyph> SegmentCrop(SKBitmap sourceCrop)
    {
        using var scaled = ScanImageOps.Scale(sourceCrop, UpscaleFactor);
        using var green = ScanImageOps.GreenChannel(scaled);
        using var normalized = ScanImageOps.NormalizeContrast(green, tileSize: 0);
        return Segment(normalized);
    }

    /// <summary>Lit un recadrage source ; null si le nombre de glyphes n'est pas plausible (4 à 6 chiffres).</summary>
    public Reading? Read(SKBitmap sourceCrop)
    {
        var glyphs = SegmentCrop(sourceCrop);
        if (glyphs.Count is < 4 or > 6) return null;

        var digits = new char[glyphs.Count];
        var scores = new double[glyphs.Count];
        for (var i = 0; i < glyphs.Count; i++)
        {
            (digits[i], scores[i]) = Classify(glyphs[i].Pixels);
        }

        var text = new string(digits);
        return new Reading(text, int.Parse(text), scores.Average(), scores.Min(), glyphs);
    }

    /// <summary>
    /// Plus proche voisin par corrélation : chaque glyphe d'apprentissage est conservé tel quel (un gabarit moyen
    /// par chiffre lisse précisément les détails qui séparent 0/6/9 ou 6/8 à cette taille). Le score est la
    /// corrélation avec le meilleur voisin ; à égalité de chiffre, les k meilleurs votent.
    /// </summary>
    private (char Digit, double Score) Classify(float[] pixels)
    {
        var best = _samples
            .Select(s => (s.Digit, Score: Dot(pixels, s.Pixels)))
            .OrderByDescending(s => s.Score)
            .Take(NearestNeighbours)
            .ToList();
        if (best.Count == 0) return ('?', -1);

        var digit = best.GroupBy(b => b.Digit)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(b => b.Score))
            .First().Key;
        return (digit, best.First(b => b.Digit == digit).Score);
    }

    /// <summary>
    /// Apprend les glyphes : pour chaque échantillon, les glyphes segmentés sont associés dans l'ordre aux
    /// chiffres attendus, à condition que leur nombre corresponde (sinon l'échantillon est ignoré).
    /// </summary>
    public static (DigitTemplateReader Reader, int Used, int Skipped) Train(IEnumerable<(SKBitmap Crop, string Digits)> samples)
    {
        var labeled = new List<LabeledGlyph>();
        var used = 0;
        var skipped = 0;

        foreach (var (crop, digits) in samples)
        {
            var glyphs = Label(crop, digits);
            if (glyphs == null)
            {
                skipped++;
                continue;
            }
            used++;
            labeled.AddRange(glyphs);
        }

        return (new DigitTemplateReader(labeled), used, skipped);
    }

    /// <summary>
    /// Étiquette les glyphes d'un recadrage dont la valeur est connue (auto-apprentissage) ; null si la
    /// segmentation ne donne pas exactement un glyphe par chiffre.
    /// </summary>
    public static IReadOnlyList<LabeledGlyph>? Label(SKBitmap crop, string digits)
    {
        var glyphs = SegmentCrop(crop);
        if (glyphs.Count != digits.Length) return null;
        return glyphs.Select((g, i) => new LabeledGlyph(digits[i], g.Raw, g.Pixels)).ToList();
    }

    /// <summary>Segmente une image en niveaux de gris déjà normalisée (texte clair sur fond sombre).</summary>
    public static IReadOnlyList<Glyph> Segment(SKBitmap normalized)
    {
        var width = normalized.Width;
        var height = normalized.Height;
        var bytes = normalized.Bytes;
        bool On(int x, int y) => bytes[(y * width + x) * 4 + 1] >= BinarizeThreshold;

        // Bande de lignes : la plus longue suite de lignes contenant du texte.
        var rowHas = new bool[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width && !rowHas[y]; x++) rowHas[y] = On(x, y);
        }
        var (bandTop, bandBottom) = LongestRun(rowHas);
        if (bandBottom <= bandTop) return Array.Empty<Glyph>();
        var bandHeight = bandBottom - bandTop;

        // Segments de colonnes dans la bande, en tolérant de petits trous.
        var colHas = new bool[width];
        for (var x = 0; x < width; x++)
        {
            for (var y = bandTop; y < bandBottom && !colHas[x]; y++) colHas[x] = On(x, y);
        }

        // Segments de colonnes (runs), bornés étroitement ; ceux qui touchent un bord du recadrage sont des
        // résidus de l'icône « pin » ou du texte voisin.
        var runs = new List<SKRectI>();
        var x0 = -1;
        var gap = 0;
        for (var x = 0; x <= width; x++)
        {
            var on = x < width && colHas[x];
            if (on)
            {
                if (x0 < 0) x0 = x;
                gap = 0;
            }
            else if (x0 >= 0 && (++gap > MaxGapInsideGlyph || x == width))
            {
                var x1 = x - gap + 1;
                if (x0 > 0 && x1 < width) runs.Add(TightBounds(x0, x1, bandTop, bandBottom, On));
                x0 = -1;
                gap = 0;
            }
        }
        if (runs.Count == 0) return Array.Empty<Glyph>();

        // Hauteur de chiffre = médiane des hauteurs de runs (la virgule et le bruit sont minoritaires) ; la police
        // HUD étant à chasse quasi fixe, la largeur attendue d'un chiffre s'en déduit. Les chiffres voisins
        // (ex. « 000 ») se touchent parfois après agrandissement : un run trop large est découpé en parts égales.
        var heights = runs.Select(r => r.Height).OrderBy(h => h).ToList();
        var digitHeight = heights[heights.Count / 2];
        var expectedWidth = digitHeight * DigitWidthToHeight;

        var glyphs = new List<Glyph>();
        foreach (var run in runs)
        {
            if (run.Height < digitHeight * MinDigitHeightRatio) continue; // virgule
            if (run.Width < expectedWidth * MinWidthRatio) continue;       // résidu
            var parts = Math.Max(1, (int)Math.Round(run.Width / expectedWidth));
            for (var p = 0; p < parts; p++)
            {
                var px0 = run.Left + run.Width * p / parts;
                var px1 = run.Left + run.Width * (p + 1) / parts;
                var bounds = TightBounds(px0, px1, bandTop, bandBottom, On);
                glyphs.Add(ExtractGlyph(normalized, bounds));
            }
        }
        return glyphs;
    }

    private static (int Start, int End) LongestRun(bool[] flags)
    {
        var bestStart = 0;
        var bestEnd = 0;
        var start = -1;
        for (var i = 0; i <= flags.Length; i++)
        {
            var on = i < flags.Length && flags[i];
            if (on && start < 0) start = i;
            if (!on && start >= 0)
            {
                if (i - start > bestEnd - bestStart) (bestStart, bestEnd) = (start, i);
                start = -1;
            }
        }
        return (bestStart, bestEnd);
    }

    private static SKRectI TightBounds(int x0, int x1, int y0, int y1, Func<int, int, bool> on)
    {
        var top = y1;
        var bottom = y0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (!on(x, y)) continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
                break;
            }
        }
        return new SKRectI(x0, top, x1, Math.Max(bottom, top + 1));
    }

    /// <summary>
    /// Ramène le glyphe à la hauteur <see cref="GlyphHeight"/> en conservant son rapport largeur/hauteur, centré
    /// dans une case <see cref="GlyphWidth"/>×<see cref="GlyphHeight"/> (un « 1 » étroit ne doit pas devenir un « 7 » large).
    /// </summary>
    private static Glyph ExtractGlyph(SKBitmap image, SKRectI bounds)
    {
        using var glyph = ScanImageOps.Crop(image, bounds);
        var width = Math.Clamp((int)Math.Round(bounds.Width * (double)GlyphHeight / bounds.Height), 1, GlyphWidth);
        using var resized = glyph.Resize(new SKImageInfo(width, GlyphHeight, SKColorType.Bgra8888, SKAlphaType.Opaque), SKFilterQuality.High)
                            ?? glyph.Copy();
        var bytes = resized.Bytes;
        var raw = new byte[GlyphWidth * GlyphHeight];
        var offset = (GlyphWidth - width) / 2;
        for (var y = 0; y < GlyphHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                raw[y * GlyphWidth + offset + x] = bytes[(y * width + x) * 4 + 1];
            }
        }
        return new Glyph(bounds, raw, NormalizeRaw(raw));
    }

    public static float[] NormalizeRaw(byte[] raw)
    {
        var pixels = new float[raw.Length];
        for (var i = 0; i < raw.Length; i++) pixels[i] = raw[i];
        return Normalize(pixels);
    }

    /// <summary>Moyenne 0, norme 1 : la corrélation devient un simple produit scalaire.</summary>
    private static float[] Normalize(float[] pixels)
    {
        var mean = pixels.Average();
        var result = new float[pixels.Length];
        double sumSq = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            result[i] = pixels[i] - (float)mean;
            sumSq += result[i] * result[i];
        }
        var norm = (float)Math.Sqrt(sumSq);
        if (norm < 1e-6f) return result;
        for (var i = 0; i < result.Length; i++) result[i] /= norm;
        return result;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}
