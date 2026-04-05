using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Converts a decimal ratio (e.g. 0.8) to a percentage string (e.g. "80%").
/// An optional ConverterParameter (int) sets the total width, right-aligned with spaces.
/// Examples:
///   0.8              → "80%"
///   0.8, width=5     → "  80%"
///   0.1234, width=7  → "  12.3%"
/// </summary>
public sealed class PercentageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        double ratio;
        switch (value)
        {
            case double d:   ratio = d;         break;
            case float f:    ratio = f;          break;
            case decimal dec: ratio = (double)dec; break;
            case int i:      ratio = i;          break;
            case long l:     ratio = l;          break;
            case string s when double.TryParse(s, NumberStyles.Any, culture, out var parsed):
                ratio = parsed;
                break;
            default:
                return value?.ToString();
        }

        var percent = ratio * 100.0;

        // Format: drop decimals when the value is a whole number, otherwise keep one decimal
        var formatted = percent % 1.0 == 0.0
            ? $"{percent:0}%"
            : $"{percent:0.#}%";

        if (parameter is not null)
        {
            var widthStr = parameter?.ToString();
            if (int.TryParse(widthStr, out var width) && width > 0)
                return formatted.PadLeft(width);
        }

        return formatted;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s)
            return null;

        s = s.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(s, NumberStyles.Any, culture, out var percent))
            return null;

        var ratio = percent / 100.0;

        if (targetType == typeof(float) || targetType == typeof(float?))   return (float)ratio;
        if (targetType == typeof(decimal) || targetType == typeof(decimal?)) return (decimal)ratio;
        return ratio;
    }
}