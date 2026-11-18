using StarBreaker.DataCoreGenerated;

using StarXelem.Models;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel
{
    private void ApplyLocationDisplays(IReadOnlyList<ItemViewModel> items)
    {
        var itemsById = items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());

        foreach (var item in items)
        {
            item.LocationDisplay = _resolvedLocations.TryGetValue(item.Id, out var location)
                ? CreateLocationDisplay(location, itemsById)
                : null;
        }
    }

    private ItemLocationDisplay CreateLocationDisplay(ResolvedLocation location, Dictionary<ulong, ItemViewModel> itemsById)
    {
        var path = BuildLocationPath(location);

        return new ItemLocationDisplay(
            GetCategory(location, itemsById),
            location.Name,
            path.Count == 0 ? null : string.Join(" › ", path),
            path.Count == 0 ? null : path[^1]);
    }

    private static ItemLocationCategory GetCategory(ResolvedLocation location, Dictionary<ulong, ItemViewModel> itemsById)
    {
        return location.Kind switch
        {
            LocationKind.Location => ItemLocationCategory.Place,
            LocationKind.Hangar => ItemLocationCategory.Hangar,
            LocationKind.Worn => ItemLocationCategory.Worn,
            LocationKind.Item when location.Id is { } id && itemsById.TryGetValue(id, out var carrier) => GetItemCategory(carrier.ItemType),
            _ => ItemLocationCategory.Other
        };
    }

    private static ItemLocationCategory GetItemCategory(EItemType type)
    {
        var name = type.ToString();

        if (type == EItemType.NOITEM_Vehicle)
        {
            return ItemLocationCategory.Ship;
        }

        if (type == EItemType.NOITEM_Player)
        {
            return ItemLocationCategory.Worn;
        }

        if (type is EItemType.Container or EItemType.InventoryContainer)
        {
            return ItemLocationCategory.Container;
        }

        if (name.StartsWith("Char_Armor_", StringComparison.Ordinal))
        {
            return ItemLocationCategory.Armor;
        }

        return type == EItemType.WeaponPersonal || name.StartsWith("FPS_Weapon", StringComparison.Ordinal)
            ? ItemLocationCategory.Weapon
            : ItemLocationCategory.Other;
    }

    // Le service ne retourne que le dernier saut. Chaque objet porteur ayant lui-même été résolu, on en déduit le chemin.
    private List<string> BuildLocationPath(ResolvedLocation location)
    {
        var path = new List<string>();
        var visited = new HashSet<ulong>();
        var current = location;

        while (current.Kind == LocationKind.Item
               && current.Id is { } id
               && visited.Add(id)
               && _resolvedLocations.TryGetValue(id, out var next))
        {
            path.Add(next.DisplayName);
            current = next;
        }

        return path;
    }
}
