using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace StarXelem.Converters;

/// <summary>
/// Convertit une valeur d'énumération en une chaîne de caractères lisible pour l'utilisateur.
/// </summary>
public class EnumToTextConverter : IValueConverter
{
    /// <summary>
    /// Transforme la valeur d'énumération en texte selon les règles suivantes :
    /// 1. Si l'énumération possède un <see cref="DisplayAttribute"/>, utilise son nom.
    /// 2. Sinon, utilise le nom du membre en ajoutant un espace avant chaque majuscule (CamelCase).
    /// 3. Si la valeur n'est pas une énumération, retourne un message d'erreur.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is Enum enumValue)
        {
            var type = value.GetType();
            var memberInfo = type.GetMember(enumValue.ToString())[0];
            var displayAttribute = memberInfo.GetCustomAttribute<DisplayAttribute>();

            if (displayAttribute != null && !string.IsNullOrEmpty(displayAttribute.Name))
            {
                return displayAttribute.Name;
            }

            var name = enumValue.ToString();
            return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        }

        return "#ERR not an enum";
    }

    /// <summary>
    /// Conversion inverse non implémentée.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
