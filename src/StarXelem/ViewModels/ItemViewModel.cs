using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly System.Lazy<Task<ResolvedLocation?>> _location;
    public readonly Lazy<Task<string>> _owner;
    // Types sur lesquels s'arrête la résolution d'un emplacement : joueur et vaisseaux, armes, armures, caisses.
    // Les pièces intermédiaires (supports, portes, modules…) sont traversées pour remonter à l'un d'eux.
    private static readonly IList<EItemType> LocationStopTypes = Enum.GetValues<EItemType>()
        .Where(type => IsLocationStopType(type.ToString()))
        .ToList();
    [ObservableProperty] private ItemLocationDisplay? _locationDisplay;
    private ushort? _contentQuality;
    private ulong? _contentQuantity;
    private bool _isCrafted;
    private readonly Lazy<Task<CraftedItemInfo?>> _crafted;

    public ulong Id => _entityNodeProperties.EntityNodeProperties.Geid;
    public ulong OwnerId => _entityNodeProperties.EntityNodeProperties.OwnerId;
    public Task<string> Owner => _owner.Value;
    public string ParentUrn => _entityNodeProperties.EntityNodeProperties.ParentUrn;
    public EItemType ItemType => (EItemType)_entityNodeProperties.EntityNodeProperties.ItemTypeEnum;
    public string ItemTypeLabel => ItemTypeLabels.Get(ItemType);
    public EItemSubType ItemSubType => (EItemSubType)_entityNodeProperties.EntityNodeProperties.ItemSubTypeEnum;
    public uint LocationId => _entityNodeProperties.EntityNodeProperties.LocationId;
    public string? StowLocation => null;
    public string? StowShard => null;
    public string? LocalTypeName => _scItem?.TechnicalName;
    public Task<ResolvedLocation?> Location => _location.Value;

    public EntityEdge? Edge => _entityNodeProperties.EntityEdge;
    public EntityEdgeType? EdgeType => _entityNodeProperties.EntityEdge?.Type;

    public string? EdgeLocation => _entityNodeProperties.EntityEdge?.End.HasInventoryId ?? false
        ? _entityNodeProperties.EntityEdge?.End.InventoryId
        : _entityNodeProperties.EntityEdge?.End.EntityId.ToString();

    public uint? EdgeOccupancy => _entityNodeProperties.EntityEdge?.Properties?.Physical?.Occupancy;
    public uint ClassGuidCrc => _entityNodeProperties.EntityNodeProperties!.ClassGuidCrc;

    public uint? StackSize => _entityNodeProperties.EntityNodeProperties?.StackSize;

    public ushort? ContentQuality => _contentQuality;

    /// <summary>Quantité réellement contenue, en µSCU (variable « rsrccntr.map »), ou null si l'objet n'est pas un conteneur de ressource.</summary>
    public ulong? ContentQuantity => _contentQuantity;

    /// <summary>Vrai si l'objet porte la variable « Crafting.PersistentItemData ».</summary>
    public bool IsCrafted => _isCrafted;

    /// <summary>Données de craft décodées (valeurs améliorées, matériaux), lues à la première demande ; null si l'objet n'est pas crafté.</summary>
    public Task<CraftedItemInfo?> Crafted => _crafted.Value;

    /// <summary>Classe de l'objet (RecordId de ScItems), ou null si elle est absente de la base locale.</summary>
    public string? ClassRecordId => _scItem?.RecordId;

    /// <summary>Ressource contenue d'après la classe de l'objet, ou null.</summary>
    public string? ContainedResourceRef => _scItem?.ContainedResourceRef;
    public AttachmentType? EdgeAttachmentType => _entityNodeProperties.EntityEdge?.Properties?.AttachmentType;

//    public string? TypeGuid => _entityClassProperties?.Guid;
//    public string? TypeName => _entityClassProperties?.ClassName;
    // Mise en cache : le nom est lu à chaque affichage de cellule, tri et filtrage. Sans cela, chaque accès lançait
    // une nouvelle requête sur la base de locales.
    private readonly Lazy<Task<string?>> _name;
    public Task<string?> Name => _name.Value;
    
    /// <param name="knownLocation">
    /// Emplacement déjà résolu (relu du cache) : il est repris tel quel, sans interroger le serveur, injoignable quand le jeu est fermé.
    /// </param>
    public ItemViewModel(ILocaleEntryRepository localeRepository, ILocationService locationService, IGrpcClientService grpcClientService, ItemsTabViewModel parent,
        EntityItemQueryResult entityItemResult, ScItemEntity? scItem = null, ResolvedLocation? knownLocation = null)
    {
        _localeRepository = localeRepository;
        _locationService = locationService;
        _grpcClientService = grpcClientService;
        _parent = parent;
        _entityNodeProperties = entityItemResult;
        _scItem = scItem;
        _name = new Lazy<Task<string?>>(GetLocaleValue);
        _location = knownLocation != null
            ? new Lazy<Task<ResolvedLocation?>>(Task.FromResult<ResolvedLocation?>(knownLocation))
            : new Lazy<Task<ResolvedLocation?>>(() => GetLocation().ContinueWith(t =>
            {
                OnPropertyChanged(nameof(Location));
                return t.Result;
            }));
        _owner = new Lazy<Task<string>>(() => _playerNames.GetOrAdd(OwnerId, GetOwner));
        ExtractQuality();
        _crafted = new Lazy<Task<CraftedItemInfo?>>(() => _isCrafted
            ? _parent.ReadCraftedInfoAsync(entityItemResult)
            : Task.FromResult<CraftedItemInfo?>(null));
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

    private static bool IsLocationStopType(string typeName)
    {
        return typeName.StartsWith("NOITEM_", StringComparison.Ordinal)
               || typeName.StartsWith("Char_Armor_", StringComparison.Ordinal)
               || typeName.StartsWith("FPS_Weapon", StringComparison.Ordinal)
               || typeName is nameof(EItemType.WeaponPersonal) or nameof(EItemType.Container) or nameof(EItemType.InventoryContainer);
    }

    private async Task<ResolvedLocation?> GetLocation()
    {
        return await _locationService.ResolveLocation(_entityNodeProperties, LocationStopTypes).ConfigureAwait(false);
        
    }

    /// <summary>
    /// Parcours les données de snapshot pour trouver les valeurs de qualité.
    /// Pour l'instant ne fonctionne que pour les coneteneurs simple (hand mining, déjà raffiné)
    /// </summary>
    private void ExtractQuality()
    {
        if (_entityNodeProperties.EntitySnapshot is null)
        {
            // Pas de snapshot, pas de qualité
            return;
        }

        foreach (var snapshotVariable in _entityNodeProperties.EntitySnapshot.Variables)
        {
            var varName = (SnapshotVariableNameCrc)Enum.ToObject(typeof(SnapshotVariableNameCrc), snapshotVariable.NameCrc);

            switch (varName)
            {
                case SnapshotVariableNameCrc.ConteneurRessources:
                    // Un "conteneur" (commodity) qui contient une ressource avec de la qualité
                    if (snapshotVariable.Snapshot.Length >= 7)
                    {
                        _contentQuality = BinaryPrimitives.ReadUInt16LittleEndian(snapshotVariable.Snapshot.Span[(1 + 4)..]);
                    }
                    else
                    {
                        Debug.WriteLine($"La donnée est trop petite pour la qualité, id {_entityNodeProperties.EntityNodeProperties?.Geid}");
                    }

                    // Entrée : clé u32, qualité u16, puis quantité i64 en µSCU (64 cSCU dans une caisse de 1 SCU de minerai).
                    if (snapshotVariable.Snapshot.Length >= 1 + 6 + 8)
                    {
                        var quantity = BinaryPrimitives.ReadInt64LittleEndian(snapshotVariable.Snapshot.Span[(1 + 6)..]);
                        _contentQuantity = quantity >= 0 ? (ulong)quantity : null;
                    }
                    else
                    {
                        Debug.WriteLine($"La donnée est trop petite pour la quantité, id {_entityNodeProperties.EntityNodeProperties?.Geid}");
                    }
                    break;
                case SnapshotVariableNameCrc.SouteMinage:
                    // Contenu des sacs de minage, extraire ?
                    break;
                case SnapshotVariableNameCrc.DonneesCraft:
                    // Le décodage (valeurs améliorées, matériaux) passe par DataCore : il n'est fait qu'à l'affichage du détail.
                    _isCrafted = true;
                    break;
                case SnapshotVariableNameCrc.QualiteCraft:
                    break;
                default:
                    // Pas de valeur reconnue, rien à faire
                    break;
            }
        }
    }

    enum SnapshotVariableNameCrc: uint
    {
        ConteneurRessources = 4_185_637_390, // "rsrccntr.map"
        SouteMinage         = 3_143_846_246, // "miningcargodata"
        DonneesCraft        = 2_174_272_695, // "crafting.persistentitemdata"
        QualiteCraft        = 3_382_973_477  // u16 LE, affiché = brut / 8.851
    }
    
    private static ConcurrentDictionary<ulong, Task<string?>> _playerNames = new();
    
}
