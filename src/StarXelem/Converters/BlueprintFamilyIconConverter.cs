using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Models;

namespace StarXelem.Converters;

/// <summary>Convertit la clé d'une puce de famille (nom de <see cref="BlueprintFamily"/>) en icône.</summary>
public sealed class BlueprintFamilyIconConverter : IValueConverter
{
    public static BlueprintFamilyIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && Enum.TryParse<BlueprintFamily>(key, out var family) ? BlueprintFamilyInfo.GetIcon(family) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
