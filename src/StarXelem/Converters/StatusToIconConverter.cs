using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Models;

namespace StarXelem.Converters;

/// <summary>
/// MultiValueConverter that returns a simple icon (string) based on the Status value.
/// Even though only one value is required, a multi-value converter is used per request.
/// </summary>
public class StatusToIconConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0)
            return "";

        var statusObj = values[0];
        if (statusObj is ComparisonDiffType status)
        {
            // Return a unicode icon. You can switch to MDL2 glyphs or images later if desired.
            return status switch
            {
                ComparisonDiffType.Equal => "✅",
                ComparisonDiffType.Gain => "⬆️",
                ComparisonDiffType.Loss => "⬇️",
                ComparisonDiffType.OnlySource => "📥", // present only in source
                ComparisonDiffType.OnlyTarget => "📤", // present only in target
                _ => "❓"
            };
        }

        // Try to coerce from string or int if bound from XAML as different type
        if (statusObj is string s && Enum.TryParse<ComparisonDiffType>(s, out var parsed))
        {
            return Convert(new object?[] { parsed }, targetType, parameter, culture);
        }

        return "❓";
    }
}
