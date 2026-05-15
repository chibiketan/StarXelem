using Sc.External.Common.Shard.V1;
using Sc.External.Services.BlueprintLibrary.V1;
using Sc.External.Services.Contacts.V1;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Friends.V1;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem;

public class DesignGrpcClientService : IGrpcClientService
{
    public event EventHandler<GrpcConnectionStatus>? OnStatusChanged;
    public Task InitClient(P4kFileModel p4kFile)
    {
        return Task.CompletedTask;
    }

    public async Task<IList<SpaceshipModel>> GetSpaceships()
    {
        await Task.Delay(2000);
        return new List<SpaceshipModel>{new(new Entitlement
        {
            EntityClassGuid = "GUIDOfTestShip",
//            ItemType = EntitlementItemType.Ship,
            Status = EntitlementStatus.Undelivered,
            Metadata = new EntitlementMetadata
            {
                Name = "My test ship",
                RealMoney = false,
            }
        })};
    }

    public Task<IList<EntityItemQueryResult>> QueryGraphBySearch(ItemQueryModel queryModel)
    {
        return Task.FromResult<IList<EntityItemQueryResult>>(new List<EntityItemQueryResult>());
    }

    public Task<IList<EntityNodeProperties>> QueryGraphByParentUrnList(IList<string> parentUrnList)
    {
        return Task.FromResult<IList<EntityNodeProperties>>(new List<EntityNodeProperties>());
    }

    public Task<IList<EntityNodeProperties>> QueryGraphByGeidListWithoutOwner(IList<ulong> geidList)
    {
        return Task.FromResult<IList<EntityNodeProperties>>(new List<EntityNodeProperties>());
    }

    public Task<IList<EntityStowContext>> QueryStowContextByGeidList(IList<ulong> geidList)
    {
        return Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());
    }

    public Task<IList<EntityStowContext>> QueryStowContextByOwnerId(ulong ownerId)
    {
        return Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());
    }
    
    public Task<IList<Inventory>> QueryInventoryById(String id)
    {
        return Task.FromResult<IList<Inventory>>(new List<Inventory>());
    }

    public Task<string?> GetPlayerName(ulong playerId)
    {
        return Task.FromResult($"TestPlayer:{playerId}");
    }

    public Task<IList<Node>> QueryInventoryBisById(String id)
    {
        return Task.FromResult<IList<Node>>(new List<Node>());
    }

    public Task<IList<InventoryNodeProperties>> QueryInventories()
    {
        return Task.FromResult<IList<InventoryNodeProperties>>(new List<InventoryNodeProperties>());
    }

    public Task TestRequest()
    {
        return Task.CompletedTask;
    }

    public GrpcConnectionStatus Status => GrpcConnectionStatus.Connected;
    public string? ErrorMessage => null;
    public ShardInfo? CurrentShardInfo => null;
    public string? CurrentShard => null;
    public Task<IList<Contact>> GetFriendList()
    {
        return Task.FromResult<IList<Contact>>(new List<Contact>
        {
            new()
            {
                Account = new()
                {
                    AccountId = 1,
                    AvatarUrl = "",
                    DisplayName = "Test 1",
                    Nickname = "Nick 1",
                    PlayerId = 1
                }
            }
        });
    }

    public Task<ShardInfo> GetShardInfo(int accountId)
    {
        return Task.FromResult(new ShardInfo());
    }

    public Task<IList<EntityStowContext>> GetEntityStowContextByParentUrnList(IList<string> urnList, List<uint> list)
    {
        return Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());
    }

    public Task<List<BlueprintEntry>> GetBlueprintList()
    {
        return Task.FromResult(new List<BlueprintEntry>());
    }
}