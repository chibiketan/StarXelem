using System;
using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Convertit un tier (1-7) en le brush <c>RepTier{N}FillBrush</c> depuis les ressources de l'application.
/// </summary>
public class TierToFillBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int tier)
            return null;

        var clampedTier = Math.Max(1, Math.Min(7, tier));

        var resourceName = $"RepTier{clampedTier}FillBrush";

        if (Application.Current is { } app && app.Resources.TryGetResource(resourceName, app.ActualThemeVariant, out var resource))
            return resource;

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
