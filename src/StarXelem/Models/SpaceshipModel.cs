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
    public string? Location => EntityProperties?.EntityNodeProperties?.StowCtx?.Inv;
    public string? ReadableLocation { get; set; }
    public string? Shard => EntityProperties?.EntityNodeProperties?.StowCtx?.Shd;
    public EItemType ItemType => (EItemType)(EntityProperties?.EntityNodeProperties?.ItemTypeEnum ?? -1);
    public EItemSubType ItemSubType => (EItemSubType)(EntityProperties?.EntityNodeProperties?.ItemSubTypeEnum ?? -1);

    public Entitlement Entitlement { get; }
    public EntityClassDefinition? EntityClassDefinition { get; set; }
    public EntityItemQueryResult? EntityProperties { get; set; }

    public SpaceshipModel(Entitlement entitlement)
    {
        Entitlement = entitlement;
    }

    private SpaceshipState GetState()
    {
        if (Entitlement.Status == EntitlementStatus.Unclaimed)
            return SpaceshipState.UNCLAIMED;
        if (Entitlement.Status == EntitlementStatus.Fulfilled)
        {
            if (null == EntityProperties)
            {
                // Pas d'entité, le vaisseau est détruit
                return SpaceshipState.DESTROYED;
            }
            
            // TODO Comment détecter un vaisseau en unknown ? (F7A)
            if (EntityProperties.EntityNodeProperties?.StowCtx.Shd != "")
            {
                // Le vaisseau est associé à une shard, du coup il est dans la nature
                return SpaceshipState.UNSTOWED;
            }
            
            return SpaceshipState.STOWED;
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