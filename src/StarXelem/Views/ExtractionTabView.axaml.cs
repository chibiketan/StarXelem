using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StarXelem.Views;

public partial class ExtractionTabView : UserControl
{
    public ExtractionTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
