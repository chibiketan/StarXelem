using CommunityToolkit.Mvvm.ComponentModel;

namespace StarXelem.ViewModels;

/// <summary>
/// Une valeur proposée dans le filtre d'une colonne de tableau.
/// </summary>
public partial class ColumnFilterOptionViewModel : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Initialise une valeur de filtre.
    /// </summary>
    /// <param name="key">Identifiant stable de la valeur.</param>
    /// <param name="label">Texte affiché à l'utilisateur.</param>
    public ColumnFilterOptionViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Identifiant stable de la valeur.</summary>
    public string Key { get; }

    /// <summary>Texte affiché à l'utilisateur.</summary>
    public string Label { get; }
}
