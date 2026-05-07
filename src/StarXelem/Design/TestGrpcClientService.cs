using Sc.External.Common.Shard.V1;
using Sc.External.Services.BlueprintLibrary.V1;
using Sc.External.Services.Contacts.V1;
using Sc.External.Services.Entitlement.V1;
using Sc.External.Services.Entitygraph.V1;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.Design;

/// <summary>
/// Mock IGrpcClientService retournant des données prévisibles pour les tests headless.
/// Simule un état "Connecté" avec une liste d'amis structurée mais fictive.
/// </summary>
public class TestGrpcClientService : IGrpcClientService
{
    public event EventHandler<GrpcConnectionStatus>? OnStatusChanged;

    public GrpcConnectionStatus Status => GrpcConnectionStatus.Connected;
    public string? ErrorMessage => null;
    public string? CurrentShard => "pub_euw1_test_000";

    public Task InitClient(P4kFileModel p4kFile) => Task.CompletedTask;

    public async Task<IList<SpaceshipModel>> GetSpaceships()
    {
        await Task.Delay(10);
        return new List<SpaceshipModel>
        {
            new(new Entitlement
            {
                Name = "Carrack",
                EntityClassGuid = "GUID_CARRACK",
                ItemType = EntitlementItemType.Ship
            })
        };
    }

    public Task<IList<EntityItemQueryResult>> QueryGraphBySearch(ItemQueryModel queryModel)
        => Task.FromResult<IList<EntityItemQueryResult>>(new List<EntityItemQueryResult>());

    public Task<IList<EntityNodeProperties>> QueryGraphByParentUrnList(IList<string> parentUrnList)
        => Task.FromResult<IList<EntityNodeProperties>>(new List<EntityNodeProperties>());

    public Task<IList<EntityNodeProperties>> QueryGraphByGeidListWithoutOwner(IList<ulong> geidList)
        => Task.FromResult<IList<EntityNodeProperties>>(new List<EntityNodeProperties>());

    public Task<IList<EntityStowContext>> QueryStowContextByGeidList(IList<ulong> geidList)
        => Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());

    public Task<IList<EntityStowContext>> QueryStowContextByOwnerId(ulong ownerId)
        => Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());

    public Task<IList<Inventory>> QueryInventoryById(string id)
        => Task.FromResult<IList<Inventory>>(new List<Inventory>());

    public Task<string?> GetPlayerName(ulong playerId)
        => Task.FromResult<string?>((string)$"TestPlayer:{playerId}");

    public Task<IList<Node>> QueryInventoryBisById(string id)
        => Task.FromResult<IList<Node>>(new List<Node>());

    public Task<IList<InventoryNodeProperties>> QueryInventories()
        => Task.FromResult<IList<InventoryNodeProperties>>(new List<InventoryNodeProperties>());

    public Task TestRequest() => Task.CompletedTask;

    public Task<IList<Contact>> GetFriendList()
    {
        return Task.FromResult<IList<Contact>>([
            new Contact()
            {
                Account = new()
                {
                    AccountId = 1001,
                    DisplayName = "CommanderVik",
                    Nickname = "vik_2847",
                    PlayerId = 50001
                },
                Presence = new()
            },
            new Contact()
            {
                Account = new()
                {
                    AccountId = 1002,
                    DisplayName = "StarfarerNova",
                    Nickname = "nova_stars",
                    PlayerId = 50002
                },
                Presence = new()
            },
            new Contact()
            {
                Account = new()
                {
                    AccountId = 1003,
                    DisplayName = "OldCaptainReed",
                    Nickname = "reed_99",
                    PlayerId = 50003
                }
            }
        ]);
    }

    public Task<ShardInfo> GetShardInfo(int accountId)
        => Task.FromResult(new ShardInfo());

    public Task<IList<EntityStowContext>> GetEntityStowContextByParentUrnList(IList<string> urnList, List<uint> typeList)
        => Task.FromResult<IList<EntityStowContext>>(new List<EntityStowContext>());

    public Task<List<BlueprintEntry>> GetBlueprintList()
        => Task.FromResult(new List<BlueprintEntry>());
}
