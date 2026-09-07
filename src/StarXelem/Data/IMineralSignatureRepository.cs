namespace StarXelem.Data;

public interface IMineralSignatureRepository
{
    /// <summary>Toutes les lignes dont |Signature - value| ≤ tolerance, triées par écart absolu puis ClusterSize.</summary>
    Task<IReadOnlyList<MineralSignatureEntity>> FindBySignatureAsync(int value, int tolerance, CancellationToken ct = default);

    Task<IReadOnlyList<MineralSignatureEntity>> GetAllAsync(CancellationToken ct = default);
}
