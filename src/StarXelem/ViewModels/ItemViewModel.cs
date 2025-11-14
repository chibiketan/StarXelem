using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarBreaker.P4k;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public partial class ItemViewModel : ViewModelBase
{
    private readonly IP4kService _p4KFileService;
    private readonly ILocationService _locationService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ItemsTabViewModel _parent;
    private readonly EntityItemQueryResult _entityNodeProperties;
    private readonly DataCoreTypedRecord? _entityClassProperties;
    private readonly System.Lazy<Task<string?>> _location;
    public readonly Lazy<Task<string>> _owner;

    public ulong Id => _entityNodeProperties.EntityNodeProperties.Geid;
    public ulong OwnerId => _entityNodeProperties.EntityNodeProperties.OwnerId;
    public Task<string> Owner => _owner.Value;
    public string ParentUrn => _entityNodeProperties.EntityNodeProperties.ParentUrn;
    public EItemType ItemType => (EItemType)_entityNodeProperties.EntityNodeProperties.ItemTypeEnum;
    public EItemSubType ItemSubType => (EItemSubType)_entityNodeProperties.EntityNodeProperties.ItemSubTypeEnum;
    public uint LocationId => _entityNodeProperties.EntityNodeProperties.LocationId;
    public string? StowLocation => _entityNodeProperties.EntityNodeProperties.StowCtx?.Inv;
    public string? StowShard => _entityNodeProperties.EntityNodeProperties.StowCtx?.Shd;
    public string? LocalTypeName => _entityClassProperties?.RecordName;
    public Task<string?> Location => _location.Value;
    
    public EntityEdge? Edge => _entityNodeProperties.EntityEdge;
    public EntityEdgeType? EdgeType => _entityNodeProperties.EntityEdge?.Type;
    public string? EdgeLocation => _entityNodeProperties.EntityEdge?.End.HasInventoryId ?? false ? _entityNodeProperties.EntityEdge?.End.InventoryId : _entityNodeProperties.EntityEdge?.End.EntityId.ToString();
    public uint? EdgeOccupancy => _entityNodeProperties.EntityEdge?.Properties?.Physical?.Occupancy;
    public uint? StackSize => _entityNodeProperties.EntityNodeProperties?.StackSize;
    public AttachmentType? EdgeAttachmentType => _entityNodeProperties.EntityEdge?.Properties?.AttachmentType;

//    public string? TypeGuid => _entityClassProperties?.Guid;
//    public string? TypeName => _entityClassProperties?.ClassName;
    public Task<string?> Name => GetLocaleValue();
    
    public ItemViewModel(IP4kService p4kFileService, ILocationService locationService, IGrpcClientService grpcClientService, ItemsTabViewModel parent, EntityItemQueryResult entityItemResult, DataCoreTypedRecord? entityClassProperties = null)
    {
        _p4KFileService = p4kFileService;
        _locationService = locationService;
        _grpcClientService = grpcClientService;
        _parent = parent;
        _entityNodeProperties = entityItemResult;
        _entityClassProperties = entityClassProperties;
        _location = new Lazy<Task<string?>>(() => GetLocation().ContinueWith(t =>
        {
            OnPropertyChanged(nameof(Location));
            return t.Result;
        }));
        _owner = new Lazy<Task<string>>(() => _playerNames.GetOrAdd(OwnerId, GetOwner));
    }

    private async Task<string?> GetLocaleValue()
    {
        var c = (_entityClassProperties?.Data as EntityClassDefinition)?.Components
            .OfType<SAttachableComponentParams>().FirstOrDefault();

        if (null != c)
        {
            return await _p4KFileService.GetLocaleValue(c.AttachDef.Localization.Name);
        }

        return null;
    }

    private async Task<string> GetOwner(ulong ownerId)
    {
        if (0 == ownerId)
        {
            return "Aucun [0]";
        }

        try
        {
            CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var name = await _grpcClientService.GetPlayerName(ownerId).WaitAsync(TimeSpan.FromSeconds(5), cts.Token);

            return $"{(String.IsNullOrEmpty(name) ? "Inconnu" : name)} [{ownerId}]";
        }
        catch
        {
            return $"ERREUR [{ownerId}]";
        }
    }

    private async Task<string?> GetLocation()
    {
        return await _locationService.ResolveLocation(_entityNodeProperties).ConfigureAwait(false);
        
    }
    
    private static ConcurrentDictionary<ulong, Task<string?>> _playerNames = new();
}