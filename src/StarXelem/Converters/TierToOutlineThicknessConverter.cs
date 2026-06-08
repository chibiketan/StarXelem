using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Convertit un tier (1-7) en un <see cref="Thickness"/> d'outline : retourne 1.5 px pour le tier 7, sinon 0.
/// </summary>
public class TierToOutlineThicknessConverter : IValueConverter
{
    private const double OutlineThickness = 1.5;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int tier)
            return new Thickness(0);

        return tier == 7 ? new Thickness(OutlineThickness) : new Thickness(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
