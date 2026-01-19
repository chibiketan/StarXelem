using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Models;

namespace StarXelem.Converters;

/// <summary>
/// MultiValueConverter that returns a simple icon (string) based on the Status value.
/// Even though only one value is required, a multi-value converter is used per request.
/// </summary>
public class ComponentGradeToBackgroundColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is P4kShipComponentModel grade)
        {
            switch (grade.Grade)
            {
                case "A":
                    return "#33CC33"; // Vert
                case "B":
                    return "#99CC33"; // Vert jaunatre
                case "C":
                    return "#CC9933"; // Orange
                case "D":
                    return "#CC0000"; // Rouge
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
