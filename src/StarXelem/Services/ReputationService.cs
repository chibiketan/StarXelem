using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sc.External.Services.Reputation.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services;

public class ReputationService : IReputationService
{
    private const string DatasetKey = "reputations";

    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly IGrpcDataCache _dataCache;
    private readonly ILogger<ReputationService> _logger;

    public ReputationService(IP4kService p4kService, IGrpcClientService grpcClientService, IGrpcDataCache dataCache, ILogger<ReputationService> logger)
    {
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
        _dataCache = dataCache;
        _logger = logger;
    }

    public async Task<ReputationResult> GetSynchronizedReputationsAsync(bool forceRefresh = false)
    {
        // 1. Retrieve static data from P4K
        var factionReputationRecordList = await _p4kService.GetAllFactionReputations().ConfigureAwait(false);

        // 2. Retrieve player data from gRPC
        var (playerReputations, fetchedAt, isFromCache) = await GetPlayerReputationsAsync(forceRefresh).ConfigureAwait(false);

        var contractorMap = new Dictionary<ulong, ContractorModel>();

        foreach (var dataCoreTypedRecord in factionReputationRecordList)
        {
            if (dataCoreTypedRecord.Data is not FactionReputation factionReputation)
            {
                _logger.LogWarning("No faction reputation found for record {RecordId}", dataCoreTypedRecord.RecordId);
                continue;
            }

            if (factionReputation.reputationContextPropertiesUI is null)
            {
                _logger.LogInformation("No reputation context found for faction reputation {RecordId}", dataCoreTypedRecord.RecordId);
                continue;
            }

            if (factionReputation.hideInDelpihApp)
            {
                continue;
            }

            var contractor = new ContractorModel
            {
                Id = dataCoreTypedRecord.RecordId,
                Name = await _p4kService.GetLocaleValue(factionReputation.displayName),
                Geid = factionReputation.GEID
            };

            foreach (var scopeContext in factionReputation.reputationContextPropertiesUI.scopeContextList)
            {
                if (scopeContext.scope is null)
                {
                    _logger.LogWarning("Null scope found for faction reputation {RecordId}", dataCoreTypedRecord.RecordId);
                    continue;
                }

                var scope = new ReputationModel
                {
                    Category = scopeContext.scope.scopeName,
                    DisplayName = await _p4kService.GetLocaleValue(scopeContext.scope.displayName)
                };

                contractor.Reputations.Add(scope);

                for (int standingIndex = 0; standingIndex < scopeContext.scope.standingMap.standings.Count(); standingIndex++)
                {
                    var standing = scopeContext.scope.standingMap.standings[standingIndex];

                    if (standing is null)
                    {
                        _logger.LogWarning("Null standing found for faction reputation {RecordId}", dataCoreTypedRecord.RecordId);
                        continue;
                    }

                    scope.StandingList.Add(new StandingModel
                    {
                        Name = standing.name,
                        DisplayName = await _p4kService.GetLocaleValue(standing.displayName),
                        Min = standing.minReputation,
                        Tier = Math.Min(standingIndex + 1, 7)
                    });
                }

                // On défini la valeur max comme le min - 1 du prochain standing
                for (int i = scope.StandingList.Count - 2; i >= 0; --i)
                {
                    scope.StandingList[i].Max = scope.StandingList[i + 1].Min - 1;
                }

                scope.StandingList[^1].Max = scopeContext.scope.standingMap.reputationCeiling;
                scope.CurrentStanding = scope.StandingList.FirstOrDefault();
            }

            // Assurer que les scopes d'hostilité et d'alliance sont chargés même s'ils ne sont pas dans scopeContextList
            var additionalParams = new List<RelationStandingParams>();
            if (factionReputation.hostilityParams != null) additionalParams.Add(factionReputation.hostilityParams);
            if (factionReputation.alliedParams != null) additionalParams.Add(factionReputation.alliedParams);

            foreach (var p in additionalParams)
            {
                if (p.scope == null || contractor.Reputations.Any(r => r.Category == p.scope.scopeName)) continue;

                var scope = new ReputationModel
                {
                    Category = p.scope.scopeName,
                    DisplayName = await _p4kService.GetLocaleValue(p.scope.displayName)
                };

                if (p.scope.standingMap != null)
                {
                    for (int standingIndex = 0; standingIndex < p.scope.standingMap.standings.Count(); standingIndex++)
                    {
                        var standing = p.scope.standingMap.standings[standingIndex];
                        if (standing == null) continue;

                        scope.StandingList.Add(new StandingModel
                        {
                            Name = standing.name,
                            DisplayName = await _p4kService.GetLocaleValue(standing.displayName),
                            Min = standing.minReputation,
                            Tier = Math.Min(standingIndex + 1, 7)
                        });
                    }

                    for (int i = scope.StandingList.Count - 2; i >= 0; --i)
                    {
                        scope.StandingList[i].Max = scope.StandingList[i + 1].Min - 1;
                    }
                    if (scope.StandingList.Count > 0)
                    {
                        scope.StandingList[^1].Max = p.scope.standingMap.reputationCeiling;
                    }
                    
                    scope.CurrentStanding = scope.StandingList.FirstOrDefault();
                }
                contractor.Reputations.Add(scope);
            }

            contractorMap.Add(contractor.Geid, contractor);
        }

        // // 3. Merge with player data
        
        // Pour joindre les données :
        // entity(extraire le geid pour avoir la faction) => scopeName pour avoir le scope => standing.name pour récupérer le standing (inutile, on se base sur la valeur de réputation et en déduit le standing)
        foreach (var playerRep in playerReputations)
        {
            var geid = ulong.Parse(playerRep.Reputation.Entity.Split(":")[^1]);
            // Match playerRep.Contractor with contractorMap
            if (!contractorMap.TryGetValue(geid, out var contractor))
            {
                // Pas de contractor qui match côté p4k, on ignore
                _logger.LogWarning("No contractor found for GEID {GEID}", geid);
                continue;
            }
        
            // Match playerRep.Category with contractor's reputations
            var reputation = contractor.Reputations.FirstOrDefault(r => r.Category.Equals(playerRep.Reputation.Scope, StringComparison.OrdinalIgnoreCase));
            if (reputation != null)
            {
                // Maintenant on récupère le standing et on l'assigne comme courant
                reputation.CurrentValue = playerRep.Reputation.Score;
                reputation.CurrentStanding = reputation.StandingList.FirstOrDefault(s => s.Min <= playerRep.Reputation.Score && playerRep.Reputation.Score <= s.Max);
            }
            else
            {
                // Pas de standing qui march, ça ne sans pas bon
                _logger.LogWarning("No scope found for scope {scope} with id {standingId}", playerRep.Reputation.Scope, playerRep.Reputation.StandingsId);
            }
        }

        foreach (var contractor in contractorMap.Values)
        {
            if (contractor.FactionStatus == FactionStatus.NotLoaded)
            {
                // On met en neutre toutes les factions avec des données mais pas de status
                if (contractor.Reputations.Any(r => r.CurrentValue.HasValue))
                {
                    contractor.FactionStatus = FactionStatus.Neutral;
                }
            }
        }

        // 4. Sort alphabetically by name
        return new ReputationResult(contractorMap.Values.OrderBy(c => c.Name).ToList(), fetchedAt, isFromCache);
    }

    /// <summary>
    /// Réputations du joueur : le dernier chargement enregistré s'il existe ; sinon, ou si <paramref name="forceRefresh"/> est vrai, lecture
    /// auprès du jeu s'il est connecté, puis enregistrement. Si le jeu ne répond pas, le cache reste la solution de repli. Sans cache,
    /// l'appel au jeu est tenté et son erreur remonte, comme avant la mise en cache.
    /// </summary>
    private async Task<(List<VersionedReputation> Reputations, DateTimeOffset FetchedAt, bool IsFromCache)> GetPlayerReputationsAsync(bool forceRefresh)
    {
        if (!forceRefresh && await LoadCachedPlayerReputationsAsync().ConfigureAwait(false) is { } cachedFirst)
        {
            return cachedFirst;
        }

        Exception? failure = null;
        if (_grpcClientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame)
        {
            try
            {
                return await FetchPlayerReputationsAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Impossible de lire les réputations auprès du jeu, recherche dans le cache");
                failure = ex;
            }
        }

        // Sans actualisation forcée, le cache a déjà été consulté : il est vide.
        if (forceRefresh && await LoadCachedPlayerReputationsAsync().ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return await FetchPlayerReputationsAsync().ConfigureAwait(false);
    }

    private async Task<(List<VersionedReputation> Reputations, DateTimeOffset FetchedAt, bool IsFromCache)?> LoadCachedPlayerReputationsAsync()
    {
        var cached = await _dataCache.LoadAsync(DatasetKey).ConfigureAwait(false);
        if (cached == null)
        {
            return null;
        }

        try
        {
            return (GrpcCachePayload.ReadMessages(cached.Data, VersionedReputation.Parser), cached.FetchedAt, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réputations illisibles dans le cache");
            return null;
        }
    }

    private async Task<(List<VersionedReputation> Reputations, DateTimeOffset FetchedAt, bool IsFromCache)> FetchPlayerReputationsAsync()
    {
        var reputations = await _grpcClientService.QueryReputationsAsync().ConfigureAwait(false);
        var fetchedAt = DateTimeOffset.Now;
        await _dataCache.SaveAsync(DatasetKey, GrpcCachePayload.WriteMessages(reputations), fetchedAt).ConfigureAwait(false);
        return (reputations, fetchedAt, false);
    }

    private static string TransformRecordName(string typeName)
    {
        return typeName.Split('.', 2)[^1].Split('_', 2)[^1];
    }
}

public class StandingModel
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
    /// <summary>
    /// Palier (1-7) correspondant au design system §17.4. Index 0 → T1, ..., 6+ → T7.
    /// </summary>
    public int Tier { get; set; }
}
