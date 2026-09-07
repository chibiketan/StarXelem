namespace StarXelem.Services.Mining;

/// <summary>Signature radar de base (cluster d'un seul rocher) d'un minéral, lue depuis le p4k.</summary>
public sealed record MineralBaseSignature(string MineralKey, string MineralName, string Rarity, int BaseSignature, MiningKind Kind);

/// <summary>
/// Extrait, depuis les entités <c>MineableRock_*</c> du p4k déjà ouvert, la signature radar de base
/// (pour un rocher isolé) de chaque minéral minable.
/// </summary>
public interface IMineralSignatureExtractor
{
    /// <summary>Le p4k doit déjà être ouvert par l'appelant (<see cref="IP4kService.OpenP4k"/>).</summary>
    Task<IReadOnlyList<MineralBaseSignature>> ExtractAsync(CancellationToken cancellationToken = default);
}
