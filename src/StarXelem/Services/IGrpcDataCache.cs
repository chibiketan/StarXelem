using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Conserve entre deux lancements les données récupérées par gRPC, pour les réafficher sans le jeu.
/// Les données sont rangées par environnement (LIVE, PTU…) et par compte de joueur. Tant que le jeu n'est pas connecté,
/// le compte retenu est le dernier vu sur l'environnement sélectionné.
/// </summary>
public interface IGrpcDataCache
{
    /// <summary>
    /// Relit un jeu de données enregistré.
    /// </summary>
    /// <param name="datasetKey">Nom du jeu de données (ex : « items »).</param>
    /// <param name="token">Jeton d'annulation.</param>
    /// <returns>Le contenu enregistré et sa date de récupération, ou <c>null</c> s'il n'y en a pas ou s'il est illisible.</returns>
    Task<CachedDataset<byte[]>?> LoadAsync(string datasetKey, CancellationToken token = default);

    /// <summary>
    /// Enregistre un jeu de données pour le compte connecté, en remplaçant le précédent. Sans compte connecté, rien n'est enregistré.
    /// Une erreur d'écriture est journalisée sans être propagée : le cache ne doit jamais faire échouer un écran.
    /// </summary>
    /// <param name="datasetKey">Nom du jeu de données (ex : « items »).</param>
    /// <param name="payload">Contenu sérialisé.</param>
    /// <param name="fetchedAt">Date de récupération des données auprès du serveur.</param>
    /// <param name="token">Jeton d'annulation.</param>
    Task SaveAsync(string datasetKey, byte[] payload, DateTimeOffset fetchedAt, CancellationToken token = default);
}
