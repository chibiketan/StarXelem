using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StarXelem.Models;
using StarXelem.ViewModels;

namespace StarXelem.Views;

public partial class ShipTabView : UserControl
{
    public ShipTabView()
    {
        InitializeComponent();
    }

    /// <summary>Un double-clic sur une ligne du tableau ouvre la fiche loadout du vaisseau.</summary>
    private void OnShipGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Un double-clic sur l'en-tête (tri, filtre) ne doit rien ouvrir.
        if (e.Source is not Control source || source.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: SpaceshipModel ship })
        {
            return;
        }

        if (DataContext is ShipTabViewModel viewModel)
        {
            viewModel.OpenLoadoutCommand.Execute(ship);
        }
    }
}
