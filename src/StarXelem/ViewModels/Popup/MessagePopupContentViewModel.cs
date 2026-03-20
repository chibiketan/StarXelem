using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace StarXelem.ViewModels.Popup;

public partial class MessagePopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private string _title = "Erreur";
    [ObservableProperty] private string _message = string.Empty;

    public Task OnPopupShownAsync()
    {
        // Aucune initialisation spécifique requise
        return Task.CompletedTask;
    }
}
