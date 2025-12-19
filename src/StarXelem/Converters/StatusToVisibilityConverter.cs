using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Models;

namespace StarXelem.Converters;

/// <summary>
/// Converts a <see cref="ComparisonDiffType"/> value to a boolean visibility flag
/// by comparing it to the enum name provided via ConverterParameter.
/// Example usage in XAML:
///   IsVisible="{Binding Status, Converter={StaticResource StatusToVisibilityConverter}, ConverterParameter=Gain}"
/// </summary>
public class StatusToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ComparisonDiffType status)
        {
            if (parameter is string enumName && Enum.TryParse<ComparisonDiffType>(enumName, out var expected))
                return status == expected;

            // If parameter is missing, default to false
            return false;
        }

        // Try to coerce from string if bound differently
        if (value is string s && Enum.TryParse<ComparisonDiffType>(s, out var parsed))
        {
            if (parameter is string enumName2 && Enum.TryParse<ComparisonDiffType>(enumName2, out var expected2))
                return parsed == expected2;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
