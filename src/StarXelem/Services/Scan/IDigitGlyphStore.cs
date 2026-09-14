namespace StarXelem.Services.Scan;

/// <summary>
/// Base de glyphes de chiffres étiquetés pour <see cref="DigitTemplateReader"/> : une graine embarquée dans
/// l'application, enrichie au fil des scans par auto-apprentissage et persistée dans le profil utilisateur.
/// </summary>
public interface IDigitGlyphStore
{
    /// <summary>Instantané des glyphes connus (thread-safe, ne change pas après retour).</summary>
    IReadOnlyList<DigitTemplateReader.LabeledGlyph> Glyphs { get; }

    /// <summary>false pour figer la base (bancs de test reproductibles).</summary>
    bool AutoLearnEnabled { get; }

    /// <summary>Ajoute des glyphes appris et planifie la sauvegarde. Sans effet si <see cref="AutoLearnEnabled"/> est false.</summary>
    void Learn(IEnumerable<DigitTemplateReader.LabeledGlyph> glyphs);
}
