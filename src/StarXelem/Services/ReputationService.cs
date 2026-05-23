using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services;

public class ReputationService : IReputationService
{
    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ILogger<ReputationService> _logger;

    public ReputationService(IP4kService p4kService, IGrpcClientService grpcClientService, ILogger<ReputationService> logger)
    {
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
        _logger = logger;
    }

    public async Task<List<ContractorModel>> GetSynchronizedReputationsAsync()
    {
        // TODO ne plus partir des faction mais partir carrément des FactionReputation !
        // 1. Retrieve static data from P4K
        var reputationContextRecordList = await _p4kService.GetAllFactions().ConfigureAwait(false);

        // 2. Retrieve player data from gRPC
        var playerReputations = await _grpcClientService.QueryReputationsAsync().ConfigureAwait(false);

        var contractorMap = new Dictionary<ulong, ContractorModel>();

        foreach (var dataCoreTypedRecord in reputationContextRecordList)
        {
            if (dataCoreTypedRecord.Data is not Faction faction)
            {
                // pas de données, pourquoi ?
                _logger.LogWarning("No faction found for record {RecordId}", dataCoreTypedRecord.RecordId);
                continue;
            }

            if (faction.factionReputationRef?.reputationContextPropertiesUI is null)
            {
                // pas une faction avec réputation affichée in-game on dirait
                _logger.LogInformation("No reputation context found for faction {RecordId}", dataCoreTypedRecord.RecordId);
                continue;
            }

            if (faction.factionReputationRef.hideInDelpihApp)
            {
                // Caché d'après la configuration
                continue;
            }

            var contractor = new ContractorModel
            {
                Id = dataCoreTypedRecord.RecordId,
                Name = await _p4kService.GetLocaleValue(faction.factionReputationRef.displayName),
                Geid = faction.factionReputationRef.GEID
            };

            foreach (var scopeContext in faction.factionReputationRef.reputationContextPropertiesUI.scopeContextList)
            {
                if (scopeContext.scope is null)
                {
                    _logger.LogWarning("Null scope found for faction {RecordId}", dataCoreTypedRecord.RecordId);
                    continue;
                }
                
                var scope = new ReputationModel();

                scope.Category = scopeContext.scope.scopeName;
                scope.DisplayName = await _p4kService.GetLocaleValue(scopeContext.scope.scopeName);

                contractor.Reputations.Add(scope);

                foreach (var standing in scopeContext.scope.standingMap.standings)
                {
                    if (standing is null)
                    {
                        _logger.LogWarning("Null standing found for faction {RecordId}", dataCoreTypedRecord.RecordId);
                        continue;
                    }
                    var standingModel = new StandingModel
                    {
                        Name = standing.name,
                        DisplayName = await _p4kService.GetLocaleValue(standing.displayName),
                        Min = standing.minReputation
                    };
                    
                    scope.StandingList.Add(standingModel);
                }

                // On défini la valeur max comme le min - 1 du prochain standing
                for (int i = scope.StandingList.Count - 2; i >= 0; --i)
                {
                    scope.StandingList[i].Max = scope.StandingList[i+1].Min - 1;
                }
                
                scope.StandingList[^1].Max = scopeContext.scope.standingMap.reputationCeiling;
                // Le Standing par défaut sera le premier standing du scope
                scope.CurrentStanding = scope.StandingList.FirstOrDefault();
            }
            
            // On récupère le scope de status
            if (faction.factionReputationRef.reputationContextPropertiesUI.primaryScopeContext is not null)
            {
                var scope = faction.factionReputationRef.reputationContextPropertiesUI.primaryScopeContext;
                
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
        return contractorMap.Values.OrderBy(c => c.Name).ToList();
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
}
