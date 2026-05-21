using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Sc.External.Common.Api.V1;
using Sc.External.Common.Shard.V1;
using Sc.External.Services.BlueprintLibrary.V1;
using Sc.External.Services.Contacts.V1;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Identity.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using DateTime = System.DateTime;
using EntityFilter = Sc.External.Services.Entitygraph.V1.EntityFilter;
using PropertyFilter = Sc.External.Common.Api.V1.PropertyFilter;

namespace StarXelem.Services;

public class GrpcClientService : IGrpcClientService
{
    private static readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    private readonly ILogger<GrpcClientService> _logger;
    private StarCitizenClientWatcher? _watcher;
    private GetCurrentPlayerResponse? _playerInfo;
    private Metadata? _authHeaders;
    private GrpcChannel? _channel;
    private readonly IP4kService _p4kService;
    private CancellationTokenSource? _pollCts;
    private IFileTailService? _fileTailWatcher;
    private CancellationTokenSource? _logWatchCts;

    public event EventHandler<GrpcConnectionStatus>? OnStatusChanged;

    public GrpcConnectionStatus Status { get; private set; } = GrpcConnectionStatus.Disconnected;
    public string? ErrorMessage { get; private set; }
    public string? CurrentShard { get; private set; }

    public GrpcClientService(ILogger<GrpcClientService> logger, IP4kService p4kService)
    {
        _watcher = null;
        _authHeaders = null;
        _logger = logger;
        _p4kService = p4kService;
    }

    public async Task InitClient(P4kFileModel p4kFile)
    {
        CleanWatch();

        var dir = new FileInfo(p4kFile.Path).Directory?.FullName;
        _watcher = new StarCitizenClientWatcher(dir ?? "");
        _watcher.Start();
        var loginData = await _watcher.WaitForLoginData().ConfigureAwait(false);

        SetStatus(GrpcConnectionStatus.Connecting);
        _logger.LogInformation("Got Login data for user: \"{Username}\" on server: \"{ServicesEndpoint}\"", loginData.Username, loginData.StarNetwork.ServicesEndpoint);

        try
        {
            _channel = GrpcChannel.ForAddress(new Uri(loginData.StarNetwork.ServicesEndpoint));
            var identityClient = new IdentityService.IdentityServiceClient(_channel);

            var currentPlayer = await identityClient.GetCurrentPlayerAsync(new GetCurrentPlayerRequest(), new Metadata { { "Authorization", $"Bearer {loginData.AuthToken}" } }).ConfigureAwait(false);

            _playerInfo = currentPlayer;
            _authHeaders = new Metadata {
                { "Authorization", $"Bearer {currentPlayer.Jwt}" },
                { "grpc-timeout", "60S" }
            };
            _logger.LogDebug("player jwt : {jwt}", currentPlayer.Jwt);

            SetStatus(GrpcConnectionStatus.Connected);
            StartGameLogWatching(p4kFile.Path);
            StartShardPolling();
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC error during InitClient");
            SetStatus(GrpcConnectionStatus.Error, ex.Status.Detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during InitClient");
            SetStatus(GrpcConnectionStatus.Error, ex.Message);
        }
    }

    private void SetStatus(GrpcConnectionStatus newStatus, string? errorMessage = null)
    {
        if (Status == newStatus && ErrorMessage == errorMessage)
            return;
        Status = newStatus;
        ErrorMessage = errorMessage;
        OnStatusChanged?.Invoke(this, newStatus);
    }

    private void StartShardPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
    }

    public async Task<IList<SpaceshipModel>> GetSpaceships()
    {
        var entitlementServiceClient = new ExternalEntitlementService.ExternalEntitlementServiceClient(_channel);
        var result = new List<SpaceshipModel>(50);
        
        // Appel réel
        Console.WriteLine($"{DateTime.Now} avant la requête");
        await semaphoreSlim.WaitAsync();
        try
        {
            // On ne récupère que les véhicules
            using var response = entitlementServiceClient.QueryEntitlementsByItemTypeStream(new QueryEntitlementsByItemTypeStreamRequest
            {
                ItemType = nameof(EItemType.NOITEM_Vehicle)
            }, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));

            while (await response.ResponseStream.MoveNext(CancellationToken.None))
            {
                var current = response.ResponseStream.Current;
                
                result.AddRange(current.Results.Select(e => new SpaceshipModel(e)));
            }
        }
        finally
        {
            semaphoreSlim.Release();
        }
        
        Console.WriteLine($"{DateTime.Now} après la requête, {result.Count} résultats");
        return result;
    }
    
    public async Task<IList<EntityItemQueryResult>> QueryGraphBySearch(ItemQueryModel itemQueryModel)
    {
        // Helpers pour simplifier la création des filtres
        static ScalarValue ULong(ulong v) => new ScalarValue { UnsignedBigintValue = v };
        static ScalarValue Int(int v) => new ScalarValue { IntegerValue = v };
        static ScalarValue Str(string v) => new ScalarValue { StringValue = v };

        EntityFilter PropEqULong(string property, ulong value) => new EntityFilter
        {
            PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.Equal,
                Property = property,
                Values = { ULong(value) }
            }
        };

        EntityFilter PropEqString(string property, string value) => new EntityFilter
        {
            PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.Equal,
                Property = property,
                Values = { Str(value) }
            }
        };

        EntityFilter PropIn(string property, IEnumerable<ScalarValue> values) => new EntityFilter
        {
            PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.In,
                Property = property,
                Values = { values }
            }
        };

        var andFilters = new List<EntityFilter>(8);

        // Filtre owner
        if ((itemQueryModel.useConnectedUserOwner && (itemQueryModel.InventoryIdList?.Count ?? 0) == 0) || !string.IsNullOrEmpty(itemQueryModel.ownerId))
        {
            ulong ownerId = itemQueryModel.useConnectedUserOwner ? _playerInfo.Player.Geid : ulong.Parse(itemQueryModel.ownerId);
            andFilters.Add(PropEqULong("ownerId", ownerId));
        }

        // Filtre par id d'objet
        if (!string.IsNullOrEmpty(itemQueryModel.Id))
        {
            andFilters.Add(PropEqULong("geid", ulong.Parse(itemQueryModel.Id)));
        }

        // Filtre par type
        if ((itemQueryModel.TypeList?.Count ?? 0) > 0)
        {
            andFilters.Add(PropIn("itemTypeEnum", itemQueryModel.TypeList!.Select(t => Int((int)t))));
        }

        // Filtre par conteneur (STOWED_IN) avec option OR owner
        if ((itemQueryModel.InventoryIdList?.Count ?? 0) > 0)
        {
            var stowedIn = new EdgeFilter { EdgeType = "STOWED_IN" };
            stowedIn.Values.AddRange(itemQueryModel.InventoryIdList!.Select(id => Str(id)));

            var or = new EntityCompositeFilter { Operator = LogicalOperator.Or };
            or.Filters.Add(new EntityFilter { EdgeFilter = stowedIn });

            if (itemQueryModel.useConnectedUserOwner)
            {
                or.Filters.Add(PropEqULong("ownerId", _playerInfo.Player.Geid));
            }

            andFilters.Add(new EntityFilter { CompositeFilter = or });
        }

        // Filtre par parentUrn
        if ((itemQueryModel.ParentUrnList?.Count ?? 0) > 0)
        {
            andFilters.Add(PropIn("parentUrn", itemQueryModel.ParentUrnList!.Select(Str)));
        }

        var compositeFilter = new EntityCompositeFilter { Operator = LogicalOperator.And };
        compositeFilter.Filters.AddRange(andFilters);

        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        var request = new EntityQueryRequest
        {
            Body = new EntityQueryRequestBody
            {
                Scope = new Scope { Type = ScopeType.Global, ShardId = "" },
                Query = new EntityGraphQuery
                {
                    Filter = new EntityFilter { CompositeFilter = compositeFilter },
                    Projection = new EntityProjection
                    {
                        Tree = new EntityTreeProjection
                        {
                            Enabled = itemQueryModel.UseProjection,
                            IncludeInventoryNodes = false,
                            PathMode = true
                        },
                        OutgoingEdges = true,
                        Snapshots = true,
                        EntityClasses = false
                    },
                    Language = "",
                    Pagination = new PaginationArguments { First = 250, After = "" },
                    Sort = new EntitySortingArguments
                    {
                        EntityProperty = new EntitySortingByProperty { Property = "geid", SortComparator = SortComparator.Numerical },
                        Order = PaginationOrder.Ascending
                    }
                }
            }
        };

        var edgeDict = new Dictionary<ulong, EntityEdge>(500);
        var nodes = new List<Node>(800);

        // Exécution paginée générique
        async Task RunPagedQueryAsync()
        {
            EntityQueryResponse? response = null;
            await semaphoreSlim.WaitAsync().ConfigureAwait(false);
            try
            {
                do
                {
                    if (response != null)
                        request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;

                    response = await service.EntityQueryAsync(request, _authHeaders).ConfigureAwait(false);

                    foreach (var edge in response.Body.Results.Edges)
                    {
                        if (edge.Start.HasEntityId)
                            edgeDict.TryAdd(edge.Start.EntityId, edge);
                        else
                            _logger.LogInformation("Edge start pas de type Entity : {Type}", edge.Start.Type);
                    }

                    nodes.AddRange(response.Body.Results.Nodes);
                } while (response.Body.PageInfo.HasNextPage);
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        // 1) Requête principale
        await RunPagedQueryAsync().ConfigureAwait(false);

        // 2) Chargement optionnel du contenu des inventaires trouvés
        if (itemQueryModel.LoadInventoryContent)
        {
            var containerList = new List<ulong>(500);

            // Détection des conteneurs déjà trouvés
            foreach (var node in nodes)
            {
                var type = await _p4kService.GetEntityType(node.Properties.EntityProperties.ClassGuidCrc).ConfigureAwait(false);
                var container = ((EntityClassDefinition?)type?.Data)?.Components.OfType<SCItemInventoryContainerComponentParams>().FirstOrDefault();
                if (container != null)
                {
                    containerList.Add(node.Properties.EntityProperties.Geid);
                }
            }

            // Fonction locale pour mettre à jour/ajouter le filtre STOWED_IN (OR owner éventuellement)
            void UpsertStowedInFilter(IEnumerable<ulong> geids)
            {
                var inventoryIdListFilter = new EdgeFilter { EdgeType = "STOWED_IN" };
                inventoryIdListFilter.Values.AddRange(geids.Select(g => Str($"{g}:Container:0")));

                var or = new EntityCompositeFilter { Operator = LogicalOperator.Or };
                or.Filters.Add(new EntityFilter { EdgeFilter = inventoryIdListFilter });

                if (itemQueryModel.useConnectedUserOwner)
                    or.Filters.Add(PropEqULong("ownerId", _playerInfo.Player.Geid));

                var existing = compositeFilter.Filters.FirstOrDefault(c =>
                    c.CompositeFilter != null &&
                    c.CompositeFilter.Operator == LogicalOperator.Or &&
                    c.CompositeFilter.Filters.Any(f => f.EdgeFilter is { EdgeType: "STOWED_IN" }));

                if (existing != null)
                    existing.CompositeFilter = or;
                else
                    compositeFilter.Filters.Add(new EntityFilter { CompositeFilter = or });
            }

            while (containerList.Count > 0)
            {
                var batch = containerList.Take(50).ToList();
                containerList.RemoveRange(0, batch.Count);

                UpsertStowedInFilter(batch);

                // Reset pagination pour repartir du début
                request.Body.Query.Pagination.After = "";

                // Requête paginée pour cette fournée + découverte de nouveaux conteneurs
                EntityQueryResponse? response = null;
                await semaphoreSlim.WaitAsync().ConfigureAwait(false);
                try
                {
                    do
                    {
                        if (response != null)
                            request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;

                        response = await service.EntityQueryAsync(request, _authHeaders).ConfigureAwait(false);

                        foreach (var edge in response.Body.Results.Edges)
                        {
                            if (edge.Start.HasEntityId)
                                edgeDict.TryAdd(edge.Start.EntityId, edge);
                            else
                                _logger.LogInformation("Edge start pas de type Entity : {Type}", edge.Start.Type);
                        }

                        nodes.AddRange(response.Body.Results.Nodes);

                        foreach (var containerNode in response.Body.Results.Nodes)
                        {
                            var type = await _p4kService.GetEntityType(containerNode.Properties.EntityProperties.ClassGuidCrc).ConfigureAwait(false);
                            var container = ((EntityClassDefinition?)type?.Data)?.Components.OfType<SCItemInventoryContainerComponentParams>().FirstOrDefault();
                            if (container != null)
                                containerList.Add(containerNode.Properties.EntityProperties.Geid);
                        }
                    } while (response.Body.PageInfo.HasNextPage);
                }
                finally
                {
                    semaphoreSlim.Release();
                }
            }
        }

        return nodes.Select(n => new EntityItemQueryResult
        {
            EntityNodeProperties = n.Properties.EntityProperties,
            EntityEdge = GetFromDict(edgeDict, n.Properties.EntityProperties.Geid),
            EntityClassProperties = null
        }).ToList();
    }

    public async Task<IList<EntityStowContext>> GetEntityStowContextByParentUrnList(IList<string> urnList, List<uint> crcTypeList)
    {
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        var request = new GetEntityStowContextsByParentUrnsRequest
        {
            Body = new GetEntityStowContextsByParentUrnsRequestBody
            {
                OwnerId = _playerInfo.Player.Geid,
                ParentUrns = {urnList},
                EntityClassCrcs = { crcTypeList }
            }
        };
        
        var result = await service.GetEntityStowContextsByParentUrnsAsync(request, _authHeaders, DateTime.UtcNow.AddSeconds(30)).ConfigureAwait(false);
        
        return result.Body.Results;
        
    }

    private T? GetFromDict<T, U>(Dictionary<U, T> dict, U key)
    {
        if (dict.TryGetValue(key, out var fromDict))
        {
            return fromDict;
        }
        
        return default(T);
    }

    
    public async Task<IList<EntityNodeProperties>> QueryGraphByParentUrnList(IList<string> parentUrnList)
    {
        // filtre par owner
        var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "ownerId"
        };
        ownerFilter.Values.Add(new ScalarValue { UnsignedBigintValue = _playerInfo?.Player.Geid ?? 0ul});

        // Liste des parentUrn
        var geidListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "parentUrn"
        };
        geidListFilter.Values.AddRange(parentUrnList.Select(x => new ScalarValue { StringValue = x }));
        
        // Liste des type d'entité
        var itemTypeListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "itemTypeEnum"
        };
        itemTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemType.NOITEM_Vehicle });

        // Liste des sous type d'entité
        var itemSubTypeListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "itemSubTypeEnum"
        };
        itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_Boat });
        itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_GroundVehicle });
        itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_PowerSuit });
        itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_Spaceship });


        var filter = new EntityCompositeFilter
        {
            Operator = LogicalOperator.And,
            Filters =
            {
                new EntityFilter { PropertyFilter = ownerFilter },
                new EntityFilter { PropertyFilter = geidListFilter },
                new EntityFilter { PropertyFilter = itemTypeListFilter },
                new EntityFilter { PropertyFilter = itemSubTypeListFilter },
            }
        };

        
        
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        EntityQueryRequest request = new()
        {
            Body = new EntityQueryRequestBody
            {
                Scope = new Scope
                {
                    Type = ScopeType.Global,
                    ShardId = ""
                },
                Query = new EntityGraphQuery
                {
                    Filter = new EntityFilter
                    {
                        CompositeFilter = filter
                    },
                    Projection = new EntityProjection
                    {
                        Tree = new EntityTreeProjection
                        {
                            Enabled = false,
                            IncludeInventoryNodes = false,
                            PathMode = false,
                            // Prune = new EntityPruneConstraint
                            // {
                            //     InventoryFilter = new InventoryFilter
                            //     {
                            //         PropertyFilter = inventoryidListFilter
                            //     }
                            // }
                        },
                        OutgoingEdges = false,
                        Snapshots = false,
                        EntityClasses = false
                    },
                    Language = ""
                }
            }
        };
        await semaphoreSlim.WaitAsync();
        EntityQueryResponse response;
        try
        {
            response = await service.EntityQueryAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
        }
        finally
        {
            semaphoreSlim.Release();
        }

        return response.Body.Results.Nodes.Select(n => n.Properties.EntityProperties).ToList();
    }
    
    public async Task<IList<EntityNodeProperties>> QueryGraphByGeidListWithoutOwner(IList<ulong> geidList)
    {
        // // filtre par owner
        // var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        // {
        //     Operator = ComparisonOperator.Equal,
        //     Property = "ownerId"
        // };
        // ownerFilter.Values.Add(new ScalarValue { UnsignedBigintValue = _playerInfo?.Player.Geid ?? 0ul});

        // Liste des parentUrn
        var geidListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "geid"
        };
        geidListFilter.Values.AddRange(geidList.Select(x => new ScalarValue { UnsignedBigintValue = x }));

        var filter = new EntityCompositeFilter
        {
            Operator = LogicalOperator.And,
            Filters =
            {
                // new EntityFilter { PropertyFilter = ownerFilter },
                new EntityFilter { PropertyFilter = geidListFilter }
            }
        };
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        EntityQueryRequest request = new()
        {
            Body = new EntityQueryRequestBody
            {
                Scope = new Scope
                {
                    Type = ScopeType.Global,
                    ShardId = ""
                },
                Query = new EntityGraphQuery
                {
                    Filter = new EntityFilter
                    {
                        CompositeFilter = filter
                    },
                    Projection = new EntityProjection
                    {
                        Tree = new EntityTreeProjection
                        {
                            Enabled = false,
                            IncludeInventoryNodes = false,
                            PathMode = false
                        },
                        OutgoingEdges = false,
                        Snapshots = false,
                        EntityClasses = false
                    },
                    Language = ""
                }
            }
        };
        await semaphoreSlim.WaitAsync();
        EntityQueryResponse response;
        try
        {
            response = await service.EntityQueryAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
        }
        finally
        {
            semaphoreSlim.Release();
        }

        return response.Body.Results.Nodes.Select(n => n.Properties.EntityProperties).ToList();
    }

    // /**
    //  * Récupère une liste de contexte de stockage pour une liste de geid (vaisseau, équipement, etc.)
    //  */
    // public async Task<IList<EntityStowContext>> QueryStowContextByGeidList(IList<ulong> geidList)
    // {
    //     var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
    //     {
    //         Operator = ComparisonOperator.Equal,
    //         Property = "ownerId",
    //     };
    //     ownerFilter.Values.Add(new ScalarValue {UnsignedBigintValue = _playerInfo.Player.Geid});
    //
    //     
    //     var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
    //     {
    //         Operator = ComparisonOperator.In,
    //         Property = "entityId",
    //     };
    //     filter.Values.AddRange(geidList.Select(x => new ScalarValue { UnsignedBigintValue = x }));
    //     
    //     var service = new EntityGraphService.EntityGraphServiceClient(_channel);
    //
    //     GetEntityStowContextsRequest request = new()
    //     {
    //         Body = new GetEntityStowContextsRequestBody
    //         {
    //             Query = new EntityStowContextQuery
    //             {
    //                 Filter = new EntityStowContextFilter
    //                 {
    //                     CompositeFilter = new EntityStowContextCompositeFilter
    //                     {
    //                         Operator = LogicalOperator.And,
    //                         Filters =
    //                         {
    //                             new EntityStowContextFilter { PropertyFilter = ownerFilter },
    //                             new EntityStowContextFilter { PropertyFilter = filter }
    //                         }
    //                     }
    //                 },
    //                 Pagination = new PaginationArguments
    //                 {
    //                     First = 0,
    //                     After = ""
    //                 }
    //             }
    //         }
    //     };
    //
    //     await semaphoreSlim.WaitAsync();
    //     GetEntityStowContextsResponse response;
    //     try
    //     {
    //         response = await service.GetEntityStowContextsAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
    //     }
    //     finally
    //     {
    //         semaphoreSlim.Release();
    //     }
    //
    //     return response.Body.Results;
    // }

    // public async Task<IList<EntityStowContext>> QueryStowContextByOwnerId(ulong ownerId)
    // {
    //     var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
    //     {
    //         Operator = ComparisonOperator.Equal,
    //         Property = "ownerId",
    //     };
    //     filter.Values.Add(new ScalarValue {UnsignedBigintValue = ownerId});
    //     
    //     var service = new EntityGraphService.EntityGraphServiceClient(_channel);
    //
    //     GetEntityStowContextsRequest request = new()
    //     {
    //         Body = new GetEntityStowContextsRequestBody
    //         {
    //             Query = new EntityStowContextQuery
    //             {
    //                 Filter = new EntityStowContextFilter
    //                 {
    //                     PropertyFilter = filter
    //                 },
    //                 Pagination = new PaginationArguments
    //                 {
    //                     First = 0,
    //                     After = ""
    //                 }
    //             }
    //         }
    //     };
    //     
    //     await semaphoreSlim.WaitAsync();
    //     GetEntityStowContextsResponse response;
    //     try
    //     {
    //         response = await service.GetEntityStowContextsAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
    //     }
    //     finally
    //     {
    //         semaphoreSlim.Release();
    //     }
    //
    //     return response.Body.Results;
    // }
    
    public async Task<IList<Inventory>> QueryInventoryById(String id)
    {
        // var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        // {
        //     Operator = ComparisonOperator.NotEqual,
        //     Property = "id",
        // };
        // filter.Values.Add(new ScalarValue {StringValue = id});

        var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "ownerId",
        };
        filter.Values.Add(new ScalarValue {UnsignedBigintValue = _playerInfo?.Player.Geid ?? 0});

        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        
        InventoryQueryRequest request = new()
        {
            Body = new InventoryQueryRequestBody
            {
                Query = new InventoryQuery
                {
                    Filter = new InventoryFilter
                    {
                        PropertyFilter = filter
                    },
                    Pagination = new PaginationArguments
                    {
                        First = 1024,
                        After = ""
                    },
                    // Tree = new InventoryTreeFilter
                    // {
                    //     // InventoryId = "3490636 373",
                    //     //EntityGeid = 3490636373,
                    //     // Filter = new InventoryFilter
                    //     // {
                    //     //     PropertyFilter = filter
                    //     // }
                    // }
                }
            }
        };

        await semaphoreSlim.WaitAsync();
        var result = new List<Inventory>(1024);
        InventoryQueryResponse? response = null;
        try
        {
            do
            {
                if (null != response)
                {
                    request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;
                }
                
                response = await service.InventoryQueryAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
                result.AddRange(response.Body.Results);
            } while (response.Body.PageInfo.HasNextPage);
        }
        finally
        {
            semaphoreSlim.Release();
        }


        return result;
    }

    
    public async Task<IList<InventoryNodeProperties>> QueryInventories()
    {
        var request = new GetInventoriesRequest
        {
            Body = new GetInventoriesRequestBody
            {
                OwnerId = _playerInfo.Player.Geid.ToString()
            }
        };
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);
        await semaphoreSlim.WaitAsync();
        GetInventoriesResponse response;
        try
        {
            response = await service.GetInventoriesAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
        }
        finally
        {
            semaphoreSlim.Release();
        }

        return response.Body.Inventories.Select( n => n.Properties.InventoryProperties).ToList();
    }
    
    
    public async Task TestRequest()
    {
        var service = new Sc.External.Services.BlueprintLibrary.V1.BlueprintLibraryService.BlueprintLibraryServiceClient(_channel);
        var request = new QueryBlueprintEntriesRequest
        {
            Query = new Query
            {
                Pagination = new PaginationArguments
                {
                    First = 250,
                    After = ""
                }
            }
        };
        
        var response = await service.QueryBlueprintEntriesAsync(request, _authHeaders).ConfigureAwait(false);
        // Guid de  l'objet DB des types
        var typeDb = await _p4kService.GetRecordWithSpecificDepth(new CigGuid("2f8b8c66-af27-4e6a-ba30-e95efd593dd4"), 0);

        foreach (var responseResult in response.Results)
        {
            //var guidCrc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([new CigGuid(responseResult.BlueprintId)]));
            var blueprint = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(responseResult.BlueprintId), 2);
            var category = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(responseResult.CategoryId), 1);
            //var categoryBis = ((BlueprintCategoryDatabaseRecord)typeDb.Data).categories.FirstOrDefault(c => c)
            var itemClass = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(responseResult.ItemClassId), 1);
            Console.WriteLine("Coucou");
        }
        
        Console.WriteLine("Coucou");

    }

    public async Task<List<VersionedReputation>> QueryReputationsAsync()
    {
        var response = await _grpcClient.QueryReputationsAsync(new QueryReputationsRequest());
        return response.Reputations.ToList();
    }

    public async Task<List<BlueprintEntry>> GetBlueprintList()
    {
        var service = new BlueprintLibraryService.BlueprintLibraryServiceClient(_channel);
        var request = new QueryBlueprintEntriesRequest
        {
            Query = new Query
            { 
                Pagination = new PaginationArguments
                {
                    First = 250,
                    After = ""
                }
            }
        };
        var result = new List<BlueprintEntry>();
        QueryBlueprintEntriesResponse? response = null;

        do
        {
            if (null != response)
            {
                request.Query.Pagination.After = response.PageInfo.EndCursor;
            }
            
            response = await service.QueryBlueprintEntriesAsync(request, _authHeaders).ConfigureAwait(false);
        } while (response.PageInfo.HasNextPage);


        result.AddRange(response.Results);
        return result;
    }


    public async Task<string?> GetPlayerName(ulong playerId)
    {
        var request = new GetPlayersNamesRequest
        {
            PlayerGeids = { playerId }
        };
        
        
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var service = new IdentityService.IdentityServiceClient(_channel);
        await semaphoreSlim.WaitAsync(cts.Token);
        try
        {
            var response = service.GetPlayersNamesAsync(request, headers: _authHeaders, cancellationToken: cts.Token, deadline: DateTime.UtcNow.AddSeconds(1));
            using var cancelTask = Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

            try
            {
                _ = await Task.WhenAny(cancelTask, response.ResponseAsync).ConfigureAwait(false);
                if (!response.ResponseAsync.IsCompletedSuccessfully)
                {
                    // On a timeout, retour de null
                    return null;
                }

                await cts.CancelAsync();
                var names = await response;
                return (names.Names.FirstOrDefault() ?? new GetPlayersNamesResponse.Types.PlayerName()).Name;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while getting player name");
                return null;
            }
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }
    
    public async Task<IList<Node>> QueryInventoryBisById(String id)
    {
        var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "id",
        };
        filter.Values.Add(new ScalarValue {StringValue = id});
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        GetInventoriesRequest request = new()
        {
            Body = new GetInventoriesRequestBody()
            {
                OwnerId = _playerInfo.Player.Geid.ToString()
            }
        };
        
        await semaphoreSlim.WaitAsync();
        try
        {
            var response = await service.GetInventoriesAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));

            return response.Body.Inventories;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }
    
    public async Task<IList<Contact>> GetFriendList()
    {
        var service = new ContactsService.ContactsServiceClient(_channel);
        await semaphoreSlim.WaitAsync();
        try
        {
            var response = await service.ListContactsAsync(new ListContactsRequest(), _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10)).ConfigureAwait(false);

            return response.Contacts;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    public async Task<ShardInfo> GetShardInfo(int accountId)
    {
        var request = new GetShardInfoRequest();
        
        request.AccountId = accountId;
        
        var service = new ContactsService.ContactsServiceClient(_channel);
        await semaphoreSlim.WaitAsync();
        try
        {
            var response = await service.GetShardInfoAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));

            return response.ShardInfo;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    private async Task<ShardInfo> GetUserShard(int accountId)
    {
        return null;
        // var request = new GetInstanceInfoRequest();
        //
        // request.PlayerUrn = accountId.ToString();
        //
        // var service = new PresenceService.PresenceServiceClient(_channel);
        // await semaphoreSlim.WaitAsync();
        // try
        // {
        //     var toto = service.PresenceStream(_authHeaders);
        //     
        //     //toto.RequestStream.WriteAsync()
        //     var response = await service.GetInstanceInfoAsync(request, _authHeaders, deadline: DateTime.UtcNow.AddSeconds(10));
        //
        //     return response.ShardInfo;
        // }
        // finally
        // {
        //     semaphoreSlim.Release();
        // }
    }


    private void StartGameLogWatching(string p4kPath)
    {
        var logPath = Path.Combine(new FileInfo(p4kPath).Directory?.FullName ?? "", "Game.log");
        _logWatchCts = new CancellationTokenSource();

        _fileTailWatcher = new FileTailWatcher();
        _fileTailWatcher.StateChanged += OnLogFileStateChanged;
        _fileTailWatcher.LineReceived += OnLogLineReceived;

        _ = _fileTailWatcher.StartAsync(logPath, _logWatchCts.Token).ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Failed to start Game.log watcher");
            },
            TaskScheduler.Default);
    }

    private void StopGameLogWatching()
    {
        _fileTailWatcher?.StateChanged -= OnLogFileStateChanged;
        _fileTailWatcher?.LineReceived -= OnLogLineReceived;
        _fileTailWatcher?.Stop();
        _fileTailWatcher?.Dispose();
        _fileTailWatcher = null;

        _logWatchCts?.Cancel();
        _logWatchCts?.Dispose();
        _logWatchCts = null;
    }

    private void OnLogFileStateChanged(object? sender, FileState state)
    {
        if (state == FileState.Missing && Status == GrpcConnectionStatus.InGame)
        {
            _logger.LogInformation("Game.log disappeared while InGame — resetting to Connected");
            CurrentShard = null;
            SetStatus(GrpcConnectionStatus.Connected);
        }
    }

    private void OnLogLineReceived(object? sender, FileTailEventArgs e)
    {
        var line = e.Line;

        if (line.Contains("<Join PU>") && line.Contains("shard["))
        {
            CurrentShard = ExtractBracketValue(line, "shard[");
            SetStatus(GrpcConnectionStatus.InGame);
            _logger.LogInformation("Player joined shard: {Shard}", CurrentShard);
            return;
        }

        if (line.Contains("<Channel Disconnected>")
            && line.Contains("Remote Disconnect - Player requested disconnect")
            && line.Contains("SC_Default"))
        {
            CurrentShard = null;
            SetStatus(GrpcConnectionStatus.Connected);
            _logger.LogInformation("Player disconnected from shard (player requested)");
            return;
        }

        if (line.Contains("Client quitting game") || line.Contains("Fast Shutdown"))
        {
            CurrentShard = null;
            SetStatus(GrpcConnectionStatus.Connected);
            _logger.LogInformation("Game closed");
        }
    }

    private static string? ExtractBracketValue(string line, string prefix)
    {
        int start = line.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += prefix.Length;
        int end = line.IndexOf(']', start);
        if (end < 0)
            return null;

        return line[start..end];
    }

    private void CleanWatch()
    {
        SetStatus(GrpcConnectionStatus.Disconnected);
        StopGameLogWatching();
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;

        if (_watcher != null)
        {
            _watcher.Dispose();
            _watcher = null;
            _authHeaders = null;
            _playerInfo = null;
            CurrentShard = null;
        }

        if (null != _channel)
        {
            _channel.Dispose();
            _channel = null;
        }
    }
}