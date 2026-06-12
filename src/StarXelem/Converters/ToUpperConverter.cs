using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Transforme une chaine en majuscules via CultureInfo current.
/// </summary>
public class ToUpperConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str)
            return value;
        return str.ToUpper(culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
