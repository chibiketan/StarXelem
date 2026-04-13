using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Cig.Protocols.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Sc.Internal.Services.UniverseHierarchy.V1;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.Services.LocationService;

namespace StarXelem.ViewModels;

public partial class ShipTabViewModel : PageViewModelBase
{
    private const string dataCorePath = "Data\\Game2.dcb";
    private readonly IGrpcClientService  _clientService;
    private readonly IP4kService _p4KService;
    private readonly ILocationService _locationService;
    public override string Name => "Mon hangar";
    public override string Icon => nameof(Symbol.Home);
    [ObservableProperty] public Task<IList<SpaceshipModel>>? _spaceships;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _treatmentStatus = "";

    public ShipTabViewModel(IGrpcClientService clientService, IP4kService p4kService, ILocationService locationService)
    {
        _clientService = clientService;
        _p4KService = p4kService;
        _locationService = locationService;

        _p4KService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;
        _clientService.OnConnectedChanged += (sender, b) => LoadShipNotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanLoadShipList))]
    public async Task LoadShipList()
    {
        IsLoading = true;
        _locationService.ClearCache();
        TreatmentStatus = "Appel RSI";
        var spaceships = await _clientService.GetSpaceships();
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement p4k");
        // chargement du fichier p4k
        await _p4KService.OpenP4k(_p4KService.SelectedP4KFile.Path, new Progress<double>(), new Progress<double>());
        //  chargement de la traduction
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement traduction");
        var globalEntry = _p4KService.P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
        Dictionary<string, string> lang = new Dictionary<string, string>(500);
        string iniFile;
        using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
        {
            while (await sr.ReadLineAsync() is { } line) {

                if (!String.IsNullOrEmpty(line))
                {
                    var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    var key = parts[0];
                    var value = parts[1];
                    
                    if (key.EndsWith(",P"))
                        key = key[..^2];
                    lang.Add($"@{key}", value);
                }
            }
        }
        await globalEntry.DisposeAsync();
        
        // Chargement des informations de classes sur les vaisseaux
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Chargement classes des vaisseaux");
        var entry = _p4KService.P4KFileSystem.OpenRead(dataCorePath);
        var dcb = new DataCoreDatabase(entry);
        var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        await entry.DisposeAsync();

        //var titi = df.DataCore.Database.RecordDefinitions.FirstOrDefault(r => r.FileName.Contains("2273540638"));
        
        
        foreach (var spaceship in spaceships)
        {
            var record = df.GetFromRecord(new CigGuid(spaceship.Entitlement.EntityClassGuid));

            if (null != record)
            {
                spaceship.EntityClassDefinition = record.Data as EntityClassDefinition;
                var toto = (record.Data as EntityClassDefinition).Components.FirstOrDefault(t => t is SCItemPurchasableParams) as SCItemPurchasableParams;

                if (null != toto)
                {
                    try
                    {
                        spaceship.Shipname = lang[toto.displayName];
                    }
                    catch (Exception)
                    {
                        // Ignore for now
                    }
                }
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
        
        //var spaceshipEntityList = await _clientService.QueryGraphByParentUrnList(spaceshipUrnList);
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
                spaceship.ReadableLocation = await _locationService.ResolveLocation(spaceship.EntityProperties);
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
        
        // var locationGeidList = spaceships.Where(s => s.EntityProperties?.StowCtx?.Inv != null).Select(s => StowLocationToGeid(s.EntityProperties.StowCtx.Inv)).ToList();
        // // var response = await _clientService.QueryGraphByGeidListWithoutOwner(locationGeidList);
        //
        // await _clientService.TestRequest();
        //
        //  // var responseZones = await _clientService.QueryGraphByGeidListWithoutOwner(new List<ulong> { 2273540638, 3490636373 });
        // //
        // // var responseStowShips = await _clientService.QueryStowContextByGeidList(spaceshipEntityList.Select(s => s.Geid).ToList());
        // //
        // // var responseStowAll = await _clientService.QueryStowContextByOwnerId(spaceshipEntityList.First().OwnerId);
        //
        // // var responseInventory = await _clientService.QueryInventoryById(spaceshipEntityList.First().StowCtx.Inv);
        // // var responseInventory = await _clientService.QueryInventoryById("201962463294:Location:3490636373");
        //
        // //var responseInventoryBis = await _clientService.QueryInventoryBisById("TOTO");
        
        
        await Dispatcher.UIThread.InvokeAsync(() => TreatmentStatus = "Terminé");
        Spaceships = Task.FromResult(spaceships);
        IsLoading = false;
    }

    private ulong StowLocationToGeid(string urn)
    {
        var parts = urn.Split("Location:", 2, StringSplitOptions.TrimEntries);
        return ulong.Parse(parts[1]);
    }

    public bool CanLoadShipList()
    {
        return _clientService.IsConnected && !IsLoading;
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
}