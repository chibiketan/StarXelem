using CommunityToolkit.Mvvm.ComponentModel;

namespace StarXelem.ViewModels;

/// <summary>
/// Propriété d'un composant évaluée à la qualité retenue pour son matériau.
/// </summary>
public partial class CraftStatViewModel : ObservableObject
{
    /// <summary>
    /// Prépare l'affichage d'une propriété.
    /// </summary>
    /// <param name="stat">Modificateur défini par le blueprint.</param>
    public CraftStatViewModel(BlueprintStatModelBase stat)
    {
        Stat = stat;
    }

    /// <summary>Modificateur défini par le blueprint.</summary>
    public BlueprintStatModelBase Stat { get; }

    /// <summary>Nom de la propriété.</summary>
    public string Name => Stat.Name;

    /// <summary>Plage du modificateur : « 120% – 80% », ou valeurs par plage pour un additif.</summary>
    public string RangeLabel => Stat switch
    {
        BlueprintStatLinearModel linear => linear.RangeDisplay,
        BlueprintStatAdditiveModel additive => string.Join(" · ", additive.Bands.Select(band => band.FormattedValue)),
        _ => string.Empty
    };

    /// <summary>Valeur à la qualité retenue, ou null si aucune qualité n'est connue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueLabel), nameof(Position), nameof(HasValue))]
    private double? _value;

    /// <summary>Valeur formatée : « 90,2% », « +1 », ou un tiret.</summary>
    public string ValueLabel => Value is { } value ? Stat.FormatValue(value) : "—";

    /// <summary>Vrai si une valeur est calculée.</summary>
    public bool HasValue => Value != null;

    /// <summary>Vrai pour un multiplicateur : affiche la jauge entre ses bornes.</summary>
    public bool ShowGauge => Stat is BlueprintStatLinearModel;

    /// <summary>Position de la valeur entre la borne à Q0 (0) et la borne à Q1000 (100), pour la jauge.</summary>
    public double Position
    {
        get
        {
            if (Value is not { } value || Stat is not BlueprintStatLinearModel linear || Math.Abs(linear.Max - linear.Min) < 1e-9)
            {
                return 0;
            }

            return Math.Clamp((value - linear.Min) / (linear.Max - linear.Min), 0, 1) * 100;
        }
    }

    /// <summary>Recalcule la valeur pour une qualité.</summary>
    /// <param name="quality">Qualité du matériau, ou null si inconnue.</param>
    public void Update(int? quality) => Value = quality is { } q ? Stat.ValueAt(q) : null;
}
