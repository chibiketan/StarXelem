using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Services;

namespace StarXelem.Converters;

public class GrpcConnectionStatusToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            GrpcConnectionStatus.Disconnected => "Jeu non détecté",
            GrpcConnectionStatus.Connecting => "Connexion en cours…",
            GrpcConnectionStatus.Connected => "Connecté",
            GrpcConnectionStatus.InGame => "En jeu",
            GrpcConnectionStatus.Error => "Erreur",
            _ => string.Empty,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
