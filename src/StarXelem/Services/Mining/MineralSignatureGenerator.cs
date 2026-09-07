using StarXelem.Constants;
using StarXelem.Data;

namespace StarXelem.Services.Mining;

/// <summary>
/// Génère les lignes de la table <c>MineralSignatures</c> à partir des signatures de base extraites du p4k.
/// Deux plages de taille de cluster selon le contexte de minage (<see cref="MiningKind"/>) :
/// <see cref="ScanConstants.MinClusterSize"/>-<see cref="ScanConstants.MaxClusterSize"/> pour les rochers
/// minés au vaisseau/en surface (signature unique par minéral), <see cref="ScanConstants.MinLargeClusterSize"/>-
/// <see cref="ScanConstants.MaxLargeClusterSize"/> pour ceux minés à la main ou au véhicule terrestre
/// (signature générique par catégorie, cf. Documents/Datafiles/MineralSignatures.md).
/// C'est le seul endroit à modifier si l'hypothèse "signature(cluster de N) = N × signature(rocher isolé)"
/// devait être invalidée après validation en jeu (cf. Documents/Plans/2026-09-07-scan-signature-overlay.md, §0.1).
/// </summary>
public static class MineralSignatureGenerator
{
    public static IEnumerable<MineralSignatureEntity> Generate(IEnumerable<MineralBaseSignature> bases)
    {
        foreach (var b in bases)
        {
            var (min, max) = b.Kind switch
            {
                MiningKind.Fps or MiningKind.GroundVehicle => (ScanConstants.MinLargeClusterSize, ScanConstants.MaxLargeClusterSize),
                _ => (ScanConstants.MinClusterSize, ScanConstants.MaxClusterSize),
            };

            for (var n = min; n <= max; n++)
            {
                yield return new MineralSignatureEntity
                {
                    MineralKey = b.MineralKey,
                    MineralName = b.MineralName,
                    Rarity = b.Rarity,
                    MiningType = b.Kind.ToString(),
                    BaseSignature = b.BaseSignature,
                    ClusterSize = n,
                    Signature = b.BaseSignature * n,
                };
            }
        }
    }
}
