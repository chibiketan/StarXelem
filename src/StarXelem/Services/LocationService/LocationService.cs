using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public class LocationService : ILocationService
{
    public Task<string?> ResolveEntityLocation(string? entityLocation)
    {
        if (string.IsNullOrWhiteSpace(entityLocation))
        {
            return Task.FromResult<string?>(null);
        }
        var split = entityLocation.Split(":", 3, StringSplitOptions.None);

        var type = ELocationType.UNKNOWN;
        Enum.TryParse<ELocationType>(split[1], true, out type);

        if (type == ELocationType.UNKNOWN)
        {
            return Task.FromResult<string?>(entityLocation);
        }

        if (type == ELocationType.PlayerInventory)
        {
            return Task.FromResult<string?>("Porté");       
        }

        var id = ulong.Parse(split[2]);

        if (type == ELocationType.Location)
        {
            var loc = (ELocation)id;
            
            return Task.FromResult(loc.ToString());
        }
        
        return Task.FromResult(entityLocation);
    }

    public Task<string?> ResolveLocation(EntityItemQueryResult entity)
    {
        return ResolveEntityLocation(entity.EntityEdge?.End.InventoryId ?? entity.EntityNodeProperties?.StowCtx.Inv);
    }

    enum ELocation:ulong
    {
        UNKNOWN = 0,
        AREA_18 = 2273540638,
        BAIJINI = 3490636373,
        EVERUS_HARBOR = 308639451,
        LORVILLE = 4005457614,
        ARC_L2 = 4129327548,
        STARLIGHT = 844538234,
        PORT_TRESLER = 2147648880,
        PYRO_GATEWAY = 1547955914,
        SERAPHIM = 1752411604,
        ONYX_S3B1 = 2302725662,
        ORBITUARY = 848868938,
        CHECKMATE = 810966700,
        STANTON_GATEWAY = 639835600,
        ROD_S_FUEL_N_SUPPLIES = 2421159735,
        ENDGAME = 4067808819,
        GASLIGHT = 3531251586,
        DUDLEY_AND_DAUGHTERS = 1309454298,
        RAT_S_NEST = 660982239
    }
    
    enum ELocationType
    {
        UNKNOWN = 0,
        Location,
        Container,
        PersonalEntityInventory,
        PlayerInventory,
        Hangar
    }
}