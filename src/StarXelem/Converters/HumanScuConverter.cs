using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

public sealed class HumanScuConverter : IValueConverter
{
    // Converts a value expressed in SCU to the most human-readable unit:
    // - >= 1 SCU -> display in SCU
    // - >= 0.01 SCU -> display in cSCU (value * 100)
    // - < 0.01 SCU -> display in µSCU (value * 1,000,000), not lower than µSCU
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        try
        {
            decimal scu;
            switch (value)
            {
                case uint u:
                    scu = u;
                    break;
                case ulong ul:
                    scu = (decimal)ul;
                    break;
                case int i:
                    scu = i;
                    break;
                case long l:
                    scu = l;
                    break;
                case decimal d:
                    scu = d;
                    break;
                case double db:
                    scu = (decimal)db;
                    break;
                case float f:
                    scu = (decimal)f;
                    break;
                case string s when decimal.TryParse(s, NumberStyles.Any, culture, out var parsedDec):
                    scu = parsedDec;
                    break;
                default:
                    return value?.ToString();
            }

            decimal absScu = Math.Abs(scu);

            if (absScu >= 1m || scu == 0)
            {
                return FormatNumber(scu, culture) + " SCU";
            }
            
            if (absScu >= 0.01m)
            {
                var cscu = scu * 100m;
                return FormatNumber(cscu, culture) + " cSCU";
            }

            // microSCU (on ne va pas plus bas)
            var micro = scu * 1_000_000m;
            return FormatNumber(micro, culture) + " µSCU";
        }
        catch
        {
            return value?.ToString();
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return null;

        if (value is string s)
        {
            s = s.Trim();
            if (s.Length == 0)
                return null;

            var numberPart = s;
            var factor = 1m; // default SCU

            if (s.EndsWith("µSCU", StringComparison.OrdinalIgnoreCase) || s.EndsWith("μSCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1m / 1_000_000m;
                numberPart = s[..^4].Trim();
            }
            else if (s.EndsWith("cSCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1m / 100m;
                numberPart = s[..^4].Trim();
            }
            else if (s.EndsWith("SCU", StringComparison.OrdinalIgnoreCase))
            {
                factor = 1m;
                numberPart = s[..^3].Trim();
            }

            if (decimal.TryParse(numberPart, NumberStyles.Any, culture, out var num))
            {
                var scu = num * factor;
                if (targetType == typeof(uint) || targetType == typeof(uint?))
                {
                    if (scu < 0) scu = 0;
                    if (scu > uint.MaxValue) scu = uint.MaxValue;
                    return (uint)decimal.Truncate(scu);
                }
                if (targetType == typeof(double) || targetType == typeof(double?))
                {
                    return (double)scu;
                }
                return scu;
            }
        }

        return null;
    }

    private static string FormatNumber(decimal number, CultureInfo culture)
    {
        // up to 3 decimals, trim trailing zeros
        return number.ToString("0.###", culture);
    }
}
