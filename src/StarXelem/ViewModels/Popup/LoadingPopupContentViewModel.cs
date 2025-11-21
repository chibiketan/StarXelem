using CommunityToolkit.Mvvm.ComponentModel;

namespace StarXelem.ViewModels.Popup;

public partial class LoadingPopupContentViewModel : ViewModelBase
{
    [ObservableProperty] private string _message = "Chargement";
    [ObservableProperty] private bool _showLoading;
    
    
}