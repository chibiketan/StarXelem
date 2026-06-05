using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Convertit un tier (1-7) en le brush d'outline : retourne <c>RepTier7OutlineBrush</c> pour le tier 7, sinon <c>null</c>.
/// </summary>
public class TierToOutlineBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int tier)
            return null;

        if (tier != 7)
            return null;

        if (Application.Current is { } app && app.Resources.TryGetResource("RepTier7OutlineBrush", app.ActualThemeVariant, out var resource))
            return resource;

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
