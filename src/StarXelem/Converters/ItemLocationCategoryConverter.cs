using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

using StarXelem.Models;

namespace StarXelem.Converters;

/// <summary>
/// Convertit une catégorie d'emplacement en icône (paramètre <c>icon</c>), couleur (<c>foreground</c>)
/// ou fond de pastille (<c>background</c>).
/// </summary>
public class ItemLocationCategoryConverter : IValueConverter
{
    private const string PinIcon = "M12 21s7-6.1 7-11.5A7 7 0 0 0 5 9.5C5 14.9 12 21 12 21zM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z";
    private const string HangarIcon = "M3 11l9-8 9 8v10H3zM9 21v-7h6v7";
    private const string ShipIcon = "M12 2l3 8 6 5v3l-6-2-1 4h-4l-1-4-6 2v-3l6-5z";
    private const string WeaponIcon = "M3 10h15v5H3zM18 11h3v3h-3M6 15v5h5v-5";
    private const string ArmorIcon = "M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11z";
    private const string ContainerIcon = "M21 8l-9-5-9 5v8l9 5 9-5V8zM3 8l9 5 9-5M12 13v8";
    private const string WornIcon = "M20 21v-1a5 5 0 0 0-5-5H9a5 5 0 0 0-5 5v1M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    private const string OtherIcon = "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM9.5 9.5a2.5 2.5 0 1 1 4 2c-1 .7-1.5 1.2-1.5 2.5M12 17h.01";

    private static readonly Dictionary<ItemLocationCategory, Geometry> Icons = new()
    {
        [ItemLocationCategory.Place] = Geometry.Parse(PinIcon),
        [ItemLocationCategory.Hangar] = Geometry.Parse(HangarIcon),
        [ItemLocationCategory.Ship] = Geometry.Parse(ShipIcon),
        [ItemLocationCategory.Weapon] = Geometry.Parse(WeaponIcon),
        [ItemLocationCategory.Armor] = Geometry.Parse(ArmorIcon),
        [ItemLocationCategory.Container] = Geometry.Parse(ContainerIcon),
        [ItemLocationCategory.Worn] = Geometry.Parse(WornIcon),
        [ItemLocationCategory.Other] = Geometry.Parse(OtherIcon)
    };

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ItemLocationCategory category)
        {
            return null;
        }

        return (parameter as string) switch
        {
            "icon" => Icons[category],
            "foreground" => FindBrush(GetTone(category) + "Brush"),
            "background" => FindBrush(GetTone(category) + "SoftBrush"),
            _ => null
        };
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetTone(ItemLocationCategory category) => category switch
    {
        ItemLocationCategory.Place or ItemLocationCategory.Hangar => "Sx.Info",
        ItemLocationCategory.Ship => "Sx.Accent",
        ItemLocationCategory.Container => "Sx.Warning",
        ItemLocationCategory.Weapon or ItemLocationCategory.Armor or ItemLocationCategory.Worn => "Sx.Success",
        _ => "Sx.Neutral"
    };

    private static object? FindBrush(string key)
    {
        return Application.Current != null && Application.Current.TryGetResource(key, ThemeVariant.Dark, out var brush)
            ? brush
            : null;
    }
}
