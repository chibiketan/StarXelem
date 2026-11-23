namespace StarXelem.Models;

/// <summary>
/// Stock possédé d'un matériau de blueprint : groupes par qualité et indicateurs du bilan.
/// </summary>
public sealed class MaterialStock
{
    /// <summary>
    /// Construit le stock à partir des groupes, triés de la meilleure qualité à la moins bonne.
    /// </summary>
    /// <param name="groups">Groupes par qualité, exclus compris.</param>
    /// <param name="requiredQuantity">Quantité demandée par le blueprint.</param>
    public MaterialStock(IReadOnlyList<QualityGroup> groups, double requiredQuantity)
    {
        Groups = groups;
        RequiredQuantity = requiredQuantity;
        UsableGroups = groups.Where(group => !group.IsExcluded).ToList();
        UsableQuantity = UsableGroups.Sum(group => group.Quantity);

        var rated = UsableGroups.Where(group => group.Quality != null).ToList();
        var ratedQuantity = rated.Sum(group => group.Quantity);
        AverageQuality = ratedQuantity > 0 ? (int)Math.Round(rated.Sum(group => group.Quality!.Value * group.Quantity) / ratedQuantity) : null;

        BestGroup = rated.FirstOrDefault() ?? UsableGroups.FirstOrDefault();
        SelfSufficientGroup = UsableGroups.FirstOrDefault(group => group.Quantity >= requiredQuantity);
        CraftCount = requiredQuantity > 0 ? (int)Math.Floor(UsableQuantity / requiredQuantity + 1e-9) : 0;
        Status = SelfSufficientGroup != null ? MaterialStockStatus.Enough
            : UsableQuantity >= requiredQuantity ? MaterialStockStatus.NeedsMix
            : UsableQuantity > 0 ? MaterialStockStatus.Insufficient
            : MaterialStockStatus.Missing;
    }

    /// <summary>Tous les groupes, exclus compris, de la meilleure qualité à la moins bonne.</summary>
    public IReadOnlyList<QualityGroup> Groups { get; }

    /// <summary>Groupes dont la qualité atteint le minimum exigé.</summary>
    public IReadOnlyList<QualityGroup> UsableGroups { get; }

    /// <summary>Quantité demandée par le blueprint.</summary>
    public double RequiredQuantity { get; }

    /// <summary>Quantité totale des groupes utilisables.</summary>
    public double UsableQuantity { get; }

    /// <summary>Qualité moyenne des groupes utilisables, pondérée par la quantité.</summary>
    public int? AverageQuality { get; }

    /// <summary>Groupe de meilleure qualité parmi les utilisables.</summary>
    public QualityGroup? BestGroup { get; }

    /// <summary>Meilleur groupe qui couvre seul le besoin, sans mélange.</summary>
    public QualityGroup? SelfSufficientGroup { get; }

    /// <summary>Nombre de fabrications que permet ce matériau, en mélangeant les lots.</summary>
    public int CraftCount { get; }

    /// <summary>Disponibilité du matériau.</summary>
    public MaterialStockStatus Status { get; }

    /// <summary>Vrai si le besoin est couvert, avec ou sans mélange.</summary>
    public bool IsCovered => Status is MaterialStockStatus.Enough or MaterialStockStatus.NeedsMix;
}
