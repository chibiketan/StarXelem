using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input; // AsyncRelayCommand, RelayCommand
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfilListEnabled))]
    private List<ProfilItem> _profiles = new();
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ProfilItem? _selectedProfile;

    // Indicateur de chargement des profils (désactive le ComboBox pendant le chargement)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(IsProfilListEnabled))]
    private bool _isLoadingProfiles = false;

    // Indicateur de synchronisation en cours (désactive les boutons)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isSyncing;

    public bool IsProfilListEnabled =>
        !IsLoadingProfiles
        && Profiles is { Count: >0 };

    /// <summary>Indique si l'utilisateur peut interagir avec la popup (ni chargement ni synchro en cours).</summary>
    public bool CanInteract => !IsLoadingProfiles && !IsSyncing;

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

    /// <summary>
    /// Valide l'envoi des blueprints vers Alliance Orbital.
    /// Vérifie qu'un profil est sélectionné, synchronise les blueprints, puis affiche le résultat pendant 5s avant de fermer la popup.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task OnConfirm()
    {
        // Vérifier qu'un profil est sélectionné
        if (SelectedProfile == null)
        {
            StatusMessage = "Veuillez sélectionner un profil à synchroniser.";
            return;
        }

        IsSyncing = true;
        StatusMessage = "Synchronisation en cours...";

        try
        {
            // Extraire les IDs des blueprints à envoyer
            var blueprintIds = BlueprintsToSend!
                .Select(b => b.BlueprintId.ToString())
                .ToList();

            var result = await _allianceOrbitalService.SyncBlueprintsAsync(SelectedProfile.Guid, blueprintIds);

            if (result.Success)
            {
                StatusMessage = $"Synchronisé : {result.Received} reçus, {result.Matched} reconnus, {result.Updated} mis à jour.";
                // Attendre 5 secondes puis fermer la popup automatiquement
                await Task.Delay(TimeSpan.FromSeconds(5));
                WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
            }
            else
            {
                StatusMessage = "La synchronisation a échoué. Vérifiez votre clé API.";
            }
        }
        catch (System.Exception ex)
        {
            // Erreur possible : token invalide, profil non autorisé, erreur réseau...
            StatusMessage = $"Erreur synchronisation : {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private bool CanConfirm()
    {
        return CanInteract && SelectedProfile != null;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void OnCancel()
    {
        WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
    }

    private bool CanCancel()
    {
        return CanInteract;
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
