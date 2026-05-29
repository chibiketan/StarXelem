using System.Globalization;
using Avalonia.Data.Converters;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.Converters;

/// <summary>
/// Retourne les classes CSS pour un standing dans la liste de l'accordéon (§17.7).
/// Valeurs d'entrée attendues : [StandingModel, ReputationModel]
/// </summary>
public class StandingClassesConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return "standing-item";
        if (values[0] is not StandingModel standing)
            return "standing-item";
        if (values[1] is not ReputationModel scope)
            return "standing-item";

        if (standing.Name == scope.CurrentStanding?.Name)
            return "standing-item current-standing";
        if (scope.CurrentValue.HasValue && standing.Min > scope.CurrentValue.Value)
            return "standing-item locked-standing";
        return "standing-item";
    }
}
