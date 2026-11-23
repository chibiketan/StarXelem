using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Matériaux possédés confrontés à un blueprint : bilan, stock par composant et propriétés finales à la qualité retenue.
/// </summary>
public partial class BlueprintCraftViewModel : ObservableObject
{
    /// <summary>
    /// Calcule le stock de chaque matériau du blueprint dans l'inventaire.
    /// </summary>
    /// <param name="blueprint">Blueprint sélectionné.</param>
    /// <param name="inventory">Objets possédés.</param>
    public BlueprintCraftViewModel(BlueprintViewModel blueprint, OwnedMaterialInventory inventory)
    {
        Inventory = inventory;
        Categories = blueprint.CategoryList
            .Where(category => category.MaterialList.Count > 0)
            .Select(category => new CraftCategoryViewModel(
                category.Name,
                category.MaterialList
                    .Select(material => new MaterialStockViewModel(category.Name, material, BlueprintStockCalculator.ComputeStock(inventory, material)))
                    .ToList(),
                category.StatModifierList))
            .ToList();
        Materials = Categories.SelectMany(category => category.Materials).ToList();

        foreach (var category in Categories)
        {
            category.QualityChanged += (_, _) => RefreshFinalProperties();
        }

        RefreshFinalProperties();
    }

    /// <summary>Objets possédés.</summary>
    public OwnedMaterialInventory Inventory { get; }

    /// <summary>Composants du blueprint qui demandent un matériau.</summary>
    public IReadOnlyList<CraftCategoryViewModel> Categories { get; }

    /// <summary>Tous les matériaux, une ligne par matériau dans le tableau du bilan.</summary>
    public IReadOnlyList<MaterialStockViewModel> Materials { get; }

    /// <summary>Propriétés finales de l'objet fabriqué, modificateurs des composants sommés.</summary>
    public ObservableCollection<CraftFinalPropertyViewModel> FinalProperties { get; } = new();

    /// <summary>Vrai si au moins une propriété finale est calculable.</summary>
    public bool HasFinalProperties => FinalProperties.Count > 0;

    /// <summary>Fraîcheur du relevé : « Objets relevés aujourd'hui à 14:32 · 1 284 objets ».</summary>
    public string FreshnessLabel
    {
        get
        {
            var loadedAt = Inventory.LoadedAt;
            var day = loadedAt.Date == DateTime.Today ? "aujourd'hui" : $"le {loadedAt.ToString("d", CultureInfo.CurrentCulture)}";
            var label = $"Objets relevés {day} à {loadedAt:HH:mm} · {Inventory.ItemCount.ToString("N0", CultureInfo.CurrentCulture)} objets";
            return Inventory.AreLocationsResolved ? label : label + " · emplacements en cours de résolution";
        }
    }

    /// <summary>Nombre de fabrications possibles, limité par le matériau le moins abondant.</summary>
    public int CraftCount => Materials.Count == 0 ? 0 : Materials.Min(material => material.Stock.CraftCount);

    /// <summary>Nombre de fabrications : « ×3 ».</summary>
    public string CraftCountLabel => $"×{CraftCount}";

    /// <summary>Matériau qui limite le nombre de fabrications : « limité par Titanium ».</summary>
    public string LimitingMaterialLabel => Materials.Count == 0
        ? string.Empty
        : $"limité par {Materials.MinBy(material => material.Stock.CraftCount)!.Name}";

    /// <summary>Nombre de matériaux dont le besoin est couvert.</summary>
    public int CoveredCount => Materials.Count(material => material.Stock.IsCovered);

    /// <summary>Matériaux réunis : « 3 / 4 ».</summary>
    public string CoveredLabel => $"{CoveredCount} / {Materials.Count}";

    /// <summary>Nombre d'emplacements distincts où se trouvent les lots utilisables.</summary>
    public int PlaceCount => Materials
        .SelectMany(material => material.Stock.UsableGroups)
        .SelectMany(group => group.Places)
        .Select(place => place.Name)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .Count();

    /// <summary>Nombre de propriétés portées par plusieurs composants.</summary>
    public int CumulativeCount => FinalProperties.Count(property => property.IsCumulative);

    /// <summary>Badge d'en-tête : « Fabricable », « Fabricable en mélangeant », « Manque 2 matériaux ».</summary>
    public string StatusLabel
    {
        get
        {
            var missing = Materials.Count - CoveredCount;
            if (missing > 0)
            {
                return missing > 1 ? $"Manque {missing} matériaux" : "Manque 1 matériau";
            }

            return Materials.All(material => material.Stock.Status == MaterialStockStatus.Enough) ? "Fabricable" : "Fabricable en mélangeant";
        }
    }

    /// <summary>Ton du badge d'en-tête (classe de style <c>sx-badge</c>).</summary>
    public string StatusTone => CoveredCount < Materials.Count
        ? CoveredCount == 0 ? "danger" : "warning"
        : Materials.All(material => material.Stock.Status == MaterialStockStatus.Enough) ? "success" : "info";

    /// <summary>Badge vert : chaque matériau a un lot qui suffit seul.</summary>
    public bool IsToneSuccess => StatusTone == "success";

    /// <summary>Badge bleu : fabricable en mélangeant des lots.</summary>
    public bool IsToneInfo => StatusTone == "info";

    /// <summary>Badge orange : une partie des matériaux manque.</summary>
    public bool IsToneWarning => StatusTone == "warning";

    /// <summary>Badge rouge : aucun matériau n'est réuni.</summary>
    public bool IsToneDanger => StatusTone == "danger";

    private void RefreshFinalProperties()
    {
        // Une même propriété portée par plusieurs composants : les écarts au neutre s'additionnent (102 % et 104 % donnent 106 %).
        var properties = Categories
            .SelectMany(category => category.Stats.Select(stat => (Category: category, Stat: stat)))
            .GroupBy(entry => (entry.Stat.Name, entry.Stat.Stat.IsAdditive))
            .Select(BuildFinalProperty)
            .ToList();

        FinalProperties.Clear();
        foreach (var property in properties)
        {
            FinalProperties.Add(property);
        }

        OnPropertyChanged(nameof(HasFinalProperties));
        OnPropertyChanged(nameof(CumulativeCount));
    }

    private static CraftFinalPropertyViewModel BuildFinalProperty(IGrouping<(string Name, bool IsAdditive), (CraftCategoryViewModel Category, CraftStatViewModel Stat)> group)
    {
        var entries = group.ToList();
        var model = entries[0].Stat.Stat;
        var isComplete = entries.All(entry => entry.Stat.Value != null);
        var contributions = string.Join(" · ", entries.Select(entry =>
            $"{entry.Category.Name} {(entry.Stat.Value is { } value ? model.FormatDelta(value) : "—")}"));

        string result;
        if (!isComplete)
        {
            result = "incomplet";
        }
        else if (group.Key.IsAdditive)
        {
            result = model.FormatValue(entries.Sum(entry => entry.Stat.Value!.Value));
        }
        else
        {
            result = model.FormatValue(1 + entries.Sum(entry => entry.Stat.Value!.Value - 1));
        }

        return new CraftFinalPropertyViewModel(group.Key.Name, contributions, result, entries.Count, isComplete);
    }
}
