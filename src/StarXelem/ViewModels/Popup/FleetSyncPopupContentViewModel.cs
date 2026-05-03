using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels.Popup;

/// <summary>
/// ViewModel pour la popup de synchronisation de flotte de vaisseaux vers Alliance Orbital.
/// </summary>
public partial class FleetSyncPopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private bool _showConfirmButton = true;
    [ObservableProperty] private string _title = "Envoi Flotte Alliance Orbital";
    [ObservableProperty] private string _message = "Prêt à envoyer les données de flotte ?";
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>
    /// Liste des vaisseaux regroupés par classe, prêts à être envoyés.
    /// </summary>
    [ObservableProperty] private List<FleetSyncItem>? _fleetToSend;

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

    public FleetSyncPopupContentViewModel(IAllianceOrbitalService allianceOrbitalService)
    {
        _allianceOrbitalService = allianceOrbitalService;
        FleetToSend = new List<FleetSyncItem>();
    }

    private void UpdateSummaryText()
    {
        if (FleetToSend == null || FleetToSend.Count == 0)
        {
            Message = "Aucun vaisseau à envoyer.";
            StatusMessage = "";
            return;
        }

        Message = $"Flotte à envoyer : {FleetToSend.Count} classes de vaisseaux";
    }

    /// <summary>
    /// Valide l'envoi de la flotte vers Alliance Orbital.
    /// Vérifie qu'un profil est sélectionné, synchronise les données de flotte, puis affiche le résultat pendant 5s avant de fermer la popup.
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
            var result = await _allianceOrbitalService.SyncFleetAsync(SelectedProfile.Guid, FleetToSend!);

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
    /// Appelé à l'ouverture de la popup. Charge les profils Alliance Orbital.
    /// </summary>
    public async Task OnPopupShownAsync()
    {
        if (FleetToSend == null)
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
