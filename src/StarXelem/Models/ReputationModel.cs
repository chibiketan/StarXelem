using StarXelem.Services;

namespace StarXelem.Models;

public class ReputationModel
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public int? CurrentValue { get; set; }
    public float MaxValue { get; set; }
    public string DisplayName { get; set; }
    public List<StandingModel> StandingList { get; set; } = new();
    public StandingModel? CurrentStanding { get; set; }
    public bool IsExpanded { get; set; }
}
