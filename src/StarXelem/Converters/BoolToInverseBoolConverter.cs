using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Inverse une valeur booléenne (true → false, false → true).
/// </summary>
public class BoolToInverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
