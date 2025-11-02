using Sc.External.Services.Entitygraph.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarBreaker.P4k;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public class ItemViewModel : ViewModelBase
{
    private readonly IP4kService _p4KFileService;
    private readonly ILocationService _locationService;
    private readonly ItemsTabViewModel _parent;
    private readonly EntityItemQueryResult _entityNodeProperties;
    private readonly DataCoreTypedRecord? _entityClassProperties;

    public ulong Id => _entityNodeProperties.EntityNodeProperties.Geid;
    public ulong OwnerId => _entityNodeProperties.EntityNodeProperties.OwnerId;
    public string ParentUrn => _entityNodeProperties.EntityNodeProperties.ParentUrn;
    public EItemType ItemType => (EItemType)_entityNodeProperties.EntityNodeProperties.ItemTypeEnum;
    public EItemSubType ItemSubType => (EItemSubType)_entityNodeProperties.EntityNodeProperties.ItemSubTypeEnum;
    public uint LocationId => _entityNodeProperties.EntityNodeProperties.LocationId;
    public string? StowLocation => _entityNodeProperties.EntityNodeProperties.StowCtx?.Inv;
    public string? StowShard => _entityNodeProperties.EntityNodeProperties.StowCtx?.Shd;
    public string? LocalTypeName => _entityClassProperties?.RecordName;
    public Task<String?> Location => _locationService.ResolveLocation(_entityNodeProperties);
    
    public EntityEdge? Edge => _entityNodeProperties.EntityEdge;
    public EntityEdgeType? EdgeType => _entityNodeProperties.EntityEdge?.Type;
    public string? EdgeLocation => _entityNodeProperties.EntityEdge?.End.HasInventoryId ?? false ? _entityNodeProperties.EntityEdge?.End.InventoryId : _entityNodeProperties.EntityEdge?.End.EntityId.ToString();
    public uint? EdgeOccupancy => _entityNodeProperties.EntityEdge?.Properties?.Physical?.Occupancy;
    public uint? StackSize => _entityNodeProperties.EntityNodeProperties?.StackSize;
    public AttachmentType? EdgeAttachmentType => _entityNodeProperties.EntityEdge?.Properties?.AttachmentType;

//    public string? TypeGuid => _entityClassProperties?.Guid;
//    public string? TypeName => _entityClassProperties?.ClassName;
    public Task<string?> Name => GetLocaleValue();
    
    public ItemViewModel(IP4kService p4kFileService, ILocationService locationService, ItemsTabViewModel parent, EntityItemQueryResult entityItemResult, DataCoreTypedRecord? entityClassProperties = null)
    {
        _p4KFileService = p4kFileService;
        _locationService = locationService;
        _parent = parent;
        _entityNodeProperties = entityItemResult;
        _entityClassProperties = entityClassProperties;
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
}