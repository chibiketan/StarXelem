using CommunityToolkit.Mvvm.ComponentModel;
using StarXelem.Services;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public partial class ReputationModel : ViewModelBase
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public int? CurrentValue { get; set; }
    public float MaxValue { get; set; }
    public string DisplayName { get; set; }
    public List<StandingModel> StandingList { get; set; } = new();
    public StandingModel? CurrentStanding { get; set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }
}
