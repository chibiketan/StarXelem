using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Implémentation en mémoire de <see cref="IOwnedMaterialService"/> : l'instantané n'est pas conservé entre deux lancements.
/// </summary>
public class OwnedMaterialService : IOwnedMaterialService
{
    private OwnedMaterialInventory? _current;

    /// <inheritdoc />
    public OwnedMaterialInventory? Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler? InventoryChanged;

    /// <inheritdoc />
    public void Publish(OwnedMaterialInventory inventory)
    {
        Volatile.Write(ref _current, inventory);
        InventoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
