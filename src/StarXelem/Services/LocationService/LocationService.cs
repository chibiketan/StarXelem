using Sc.External.Services.Entitygraph.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

public class LocationService : ILocationService
{
    private readonly IGrpcClientService _grpcClientService;
    private readonly IP4kService _p4KService;

    public LocationService(IGrpcClientService grpcClientService, IP4kService p4KService)
    {
        _grpcClientService = grpcClientService;
        _p4KService = p4KService;
    }
    
    public async Task<string?> ResolveEntityLocation(string? entityLocation)
    {
        if (string.IsNullOrWhiteSpace(entityLocation))
        {
            return null;
        }
        var split = entityLocation.Split(":", 3, StringSplitOptions.None);

        var type = ELocationType.UNKNOWN;
        Enum.TryParse<ELocationType>(split[1], true, out type);

        if (type == ELocationType.UNKNOWN)
        {
            return entityLocation;
        }

        if (type == ELocationType.PlayerInventory)
        {
            return "Porté";       
        }

        var id = ulong.Parse(split[2]);

        if (type == ELocationType.Location)
        {
            var loc = (ELocation)id;
            
            return $"[INVENTAIRE] {loc}";
        }

        if (type == ELocationType.Hangar)
        {
            var loc = (ELocation)id;
            
            return $"[HANGAR] {loc}";
        }

        if (type == ELocationType.Container)
        {
            var containerId = ulong.Parse(split[0]);

            var queryProp = new ItemQueryModel();
            queryProp.Id = split[0];
            queryProp.useConnectedUserOwner = false;
            queryProp.UseProjection = false;
            var results = await _grpcClientService.QueryGraphBySearch(queryProp);

            if (results.Count == 0)
            {
                return $"Entité non trouvée ({split[0]})";
            }

            var entityType = await _p4KService.GetEntityType(results[0].EntityNodeProperties.ClassGuidCrc);
            var typeName = entityType.RecordName;
            var c = (entityType?.Data as EntityClassDefinition)?.Components
                .OfType<SAttachableComponentParams>().FirstOrDefault();

            if (null != c)
            {
                if (c.AttachDef.Localization.Name == "@LOC_PLACEHOLDER")
                {
                    // C'est un placeholder, on va chercher son possesseur
                    return await ResolveLocation(results[0]);
                }
                
                var tmpeName = await _p4KService.GetLocaleValue(c.AttachDef.Localization.Name);

                if (!string.IsNullOrEmpty(tmpeName))
                {
                    typeName = tmpeName;
                }
            }
            
            return $"[{split[0]}] {typeName}";
        }
        
        return entityLocation;
    }

    public Task<string?> ResolveLocation(EntityItemQueryResult entity)
    {
        if (entity.EntityEdge != null)
        {
            // On a un edge à traiter, on va chercher la destination
            switch (entity.EntityEdge.Type)
            {
                case EntityEdgeType.AttachedTo:
                    // Attaché à une autre entité
                    //return Task.FromResult("[EDGE] attached to traiter")!;
                return ResolveEntityLocation(entity.EntityEdge.End.EntityId);
                case EntityEdgeType.StowedIn:
                    // Rangé dans un conteneur
                    return this.ResolveEntityLocation(entity.EntityEdge.End.InventoryId);
                case EntityEdgeType.References:
                    // Lié à une référence ?
                    return Task.FromResult("[EDGE] Référence à traiter")!;
            }
        }

        if (!String.IsNullOrEmpty(entity.EntityNodeProperties?.StowCtx?.Inv))
        {
            ResolveEntityLocation(entity.EntityNodeProperties.StowCtx.Inv);
        }
        
        return Task.FromResult("pas de données")!;
    }

    public async Task<String?> ResolveEntityLocation(ulong guid)
    {
        var queryProp = new ItemQueryModel();
        queryProp.Id = guid.ToString();
        queryProp.useConnectedUserOwner = false;
        queryProp.UseProjection = false;
        var results = await _grpcClientService.QueryGraphBySearch(queryProp);

        if (results.Count == 0)
        {
            return $"Entité non trouvée ({guid})";
        }

        var entityType = await _p4KService.GetEntityType(results[0].EntityNodeProperties.ClassGuidCrc);
        var typeName = entityType.RecordName;
        var c = (entityType?.Data as EntityClassDefinition)?.Components
            .OfType<SAttachableComponentParams>().FirstOrDefault();

        if (null != c)
        {
            if (c.AttachDef.Localization.Name == "@LOC_PLACEHOLDER")
            {
                // C'est un placeholder, on va chercher son possesseur
                return await ResolveLocation(results[0]);
            }
                
            var tmpeName = await _p4KService.GetLocaleValue(c.AttachDef.Localization.Name);

            if (!string.IsNullOrEmpty(tmpeName))
            {
                typeName = tmpeName;
            }
        }
            
        return $"[{guid}] {typeName}";
    }

    enum ELocation:ulong
    {
        UNKNOWN = 0,
        AREA_18 = 2273540638,
        BAIJINI = 3490636373,
        EVERUS_HARBOR = 308639451,
        LORVILLE = 4005457614,
        NEW_BABBAGE = 3170699229,
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
        RAT_S_NEST = 660982239,
        RUIN_STATION = 2026442305,
        CRU_L1 = 1510935576
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