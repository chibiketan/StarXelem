using System.Text.Json.Serialization;

namespace StarXelem.Models;

/// <summary>
/// Représente un item de l'inventaire pour la synchronisation avec Alliance Orbital.
/// Correspond au schéma ItemDto de l'API /api/external/items.
/// </summary>
public class ItemSyncItem
{
    /// <summary>Global Entity ID (identifiant unique de l'item dans l'EntityGraph).</summary>
    [JsonPropertyName("geid")]
    public ulong Geid { get; set; }

    /// <summary>CRC32 du GUID de classe de l'item. Utilisé pour résoudre le nom et le type via l'EntityIndex.</summary>
    [JsonPropertyName("classGuidCrc")]
    public uint ClassGuidCrc { get; set; }

    /// <summary>GEID du propriétaire de l'item.</summary>
    [JsonPropertyName("ownerId")]
    public ulong OwnerId { get; set; }

    /// <summary>URN de l'entité parente (format "sc:entity:<geid>").</summary>
    [JsonPropertyName("parentUrn")]
    public string? ParentUrn { get; set; } = "";

    /// <summary>Type d'item EntityGraph : 1=Vehicle, 2=Weapon, 3=Armor, 4=Consumable, 99=Other.</summary>
    [JsonPropertyName("itemType")]
    public int ItemType { get; set; }

    /// <summary>Sous-type : 1=Boat, 2=GroundVehicle, 3=PowerSuit, 4=Spaceship, 99=Other.</summary>
    [JsonPropertyName("itemSubType")]
    public int? ItemSubType { get; set; }

    /// <summary>Identifiant de l'inventaire parent au format "ownerId:Location:crc" ou "ownerId:Inventory:containerGeid".</summary>
    [JsonPropertyName("stowedIn")]
    public string? StowedIn { get; set; }
}
