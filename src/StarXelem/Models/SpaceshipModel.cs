using System.ComponentModel.DataAnnotations;
using Sc.External.Services.Entitlement.V1;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Extensions;

namespace StarXelem.Models;

public class SpaceshipModel
{
    public string Name => (Entitlement.Name == Entitlement.SourceSku ? "" : Entitlement.Name);
    public string Ship => (EntityClassDefinition?.Components.FirstOrDefault(c => c is VehicleComponentParams) as VehicleComponentParams)?.vehicleName ?? Entitlement.EntityClassGuid;
    public string? PackageSource => Entitlement.SourceSku;
    public string Shipname { get; set; }
    public bool IsRealPurchase => Entitlement.RealMoney;
    public SpaceshipState State => GetState();
    public string? DisplayState => GetState().GetDisplayName();
    public string? Location => EntityProperties?.EntityEdge?.End?.InventoryId;
    public string? ReadableLocation { get; set; }
    public bool IsPorte     => ReadableLocation == "Porté";
    public bool IsStowed    => State == SpaceshipState.STOWED;
    public bool IsUnstowed  => State == SpaceshipState.UNSTOWED;
    public bool IsUnclaimed => State == SpaceshipState.UNCLAIMED;
    public bool IsDestroyed => State == SpaceshipState.DESTROYED;
    public bool IsEmptyLocation => string.IsNullOrEmpty(ReadableLocation);
    public bool IsStandardLocation => !IsPorte && !IsEmptyLocation;
    public string? LocationPrefix => IsStandardLocation ? ExtractLocationPrefix(ReadableLocation!) : null;
    public string? LocationName  => IsStandardLocation ? ExtractLocationName(ReadableLocation!)  : null;
    public string? Shard => StowContext?.ShardId;
    public EItemType ItemType => (EItemType)(EntityProperties?.EntityNodeProperties?.ItemTypeEnum ?? -1);
    public EItemSubType ItemSubType => (EItemSubType)(EntityProperties?.EntityNodeProperties?.ItemSubTypeEnum ?? -1);

    public Entitlement Entitlement { get; }
    public EntityClassDefinition? EntityClassDefinition { get; set; }
    public EntityItemQueryResult? EntityProperties { get; set; }
    public EntityStowContext? StowContext { get; set; }

    public SpaceshipModel(Entitlement entitlement)
    {
        Entitlement = entitlement;
        Shipname = "";
    }

    private static string ExtractLocationPrefix(string location)
    {
        if (!location.StartsWith('[')) return string.Empty;
        var end = location.IndexOf(']');
        return end < 0 ? string.Empty : location[..(end + 1)];
    }

    private static string ExtractLocationName(string location)
    {
        if (!location.StartsWith('[')) return location;
        var end = location.IndexOf(']');
        return end < 0 ? location : location[(end + 1)..].TrimStart();
    }

    private SpaceshipState GetState()
    {
        if (Entitlement.Status == EntitlementStatus.Unclaimed)
            return SpaceshipState.UNCLAIMED;
        if (Entitlement.Status == EntitlementStatus.Fulfilled)
        {
            if (null == StowContext)
            {
                // Pas d'entité, le vaisseau est détruit
                return SpaceshipState.DESTROYED;
            }

            if (StowContext.IsStowed)
            {
                return SpaceshipState.STOWED;
            }
            
            return SpaceshipState.UNSTOWED;
        }
        
        return SpaceshipState.UNKNOWN;
    }
}

public enum SpaceshipState
{
    [Display(Name = "Inconnu")]
    UNKNOWN = 0,
    [Display(Name = "Non demandé")]
    UNCLAIMED,
    [Display(Name = "Rangé")]
    STOWED,
    [Display(Name = "Dans la nature")]
    UNSTOWED,
    [Display(Name = "Détruit")]
    DESTROYED
}