using StarXelem.Models;

namespace StarXelem.Services.Scan;

/// <summary>Implémentation inerte utilisée en mode design (Avalonia <c>Design.IsDesignMode</c>).</summary>
public class DesignSignatureOcrService : ISignatureOcrService
{
    public bool IsAvailable => false;
    public string? UnavailableReason => "OCR non disponible en mode design.";

    public Task<IReadOnlyList<SignatureCandidate>> FindSignatureCandidatesAsync(CapturedFrame frame, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SignatureCandidate>>(Array.Empty<SignatureCandidate>());
}
