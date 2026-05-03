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
}
