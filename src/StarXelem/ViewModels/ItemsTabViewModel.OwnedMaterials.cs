using StarXelem.Models;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel
{
    private const double MicroScuPerScu = 1_000_000d;

    /// <summary>
    /// Publie les objets chargés pour l'écran Blueprints. Tous les objets sont retenus : un coût « Item » peut désigner
    /// n'importe quelle classe, et l'index par classe rend la recherche immédiate.
    /// </summary>
    private void PublishOwnedMaterials(IReadOnlyList<ItemViewModel> items, DateTime loadedAt, bool areLocationsResolved)
    {
        var lots = items
            .Select(item => new OwnedMaterialLot(
                item.Id,
                item.ClassRecordId,
                item.ContainedResourceRef,
                item.ContentQuality,
                (item.ContentQuantity ?? 0) / MicroScuPerScu,
                (int)(item.StackSize ?? 1),
                item.LocationDisplay))
            .ToList();

        _ownedMaterialService.Publish(new OwnedMaterialInventory(lots, loadedAt, items.Count, areLocationsResolved));
    }
}
