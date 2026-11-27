using Avalonia.Controls;

namespace StarXelem.Components;

/// <summary>
/// Badge indiquant la date de récupération des données d'un écran et leur provenance (jeu ou cache local).
/// </summary>
public partial class DataFreshnessBadge : UserControl
{
    /// <summary>
    /// Initialise le badge.
    /// </summary>
    public DataFreshnessBadge()
    {
        InitializeComponent();
    }
}
