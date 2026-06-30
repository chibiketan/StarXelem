using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace StarXelem.ViewModels.Popup;

public partial class LoadingPopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private string _message = "Chargement";
    [ObservableProperty] private bool _showLoading;
    [ObservableProperty] private double? _progress;
    [ObservableProperty] private string _phaseLabel = "";

    public Task OnPopupShownAsync()
    {
        return Task.CompletedTask;
    }
}
