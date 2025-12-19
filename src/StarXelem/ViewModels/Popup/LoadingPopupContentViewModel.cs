using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace StarXelem.ViewModels.Popup;

public partial class LoadingPopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private string _message = "Chargement";
    [ObservableProperty] private bool _showLoading;

    public Task OnPopupShownAsync()
    {
        // Pas d'initialisation spécifique requise pour ce contenu
        return Task.CompletedTask;
    }
}