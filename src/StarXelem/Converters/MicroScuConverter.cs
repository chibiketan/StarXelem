using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

public sealed class MicroScuConverter : IValueConverter
{
    // Converts a value expressed in microSCU (µSCU) to a human-readable string:
    // - < 1_000 -> display in µSCU
    // - >= 1_000 and < 1_000_000 -> display in cSCU (value / 1_000)
    // - >= 1_000_000 -> display in SCU (value / 1_000_000)
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        try
        {
            // Accept uint, ulong, int, long, decimal, or string
            decimal micro;
            switch (value)
            {
                case uint u:
                    micro = u;
                    break;
                case ulong ul:
                    micro = (decimal)ul;
                    break;
                case int i:
                    micro = i;
                    break;
                case long l:
                    micro = l;
                    break;
                case decimal d:
                    micro = d;
                    break;
                case string s when decimal.TryParse(s, NumberStyles.Any, culture, out var parsedDec):
                    micro = parsedDec;
                    break;
                default:
                    return value?.ToString();
            }

            const decimal Thousand = 1000m;
            const decimal Million = 1_000_000m;

            if (micro >= Million)
            {
                var scu = micro / Million;
                return FormatNumber(scu, culture) + " SCU";
            }
            if (micro >= Thousand)
            {
                var cscu = micro / Thousand;
                return FormatNumber(cscu, culture) + " cSCU";
            }

            // microSCU
            return FormatNumber(micro, culture) + " µSCU";
        }
        catch
        {
            return value?.ToString();
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Optional: parse strings like "1.23 SCU", "456 cSCU", "789 µSCU" back to microSCU
        if (value is null)
            return null;

        if (value is string s)
        {
            s = s.Trim();
            if (s.Length == 0)
                return null;

            var numberPart = s;
            var factor = 1m; // default µSCU

            if (s.EndsWith("SCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1_000_000m;
                numberPart = s[..^3].Trim();
            }
            else if (s.EndsWith("cSCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1_000m;
                numberPart = s[..^4].Trim();
            }
            else if (s.EndsWith("µSCU", StringComparison.OrdinalIgnoreCase) || s.EndsWith("μSCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1m;
                numberPart = s[..^4].Trim();
            }

            if (decimal.TryParse(numberPart, NumberStyles.Any, culture, out var num))
            {
                var micro = num * factor;
                if (targetType == typeof(uint) || targetType == typeof(uint?))
                {
                    if (micro < 0) micro = 0;
                    if (micro > uint.MaxValue) micro = uint.MaxValue;
                    return (uint)decimal.Truncate(micro);
                }
                return micro;
            }
        }

        return null;
    }

    private static string FormatNumber(decimal number, CultureInfo culture)
    {
        // up to 3 decimals, trim trailing zeros
        var s = number.ToString("0.###", culture);
        return s;
    }
}
