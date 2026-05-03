using System.Text.Json.Serialization;

namespace StarXelem.Models;

/// <summary>
/// Représente une classe de vaisseau avec le nombre d'unités pour la synchronisation Alliance Orbital.
/// </summary>
public class FleetSyncItem
{
    /// <summary>
    /// GUID de la classe du vaisseau (EntityClassGuid).
    /// </summary>
    [JsonPropertyName("entityClassGuid")]
    public string EntityClassGuid { get; set; } = "";

    /// <summary>
    /// Nombre de vaisseaux de cette classe.
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
