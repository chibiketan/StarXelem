using System.ComponentModel.DataAnnotations;

namespace StarXelem.Models;

public enum ComparisonDiffType
{
    [Display(Name = "Égal")]
    Equal,
    [Display(Name = "Gain")]
    Gain,
    [Display(Name = "Perte")]
    Loss,
    [Display(Name = "Disparition")]
    OnlySource,
    [Display(Name = "Création")]
    OnlyTarget
}

public class ItemTypeComparisonResult
{
    public string TechnicalType { get; set; } = string.Empty;
    public int SourceCountSum { get; set; }
    public int TargetStackSum { get; set; }
    public ComparisonDiffType Status { get; set; }
    public string? Name { get; set; }
}
