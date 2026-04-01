using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using StarXelem.Services;

namespace StarXelem.Converters;

public sealed class P4kStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is P4kService.P4kFileLoadState state)
        {
            return state switch
            {
                P4kService.P4kFileLoadState.NotLoaded => Brushes.Gray,
                P4kService.P4kFileLoadState.Loading => Brushes.Orange,
                P4kService.P4kFileLoadState.Loaded => Brushes.Blue,
                P4kService.P4kFileLoadState.CacheLoading => Brushes.Orange,
                P4kService.P4kFileLoadState.CacheLoaded => Brushes.Green,
                P4kService.P4kFileLoadState.Cancelled => Brushes.Gray,
                P4kService.P4kFileLoadState.Error => Brushes.Red,
                _ => Brushes.Gray
            };
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
