using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services;

public class ReputationService : IReputationService
{
    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;

    public ReputationService(IP4kService p4kService, IGrpcClientService grpcClientService)
    {
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
    }

    public async Task<List<ContractorModel>> GetSynchronizedReputationsAsync()
    {
        // 1. Retrieve static data from P4K
        var contractGenerators = await _p4kService.GetAllContractGenerator().ConfigureAwait(false);

        // 2. Retrieve player data from gRPC
        var playerReputations = await _grpcClientService.QueryReputationsAsync().ConfigureAwait(false);

        var contractorMap = new Dictionary<string, ContractorModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var genRecord in contractGenerators)
        {
            var record = await _p4kService.GetRecordWithSpecificDepth(genRecord.RecordId, 3).ConfigureAwait(false);
            if (record?.Data is not ContractGenerator contractGenerator)
                continue;

            foreach (var generatorBase in contractGenerator.generators)
            {
                if (generatorBase == null || generatorBase.notForRelease || generatorBase.workInProgress)
                    continue;

                // The name of the contractor is usually in the generator's context or defined by the generator type
                // Based on MissionMappingService, we can use the name of the contractor associated with this generator
                string contractorName = generatorBase.contractorName ?? "Unknown Contractor";

                if (!contractorMap.TryGetValue(contractorName, out var contractor))
                {
                    contractor = new ContractorModel { Name = contractorName };
                    contractorMap[contractorName] = contractor;
                }

                // Each generatorBase can have multiple reputation categories (tiers)
                // We need to find what categories this contractor offers.
                // In the game data, this is often linked to the reputation categories defined for that contractor.
                if (generatorBase.reputationCategories != null)
                {
                    foreach (var category in generatorBase.reputationCategories)
                    {
                        if (string.IsNullOrEmpty(category)) continue;

                        // Avoid duplicates
                        if (contractor.Reputations.Any(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        contractor.Reputations.Add(new ReputationModel
                        {
                            Category = category,
                            CurrentValue = 0,
                            TierName = "Neutral" // Default
                        });
                    }
                }
            }
        }

        // 3. Merge with player data
        foreach (var playerRep in playerReputations)
        {
            // Match playerRep.Contractor with contractorMap
            var contractor = contractorMap.Values.FirstOrDefault(c => c.Name.Equals(playerRep.Contractor, StringComparison.OrdinalIgnoreCase));
            if (contractor == null) continue;

            // Match playerRep.Category with contractor's reputations
            var reputation = contractor.Reputations.FirstOrDefault(r => r.Category.Equals(playerRep.Category, StringComparison.OrdinalIgnoreCase));
            if (reputation != null)
            {
                reputation.CurrentValue = playerRep.Value;
                reputation.TierName = playerRep.Tier;
            }
        }

        // 4. Sort alphabetically by name
        return contractorMap.Values.OrderBy(c => c.Name).ToList();
    }
}
