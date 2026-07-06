using System.Collections.Concurrent;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.Services.LocationService;

/// <summary>
/// Implémentation de production de <see cref="ILocationService"/>.
/// Interroge le graphe d'entités via gRPC et les données P4K pour résoudre
/// les identifiants bruts en noms d'emplacements localisés.
/// Les résultats sont mis en cache pour éviter les requêtes redondantes au sein d'une session.
/// </summary>
public class LocationService : ILocationService
{
    private readonly IGrpcClientService _grpcClientService;
    private readonly IP4kService _p4KService;
    private readonly ILocationRepository _locationRepository;

    /// <summary>
    /// Initialise une nouvelle instance de <see cref="LocationService"/>.
    /// </summary>
    /// <param name="grpcClientService">Service de communication gRPC avec le backend Star Citizen.</param>
    /// <param name="p4KService">Service d'accès aux données P4K (types d'entités et chaînes localisées).</param>
    /// <param name="locationRepository">Repository d'accès aux emplacements persistés en base de données.</param>
    public LocationService(IGrpcClientService grpcClientService, IP4kService p4KService, ILocationRepository locationRepository)
    {
        _grpcClientService = grpcClientService;
        _p4KService = p4KService;
        _locationRepository = locationRepository;
    }
    
    /// <inheritdoc/>
    /// <remarks>
    /// La chaîne brute est découpée en trois parties (<c>entityId:type:id</c>).
    /// Le segment <c>type</c> détermine la stratégie de résolution :
    /// <list type="bullet">
    ///   <item><term>UNKNOWN</term><description>Retourne la chaîne brute telle quelle.</description></item>
    ///   <item><term>PlayerInventory</term><description>Retourne <c>"Porté"</c> directement.</description></item>
    ///   <item><term>Location / Hangar</term><description>Résout via CRC hash → <c>StarMapObject.name</c> → locale P4K.</description></item>
    ///   <item><term>Container</term><description>Interroge le graphe gRPC, récupère le type via P4K, puis localise
    ///   ou remonte l'arbre de possession si l'entité est un placeholder ou d'un type non autorisé.</description></item>
    /// </list>
    /// </remarks>
    public async Task<string?> ResolveEntityLocation(string? entityLocation, IList<EItemType>? allowedTypes = null)
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

        if (type == ELocationType.Location || type == ELocationType.Hangar || type == ELocationType.Mission)
        {
            return await ResolveLocationId((uint)id, type);
        }

        if (type == ELocationType.Container)
        {
            // TODO remove
            //return $"Entité non trouvée ({split[0]})";
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

            if (allowedTypes is { Count: > 0 })
            {
                var itemType = (EItemType)results[0].EntityNodeProperties!.ItemTypeEnum;
                if (!allowedTypes.Contains(itemType))
                {
                    return await ResolveLocation(results[0], allowedTypes);
                }
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
                    return await ResolveLocation(results[0], allowedTypes);
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
    
    /// <inheritdoc/>
    /// <remarks>
    /// Suit les arêtes du graphe d'entités :
    /// <list type="bullet">
    ///   <item><term>AttachedTo</term><description>Résout récursivement l'entité parente via son <c>EntityId</c>.</description></item>
    ///   <item><term>StowedIn</term><description>Résout récursivement le conteneur via son <c>InventoryId</c>.</description></item>
    ///   <item><term>References</term><description>Non implémenté, retourne un message indicatif.</description></item>
    /// </list>
    /// </remarks>
    public Task<string?> ResolveLocation(EntityItemQueryResult entity, IList<EItemType>? allowedTypes = null)
    {
        if (entity.EntityEdge != null)
        {
            // On a un edge à traiter, on va chercher la destination
            switch (entity.EntityEdge.Type)
            {
                case EntityEdgeType.AttachedTo:
                    // Attaché à une autre entité
                    //return Task.FromResult("[EDGE] attached to traiter")!;
                    if (entity.EntityEdge.End.HasEntityId)
                    {
                        return ResolveEntityLocation(entity.EntityEdge.End.EntityId, allowedTypes);
                    }

                    if (entity.EntityEdge.End.HasInventoryId)
                    {
                        return Task.FromResult($"[ATTACHED_TO][INVENTORY] {entity.EntityEdge.End.InventoryId}");
                    }

                    if (entity.EntityEdge.End.HasShardId)
                    {
                        return Task.FromResult($"[SHARD] {entity.EntityEdge.End.ShardId}");
                    }
                    break;
                case EntityEdgeType.StowedIn:
                    // Rangé dans un conteneur
                    return this.ResolveEntityLocation(entity.EntityEdge.End.InventoryId, allowedTypes);
                case EntityEdgeType.References:
                    // Lié à une référence ?
                    return Task.FromResult("[EDGE] Référence à traiter")!;
            }
        }

        // TODO SC 4.5 : par quoi le remplacer ?
        // if (!String.IsNullOrEmpty(entity.EntityNodeProperties?.StowCtx?.Inv))
        // {
        //     return ResolveEntityLocation(entity.EntityNodeProperties.StowCtx.Inv);
        // }
        
        return Task.FromResult("pas de données")!;
    }

    /// <summary>
    /// Résout la localisation d'une entité à partir de son identifiant numérique (GUID).
    /// Le résultat est mis en cache dans <see cref="_entityCache"/> pour éviter les requêtes répétées.
    /// </summary>
    /// <param name="guid">Identifiant unique de l'entité (GUID numérique).</param>
    /// <param name="allowedTypes">
    /// Liste optionnelle des types d'items acceptés comme emplacement final.
    /// Si le type de l'entité ne figure pas dans cette liste, la résolution remonte la chaîne de possession.
    /// </param>
    /// <returns>Le nom localisé de l'emplacement, ou un message d'erreur si l'entité est introuvable.</returns>
    public async Task<String?> ResolveEntityLocation(ulong guid, IList<EItemType>? allowedTypes = null)
    {
        // TODO remove
        var results = await _entityCache.GetOrAdd(guid, entityId =>
        {
            var queryProp = new ItemQueryModel();
            queryProp.Id = entityId.ToString();
            queryProp.useConnectedUserOwner = false;
            queryProp.UseProjection = false;
            return _grpcClientService.QueryGraphBySearch(queryProp);
        });
        // var queryProp = new ItemQueryModel();
        // queryProp.Id = guid.ToString();
        // queryProp.useConnectedUserOwner = false;
        // queryProp.UseProjection = false;
        // var results = await _grpcClientService.QueryGraphBySearch(queryProp);

        if (results.Count == 0)
        {
            return $"Entité non trouvée ({guid})";
        }

        if (allowedTypes is { Count: > 0 })
        {
            var itemType = (EItemType)results[0].EntityNodeProperties!.ItemTypeEnum;
            if (!allowedTypes.Contains(itemType))
            {
                return await ResolveLocation(results[0], allowedTypes);
            }
        }

        var entityType = await _p4KService.GetEntityType(results[0].EntityNodeProperties!.ClassGuidCrc);
        var typeName = entityType!.RecordName;
        var c = (entityType?.Data as EntityClassDefinition)?.Components
            .OfType<SAttachableComponentParams>().FirstOrDefault();

        if (null != c)
        {
            if (c.AttachDef.Localization.Name == "@LOC_PLACEHOLDER")
            {
                // C'est un placeholder, on va chercher son possesseur
                return await ResolveLocation(results[0], allowedTypes);
            }

            var tmpeName = await _p4KService.GetLocaleValue(c.AttachDef.Localization.Name);

            if (!string.IsNullOrEmpty(tmpeName))
            {
                typeName = tmpeName;
            }
        }

        return $"[{guid}] {typeName}";
    }

    /// <summary>
    /// Résout un emplacement de type <c>Location</c> ou <c>Hangar</c> à partir de son identifiant CRC.
    /// Le CRC correspond au hash du type d'entité dans les données P4K.
    /// Le résultat est mis en cache dans <see cref="_locationCache"/>.
    /// </summary>
    /// <param name="id">Hash CRC de l'emplacement.</param>
    /// <param name="type">Type de localisation (<c>Location</c> ou <c>Hangar</c>), utilisé comme préfixe dans le nom retourné.</param>
    /// <returns>Le nom localisé préfixé du type, ou un libellé de fallback si la résolution échoue.</returns>
    private async Task<string> ResolveLocationId(uint id, ELocationType type)
    {
        var locationName = await _locationCache.GetOrAdd(id, async (crc) =>
        {
            var location = await _locationRepository.GetByCrcAsync(crc);
            if (location != null)
            {
                return location.NameLocalized;
            }

            var tmp = await _p4KService.GetEntityType(crc);

            if (null == tmp)
            {
                return null;
            }
            var localKey = ((StarMapObject)tmp.Data).name;
            var resolvedName = await _p4KService.GetLocaleValue(localKey);

            return resolvedName;
        });

        if (String.IsNullOrEmpty(locationName))
        {
            return $"[{type.ToString().ToUpperInvariant()}] INCONNU ({id})";
        }

        return $"[{type.ToString().ToUpperInvariant()}] {locationName}";        
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        _entityCache.Clear();
        _locationCache.Clear();
    }
    
    private readonly ConcurrentDictionary<ulong, Task<IList<EntityItemQueryResult>>> _entityCache = new();

    private readonly ConcurrentDictionary<uint, Task<string?>> _locationCache = new();
    
    enum ELocationType
    {
        UNKNOWN = 0,
        Location,
        Container,
        PersonalEntityInventory,
        PlayerInventory,
        Hangar,
        Mission
    }
}