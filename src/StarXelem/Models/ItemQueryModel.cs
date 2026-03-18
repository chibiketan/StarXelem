using StarBreaker.DataCoreGenerated;

namespace StarXelem.Models;

public class ItemQueryModel
{
    public bool useConnectedUserOwner { get; set; } = true;
    public string? ownerId { get; set; }
    public string? Id { get; set; }
    public List<EItemType>? TypeList { get; set; }
    public List<String>? InventoryIdList { get; set; }
    public List<String>? ParentUrnList { get; set; }
    public bool UseProjection { get; set; } = false;
    public bool LoadInventoryContent { get; set; } = false;
}