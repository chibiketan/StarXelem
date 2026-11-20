using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Cig.Protocols.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sc.External.Services.Entitlement.V1;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.Services.ShipLoadout;
using StarXelem.ViewModels.Popup;

namespace StarXelem.ViewModels;

public partial class ShipTabViewModel : PageViewModelBase
{
    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly ILocationService _locationService;
    private readonly IAllianceOrbitalService _allianceOrbitalService;
    private readonly IShipLoadoutService _shipLoadoutService;
    private readonly ILogger<ShipTabViewModel> _logger;
    public override string Name => "Mon hangar";
    protected override string NavIconData => "M3 11l9-8 9 8v10H3zM9 21v-7h6v7";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Home);
    
    [ObservableProperty]
    private Task<IList<SpaceshipModel>>? _spaceships;
    
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";

    /// <summary>Indique si la synchronisation de flotte est en cours.</summary>
    [ObservableProperty] private bool _isSyncing;

    /// <summary>Fiche loadout ouverte par-dessus le tableau, ou <c>null</c> quand le tableau est affiché.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadoutOpen))]
    private OwnedShipLoadoutViewModel? _loadoutDetail;

    /// <summary>Vrai quand la fiche loadout remplace le tableau.</summary>
    public bool IsLoadoutOpen => LoadoutDetail != null;

    public ShipTabViewModel(IGrpcClientService clientService, IP4kService p4kService, ILocalDatabaseService localDatabaseService, ILocationService locationService, IAllianceOrbitalService allianceOrbitalService,
        IShipLoadoutService shipLoadoutService, ILogger<ShipTabViewModel> logger)
    {
        _logger = logger;
        _shipLoadoutService = shipLoadoutService;
        _clientService = clientService;
        _p4KService = p4kService;
        _localDatabaseService = localDatabaseService;
        _locationService = locationService;
        _allianceOrbitalService = allianceOrbitalService;
        InitializeDisplay();

        _p4KService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;
        _clientService.OnStatusChanged += (sender, _) => LoadShipNotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanLoadShipList))]
    public async Task LoadShipList()
    {
        IsLoading = true;
        _locationService.ClearCache();
        TreatmentStatus = "Appel RSI";
        var spaceships = await _clientService.GetSpaceships();
        // Chargement des informations de classes sur les vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement classes des vaisseaux");

        foreach (var spaceship in spaceships)
        {
            var guid = spaceship.Entitlement.EntityClassGuid;
            var shipEntity = await _localDatabaseService.GetShipByGuidAsync(guid);

            if (shipEntity != null)
            {
                spaceship.ClassRecordName = shipEntity.TechnicalName;
                spaceship.Shipname = shipEntity.LocalizedName;
            }
            else
            {
                spaceship.ClassRecordName = guid;
                spaceship.Shipname = guid;
            }
        }
        
        // récupérer les instances de vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Récupération des instances de vaisseaux");
        var spaceshipUrnList = spaceships.Select(v => v.Entitlement.Urn).ToList();
        var stowContextList = await _clientService.GetEntityStowContextByParentUrnList(spaceshipUrnList, spaceships.Select(s => Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([new CigGuid(s.Entitlement.EntityClassGuid)]))).ToList());

        foreach (var stowContext in stowContextList)
        {
            var ss = spaceships.FirstOrDefault(s => s.Entitlement.Urn == stowContext.ParentUrn);

            if (null != ss)
            {
                ss.StowContext = stowContext;
            }
        }
        
        var queryParameters = new ItemQueryModel
        {
            ParentUrnList = spaceshipUrnList,
            useConnectedUserOwner = true,
            UseProjection = true,
            TypeList = [EItemType.NOITEM_Vehicle],
            InventoryIdList = spaceships.Where(s => null != s.StowContext?.InventoryId).Select(s => s.StowContext!.InventoryId)
                .Where(l => l.Contains("Player", StringComparison.InvariantCulture) || (l.Contains("Location", StringComparison.InvariantCulture) && l.StartsWith(_clientService.CurrentPlayerGeid?.ToString() ?? "0")))
                .ToList()
        };
        
        // TODO si l'inventoryId appartient à un joueur une erreur d'accès à une ressource apartenant à un autre joueur est levé.
        // Dans ce cas la (inventoryId d'un autre joueur) alors l'ASOP affiche "captured", comment reproduire ce comportement ?
        // Erreur : Unhandled exception. Grpc.Core.RpcException: Status(StatusCode="PermissionDenied", Detail="Cannot access inventories owned by another player")
        var spaceshipEntityList = await _clientService.QueryGraphBySearch(queryParameters);
        var attachedByParent = IndexAttachedEntities(spaceshipEntityList);

        foreach (var spaceship in spaceships)
        {
            var spaceshipEntity = spaceshipEntityList.FirstOrDefault(e => e.EntityNodeProperties!.ParentUrn == spaceship.Entitlement.Urn
                && e.EntityNodeProperties.ItemTypeEnum == (int)EItemType.NOITEM_Vehicle);

            if (null != spaceshipEntity)
            {
                spaceship.EntityProperties = spaceshipEntity;
                spaceship.AttachedEntities = CollectAttachedEntities(spaceshipEntity, attachedByParent);
            }
        }
        
        // TODO récupérer les emplacements des vaisseaux, à virer ?
        foreach (var spaceship in spaceships)
        {
            if (null == spaceship.EntityProperties)
            {
                spaceship.ResolvedLocation = await _locationService.ResolveEntityLocation(spaceship.Location);
            }
            else
            {
                spaceship.ResolvedLocation = await _locationService.ResolveLocation(spaceship.EntityProperties, [EItemType.NOITEM_Vehicle]);
            }
        }
        
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        Spaceships = Task.FromResult(spaceships);
        IsLoading = false;
        await Dispatcher.UIThread.InvokeAsync(() => SendFleetToOrbitalAllianceCommand.NotifyCanExecuteChanged());
    }

    /// <summary>Ouvre la fiche loadout du vaisseau ; la liste de gauche reprend les vaisseaux du tableau, dans le même ordre.</summary>
    [RelayCommand]
    private void OpenLoadout(SpaceshipModel? ship)
    {
        if (ship == null)
        {
            return;
        }

        var ships = DisplayedShips.Contains(ship) ? DisplayedShips.ToList() : _allShips.ToList();
        LoadoutDetail = new OwnedShipLoadoutViewModel(_shipLoadoutService, ships, ship, () => LoadoutDetail = null, _logger);
    }

    /// <summary>Range les objets montés (arête ATTACHED_TO) par identifiant de leur porteur.</summary>
    private static Dictionary<ulong, List<EntityItemQueryResult>> IndexAttachedEntities(IEnumerable<EntityItemQueryResult> entities)
    {
        var byParent = new Dictionary<ulong, List<EntityItemQueryResult>>();
        foreach (var entity in entities)
        {
            if (entity.EntityEdge is { Type: EntityEdgeType.AttachedTo, End.HasEntityId: true } edge)
            {
                if (!byParent.TryGetValue(edge.End.EntityId, out var children))
                {
                    children = [];
                    byParent[edge.End.EntityId] = children;
                }

                children.Add(entity);
            }
        }

        return byParent;
    }

    /// <summary>
    /// Parcourt l'arbre des objets montés depuis le vaisseau. Un véhicule embarqué (moto, snub) a son propre loadout : on s'arrête à lui.
    /// </summary>
    private static List<EntityItemQueryResult> CollectAttachedEntities(EntityItemQueryResult root, Dictionary<ulong, List<EntityItemQueryResult>> byParent)
    {
        var result = new List<EntityItemQueryResult>();
        var visited = new HashSet<ulong> { root.EntityNodeProperties!.Geid };
        var pending = new Stack<ulong>();
        pending.Push(root.EntityNodeProperties.Geid);

        while (pending.Count > 0)
        {
            if (!byParent.TryGetValue(pending.Pop(), out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                var geid = child.EntityNodeProperties!.Geid;
                if (child.EntityNodeProperties.ItemTypeEnum == (int)EItemType.NOITEM_Vehicle || !visited.Add(geid))
                {
                    continue;
                }

                result.Add(child);
                pending.Push(geid);
            }
        }

        return result;
    }

    private ulong StowLocationToGeid(string urn)
    {
        var parts = urn.Split("Location:", 2, StringSplitOptions.TrimEntries);
        return ulong.Parse(parts[1]);
    }

    public bool CanLoadShipList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    private void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, on change tout, reconnexion en prime
        LoadShipListCommand.NotifyCanExecuteChanged();
    }

    private void LoadShipNotifyCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            loadShipListCommand?.NotifyCanExecuteChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(LoadShipNotifyCanExecuteChanged);
        }
    }

    /// <summary>
    /// Regroupe les vaisseaux par EntityClassGuid et compte le nombre par classe.
    /// Ne retourne que les vaisseaux possédés (non Unclaimed).
    /// </summary>
    private List<FleetSyncItem> GetFleetSummary()
    {
        var ships = Spaceships?.Result ?? [];
        return ships
            .GroupBy(s => s.Entitlement.EntityClassGuid)
            .Select(g => new FleetSyncItem
            {
                EntityClassGuid = g.Key,
                Quantity = g.Count()
            })
            .ToList();
    }

    /// <summary>
    /// Ouvre la popup de synchronisation de flotte vers Alliance Orbital.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendFleetToOrbitalAlliance))]
    private void SendFleetToOrbitalAlliance() => OpenFleetSyncPopup();

    /// <summary>Vérifie que la liste des vaisseaux est chargée et contient au moins un élément.</summary>
    private bool CanSendFleetToOrbitalAlliance() => Spaceships?.Result is { Count: > 0 };

    /// <summary>
    /// Prépare le popup de synchronisation flotte et l'ouvre via le messager.
    /// </summary>
    private void OpenFleetSyncPopup()
    {
        var vm = App.Current.Services.GetRequiredService<FleetSyncPopupContentViewModel>();
        vm.FleetToSend = GetFleetSummary();
        WeakReferenceMessenger.Default.Send(new ShowPopupMessage(
            showCloseButton: true,
            onClose: null,
            viewModel: vm
        ));
    }
}