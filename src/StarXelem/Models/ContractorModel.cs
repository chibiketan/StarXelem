using System.Collections.Generic;

namespace StarXelem.Models;

public class ContractorModel
{
    public string Name { get; set; } = string.Empty;
    public List<ReputationModel> Reputations { get; set; } = new();
}
