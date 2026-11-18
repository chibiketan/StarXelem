using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Nombre de colonnes qui tiennent dans une largeur donnée, chaque colonne mesurant au moins la largeur passée en
/// paramètre (320 par défaut), espacement de 12 compris. Équivalent de <c>repeat(auto-fill, minmax(320px, 1fr))</c>.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    private const double Spacing = 12;

    public static WidthToColumnsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var minWidth = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 320;
        return value is double width && width > 0 ? Math.Max(1, (int)((width + Spacing) / (minWidth + Spacing))) : 1;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
