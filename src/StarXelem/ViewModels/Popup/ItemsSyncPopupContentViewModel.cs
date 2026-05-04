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
/// ViewModel pour la popup de synchronisation de la liste d'objets vers Alliance Orbital.
/// </summary>
public partial class ItemsSyncPopupContentViewModel : ViewModelBase, IPopupContentViewModel
{
    [ObservableProperty] private bool _showConfirmButton = true;
    [ObservableProperty] private string _title = "Envoi Liste d'Objets Alliance Orbital";
    [ObservableProperty] private string _message = "Prêt à envoyer les données de la liste d'objets ?";
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>
    /// Liste des objets regroupés par type, prêts à être envoyés.
    /// </summary>
    [ObservableProperty] private List<ItemSyncItem>? _itemsToSend;

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

    public ItemsSyncPopupContentViewModel(IAllianceOrbitalService allianceOrbitalService)
    {
        _allianceOrbitalService = allianceOrbitalService;
        ItemsToSend = new List<ItemSyncItem>();
    }

    private void UpdateSummaryText()
    {
        if (ItemsToSend == null || ItemsToSend.Count == 0)
        {
            Message = "Aucun objet à envoyer.";
            StatusMessage = "";
            return;
        }

        Message = $"Liste d'objets à envoyer : {ItemsToSend.Count} types d'objets";
    }

    /// <summary>
    /// Valide l'envoi de la liste d'objets vers Alliance Orbital.
    /// Vérifie qu'un profil est sélectionné, synchronise les données d'objets par batches, puis affiche le résultat pendant 5s avant de fermer la popup.
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
        var syncedAt = DateTime.UtcNow;

        try
        {
            var results = await _allianceOrbitalService.SyncItemsAsync(SelectedProfile.Guid!, ItemsToSend!, syncedAt);

            if (results.Any(r => r.Success))
            {
                var totalReceived = results.Sum(r => r.Received);
                var totalMatched = results.Sum(r => r.Matched);
                var totalUpdated = results.Sum(r => r.Updated);
                var totalRemoved = results.Sum(r => r.Removed);
                var totalFiltered = results.Sum(r => r.Filtered);

                StatusMessage = $"Synchronisé : {totalReceived} reçus, {totalMatched} reconnus, {totalUpdated} mis à jour";
                if (totalRemoved > 0)
                    StatusMessage += $", {totalRemoved} obsolètes supprimés";
                if (totalFiltered > 0)
                    StatusMessage += $", {totalFiltered} rejetés";

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
        if (ItemsToSend == null)
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
