using Sc.External.Services.Entitlement.V1;
using Sc.External.Services.Entitygraph.V1;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem;

public class DesignGrpcClientService : IGrpcClientService
{
    public event EventHandler<bool>? OnConnectedChanged;
    public Task InitClient(P4kFileModel p4kFile)
    {
        return Task.CompletedTask;
    }

    public async Task<IList<SpaceshipModel>> GetSpaceships()
    {
        await Task.Delay(2000);
        return new List<SpaceshipModel>{new(new Entitlement
        {
            Name = "My test ship",
            EntityClassGuid = "GUIDOfTestShip",
            RealMoney = false,
            ItemType = EntitlementItemType.Ship,
            Status = EntitlementStatus.Unclaimed
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

    public bool IsConnected => true;
}