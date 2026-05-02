using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StarXelem.Views;

public partial class SettingsTabView : UserControl
{
    public SettingsTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
