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
using Sc.External.Services.Entitlement.V1;
using Sc.Internal.Services.UniverseHierarchy.V1;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;
using StarXelem.ViewModels.Popup;

namespace StarXelem.ViewModels;

public partial class ShipTabViewModel : PageViewModelBase
{
    private const string dataCorePath = "Data\\Game2.dcb";
    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    private readonly ILocationService _locationService;
    private readonly IAllianceOrbitalService _allianceOrbitalService;
    public override string Name => "Mon hangar";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Home);
    
    [ObservableProperty]
    private Task<IList<SpaceshipModel>>? _spaceships;
    
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";

    /// <summary>Indique si la synchronisation de flotte est en cours.</summary>
    [ObservableProperty] private bool _isSyncing;

    public ShipTabViewModel(IGrpcClientService clientService, IP4kService p4kService, ILocationService locationService, IAllianceOrbitalService allianceOrbitalService)
    {
        _clientService = clientService;
        _p4KService = p4kService;
        _locationService = locationService;
        _allianceOrbitalService = allianceOrbitalService;

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
            var record = await _p4KService.GetRecordWithSpecificDepth(new CigGuid(spaceship.Entitlement.EntityClassGuid), 1);

            if (null != record)
            {
                spaceship.ClassRecordName = record.RecordName.Split('.', 2).Last();
                spaceship.EntityClassDefinition = record.Data as EntityClassDefinition;
                spaceship.Shipname = await _p4KService.GetEntityClassName(record.Data as EntityClassDefinition) ?? "inconnue";
            }
        }
        
        // récupérer les instances de vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Récupération des instances de vaisseaux");
        var spaceshipUrnList = spaceships.Select(v => v.Entitlement.Urn).ToList();

        var queryParameters = new ItemQueryModel
        {
            ParentUrnList = spaceshipUrnList,
            useConnectedUserOwner = true,
            TypeList = [EItemType.NOITEM_Vehicle]
        };
        
        var spaceshipEntityList = await _clientService.QueryGraphBySearch(queryParameters);

        foreach (var spaceship in spaceships)
        {
            var spaceshipEntity = spaceshipEntityList.FirstOrDefault(e => e.EntityNodeProperties!.ParentUrn == spaceship.Entitlement.Urn);
            
            if (null != spaceshipEntity)
            {
                spaceship.EntityProperties = spaceshipEntity;
            }
        }
        
        // TODO récupérer les emplacements des vaisseaux, à virer ?
        foreach (var spaceship in spaceships)
        {
            if (null == spaceship.EntityProperties)
            {
                spaceship.ReadableLocation = await _locationService.ResolveEntityLocation(spaceship.Location);
            }
            else
            {
                spaceship.ReadableLocation = await _locationService.ResolveLocation(spaceship.EntityProperties, [EItemType.NOITEM_Vehicle]);
            }
        }
        
        var stowContextList = await _clientService.GetEntityStowContextByParentUrnList(spaceshipUrnList, spaceships.Select(s => Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([new CigGuid(s.Entitlement.EntityClassGuid)]))).ToList());

        foreach (var stowContext in stowContextList)
        {
            var ss = spaceships.FirstOrDefault(s => s.Entitlement.Urn == stowContext.ParentUrn);

            if (null != ss)
            {
                ss.StowContext = stowContext;
            }
        }
        
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        Spaceships = Task.FromResult(spaceships);
        IsLoading = false;
        await Dispatcher.UIThread.InvokeAsync(() => SendFleetToOrbitalAllianceCommand.NotifyCanExecuteChanged());
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