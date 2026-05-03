using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels.Popup;

public partial class SendToOrbitalAlliancePopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private bool _showConfirmButton = true;
    [ObservableProperty] private string _title = "Envoi Orbital Alliance";
    [ObservableProperty] private string _message = "Prêt à envoyer les données aux blueprints ?";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private List<BlueprintViewModel>? _blueprintsToSend;

    // Profils chargés depuis l'API Alliance Orbital pour la sélection utilisateur
    [ObservableProperty] private List<ProfilItem> _profiles = new();
    [ObservableProperty] private ProfilItem? _selectedProfile;
    [ObservableProperty] private bool _isLoadingProfiles;

    private readonly IAllianceOrbitalService _allianceOrbitalService;

    public SendToOrbitalAlliancePopupContentViewModel(IAllianceOrbitalService allianceOrbitalService)
    {
        _allianceOrbitalService = allianceOrbitalService;
        BlueprintsToSend = new List<BlueprintViewModel>();
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

    /// <summary>
    /// Appelé à l'ouverture de la popup. Charge les blueprints puis récupère les profils Alliance Orbital.
    /// </summary>
    public async Task OnPopupShownAsync()
    {
        if (BlueprintsToSend == null)
        {
            return;
        }

        UpdateSummaryText();

        // Chargement asynchrone des profils depuis l'API Alliance Orbital
        IsLoadingProfiles = true;
        StatusMessage += "\nChargement des profils...";

        try
        {
            var profiles = await _allianceOrbitalService.GetProfilesAsync();
            Profiles = profiles;
            StatusMessage = $"Profils chargés : {profiles.Count}";
            _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(task => StatusMessage = "");
        }
        catch (System.Exception ex)
        {
            // Erreur possible : token manquant, 401, erreur réseau...
            StatusMessage = $"Erreur profils : {ex.Message}";
        }
        finally
        {
            IsLoadingProfiles = false;
        }
    }
}
