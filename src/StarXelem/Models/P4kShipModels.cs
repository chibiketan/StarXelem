namespace StarXelem.Models;

public class P4kShipModel
{
    public required string Name { get; set; }
    public required string TechnicalName { get; set; }
    public required string Guid { get; set; }
    public string? Manufacturer { get; set; }
    public string Tags { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
}

public class P4kShipComponentModel
{
    public required string PortName { get; set; }
    public required string DisplayName { get; set; }
    public required ComponentClass Class { get; set; }
    public required int Size { get; set; }
    public required string Grade { get; set; }

    public string? ClassIconPath
    {
        get
        {
            switch (Class)
            {
                case ComponentClass.Military: return "/Assets/Components/icone_militaire_50.png";
                case ComponentClass.Civilian: return "/Assets/Components/icone_civile_50.png";
                case ComponentClass.Competition: return "/Assets/Components/icone_competition_50.png";
                case ComponentClass.Industrial: return "/Assets/Components/icone_industriel_50.png";
                case ComponentClass.Stealth: return "/Assets/Components/icone_stealth_50.png";
                default: return null;
            }
        }
    }
}

public class P4kShipManufacturerModel
{
    public required string Name { get; set; }
}

public enum ComponentClass
{
    Unknown,
    Military,
    Industrial,
    Civilian,
    Competition,
    Stealth,
}
