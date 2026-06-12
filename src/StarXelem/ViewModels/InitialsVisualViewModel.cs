using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace StarXelem.ViewModels;

public sealed class InitialsVisualViewModel : ObservableObject, IVisualSourceViewModel
{
    public InitialsVisualViewModel()
    {
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) => OnPropertyChanged((string?)null));
    }

    // -------------------------------------------------------------------------
    // Palette du design system StarXelem (couleurs de fond pour les avatars)
    // Teintes compatibles dark et light — semi-transparentes intentionnellement
    // -------------------------------------------------------------------------
    private static readonly Color[] Palette =
    [
        Color.Parse("#FFA78BFA"), // violet principal
        Color.Parse("#FF5DCAA5"), // teal
        Color.Parse("#FFEF9F27"), // orange
        Color.Parse("#FF85B7EB"), // bleu
        Color.Parse("#FFF09595"), // rouge atténué
        Color.Parse("#FFF0997B"), // corail
        Color.Parse("#FF7F77DD"), // violet atténué
        Color.Parse("#FF5DCAA5"), // teal bis
    ];

    // -------------------------------------------------------------------------
    // Propriétés exposées au template XAML
    // -------------------------------------------------------------------------

    /// <summary>1 à 2 lettres majuscules dérivées du nom.</summary>
    public string Initials { get; }

    /// <summary>
    /// Couleur de fond. Doit être utilisée via un IBrush dans le template.
    /// En dark : fond semi-transparent sur la couleur de palette.
    /// En light : même couleur, le fond de la fenêtre étant clair, le contraste est naturel.
    /// </summary>
    public Color BackgroundColor { get; }

    /// <summary>Clé de ressource pour la couleur du texte des initiales. Si null, utilise la couleur du thème.</summary>
    public string? ForegroundResourceKey { get; }

    /// <summary>Couleur du texte des initiales fixe. Prioritaire si définie.</summary>
    public Color? ForegroundColor { get; }
    
    // -------------------------------------------------------------------------
    // Constructeurs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calcule automatiquement les initiales et attribue une couleur stable depuis la palette.
    /// </summary>
    /// <param name="name">Nom complet (ex: "Vanduul Swarm", "9th Squadron")</param>
    public InitialsVisualViewModel(string name)
    {
        Initials         = ComputeInitials(name);
        BackgroundColor  = ComputeColorFromName(name);
        ForegroundResourceKey = null; // Use theme default
        ForegroundColor  = null;
    }

    /// <summary>
    /// Permet de forcer une couleur de fond spécifique (ex: couleur de faction connue).
    /// </summary>
    /// <param name="name">Nom affiché</param>
    /// <param name="background">Couleur de fond explicite</param>
    /// <param name="foregroundResourceKey">Clé de ressource pour le texte</param>
    /// <param name="foreground">Couleur du texte fixe</param>
    public InitialsVisualViewModel(string name, Color background, string? foregroundResourceKey = null, Color? foreground = null)
    {
        Initials        = ComputeInitials(name);
        BackgroundColor = background;
        ForegroundResourceKey = foregroundResourceKey;
        ForegroundColor = foreground;
    }

    // -------------------------------------------------------------------------
    // Helpers privés
    // -------------------------------------------------------------------------

    private static string ComputeInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(words[0][0], words[1][0]).ToUpperInvariant()
        };
    }

    private static Color ComputeColorFromName(string name)
    {
        // Hash stable (indépendant de la culture, cross-platform)
        var hash = 0;
        foreach (var c in name)
            hash = hash * 31 + c;

        var idx = Math.Abs(hash) % Palette.Length;
        return Palette[idx];
    }
}
