namespace StarXelem.Services.Mining;

/// <summary>
/// Contexte de minage d'un rocher. Détermine la plage de tailles de cluster générée par
/// <see cref="MineralSignatureGenerator"/> : les rochers minés au vaisseau ou en surface portent une
/// signature unique par minéral et se trouvent en petits clusters (1-10) ; les rochers minés à la main
/// (FPS) ou au véhicule terrestre partagent une signature générique par catégorie (pas par minéral) et
/// se trouvent en clusters beaucoup plus importants (20-30).
/// </summary>
public enum MiningKind
{
    ShipOrSurface,
    Fps,
    GroundVehicle,
}
