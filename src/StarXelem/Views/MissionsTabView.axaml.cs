using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StarXelem.Views;

public partial class MissionsTabView : UserControl
{
    public MissionsTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
