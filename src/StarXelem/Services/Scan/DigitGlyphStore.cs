using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StarXelem.Constants;

namespace StarXelem.Services.Scan;

/// <summary>
/// Implémentation fichier de <see cref="IDigitGlyphStore"/> : JSON (chiffre + 16×24 octets en base64) dans le
/// dossier de données de l'application, initialisé depuis la graine embarquée <c>Resources/scan-glyphs.json</c>.
/// La base est plafonnée par chiffre (les plus anciens glyphes sont évincés) pour borner le coût du plus proche
/// voisin et laisser la base suivre d'éventuels changements de police du HUD.
/// </summary>
public sealed class DigitGlyphStore : IDigitGlyphStore
{
    private const string SeedResourceName = "StarXelem.Resources.scan-glyphs.json";

    private readonly string? _userFilePath;
    private readonly ILogger<DigitGlyphStore> _logger;
    private readonly object _gate = new();
    private readonly Lazy<List<DigitTemplateReader.LabeledGlyph>> _glyphs;
    private IReadOnlyList<DigitTemplateReader.LabeledGlyph>? _snapshot;
    private Task _pendingSave = Task.CompletedTask;

    public bool AutoLearnEnabled { get; }

    /// <param name="userFilePath">Fichier de persistance ; null = graine embarquée seule (lecture seule).</param>
    public DigitGlyphStore(string? userFilePath, bool autoLearnEnabled, ILogger<DigitGlyphStore> logger)
    {
        _userFilePath = userFilePath;
        AutoLearnEnabled = autoLearnEnabled && userFilePath != null;
        _logger = logger;
        _glyphs = new Lazy<List<DigitTemplateReader.LabeledGlyph>>(Load);
    }

    public IReadOnlyList<DigitTemplateReader.LabeledGlyph> Glyphs
    {
        get
        {
            lock (_gate)
            {
                return _snapshot ??= _glyphs.Value.ToList();
            }
        }
    }

    public LearnResult Learn(IReadOnlyList<DigitTemplateReader.LabeledGlyph> glyphs)
    {
        if (!AutoLearnEnabled) return new LearnResult(0, 0, null);

        lock (_gate)
        {
            var list = _glyphs.Value;

            // Garde-fou contre l'auto-empoisonnement : un vote OCR unanime peut être faux (18,960 pour 16,960).
            // Si un glyphe à apprendre ressemble fortement à un glyphe connu sous une autre étiquette, c'est
            // l'étiquette OCR qui est suspecte : tout le badge est refusé.
            var toAdd = new List<DigitTemplateReader.LabeledGlyph>();
            var duplicates = 0;
            foreach (var glyph in glyphs)
            {
                var isDuplicate = false;
                foreach (var known in list)
                {
                    var score = Correlation(glyph.Pixels, known.Pixels);
                    if (known.Digit != glyph.Digit && score >= ScanConstants.GlyphConflictCorrelation)
                    {
                        return new LearnResult(0, 0, $"{glyph.Digit} ressemble à {known.Digit} ({score:F3})");
                    }
                    if (known.Digit == glyph.Digit && score >= ScanConstants.GlyphDuplicateCorrelation)
                    {
                        isDuplicate = true;
                    }
                }
                if (isDuplicate) duplicates++;
                else toAdd.Add(glyph);
            }
            if (toAdd.Count == 0) return new LearnResult(0, duplicates, null);

            list.AddRange(toAdd);
            foreach (var group in list.GroupBy(g => g.Digit).Where(g => g.Count() > ScanConstants.GlyphStoreMaxPerDigit).ToList())
            {
                foreach (var old in group.Take(group.Count() - ScanConstants.GlyphStoreMaxPerDigit)) list.Remove(old);
            }
            _snapshot = null;
            var toSave = list.ToList();
            _pendingSave = _pendingSave.ContinueWith(_ => Save(toSave), TaskScheduler.Default);
            return new LearnResult(toAdd.Count, duplicates, null);
        }
    }

    /// <summary>Attend la fin des sauvegardes planifiées (utile avant la sortie d'un processus court, ex. banc de test).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _pendingSave;
        }
    }

    private static double Correlation(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private List<DigitTemplateReader.LabeledGlyph> Load()
    {
        var glyphs = new List<DigitTemplateReader.LabeledGlyph>();
        try
        {
            if (_userFilePath != null && File.Exists(_userFilePath))
            {
                glyphs.AddRange(Parse(File.ReadAllText(_userFilePath)));
                _logger.LogInformation("Base de glyphes utilisateur chargée : {Count} glyphes ({Path}).", glyphs.Count, _userFilePath);
                return glyphs;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SeedResourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                glyphs.AddRange(Parse(reader.ReadToEnd()));
                _logger.LogInformation("Base de glyphes initialisée depuis la graine embarquée : {Count} glyphes.", glyphs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger la base de glyphes, la reconnaissance par gabarits est désactivée.");
            glyphs.Clear();
        }
        return glyphs;
    }

    private void Save(List<DigitTemplateReader.LabeledGlyph> glyphs)
    {
        if (_userFilePath == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_userFilePath)!);
            File.WriteAllText(_userFilePath, Serialize(glyphs));
            _logger.LogDebug("Base de glyphes sauvegardée : {Count} glyphes.", glyphs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de sauvegarder la base de glyphes ({Path}).", _userFilePath);
        }
    }

    public static string Serialize(IEnumerable<DigitTemplateReader.LabeledGlyph> glyphs)
    {
        var file = new GlyphFile(1, glyphs.Select(g => new GlyphEntry(g.Digit.ToString(), Convert.ToBase64String(g.Raw))).ToList());
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    public static IEnumerable<DigitTemplateReader.LabeledGlyph> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<GlyphFile>(json, JsonOptions);
        if (file == null) yield break;
        foreach (var entry in file.Glyphs)
        {
            var raw = Convert.FromBase64String(entry.Pixels);
            if (entry.Digit.Length != 1 || raw.Length != DigitTemplateReader.GlyphWidth * DigitTemplateReader.GlyphHeight) continue;
            yield return DigitTemplateReader.LabeledGlyph.FromRaw(entry.Digit[0], raw);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record GlyphFile([property: JsonPropertyName("version")] int Version, [property: JsonPropertyName("glyphs")] List<GlyphEntry> Glyphs);

    private sealed record GlyphEntry([property: JsonPropertyName("d")] string Digit, [property: JsonPropertyName("p")] string Pixels);
}
