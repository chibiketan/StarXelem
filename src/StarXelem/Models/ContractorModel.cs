using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using StarBreaker.Common;
using StarXelem.Extensions;

namespace StarXelem.Models;

public class ContractorModel
{
    public string Name { get; set; } = string.Empty;
    public FactionStatus FactionStatus { get; set; }
    public List<ReputationModel> Reputations { get; set; } = new();
    public CigGuid Id { get; set; }
    public ulong Geid { get; set; }
    
    public string StateClasses => $"{RelationshipClass}";

    /// <summary>Libellé de la relation avec la faction.</summary>
    public string FactionStatusLabel => FactionStatus.GetDisplayName() ?? FactionStatus.ToString();

    public bool IsFriendly => FactionStatus == FactionStatus.Friendly;
    public bool IsHostile => FactionStatus == FactionStatus.Hostile;

    /// <summary>Opacité de la carte : une faction dont la relation n'est pas chargée reste en retrait.</summary>
    public double CardOpacity => FactionStatus == FactionStatus.NotLoaded ? 0.7 : 1;

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
