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

    /// <summary>
    /// Ajoute les glyphes d'un badge lu et planifie la sauvegarde. Sans effet si <see cref="AutoLearnEnabled"/> est
    /// false. Le lot entier est refusé si un glyphe est quasi identique à un glyphe connu sous une autre étiquette
    /// (étiquetage OCR contredit par la base) ; les doublons d'un glyphe déjà connu sous la même étiquette sont ignorés.
    /// </summary>
    LearnResult Learn(IReadOnlyList<DigitTemplateReader.LabeledGlyph> glyphs);
}

/// <summary>Bilan d'un apprentissage : glyphes ajoutés, doublons ignorés, et chiffre en conflit s'il y en a un (lot refusé).</summary>
public sealed record LearnResult(int Added, int Duplicates, string? Conflict);
