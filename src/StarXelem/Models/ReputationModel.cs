namespace StarXelem.Models;

public class ReputationModel
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public float CurrentValue { get; set; }
    public float MaxValue { get; set; }
}
