using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StarXelem.ViewModels.Popup;

namespace StarXelem.Views.Popup;

public partial class LoadingPopupContentView : UserControl
{
    public LoadingPopupContentView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LoadingPopupContentViewModel vm)
        {
            UpdateIndeterminateState(vm);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LoadingPopupContentViewModel.Progress))
                {
                    UpdateIndeterminateState(vm);
                }
            };
        }
    }

    void UpdateIndeterminateState(LoadingPopupContentViewModel vm)
    {
        ProgressBar.IsIndeterminate = vm.Progress == null;
    }
}
