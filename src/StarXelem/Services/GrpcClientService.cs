using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Sc.External.Common.Api.V1;
using Sc.External.Services.Entitlement.V1;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Identity.V1;
using Sc.Internal.Services.Entitlement.V1;
using Sc.Internal.Services.MissionLocation.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using DateTime = System.DateTime;
using EntitlementItemType = Sc.External.Services.Entitlement.V1.EntitlementItemType;
using EntityFilter = Sc.External.Services.Entitygraph.V1.EntityFilter;
using PropertyFilter = Sc.External.Common.Api.V1.PropertyFilter;
using QueryRequest = Sc.Internal.Services.Entitlement.V1.QueryRequest;

namespace StarXelem.Services;

public class GrpcClientService : IGrpcClientService
{
    private readonly ILogger<GrpcClientService> _logger;
    private StarCitizenClientWatcher? _watcher;
    private GetCurrentPlayerResponse? _playerInfo;
    private Metadata? _authHeaders;
    private GrpcChannel? _channel;

    public event EventHandler<bool>? OnConnectedChanged;

    public GrpcClientService(ILogger<GrpcClientService> logger)
    {
        _watcher = null;
        _authHeaders = null;
        _logger = logger;
        IsConnected = false;
    }

    public async Task InitClient(P4kFileModel p4kFile)
    {
        CleanWatch();
        var dir = new FileInfo(p4kFile.Path).Directory?.FullName;
        _watcher = new StarCitizenClientWatcher(dir ?? "");
        _watcher.Start();
        var loginData = await _watcher.WaitForLoginData();
        
        _logger.LogInformation("Got Login data for user: \"{Username}\" on server: \"{ServicesEndpoint}\"", loginData.Username, loginData.StarNetwork.ServicesEndpoint);
        
        _channel = GrpcChannel.ForAddress(new Uri(loginData.StarNetwork.ServicesEndpoint));
        var identityClient = new IdentityService.IdentityServiceClient(_channel);
        
        var currentPlayer = await identityClient.GetCurrentPlayerAsync(new GetCurrentPlayerRequest(), new Metadata { { "Authorization", $"Bearer {loginData.AuthToken}" } });
        
        _playerInfo = currentPlayer;
        _authHeaders = new Metadata { { "Authorization", $"Bearer {currentPlayer.Jwt}" } };
        _logger.LogDebug("player jwt : {jwt}", currentPlayer.Jwt);
        IsConnected = true;
        OnConnectedChanged?.Invoke(this, IsConnected);
    }

    public async Task<IList<SpaceshipModel>> GetSpaceships()
    {
        var entitlementServiceClient = new EntitlementService.EntitlementServiceClient(_channel);

        var request = new QueryRequest();
        request.Query = new Query();
        request.Query.Filter = new Filter();

        var baseFilter = new CompositeFilter();

        baseFilter.Operator = CompositeFilter.Types.LogicalOperator.And;
        request.Query.Filter.CompositeFilter = baseFilter;

        // status filter
        var statusCompositeFilter = new CompositeFilter();
        statusCompositeFilter.Operator = CompositeFilter.Types.LogicalOperator.Or;
        statusCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter{
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "status",
                Value = ((int)EntitlementStatus.Fulfilled).ToString()
                
            }
        });
        statusCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "status",
                Value = ((int)EntitlementStatus.Unclaimed).ToString()
            } 
        });

        request.Query.Filter.CompositeFilter.Filters.Add(new Filter
        {
          CompositeFilter = statusCompositeFilter  
        });
        
        
        // policy filter
        var policyCompositeFilter = new CompositeFilter();
        policyCompositeFilter.Operator = CompositeFilter.Types.LogicalOperator.Or;
        policyCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter{
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "insurance.policy.coverage",
                Value = "lifetime"
                
            }
        });
        policyCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "insurance.policy.coverage",
                Value = "duration"
            }
        });

        request.Query.Filter.CompositeFilter.Filters.Add(new Filter
        {
            CompositeFilter = policyCompositeFilter  
        });
        
        
        // source filter
        var sourceCompositeFilter = new CompositeFilter();
        sourceCompositeFilter.Operator = CompositeFilter.Types.LogicalOperator.Or;
        sourceCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "source",
                Value = ((int)EntitlementSource.Platform).ToString()
            }
        });
        sourceCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "source",
                Value = ((int)EntitlementSource.PersistentUniverse).ToString()
            }
        });
        sourceCompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "source",
                Value = ((int)EntitlementSource.LongtermPersistence).ToString()
            }
        });

        request.Query.Filter.CompositeFilter.Filters.Add(new Filter
        {
            CompositeFilter = sourceCompositeFilter  
        });
        
        // itemType filter
        request.Query.Filter.CompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "itemType",
                Value = ((int)EntitlementItemType.Ship).ToString()
            }  
        });

        // player filter
        request.Query.Filter.CompositeFilter.Filters.Add(new Filter
        {
            PropertyFilter = new PropertyFilter
            {
                Operator = PropertyFilter.Types.ComparisonOperator.Equals,
                Property = "playerUrn",
                Value = $"urn:sc:global:player:geid:{_playerInfo.Player.Geid}"
            }  
        });

            
        // Appel réel
        Console.WriteLine($"{DateTime.Now} avant la requête");
        var response = await entitlementServiceClient.QueryAsync(request, _authHeaders);
            Console.WriteLine($"{DateTime.Now} après la requête, {response.Results.Count} résultats");

        var results = response.Results.Select(r => new SpaceshipModel(r)).ToList();
        
        return results;
    }
    
    public async Task<IList<EntityItemQueryResult>> QueryGraphBySearch(ItemQueryModel itemQueryModel)
    {
        var filter = new EntityCompositeFilter
        {
            Operator = LogicalOperator.And,
            Filters =
            {
                //new EntityFilter { PropertyFilter = ownerFilter },
                // new EntityFilter { PropertyFilter = geidListFilter },
                // new EntityFilter { PropertyFilter = itemTypeListFilter },
                // new EntityFilter { PropertyFilter = itemSubTypeListFilter },
            }
        };

        if ((itemQueryModel.useConnectedUserOwner && (itemQueryModel.InventoryIdList?.Count ?? 0) == 0)  || !String.IsNullOrEmpty(itemQueryModel.ownerId))
        {
            ulong ownerId = itemQueryModel.useConnectedUserOwner ? _playerInfo.Player.Geid : ulong.Parse(itemQueryModel.ownerId);
            // filtre par owner
            var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.Equal,
                Property = "ownerId"
            };
            
            ownerFilter.Values.Add(new ScalarValue { UnsignedBigintValue = ownerId});
            filter.Filters.Add(new EntityFilter { PropertyFilter = ownerFilter });
        }

        if (!String.IsNullOrEmpty(itemQueryModel.Id))
        {
            // filtre par id d'objet
            var idFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.Equal,
                Property = "geid"
            };
            
            idFilter.Values.Add(new ScalarValue { UnsignedBigintValue = ulong.Parse(itemQueryModel.Id)});
            filter.Filters.Add(new EntityFilter { PropertyFilter = idFilter });
        }

        if ((itemQueryModel.TypeList?.Count ?? 0) > 0)
        {
            // filtre par type
            var typeListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.In,
                Property = "itemTypeEnum"
            };
            
            typeListFilter.Values.AddRange(itemQueryModel.TypeList?.Select(t => new ScalarValue { IntegerValue = (int)t}));
            filter.Filters.Add(new EntityFilter { PropertyFilter = typeListFilter });
        }

        if ((itemQueryModel.InventoryIdList?.Count ?? 0) > 0)
        {
            // filtre par conteneur
            var inventoryIdListFilter = new EdgeFilter
            {
                EdgeType = "STOWED_IN",
            };
            inventoryIdListFilter.Values.AddRange(itemQueryModel.InventoryIdList?.Select(t => new ScalarValue { StringValue = t }));


            // // filtre par stowedContext pour faire un or
            // var stowContextFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            // {
            //     Operator = ComparisonOperator.Match,
            //     Property = "stowId"
            // };
            //
            // stowContextFilter.Values.AddRange(itemQueryModel.InventoryIdList?.Select(t => new ScalarValue { StringValue = t }));
            
            // filtre par owner pour faire un or
            ulong ownerId = _playerInfo.Player.Geid;
            // filtre par owner
            var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
            {
                Operator = ComparisonOperator.Equal,
                Property = "ownerId"
            };
            
            ownerFilter.Values.Add(new ScalarValue { UnsignedBigintValue = ownerId});
            var orFilter = new EntityCompositeFilter
            {
                Operator = LogicalOperator.Or
            };
            orFilter.Filters.Add(new EntityFilter { EdgeFilter = inventoryIdListFilter });
            // orFilter.Filters.Add(new EntityFilter { PropertyFilter = stowContextFilter });
            // Si on filtre par owner, on ajoute en OR l'id du owner pour avoir une vue d'ensemble
            if (itemQueryModel.useConnectedUserOwner)
            {
                orFilter.Filters.Add(new EntityFilter { PropertyFilter = ownerFilter });
            }

            filter.Filters.Add(new EntityFilter { CompositeFilter = orFilter });
        }

        // // Liste des parentUrn
        // var geidListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        // {
        //     Operator = ComparisonOperator.In,
        //     Property = "parentUrn"
        // };
        // geidListFilter.Values.AddRange(parentUrnList.Select(x => new ScalarValue { StringValue = x }));
        //
        // // Liste des type d'entité
        // var itemTypeListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        // {
        //     Operator = ComparisonOperator.In,
        //     Property = "itemTypeEnum"
        // };
        // itemTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemType.NOITEM_Vehicle });
        //
        // // Liste des sous type d'entité
        // var itemSubTypeListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        // {
        //     Operator = ComparisonOperator.In,
        //     Property = "itemSubTypeEnum"
        // };
        // itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_Boat });
        // itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_GroundVehicle });
        // itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_PowerSuit });
        // itemSubTypeListFilter.Values.Add( new ScalarValue { IntegerValue = (int)EItemSubType.Vehicle_Spaceship });




        
        
        
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
                            Enabled = itemQueryModel.UseProjection,
                            IncludeInventoryNodes = false,
                            PathMode = true,
                            // Prune = new EntityPruneConstraint
                            // {
                            //     InventoryFilter = new InventoryFilter
                            //     {
                            //         PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
                            //         {
                            //             Operator = ComparisonOperator.Equal,
                            //             Property = "ownerId",
                            //         
                            //         }
                            //     }
                            // }
                        },
                        OutgoingEdges = true,
                        Snapshots = true,
                        EntityClasses = false
                    },
                    Language = "",
                    Pagination = new PaginationArguments
                    {
                        First = 250,
                        After = ""
                    },
                    Sort = new EntitySortingArguments
                    {
                        EntityProperty = new EntitySortingByProperty
                        {
                            Property = "geid",
                            SortComparator = SortComparator.Numerical
                        },
                        // EdgeProperty = new EntitySortingByEdgeProperty
                        // {
                        //     Property = "rank",
                        //     SortComparator = SortComparator.Lexicographic,
                        //     EdgeType = "STOWED_IN"
                        // },
                        Order = PaginationOrder.Ascending
                    }
                }
            }
        };
        // TODO remove
        // request.Body.Query.Projection.Tree.Prune.InventoryFilter.PropertyFilter.Values.Add(new ScalarValue { UnsignedBigintValue = _playerInfo.Player.Geid });
        
        var classDict = new Dictionary<uint, EntityClassProperties>(500);
        var edgeDict = new Dictionary<ulong, EntityEdge>(500);
        var nodes = new List<Node>(800);
        EntityQueryResponse response = null;

        do
        {

            if (null != response)
            {
                request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;
            }

            response = await service.EntityQueryAsync(request, _authHeaders);
            
            // foreach (var entityClass in response.Body.EntityClasses)
            // {
            //     if (!classDict.ContainsKey(entityClass.Properties.GuidHashCrc))
            //     {
            //         classDict.Add(entityClass.Properties.GuidHashCrc, entityClass.Properties);
            //     }
            // }

            foreach (var entityClass in response.Body.Results.Edges)
            {
                if (entityClass.Start.HasEntityId)
                {
                    edgeDict.Add(entityClass.Start.EntityId, entityClass);
                }
                else
                {
                    Console.WriteLine($"Edge start pas de type Entity : {entityClass.Start.Type}");
                }
            }

            nodes.AddRange(response.Body.Results.Nodes);
            // Tant qu'il y a une nextPage, on continue;
        } while (response.Body.PageInfo.HasNextPage);

        //return nodes.Select(n => new EntityItemQueryResult {EntityNodeProperties = n.Properties.EntityProperties, EntityClassProperties = GetFromDict(classDict, n.Properties.EntityProperties.ClassGuidCrc)}).ToList();
        return nodes.Select(n => new EntityItemQueryResult {EntityNodeProperties = n.Properties.EntityProperties, EntityEdge = GetFromDict(edgeDict, n.Properties.EntityProperties.Geid), EntityClassProperties = null}).ToList();
        
        
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
        var response = await service.EntityQueryAsync(request, _authHeaders);

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
        var response = await service.EntityQueryAsync(request, _authHeaders);

        return response.Body.Results.Nodes.Select(n => n.Properties.EntityProperties).ToList();
    }

    /**
     * Récupère une liste de contexte de stockage pour une liste de geid (vaisseau, équipement, etc.)
     */
    public async Task<IList<EntityStowContext>> QueryStowContextByGeidList(IList<ulong> geidList)
    {
        var ownerFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "ownerId",
        };
        ownerFilter.Values.Add(new ScalarValue {UnsignedBigintValue = _playerInfo.Player.Geid});

        
        var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "entityId",
        };
        filter.Values.AddRange(geidList.Select(x => new ScalarValue { UnsignedBigintValue = x }));
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        GetEntityStowContextsRequest request = new()
        {
            Body = new GetEntityStowContextsRequestBody
            {
                Query = new EntityStowContextQuery
                {
                    Filter = new EntityStowContextFilter
                    {
                        CompositeFilter = new EntityStowContextCompositeFilter
                        {
                            Operator = LogicalOperator.And,
                            Filters =
                            {
                                new EntityStowContextFilter { PropertyFilter = ownerFilter },
                                new EntityStowContextFilter { PropertyFilter = filter }
                            }
                        }
                    },
                    Pagination = new PaginationArguments
                    {
                        First = 0,
                        After = ""
                    }
                }
            }
        };
        
        var response = await service.GetEntityStowContextsAsync(request, _authHeaders);

        return response.Body.Results;
    }

    public async Task<IList<EntityStowContext>> QueryStowContextByOwnerId(ulong ownerId)
    {
        var filter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "ownerId",
        };
        filter.Values.Add(new ScalarValue {UnsignedBigintValue = ownerId});
        
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);

        GetEntityStowContextsRequest request = new()
        {
            Body = new GetEntityStowContextsRequestBody
            {
                Query = new EntityStowContextQuery
                {
                    Filter = new EntityStowContextFilter
                    {
                        PropertyFilter = filter
                    },
                    Pagination = new PaginationArguments
                    {
                        First = 0,
                        After = ""
                    }
                }
            }
        };
        
        var response = await service.GetEntityStowContextsAsync(request, _authHeaders);

        return response.Body.Results;
    }
    
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

        var result = new List<Inventory>(1024);
        InventoryQueryResponse response = null;

        do
        {
            if (null != response)
            {
                request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;
            }
            
            response = await service.InventoryQueryAsync(request, _authHeaders);
            result.AddRange(response.Body.Results);
        } while (response.Body.PageInfo.HasNextPage);

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
        var response = await service.GetInventoriesAsync(request, _authHeaders);

        return response.Body.Inventories.Select( n => n.Properties.InventoryProperties).ToList();
    }
    
    
    public async Task TestRequest()
    {
        var filterContainerGeid = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.NotEqual,
            Property = "geid",
        };
        filterContainerGeid.Values.Add(new ScalarValue {UnsignedBigintValue = 3490636373});

        var labelFilter = new ContainerLabelFilter
        {
            Operator = LabelOperator.HasAny
        };
        labelFilter.Labels.Add(ContainerNodeLabel.Location);
        labelFilter.Labels.Add(ContainerNodeLabel.HasPhysicsGrid);
        labelFilter.Labels.Add(ContainerNodeLabel.Container);
        labelFilter.Labels.Add(ContainerNodeLabel.StarSystem);
        labelFilter.Labels.Add(ContainerNodeLabel.UniverseRoot);
        
        ContainerQueryRequest requestContainer = new()
        {
            Body = new ContainerQueryRequestBody
            {
                Scope = new Scope
                {
                    Type = ScopeType.Global,
                    ShardId = ""
                },
                Query = new ContainerGraphQuery
                {
                    Filter = new ContainerFilter
                    {
                        // LabelFilter = labelFilter
                        PropertyFilter = filterContainerGeid
                    },
                    Pagination = new PaginationArguments
                    {
                        First = 20,
                        After = ""
                    },
                    Projection = new ContainerProjection
                    {
                        Tree = new ContainerTreeProjection
                        {
                            Enabled = false
                        },
                        EntityClasses = false,
                        Snapshots = false
                    }
                }
            }
        };
        
      
        var service = new EntityGraphService.EntityGraphServiceClient(_channel);
        var responseContainer = await service.ContainerQueryAsync(requestContainer, _authHeaders);
        return;
        //responseContainer.Body.Results.Nodes.First().Properties.
        

        // var request = new GetInventoriesRequest
        // {
        //     Body = new GetInventoriesRequestBody
        //     {
        //          OwnerId = _playerInfo.Player.Geid.ToString()
        //     }
        // };
        // var response = await service.GetInventoriesAsync(request, _authHeaders);
        
            
            
        var geidListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "geid"
        };
        geidListFilter.Values.Add(new ScalarValue { UnsignedBigintValue = 6209912647207 });
        
        var inventoryidListFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.NotIn,
            //Property = "subjectId"
            Property = "id"
        };
        //inventoryidListFilter.Values.Add(new ScalarValue { UnsignedBigintValue = 3490636373 });
        inventoryidListFilter.Values.Add(new ScalarValue { StringValue = "" });
        
        
        var filterQuery = new EntityCompositeFilter
        {
            Operator = LogicalOperator.And,
            Filters =
            {
                // new EntityFilter { PropertyFilter = ownerFilter },
                new EntityFilter {
                    PropertyFilter = geidListFilter
                }
            }
        };
        
        //201962463294:Hangar:3490636373
        EntityQueryRequest requestQuery = new()
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
                        PropertyFilter = geidListFilter,
                        //CompositeFilter = filterQuery
                    },
                    Projection = new EntityProjection
                    {
                        Tree = new EntityTreeProjection
                        {
                            Enabled = true,
                            IncludeInventoryNodes = false,
                            PathMode = false,
                            // Prune = new EntityPruneConstraint
                            // {
                            //     InventoryFilter = new InventoryFilter
                            //     {
                            //         PropertyFilter = inventoryidListFilter,
                            //     }
                            // }
                        },
                        OutgoingEdges = false,
                        Snapshots = false,
                        EntityClasses = true
                    },
                    Language = "",
                    Pagination = new PaginationArguments
                    {
                        First = 0,
                        After = ""
                    }
                }
            }
        };
        // var responseQuery = await service.EntityQueryAsync(requestQuery, _authHeaders);
        //
        // // TODO fetch using EItemType ? 
        //
        //
        // while (responseQuery.Body.PageInfo.HasNextPage)
        // {
        //     requestQuery.Body.Query.Pagination.After = responseQuery.Body.PageInfo.EndCursor;
        //     responseQuery = await service.EntityQueryAsync(requestQuery, _authHeaders);            
        // }

        var ownerIdFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = "ownerId"
        };
        ownerIdFilter.Values.Add(new ScalarValue {UnsignedBigintValue = 0});

        
        var inventoryIdFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = "inventoryId"
        };
        inventoryIdFilter.Values.Add(new ScalarValue {StringValue = ""});

        var stowRequest = new GetEntityStowContextsRequest
        {
            Body = new GetEntityStowContextsRequestBody
            {
                Query = new EntityStowContextQuery
                {
                    Filter = new EntityStowContextFilter
                    {
                        CompositeFilter = new EntityStowContextCompositeFilter
                        {
                            Operator = LogicalOperator.And,
                            Filters =
                            {
                                new EntityStowContextFilter { PropertyFilter = ownerIdFilter },
                                new EntityStowContextFilter { PropertyFilter = inventoryIdFilter }
                            }
                        }
                    },
                    Pagination = new PaginationArguments
                    {
                        First = 20,
                        After = ""
                    }
                }
            } 
        };
        

        var responseStow = await service.GetEntityStowContextsAsync(stowRequest, _authHeaders);
        
        Console.Write("hello");
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
        
        
        var response = await service.GetInventoriesAsync(request, _authHeaders);

        return response.Body.Inventories;
    }
    
    public bool IsConnected { get; private set; }

    private void CleanWatch()
    {
        if (_watcher != null)
        {
            _watcher.Dispose();
            _watcher = null;
            _authHeaders = null;
            _playerInfo = null;
            if (IsConnected)
            {
                IsConnected = false;
                OnConnectedChanged?.Invoke(this, IsConnected);
            }
        }

        if (null != _channel)
        {
            _channel.Dispose();
            _channel = null;
        }
    }
}