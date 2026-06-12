using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using StarXelem.ViewModels;


namespace StarXelem.Converters;

// =============================================================================
// ColorToBrushConverter
// =============================================================================

/// <summary>
/// Convertit une <see cref="Color"/> Avalonia en <see cref="SolidColorBrush"/>.
/// Utilisé dans les DataTemplates du VisualSource pour lier BackgroundColor / ForegroundColor.
///
/// Déclaration dans App.axaml (dans les ressources globales) :
///   <conv:ColorToBrushConverter x:Key="ColorToBrushConverter"/>
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
            return new SolidColorBrush(color);

        // Tolérance : accepte aussi un string ARGB en fallback
        if (value is string str && Color.TryParse(str, out var parsed))
            return new SolidColorBrush(parsed);

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return brush.Color;
        return AvaloniaProperty.UnsetValue;
    }
}

// =============================================================================
// VisualSourceBrushConverter
// =============================================================================

/// <summary>
/// Résout la couleur d'un VisualSourceViewModel en suivant la priorité :
/// 1. Couleur fixe (Brush/Color)
/// 2. Clé de ressource dynamique
/// 3. Fallback thème
/// </summary>
public sealed class VisualSourceBrushConverter : IValueConverter
{
    public static readonly VisualSourceBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return AvaloniaProperty.UnsetValue;

        // 1. Tentative de résolution via les propriétés fixes (Priorité haute)
        if (value is FluentIconVisualViewModel fluent && fluent.Foreground != null)
            return fluent.Foreground;
        if (value is GeometryIconVisualViewModel geom && geom.Fill != null)
            return geom.Fill;
        if (value is InitialsVisualViewModel init && init.ForegroundColor != null)
            return new SolidColorBrush(init.ForegroundColor.Value);

        // 2. Tentative de résolution via la clé de ressource
        string? key = null;
        if (value is FluentIconVisualViewModel f) key = f.ForegroundResourceKey;
        else if (value is GeometryIconVisualViewModel g) key = g.FillResourceKey;
        else if (value is InitialsVisualViewModel i) key = i.ForegroundResourceKey;

        if (!string.IsNullOrEmpty(key) && Application.Current is not null)
        {
            if (Application.Current.Resources.TryGetResource(key, null, out var resource))
                return resource as IBrush;
        }

        // 3. Fallback Theme (Défini dans App.axaml)
        string defaultKey = value switch
        {
            FluentIconVisualViewModel or GeometryIconVisualViewModel => "VisualSourceIconForeground",
            InitialsVisualViewModel => "VisualSourceInitialsForeground",
            _ => null
        };

        if (!string.IsNullOrEmpty(defaultKey) && Application.Current is not null)
        {
            if (Application.Current.Resources.TryGetResource(defaultKey, null, out var resource))
                return resource as IBrush;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Convertit une clé de ressource string en <see cref="StreamGeometry"/> depuis
/// <c>Application.Current.Resources</c>.
///
/// Utilisé pour le DataTemplate GeometryIconVisualViewModel :
///   Data="{Binding ResourceKey, Converter={StaticResource ResourceKeyToGeometryConverter}}"
///
/// Si la ressource est introuvable, retourne null (Path invisible).
///
/// Déclaration dans App.axaml :
///   <conv:ResourceKeyToGeometryConverter x:Key="ResourceKeyToGeometryConverter"/>
/// </summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public static readonly ResourceKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key)
            return AvaloniaProperty.UnsetValue;

        // Recherche dans Application.Resources (inclut les MergedDictionaries)
        if (Application.Current is not null &&
            Application.Current.Resources.TryGetResource(key, null, out var resource))
        {
            return resource as Geometry; // StreamGeometry hérite de Geometry
        }

        // Ressource non trouvée — pas d'exception, Path sera invisible
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

// =============================================================================
// VisualSourceBackgroundConverter
// =============================================================================

/// <summary>
/// Résout la couleur de fond d'un InitialsVisualViewModel.
/// </summary>
public sealed class VisualSourceBackgroundConverter : IValueConverter
{
    public static readonly VisualSourceBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InitialsVisualViewModel init)
            return new SolidColorBrush(init.BackgroundColor);
            
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Inverse un booléen. Utilisé pour masquer le ContentPresenter quand IsLoading=True.
///
/// Déclaration dans App.axaml :
///   <conv:InverseBoolConverter x:Key="InverseBoolConverter"/>
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : AvaloniaProperty.UnsetValue;
}
