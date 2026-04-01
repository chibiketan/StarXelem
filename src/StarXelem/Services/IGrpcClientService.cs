using Sc.External.Common.Shard.V1;
using Sc.External.Services.BlueprintLibrary.V1;
using Sc.External.Services.Contacts.V1;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Friends.V1;
using StarXelem.Models;

namespace StarXelem.Services;

public interface IGrpcClientService
{
    event EventHandler<bool> OnConnectedChanged;
    
    Task InitClient(P4kFileModel p4kFile);
    Task<IList<SpaceshipModel>> GetSpaceships();
    Task<IList<EntityItemQueryResult>> QueryGraphBySearch(ItemQueryModel queryModel);
    Task<IList<EntityNodeProperties>> QueryGraphByParentUrnList(IList<string> parentUrnList);
    Task<IList<EntityNodeProperties>> QueryGraphByGeidListWithoutOwner(IList<ulong> geidList);
    // Task<IList<EntityStowContext>> QueryStowContextByGeidList(IList<ulong> geidList);
    // Task<IList<EntityStowContext>> QueryStowContextByOwnerId(ulong ownerId);
    Task<IList<Inventory>> QueryInventoryById(String id);
    Task<string?> GetPlayerName(ulong playerId);
    Task<IList<Node>> QueryInventoryBisById(String id);
    Task<IList<InventoryNodeProperties>> QueryInventories();
    Task TestRequest();
    bool IsConnected { get; }
    Task<IList<Contact>> GetFriendList();
    Task<ShardInfo> GetShardInfo(int accountId);
    Task<IList<EntityStowContext>> GetEntityStowContextByParentUrnList(IList<string> urnList, List<uint> crcTypeList);
    Task<List<BlueprintEntry>> GetBlueprintList();
}