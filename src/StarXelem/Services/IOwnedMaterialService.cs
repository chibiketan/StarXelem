using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Partage les matériaux possédés entre l'écran Objets, qui les charge, et l'écran Blueprints, qui les compare aux coûts.
/// </summary>
public interface IOwnedMaterialService
{
    /// <summary>Dernier instantané publié, ou null si les objets n'ont pas encore été chargés.</summary>
    OwnedMaterialInventory? Current { get; }

    /// <summary>Levé après chaque publication, sur le thread de l'appelant.</summary>
    event EventHandler? InventoryChanged;

    /// <summary>Remplace l'instantané courant.</summary>
    /// <param name="inventory">Nouvel instantané.</param>
    void Publish(OwnedMaterialInventory inventory);
}
