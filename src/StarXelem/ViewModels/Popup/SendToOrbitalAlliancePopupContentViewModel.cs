using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StarXelem.ViewModels.Popup;

public partial class SendToOrbitalAlliancePopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private bool _showConfirmButton = true;
    [ObservableProperty] private string _title = "Envoi Orbital Alliance";
    [ObservableProperty] private string _message = "Prêt à envoyer les données aux blueprints ?";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private List<BlueprintViewModel>? _blueprintsToSend;

    public SendToOrbitalAlliancePopupContentViewModel()
    {
        BlueprintsToSend = new List<BlueprintViewModel>();
    }

    public void SetBlueprintsToList(IEnumerable<BlueprintViewModel> blueprints)
    {
        var filtered = blueprints.Where(b => b.Name != null && !string.IsNullOrWhiteSpace(b.Name));
        BlueprintsToSend = filtered.ToList();
        StatusMessage = $"Nombre de blueprints : {BlueprintsToSend.Count}";

        UpdateSummaryText();
    }

    private void UpdateSummaryText()
    {
        if (BlueprintsToSend == null || BlueprintsToSend.Count == 0)
        {
            Message = "Aucun blueprint à envoyer.";
            StatusMessage = "";
            return;
        }

        Message = $"Blueprints à envoyer : {BlueprintsToSend.Count} blueprints";
    }

    [RelayCommand]
    private void OnConfirm()
    {
        // La logique d'envoi sera implémentée ultérieurement
        WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
    }

    [RelayCommand]
    private void OnCancel()
    {
        WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
    }

    public Task OnPopupShownAsync()
    {
        if (BlueprintsToSend == null)
        {
            return Task.CompletedTask;
        }

        UpdateSummaryText();
        return Task.CompletedTask;
    }
}
