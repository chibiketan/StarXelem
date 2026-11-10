using System.ComponentModel.DataAnnotations;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;
using StarXelem.Extensions;

namespace StarXelem.Models;

public class SpaceshipModel
{
    public string Name => !IsNameValid(Entitlement.Metadata.Name) ? "" : Entitlement.Metadata.Name;
    public string? PackageSource => Entitlement.Metadata.SourceSku;
    public string Shipname { get; set; }
    public bool IsRealPurchase => Entitlement.Metadata.RealMoney;
    public SpaceshipState State => GetState();
    public string? DisplayState => GetState().GetDisplayName();
    public string? Location => EntityProperties?.EntityEdge?.End?.InventoryId;
    public ResolvedLocation? ResolvedLocation { get; set; }
    public bool IsPorte     => ResolvedLocation?.Kind == LocationKind.Worn;
    public bool IsStowed    => State == SpaceshipState.STOWED;
    public bool IsUnstowed  => State == SpaceshipState.UNSTOWED;
    public bool IsUnclaimed => State == SpaceshipState.UNCLAIMED;
    public bool IsDestroyed => State == SpaceshipState.DESTROYED;
    public bool IsEmptyLocation => ResolvedLocation == null;
    public string? LocationName => ResolvedLocation?.Name;
    public bool IsLocationTypeLocation => ResolvedLocation?.Kind == LocationKind.Location;
    public bool IsLocationTypeHangar   => ResolvedLocation?.Kind == LocationKind.Hangar;
    public bool IsLocationTypeId       => ResolvedLocation?.Kind == LocationKind.Item;
    public bool IsLocationTypeOther    => ResolvedLocation?.Kind == LocationKind.Other;
    public string? Shard => StowContext?.ShardId;
    public string? ClassRecordName { get; set; }

    public Entitlement Entitlement { get; }
    public EntityItemQueryResult? EntityProperties { get; set; }
    public EntityStowContext? StowContext { get; set; }

    public SpaceshipModel(Entitlement entitlement)
    {
        Entitlement = entitlement;
        Shipname = "";
    }

    private SpaceshipState GetState()
    {
        return Entitlement.Status switch
        {
            EntitlementStatus.Undelivered => SpaceshipState.UNCLAIMED,
            EntitlementStatus.Delivered when null == StowContext => SpaceshipState.DESTROYED,
            EntitlementStatus.Delivered when StowContext.IsStowed => SpaceshipState.STOWED,
            EntitlementStatus.Delivered => SpaceshipState.UNSTOWED,
            EntitlementStatus.Packaged => SpaceshipState.UNCLAIMED,
            _ => SpaceshipState.UNKNOWN
        };
    }

    private bool IsNameValid(string entitlementName)
    {
        if (String.IsNullOrWhiteSpace(entitlementName))
        {
            return false;
        }

        if (entitlementName.Equals("Insurance grant", StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (entitlementName.Equals(Entitlement.Metadata.SourceSku, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (entitlementName.Equals(ClassRecordName, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        return true;
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