using CommunityToolkit.Mvvm.ComponentModel;

namespace StarXelem.ViewModels;

/// <summary>
/// Composant d'un blueprint (Frame, Barrel…) : son matériau, son stock et ses propriétés à la qualité retenue.
/// </summary>
public partial class CraftCategoryViewModel : ObservableObject
{
    /// <summary>
    /// Relie les propriétés du composant à la qualité retenue de son matériau.
    /// </summary>
    /// <param name="name">Nom du composant.</param>
    /// <param name="materials">Matériaux du composant (un seul en pratique).</param>
    /// <param name="stats">Propriétés modifiées par la qualité.</param>
    public CraftCategoryViewModel(string name, IReadOnlyList<MaterialStockViewModel> materials, IReadOnlyList<BlueprintStatModelBase> stats)
    {
        Name = name;
        Materials = materials;
        Stats = stats.Select(stat => new CraftStatViewModel(stat)).ToList();
        foreach (var material in materials)
        {
            material.SelectionChanged += (_, _) => Refresh();
        }

        Refresh();
    }

    /// <summary>Levé quand la qualité du composant change : les propriétés finales sont à recalculer.</summary>
    public event EventHandler? QualityChanged;

    /// <summary>Nom du composant.</summary>
    public string Name { get; }

    /// <summary>Matériaux du composant.</summary>
    public IReadOnlyList<MaterialStockViewModel> Materials { get; }

    /// <summary>Propriétés modifiées par la qualité.</summary>
    public IReadOnlyList<CraftStatViewModel> Stats { get; }

    /// <summary>Vrai si le composant a des propriétés à afficher.</summary>
    public bool HasStats => Stats.Count > 0;

    /// <summary>
    /// Qualité du composant. Un composant n'a qu'un matériau dans les données du jeu ; s'il en avait plusieurs,
    /// la moyenne de leurs qualités retenues s'appliquerait.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityLabel))]
    private int? _quality;

    /// <summary>Qualité du composant : « Q 745 », ou un tiret.</summary>
    public string QualityLabel => Quality is { } quality ? $"Q {quality}" : "—";

    private void Refresh()
    {
        var qualities = Materials.Select(material => material.SelectedGroup?.Quality).ToList();
        Quality = qualities.Count > 0 && qualities.All(quality => quality != null)
            ? (int)Math.Round(qualities.Average(quality => quality!.Value))
            : null;

        foreach (var stat in Stats)
        {
            stat.Update(Quality);
        }

        QualityChanged?.Invoke(this, EventArgs.Empty);
    }
}
