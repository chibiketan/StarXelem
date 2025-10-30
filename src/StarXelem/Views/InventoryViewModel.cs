using Sc.External.Services.Entitygraph.V1;
using StarXelem.ViewModels;

namespace StarXelem.Views;

public class InventoryViewModel : ViewModelBase
{
    private readonly InventoryNodeProperties _inventoryNodeProperties;

    public string Id => _inventoryNodeProperties.Id;
    public string Context => _inventoryNodeProperties.Context;
    public string Name => _inventoryNodeProperties.Name;
    public string UnstowedIn => _inventoryNodeProperties.UnstowedIn;
    public ulong SubjectId => _inventoryNodeProperties.SubjectId;
    public InventoryType Type => _inventoryNodeProperties.InventoryType;
    public InventoryConfiguration.ConfigurationOneofCase ConfigurationCase => _inventoryNodeProperties.Configuration.ConfigurationCase;
    public int? PhysicalCapacity => _inventoryNodeProperties.Configuration.Physical?.Capacity;
    public int? PhysicalOccupancy => _inventoryNodeProperties.Configuration.Physical?.Occupancy;
    
    public InventoryViewModel(InventoryNodeProperties inventoryNodeProperties)
    {
        _inventoryNodeProperties = inventoryNodeProperties;
    }
}