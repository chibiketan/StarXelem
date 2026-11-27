using StarXelem.Models;

namespace StarXelem.Services;

public interface IReputationService
{
    /// <summary>
    /// Contractants du Data.p4k complétés des réputations du joueur : le dernier chargement enregistré s'il existe ; sinon, ou si
    /// <paramref name="forceRefresh"/> est vrai, lecture auprès du jeu s'il est connecté. Si le jeu ne répond pas, le cache reste la
    /// solution de repli.
    /// </summary>
    /// <param name="forceRefresh">Vrai pour interroger le jeu même si un chargement est enregistré (actualisation demandée).</param>
    /// <returns>Les contractants triés par nom, avec la date des réputations et leur provenance.</returns>
    Task<ReputationResult> GetSynchronizedReputationsAsync(bool forceRefresh = false);
}

/// <summary>Contractants avec réputations du joueur, date de récupération de ces réputations et provenance.</summary>
/// <param name="Contractors">Contractants triés par nom.</param>
/// <param name="FetchedAt">Date de récupération des réputations auprès du jeu.</param>
/// <param name="IsFromCache">Vrai si les réputations ont été relues du cache.</param>
public sealed record ReputationResult(List<ContractorModel> Contractors, DateTimeOffset FetchedAt, bool IsFromCache);
