using CommunityToolkit.Mvvm.ComponentModel;

using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Stock possédé d'un matériau de blueprint, avec la qualité retenue pour calculer les propriétés.
/// </summary>
public partial class MaterialStockViewModel : ObservableObject
{
    /// <summary>
    /// Prépare l'affichage du stock ; la qualité retenue par défaut est celle du meilleur groupe qui couvre seul le besoin.
    /// </summary>
    /// <param name="categoryName">Composant du blueprint (Frame, Barrel…).</param>
    /// <param name="material">Matériau demandé.</param>
    /// <param name="stock">Stock calculé.</param>
    public MaterialStockViewModel(string categoryName, BlueprintMaterialModel material, MaterialStock stock)
    {
        CategoryName = categoryName;
        Material = material;
        Stock = stock;
        TrackPoints = stock.Groups
            .Where(group => group.Quality != null)
            .Select(group => new QualityTrackPoint(
                group.Quality!.Value,
                group.Quantity,
                group.IsExcluded,
                $"{group.Quality} · {BlueprintStockCalculator.FormatBareQuantity(group.Quantity, material)}"))
            .ToList();
        _selectedQuality = (stock.SelfSufficientGroup ?? stock.BestGroup)?.Quality;
    }

    /// <summary>Levé quand la qualité retenue change : les propriétés du composant sont à recalculer.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Composant du blueprint.</summary>
    public string CategoryName { get; }

    /// <summary>Matériau demandé.</summary>
    public BlueprintMaterialModel Material { get; }

    /// <summary>Stock calculé.</summary>
    public MaterialStock Stock { get; }

    /// <summary>Nom du matériau.</summary>
    public string Name => Material.Name;

    /// <summary>Quantité requise : « 6 cSCU », « 4 unités ».</summary>
    public string RequiredLabel => BlueprintStockCalculator.FormatQuantity(Material.RequiredQuantity, Material.IsMeasuredInScu);

    /// <summary>Quantité utilisable, sans unité.</summary>
    public string UsableLabel => BlueprintStockCalculator.FormatBareQuantity(Stock.UsableQuantity, Material);

    /// <summary>Utilisable sur requis : « 39 / 6 cSCU ».</summary>
    public string UsableOverRequiredLabel => $"{UsableLabel} / {RequiredLabel}";

    /// <summary>Qualité moyenne de tout le stock utilisable : « Q 709 ».</summary>
    public string AverageQualityLabel => Stock.AverageQuality is { } quality ? $"Q {quality}" : "—";

    /// <summary>Vrai si au moins un lot est utilisable : affiche la qualité max.</summary>
    public bool HasBestGroup => Stock.BestGroup != null;

    /// <summary>Quantité du meilleur lot, sans unité (« 11 » dans « 11 / 886 »).</summary>
    public string BestQuantityLabel => Stock.BestGroup == null ? "—" : BlueprintStockCalculator.FormatBareQuantity(Stock.BestGroup.Quantity, Material);

    /// <summary>Qualité du meilleur lot (« 886 » dans « 11 / 886 »).</summary>
    public string BestQualityLabel => Stock.BestGroup?.Quality?.ToString() ?? "?";

    /// <summary>Vrai si le meilleur lot couvre seul le besoin : sa quantité s'affiche en vert.</summary>
    public bool BestCoversNeed => Stock.BestGroup != null && Stock.BestGroup.Quantity >= Stock.RequiredQuantity;

    /// <summary>Infobulle de la qualité max.</summary>
    public string BestToolTip => Stock.BestGroup == null
        ? "Aucun lot utilisable"
        : $"{BlueprintStockCalculator.FormatQuantity(Stock.BestGroup.Quantity, Material.IsMeasuredInScu)} à Q {BestQualityLabel}"
          + (BestCoversNeed ? " · suffit seul" : " · insuffisant seul");

    /// <summary>Statut en clair.</summary>
    public string StatusLabel => Stock.Status switch
    {
        MaterialStockStatus.Enough => "Suffisant",
        MaterialStockStatus.NeedsMix => "Avec mélange",
        MaterialStockStatus.Insufficient => "Insuffisant",
        _ => "Absent"
    };

    /// <summary>Ton du badge de statut (classe de style <c>sx-badge</c>).</summary>
    public string StatusTone => Stock.Status switch
    {
        MaterialStockStatus.Enough => "success",
        MaterialStockStatus.NeedsMix => "info",
        MaterialStockStatus.Insufficient => "warning",
        _ => "danger"
    };

    /// <summary>Badge vert : le meilleur lot suffit seul.</summary>
    public bool IsEnough => Stock.Status == MaterialStockStatus.Enough;

    /// <summary>Badge bleu : le besoin est couvert en mélangeant des lots.</summary>
    public bool IsNeedsMix => Stock.Status == MaterialStockStatus.NeedsMix;

    /// <summary>Badge orange : une partie seulement du besoin est couverte.</summary>
    public bool IsInsufficient => Stock.Status == MaterialStockStatus.Insufficient;

    /// <summary>Badge rouge : aucun lot utilisable.</summary>
    public bool IsMissing => Stock.Status == MaterialStockStatus.Missing;

    /// <summary>Qualité minimale pour la piste (0 si aucune).</summary>
    public int MinQualityValue => Material.MinQuality ?? 0;

    /// <summary>Vrai si le blueprint exige une qualité minimale.</summary>
    public bool HasMinQuality => Material.MinQuality is > 0;

    /// <summary>Qualité minimale : « min. Q 500 ».</summary>
    public string MinQualityLabel => $"min. Q {Material.MinQuality}";

    /// <summary>Points de la piste de qualité (groupes de qualité connue).</summary>
    public IReadOnlyList<QualityTrackPoint> TrackPoints { get; }

    /// <summary>Vrai si au moins un groupe a une qualité connue : affiche la piste.</summary>
    public bool HasTrackPoints => TrackPoints.Count > 0;

    /// <summary>Vrai si aucun lot ne correspond.</summary>
    public bool HasNoLot => Stock.Groups.Count == 0;

    /// <summary>Lots dont la qualité n'est pas lue (objets à l'unité sans état de conteneur).</summary>
    public string? UnknownQualityLabel
    {
        get
        {
            var unknown = Stock.Groups.Where(group => group.Quality == null).Sum(group => group.Quantity);
            return unknown > 0 ? $"{BlueprintStockCalculator.FormatQuantity(unknown, Material.IsMeasuredInScu)} sans qualité connue" : null;
        }
    }

    /// <summary>Qualité retenue pour les propriétés du composant (groupe cliqué sur la piste).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGroup), nameof(SelectedPlaces), nameof(HasSelectedPlaces), nameof(SelectedQualityLabel))]
    private int? _selectedQuality;

    /// <summary>Groupe correspondant à la qualité retenue.</summary>
    public QualityGroup? SelectedGroup => Stock.UsableGroups.FirstOrDefault(group => group.Quality == SelectedQuality)
                                          ?? (SelectedQuality == null ? null : Stock.UsableGroups.FirstOrDefault());

    /// <summary>Répartition par emplacement du groupe retenu.</summary>
    public IReadOnlyList<MaterialPlace> SelectedPlaces => SelectedGroup?.Places ?? [];

    /// <summary>Vrai si le groupe retenu a des emplacements à afficher.</summary>
    public bool HasSelectedPlaces => SelectedPlaces.Count > 0;

    /// <summary>Qualité retenue : « Q 745 ».</summary>
    public string SelectedQualityLabel => SelectedGroup?.Quality is { } quality ? $"Q {quality}" : "—";

    // La piste ne rend sélectionnables que les groupes utilisables : SelectedGroup retombe sinon sur le premier d'entre eux.
    partial void OnSelectedQualityChanged(int? value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
