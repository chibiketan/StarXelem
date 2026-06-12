using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace StarXelem.ViewModels;

public record ThemeChangedMessage();

// =============================================================================
// ICÔNE GÉOMÉTRIQUE (StreamGeometry depuis App.axaml ou ResourceDictionary)
// =============================================================================

/// <summary>
/// Affiche un <see cref="Avalonia.Media.StreamGeometry"/> chargé depuis une ressource statique.
/// Typiquement : Icon.Helmet, Icon.Body, Icon.Arms, etc. (design system §13).
/// </summary>
public sealed class GeometryIconVisualViewModel : ObservableObject, IVisualSourceViewModel
{
    public GeometryIconVisualViewModel()
    {
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) => OnPropertyChanged((string?)null));
    }

    /// <summary>
    /// Clé de ressource statique contenant la StreamGeometry.
    /// Ex : "Icon.Helmet"
    /// </summary>
    public string ResourceKey { get; }

    /// <summary>Clé de ressource pour la couleur de remplissage. Si null, utilise la couleur du thème.</summary>
    public string? FillResourceKey { get; }

    /// <summary>Couleur de remplissage fixe. Prioritaire si définie.</summary>
    public IBrush? Fill { get; }

    /// <param name="resourceKey">Clé dans App.axaml (ex: "Icon.Helmet")</param>
    /// <param name="fillResourceKey">Clé de ressource pour le remplissage</param>
    /// <param name="fill">Brush de remplissage fixe</param>
    public GeometryIconVisualViewModel(string resourceKey, string? fillResourceKey = null, IBrush? fill = null)
    {
        ResourceKey = resourceKey;
        FillResourceKey = fillResourceKey;
        Fill        = fill;
    }
}

// =============================================================================
// ICÔNE FLUENT (Symbol enum de FluentAvalonia)
// =============================================================================

/// <summary>
/// Affiche une icône via le <see cref="Symbol"/> enum de FluentAvalonia.
/// Rendu via un <c>SymbolIcon</c> natif.
/// </summary>
public sealed class FluentIconVisualViewModel : ObservableObject, IVisualSourceViewModel
{
    public FluentIconVisualViewModel()
    {
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) => OnPropertyChanged((string?)null));
    }

    /// <summary>Symbole FluentAvalonia (ex: Symbol.Settings, Symbol.Star…)</summary>
    public FluentIcons.Common.Symbol Symbol { get; }

    /// <summary>Clé de ressource pour la couleur de l'icône. Si null, utilise la couleur du thème.</summary>
    public string? ForegroundResourceKey { get; }

    /// <summary>Couleur de l'icône fixe. Prioritaire si définie.</summary>
    public IBrush? Foreground { get; }

    /// <summary>Taille de la police du glyphe. Défaut : 18.</summary>
    public double FontSize { get; }

    public FluentIconVisualViewModel(FluentIcons.Common.Symbol symbol, string? foregroundResourceKey = null, IBrush? foreground = null, double fontSize = 18)
    {
        Symbol               = symbol;
        ForegroundResourceKey = foregroundResourceKey;
        Foreground           = foreground;
        FontSize             = fontSize;
    }
}

// =============================================================================
// IMAGE PAR URI (resource Avalonia, URL HTTP, ou avares://)
// =============================================================================

/// <summary>
/// Affiche une image depuis n'importe quelle URI :
/// - Ressource embarquée : <c>avares://StarXelem/Assets/img.png</c>
/// - URL réseau : <c>https://cdn.example.com/faction.png</c>
/// - Fichier local : <c>file:///C:/path/img.png</c>
/// </summary>
public sealed class UriImageVisualViewModel : IVisualSourceViewModel
{
    public Uri Source { get; }

    /// <summary>
    /// ViewModel de fallback affiché pendant le chargement ou en cas d'erreur.
    /// Si null, le contrôle affiche un fond vide.
    /// </summary>
    public IVisualSourceViewModel? Fallback { get; }

    public UriImageVisualViewModel(Uri source, IVisualSourceViewModel? fallback = null)
    {
        Source   = source;
        Fallback = fallback;
    }

    public UriImageVisualViewModel(string uri, IVisualSourceViewModel? fallback = null)
        : this(new Uri(uri), fallback) { }
}

// =============================================================================
// IMAGE PAR CHEMIN FICHIER ABSOLU
// =============================================================================

/// <summary>
/// Affiche une image depuis un chemin fichier absolu sur disque.
/// Utile pour les icônes de faction stockées dans le répertoire local du jeu.
/// </summary>
public sealed class PathImageVisualViewModel : IVisualSourceViewModel
{
    public string FilePath { get; }

    /// <summary>ViewModel de fallback si le fichier n'existe pas.</summary>
    public IVisualSourceViewModel? Fallback { get; }

    public PathImageVisualViewModel(string filePath, IVisualSourceViewModel? fallback = null)
    {
        FilePath = filePath;
        Fallback = fallback;
    }
}
