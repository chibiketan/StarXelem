using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarXelem.Services;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public partial class ReputationModel : ViewModelBase
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public int? CurrentValue { get; set; }
    public float MaxValue { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<StandingModel> StandingList { get; set; } = new();
    public StandingModel? CurrentStanding { get; set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>Valeur actuelle et seuil du rang suivant, « — » pour ce qui est inconnu ou sans objet.</summary>
    public string ValueDisplay
    {
        get
        {
            var current = CurrentValue?.ToString() ?? "—";
            return CurrentStanding is { Max: >= 0 } standing ? $"{current} / {standing.Max}" : current;
        }
    }

    /// <summary>Bornes de la barre de progression : celles du rang actuel.</summary>
    public double ProgressMinimum => CurrentStanding?.Min ?? 0;

    public double ProgressMaximum => CurrentStanding is { Max: > 0 } standing && standing.Max > standing.Min ? standing.Max : ProgressMinimum + 1;

    public double ProgressValue => CurrentValue ?? ProgressMinimum;

    public string CurrentStandingName => CurrentStanding?.DisplayName ?? "—";

    /// <summary>Vrai si la portée a des rangs à déplier.</summary>
    public bool HasStandings => StandingList.Count > 0;

    /// <summary>Rangs de la portée, avec la mise en évidence du rang actuel et des rangs non atteints.</summary>
    public IReadOnlyList<StandingRowModel> StandingRows => StandingList
        .Select(standing => new StandingRowModel(
            standing.DisplayName,
            standing.Max,
            standing.Name == CurrentStanding?.Name,
            CurrentValue.HasValue && standing.Min > CurrentValue.Value))
        .ToList();

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>
/// Ligne de la liste des rangs d'une portée.
/// </summary>
/// <param name="Name">Nom du rang.</param>
/// <param name="Threshold">Seuil supérieur du rang.</param>
/// <param name="IsCurrent">Vrai pour le rang actuel du joueur.</param>
/// <param name="IsLocked">Vrai pour un rang que le joueur n'a pas encore atteint.</param>
public sealed record StandingRowModel(string Name, long Threshold, bool IsCurrent, bool IsLocked);
