using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StarXelem.Views
{
    public partial class ReputationTabView : UserControl
    {
        public ReputationTabView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
