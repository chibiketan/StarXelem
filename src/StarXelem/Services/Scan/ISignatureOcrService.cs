using StarXelem.Models;

namespace StarXelem.Services.Scan;

public interface ISignatureOcrService
{
    /// <summary>true si le moteur OCR et la langue anglaise sont disponibles sur cette machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Raison lisible de l'indisponibilité (pack de langue manquant, etc.), null si disponible.</summary>
    string? UnavailableReason { get; }

    Task<IReadOnlyList<SignatureCandidate>> FindSignatureCandidatesAsync(CapturedFrame frame, CancellationToken ct = default);
}
