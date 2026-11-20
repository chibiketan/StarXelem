using StarXelem.Models;

namespace StarXelem.Services.ShipLoadout;

/// <summary>Construit le loadout d'un vaisseau possédé : objets réellement montés, ou équipement d'origine à défaut.</summary>
public interface IShipLoadoutService
{
    /// <summary>
    /// Renvoie le loadout réel si le vaisseau a été retrouvé dans l'univers (chaque écart avec l'origine est signalé), sinon le
    /// loadout d'origine lu dans le Data.p4k.
    /// </summary>
    Task<OwnedShipLoadoutModel> GetLoadoutAsync(SpaceshipModel ship, CancellationToken token = default);
}
