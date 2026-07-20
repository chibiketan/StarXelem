using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels;

namespace StarXelem.Models;

public partial class ItemViewModel : ViewModelBase
{
    private readonly ILocationService _locationService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ItemsTabViewModel _parent;
    private readonly EntityItemQueryResult _entityNodeProperties;
    private readonly ScItemEntity? _scItem;
    private readonly ILocaleEntryRepository _localeRepository;
    private readonly System.Lazy<Task<string?>> _location;
    public readonly Lazy<Task<string>> _owner;

    public ulong Id => _entityNodeProperties.EntityNodeProperties.Geid;
    public ulong OwnerId => _entityNodeProperties.EntityNodeProperties.OwnerId;
    public Task<string> Owner => _owner.Value;
    public string ParentUrn => _entityNodeProperties.EntityNodeProperties.ParentUrn;
    public EItemType ItemType => (EItemType)_entityNodeProperties.EntityNodeProperties.ItemTypeEnum;
    public EItemSubType ItemSubType => (EItemSubType)_entityNodeProperties.EntityNodeProperties.ItemSubTypeEnum;
    public uint LocationId => _entityNodeProperties.EntityNodeProperties.LocationId;
    public string? StowLocation => null;
    public string? StowShard => null;
    public string? LocalTypeName => _scItem?.TechnicalName;
    public Task<string?> Location => _location.Value;
    
    public EntityEdge? Edge => _entityNodeProperties.EntityEdge;
    public EntityEdgeType? EdgeType => _entityNodeProperties.EntityEdge?.Type;
    public string? EdgeLocation => _entityNodeProperties.EntityEdge?.End.HasInventoryId ?? false ? _entityNodeProperties.EntityEdge?.End.InventoryId : _entityNodeProperties.EntityEdge?.End.EntityId.ToString();
    public uint? EdgeOccupancy => _entityNodeProperties.EntityEdge?.Properties?.Physical?.Occupancy;
    public uint ClassGuidCrc => _entityNodeProperties.EntityNodeProperties!.ClassGuidCrc;

    public uint? StackSize => _entityNodeProperties.EntityNodeProperties?.StackSize;
    public AttachmentType? EdgeAttachmentType => _entityNodeProperties.EntityEdge?.Properties?.AttachmentType;

//    public string? TypeGuid => _entityClassProperties?.Guid;
//    public string? TypeName => _entityClassProperties?.ClassName;
    public Task<string?> Name => GetLocaleValue();
    
    public ItemViewModel(ILocaleEntryRepository localeRepository, ILocationService locationService, IGrpcClientService grpcClientService, ItemsTabViewModel parent, EntityItemQueryResult entityItemResult, ScItemEntity? scItem = null)
    {
        _localeRepository = localeRepository;
        _locationService = locationService;
        _grpcClientService = grpcClientService;
        _parent = parent;
        _entityNodeProperties = entityItemResult;
        _scItem = scItem;
        _location = new Lazy<Task<string?>>(() => GetLocation().ContinueWith(t =>
        {
            OnPropertyChanged(nameof(Location));
            return t.Result;
        }));
        _owner = new Lazy<Task<string>>(() => _playerNames.GetOrAdd(OwnerId, GetOwner));
    }

    private async Task<string?> GetLocaleValue()
    {
        var localeKey = _scItem?.LocaleNameKey;

        if (!string.IsNullOrEmpty(localeKey))
        {
            var resolved = await _localeRepository.GetValueByKeyAsync(localeKey);
            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }
        }

        // Fallback sur le nom localisé directement
        return _scItem?.LocalizedName;
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
