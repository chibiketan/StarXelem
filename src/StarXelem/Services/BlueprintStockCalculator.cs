using System.Globalization;

using StarXelem.Converters;
using StarXelem.Models;
using StarXelem.ViewModels;

namespace StarXelem.Services;

/// <summary>
/// Compare les matériaux d'un blueprint aux objets possédés. Sans état ni accès à l'UI : utilisable hors du thread d'UI.
/// </summary>
public static class BlueprintStockCalculator
{
    private static readonly HumanScuConverter ScuConverter = new();

    /// <summary>
    /// Stock d'un matériau : lots correspondants regroupés par qualité exacte, avec leur répartition par emplacement.
    /// </summary>
    /// <param name="inventory">Objets possédés.</param>
    /// <param name="material">Matériau demandé par le blueprint.</param>
    public static MaterialStock ComputeStock(OwnedMaterialInventory inventory, BlueprintMaterialModel material)
    {
        var lots = FindLots(inventory, material)
            .Select(lot => (Lot: lot, Quantity: QuantityOf(lot, material)))
            .Where(entry => entry.Quantity > 0)
            .ToList();

        var groups = lots
            .GroupBy(entry => (int?)entry.Lot.Quality)
            .Select(group => new QualityGroup(
                group.Key,
                group.Sum(entry => entry.Quantity),
                IsExcluded(group.Key, material.MinQuality),
                BuildPlaces(group, material)))
            .OrderByDescending(group => group.Quality ?? -1)
            .ToList();

        return new MaterialStock(groups, material.RequiredQuantity);
    }

    /// <summary>
    /// Possibilité de fabriquer un blueprint : prêt si tous ses matériaux sont couverts, partiel si une partie seulement.
    /// </summary>
    /// <param name="inventory">Objets possédés, ou null s'ils ne sont pas chargés.</param>
    /// <param name="blueprint">Blueprint à évaluer.</param>
    public static BlueprintCraftStatus ComputeCraftStatus(OwnedMaterialInventory? inventory, BlueprintViewModel blueprint)
    {
        var materials = blueprint.CategoryList.SelectMany(category => category.MaterialList).ToList();
        if (inventory == null || materials.Count == 0)
        {
            return BlueprintCraftStatus.Unknown;
        }

        var stocks = materials.Select(material => ComputeStock(inventory, material)).ToList();
        if (stocks.All(stock => stock.IsCovered))
        {
            return BlueprintCraftStatus.Ready;
        }

        return stocks.Any(stock => stock.UsableQuantity > 0) ? BlueprintCraftStatus.Partial : BlueprintCraftStatus.None;
    }

    /// <summary>Quantité formatée dans l'unité du matériau : « 6 cSCU », « 4 unités ».</summary>
    /// <param name="quantity">Quantité en SCU ou en nombre d'éléments.</param>
    /// <param name="isMeasuredInScu">Vrai pour une ressource mesurée en SCU.</param>
    public static string FormatQuantity(double quantity, bool isMeasuredInScu)
    {
        if (isMeasuredInScu)
        {
            return ScuConverter.Convert(quantity, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
        }

        var count = (int)Math.Round(quantity);
        return count > 1 ? $"{count} unités" : $"{count} unité";
    }

    /// <summary>Quantité sans unité, dans l'unité de la quantité requise (« 39 » pour 39 cSCU).</summary>
    /// <param name="quantity">Quantité à formater.</param>
    /// <param name="material">Matériau, qui fixe l'unité.</param>
    public static string FormatBareQuantity(double quantity, BlueprintMaterialModel material)
    {
        if (!material.IsMeasuredInScu)
        {
            return ((int)Math.Round(quantity)).ToString(CultureInfo.CurrentCulture);
        }

        // Mêmes paliers que HumanScuConverter, choisis d'après la quantité requise : « 0,8 / 2 cSCU », pas « 8000 / 2 ».
        var required = Math.Round(Math.Abs(material.RequiredQuantity), 6);
        var factor = required >= 1 || required == 0 ? 1d : required >= 0.01 ? 100d : 1_000_000d;
        return ((decimal)(quantity * factor)).ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static IEnumerable<OwnedMaterialLot> FindLots(OwnedMaterialInventory inventory, BlueprintMaterialModel material) => material switch
    {
        BlueprintResourceModel { ResourceRef: not null } resource => inventory.FindResourceLots(resource.ResourceRef),
        BlueprintItemModel { ItemClassRef: not null } item => inventory.FindItemLots(item.ItemClassRef),
        _ => []
    };

    private static double QuantityOf(OwnedMaterialLot lot, BlueprintMaterialModel material) =>
        material.IsMeasuredInScu ? lot.ResourceScu : lot.Count;

    // Une qualité inconnue n'est écartée que si le blueprint exige un minimum.
    private static bool IsExcluded(int? quality, int? minQuality) =>
        minQuality is > 0 && (quality == null || quality < minQuality);

    private static List<MaterialPlace> BuildPlaces(IEnumerable<(OwnedMaterialLot Lot, double Quantity)> lots, BlueprintMaterialModel material)
    {
        return lots
            .GroupBy(entry => entry.Lot.Location == null ? string.Empty : $"{entry.Lot.Location.Category}|{entry.Lot.Location.Name}|{entry.Lot.Location.Destination}")
            .Select(group =>
            {
                var quantity = group.Sum(entry => entry.Quantity);
                return new MaterialPlace(group.First().Lot.Location, quantity, FormatQuantity(quantity, material.IsMeasuredInScu));
            })
            .OrderByDescending(place => place.Quantity)
            .ToList();
    }
}
