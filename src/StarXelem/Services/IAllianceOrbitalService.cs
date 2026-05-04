using System.Collections.Generic;
using System.Threading.Tasks;
using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Service de communication avec l'API externe Alliance Orbital.
/// </summary>
public interface IAllianceOrbitalService
{
    /// <summary>
    /// Récupère la liste des profils utilisateur depuis l'API.
    /// Le jeton JWT est lu automatiquement depuis les settings (clé "ApiKey").
    /// Lance une exception si le token est manquant ou invalide (401).
    /// Retourne une liste vide en cas de 404 (aucun profil).
    /// </summary>
    Task<List<ProfilItem>> GetProfilesAsync();

    /// <summary>
    /// Synchronise la liste des blueprints possédés pour un profil RSI.
    /// Les blueprints présents sont marqués possédés, les absents remis à non-possédé.
    /// </summary>
    /// <param name="rsiProfilGuid">GUID du profil RSI (obtenu via GetProfilesAsync).</param>
    /// <param name="blueprintIds">Liste des identifiants de blueprints à synchroniser.</param>
    Task<SyncResult> SyncBlueprintsAsync(string rsiProfilGuid, List<string> blueprintIds);

    /// <summary>
    /// Synchronise la liste des vaisseaux possédés pour un profil RSI.
    /// Les vaisseaux sont regroupés par classe de vaisseau avec leur compte.
    /// </summary>
    /// <param name="rsiProfilGuid">GUID du profil RSI (obtenu via GetProfilesAsync).</param>
    /// <param name="fleetItems">Liste des couples (classId, count) à synchroniser.</param>
    Task<SyncResult> SyncFleetAsync(string rsiProfilGuid, List<FleetSyncItem> fleetItems);

    /// <summary>
    /// Synchronise les items pour un profil RSI. Les items sont envoyés par batches de 5000 max avec le même syncedAt.
    /// </summary>
    /// <param name="rsiProfilGuid">GUID du profil RSI (obtenu via GetProfilesAsync).</param>
    /// <param name="items">Liste des items à synchroniser.</param>
    /// <param name="syncedAt">Horodatage ISO 8601 commun à tous les batchs. Utilisé pour détecter les items obsolètes.</param>
    Task<List<SyncResult>> SyncItemsAsync(string rsiProfilGuid, List<ItemSyncItem> items, DateTime syncedAt);
}
