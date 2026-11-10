using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarXelem.ViewModels;

/// <summary>
/// Puce cliquable de filtre rapide.
/// </summary>
public partial class FilterChipViewModel : ObservableObject
{
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Initialise une puce.
    /// </summary>
    /// <param name="key">Identifiant de la valeur, ou <c>null</c> pour la puce qui retire le filtre.</param>
    /// <param name="label">Texte affiché.</param>
    /// <param name="toggle">Appelé avec <paramref name="key"/> au clic.</param>
    public FilterChipViewModel(string? key, string label, Action<string?> toggle)
    {
        Key = key;
        Label = label;
        ToggleCommand = new RelayCommand(() => toggle(key));
    }

    /// <summary>Identifiant de la valeur, ou <c>null</c> pour la puce qui retire le filtre.</summary>
    public string? Key { get; }

    /// <summary>Texte affiché.</summary>
    public string Label { get; }

    /// <summary>Active ou désactive la valeur de la puce.</summary>
    public IRelayCommand ToggleCommand { get; }
}
