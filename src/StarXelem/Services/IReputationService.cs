using StarXelem.Models;

namespace StarXelem.Services;

public interface IReputationService
{
    Task<List<ContractorModel>> GetSynchronizedReputationsAsync();
}
