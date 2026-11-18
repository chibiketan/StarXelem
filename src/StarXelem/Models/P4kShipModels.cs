namespace StarXelem.Models;

public class P4kShipModel
{
    public required string Name { get; set; }
    public required string TechnicalName { get; set; }
    public required string Guid { get; set; }
    public string? Manufacturer { get; set; }
    public string Tags { get; set; } = string.Empty;
    public IReadOnlyList<string> TagNames { get; set; } = [];
    public bool IsVisible { get; set; }
}

public class P4kShipComponentModel
{
    public required string PortName { get; set; }
    public required string DisplayName { get; set; }
    public required ComponentClass Class { get; set; }
    public required int Size { get; set; }
    public required string Grade { get; set; }
    public string? WeaponType { get; set; }
    public string? GuidanceType { get; set; }
    public float? AlphaDamage { get; set; }

    /// <summary>Classe et taille, ex : « Militaire · S1 ».</summary>
    public string DetailLabel => string.Join(" · ", new[] { ClassLabel, Size > 0 ? $"S{Size}" : null }.Where(part => part != null));

    /// <summary>Classe du composant en français, ou <c>null</c> si elle est inconnue.</summary>
    public string? ClassLabel => Class switch
    {
        ComponentClass.Military => "Militaire",
        ComponentClass.Industrial => "Industriel",
        ComponentClass.Civilian => "Civil",
        ComponentClass.Competition => "Compétition",
        ComponentClass.Stealth => "Furtif",
        _ => null
    };

    public bool HasGrade => !string.IsNullOrEmpty(Grade);
    public bool IsGradeA => Grade == "A";
    public bool IsGradeB => Grade == "B";
    public bool IsGradeC => Grade == "C";
    public bool IsGradeLow => Grade is "D" or "E";

    public string SizeLabel => Size > 0 ? Size.ToString() : "—";
    public string WeaponTypeLabel => string.IsNullOrEmpty(WeaponType) ? "—" : WeaponType;
    public string GuidanceLabel => string.IsNullOrEmpty(GuidanceType) ? "—" : GuidanceType;
    public string DamageLabel => AlphaDamage is > 0 ? AlphaDamage.Value.ToString("N1") : "—";
    public string ComponentClassLabel => ClassLabel ?? "—";

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

/// <summary>Groupe de composants d'un même type (générateurs, refroidisseurs…), affiché dans une carte.</summary>
public record P4kShipComponentSection(string Title, IReadOnlyList<P4kShipComponentModel> Components);

public enum ComponentClass
{
    Unknown,
    Military,
    Industrial,
    Civilian,
    Competition,
    Stealth,
}
