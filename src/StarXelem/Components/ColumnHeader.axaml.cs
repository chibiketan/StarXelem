using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using StarXelem.ViewModels;

namespace StarXelem.Components;

/// <summary>
/// Contenu d'un en-tête de colonne : titre cliquable pour trier et, si la colonne le permet, panneau de filtre par valeurs.
/// </summary>
public partial class ColumnHeader : UserControl
{
    /// <summary>
    /// Initialise l'en-tête et prépare l'ouverture et la fermeture du panneau de filtre.
    /// </summary>
    public ColumnHeader()
    {
        InitializeComponent();

        var flyout = FilterButton.Flyout;
        if (flyout != null)
        {
            flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() => SearchBox.Focus());
            flyout.Closed += (_, _) => ClearSearch();
        }
    }

    private void OnCloseFilterClick(object? sender, RoutedEventArgs e)
    {
        FilterButton.Flyout?.Hide();
    }

    private void ClearSearch()
    {
        if (DataContext is ColumnHeaderViewModel { Filter: { } filter })
        {
            filter.SearchText = string.Empty;
        }
    }
}
