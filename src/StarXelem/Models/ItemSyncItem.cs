using System.Text.Json.Serialization;

namespace StarXelem.Models;

/// <summary>
/// Représente un objet pour la synchronisation Alliance Orbital.
/// </summary>
public class ItemSyncItem
{
    /// <summary>
    /// GUID du type de l'objet (correspond à TypeGuid de ItemViewModel).
    /// </summary>
    [JsonPropertyName("itemGuid")]
    public string ItemGuid { get; set; } = "";

    /// <summary>
    /// Quantité d'objets de ce type.
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
