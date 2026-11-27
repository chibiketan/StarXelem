namespace StarXelem.Models;

/// <summary>
/// Jeu de données relu depuis le cache local des réponses gRPC.
/// </summary>
/// <typeparam name="T">Type des données relues.</typeparam>
/// <param name="Data">Données relues.</param>
/// <param name="FetchedAt">Date de récupération des données auprès du serveur.</param>
/// <param name="AccountKey">Compte du joueur auquel les données appartiennent (son geid).</param>
public sealed record CachedDataset<T>(T Data, DateTimeOffset FetchedAt, string AccountKey);
