using StarBreaker.DataCoreGenerated;

namespace StarXelem.Models;

public class P4kShipModel
{
    public required string Name { get; set; }
    public required string TechnicalName { get; set; }
    public required EntityClassDefinition EntityClass { get; init; }
    public string? Manufacturer { get; set; }
    public string Tags { get; set; }
    public bool IsVisible { get; set; }
}

public class P4kShipComponentModel
{
    public required string PortName { get; set; }
    public required string DisplayName { get; set; }
    public required string MinSize { get; set; }
    public required string MaxSize { get; set; }
    public required string Flags { get; set; }
}
