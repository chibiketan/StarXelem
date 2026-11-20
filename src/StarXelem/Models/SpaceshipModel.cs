using System.ComponentModel.DataAnnotations;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;
using StarXelem.Extensions;

namespace StarXelem.Models;

public class SpaceshipModel
{
    /// <summary>
    /// Nom donné par le joueur, ou chaîne vide. Le serveur le transmet dans <c>EntityConfiguration.Params[].CustomName</c> ;
    /// <c>Metadata.Name</c> ne contient plus que le nom technique, mais reste lu en repli pour les anciens droits.
    /// </summary>
    public string Name => GetCustomName();
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

    /// <summary>Nom court de la classe : <see cref="ClassRecordName"/> est au format « Type.NomCourt ».</summary>
    public string? TechnicalName => ClassRecordName?[(ClassRecordName.IndexOf('.') + 1)..];

    /// <summary>Vrai si le joueur a donné un nom à ce vaisseau.</summary>
    public bool HasCustomName => Name.Length > 0;

    /// <summary>Présentation de l'emplacement dans l'écran Mon hangar, ou <c>null</c> si l'emplacement est inconnu.</summary>
    public ItemLocationDisplay? LocationDisplay { get; set; }

    public Entitlement Entitlement { get; }
    public EntityItemQueryResult? EntityProperties { get; set; }
    public EntityStowContext? StowContext { get; set; }

    /// <summary>
    /// Objets montés sur ce vaisseau, à toute profondeur (arêtes ATTACHED_TO depuis <see cref="EntityProperties"/>), sans les véhicules
    /// embarqués. Vide si le vaisseau n'a pas été retrouvé dans l'univers (détruit, non demandé, inventaire inaccessible).
    /// </summary>
    public IReadOnlyList<EntityItemQueryResult> AttachedEntities { get; set; } = [];

    /// <summary>Vrai si le loadout réel du vaisseau est connu.</summary>
    public bool HasRealLoadout => EntityProperties != null;

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

    private string GetCustomName()
    {
        var customName = Entitlement.EntityConfiguration?.Params
            .Where(parameter => parameter.HasCustomName)
            .Select(parameter => parameter.CustomName.Trim())
            .FirstOrDefault(name => name.Length > 0);

        if (customName != null)
        {
            return customName;
        }

        var metadataName = Entitlement.Metadata?.Name ?? string.Empty;
        return IsNameValid(metadataName) ? metadataName : string.Empty;
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

        if (entitlementName.Equals(ClassRecordName, StringComparison.CurrentCultureIgnoreCase)
            || entitlementName.Equals(TechnicalName, StringComparison.CurrentCultureIgnoreCase))
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