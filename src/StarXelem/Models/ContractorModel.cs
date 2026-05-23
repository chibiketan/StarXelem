using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using StarBreaker.Common;

namespace StarXelem.Models;

public class ContractorModel
{
    public string Name { get; set; } = string.Empty;
    public FactionStatus FactionStatus { get; set; }
    public List<ReputationModel> Reputations { get; set; } = new();
    public CigGuid Id { get; set; }
    public ulong Geid { get; set; }
    
    public string StateClasses => $"{RelationshipClass}";

    private string RelationshipClass => FactionStatus switch
    {
        FactionStatus.Friendly    => "ally",
        FactionStatus.Hostile     => "hostile",
        FactionStatus.Neutral     => "neutral",
        _                         => "notloaded"
    };
}

public enum FactionStatus
{
    [Display(Name = "Non chargé")]
    NotLoaded,
    [Display(Name = "Allié")]
    Friendly,
    [Display(Name = "Neutre")]
    Neutral,
    [Display(Name = "Hostile")]
    Hostile
}
