using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services;
using Microsoft.Extensions.Logging;

namespace StarXelem.ViewModels;

public partial class P4kShipTabViewModel : PageViewModelBase
{
    private const string DataCorePath = @"Data\Game2.dcb";
    private readonly IP4kService _p4kService;
    private readonly ILogger<P4kShipTabViewModel> _logger;
    private bool _isLoaded = false;
    private List<P4kShipModel> _allShips = new();

    public override string Name => "P4K Ships";
    public override string Icon => nameof(Symbol.Target);

    [ObservableProperty] private ObservableCollection<P4kShipModel> _ships = new();
    [ObservableProperty] private P4kShipModel? _selectedShip;
    [ObservableProperty] private ObservableCollection<P4kShipComponentModel> _components = new();
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _showOnlyVisible = true;

    public P4kShipTabViewModel(IP4kService p4kService, ILogger<P4kShipTabViewModel> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
    }

    protected override async Task OnFirstShowAsync()
    {
        if (IsLoading || _p4kService.SelectedP4KFile == null) return;

        IsLoading = true;
        try
        {
            var shipList = new List<P4kShipModel>();

            var records = _p4kService.GetAllEntityClassDefinition();

            await foreach (var record in records)
            {
                var entityDef = (EntityClassDefinition)record.Data;
                
                var vehicleParams = entityDef.Components.OfType<VehicleComponentParams>().FirstOrDefault();
                if (vehicleParams != null)
                {
                    var eaEntityDataParams = entityDef.StaticEntityClassData.OfType<EAEntityDataParams>().FirstOrDefault();
                    var technicalName = record.RecordName;
                    var displayName = await _p4kService.GetLocaleValue(vehicleParams.vehicleName) ?? technicalName;
                    var manufacturer = await _p4kService.GetLocaleValue(vehicleParams.manufacturer?.Localization.Name) ?? vehicleParams.manufacturer?.Localization.Name;
                    var tags = String.Join(", ", (entityDef.tags?.Where(t => null != t?.tagName).Select(t => $"[{t.tagName}]") ?? Enumerable.Empty<string>())
                        .Concat(eaEntityDataParams?.inclusionParams.tags.tags.Where(t => null != t).Select(t => $"[{t.tagName}]") ?? Enumerable.Empty<string>() ));
                
                    shipList.Add(new P4kShipModel
                    {
                        Name = displayName,
                        TechnicalName = technicalName,
                        EntityClass = entityDef,
                        Manufacturer = manufacturer,
                        Tags = tags,
                        IsVisible = eaEntityDataParams?.inclusionMode == EAEntityInclusionMode.ReadyToInclude
                    });
                }
            }
            
            _allShips = shipList;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ApplyShipFilter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ships from P4K");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyShipFilter()
    {
        IEnumerable<P4kShipModel> query = _allShips;
        if (ShowOnlyVisible)
            query = query.Where(s => s.IsVisible);

        Ships = new ObservableCollection<P4kShipModel>(query.OrderBy(s => s.Name));
    }

    void VisitLoadoutEntries(SItemPortLoadoutBaseParams? loadout, Action<SItemPortLoadoutEntryParams> visitor)
    {
        if (loadout is SItemPortLoadoutManualParams manualLoadout)
        {
            foreach (var entry in manualLoadout.entries)
            {
                visitor(entry);
                VisitLoadoutEntries(entry.loadout, visitor);
            }
        }
    }

    partial void OnSelectedShipChanged(P4kShipModel? value)
    {
        var prefix = new Dictionary<string, string>()
        {
            { "hardpoint_cooler", "cooler" },
            { "hardpoint_power_plant", "powerplant" },
            { "hardpoint_powerplant", "powerplant" },
            { "hardpoint_shield", "shield" },
            { "hardpoint_radar", "Radar" },
            { "hardpoint_quantum_drive", "QuantumDrive" }, // jumpdrive is internal to this
            { "hardpoint_weapon", "armes endpoint (gimbal) et missile rack" },
            { "hardpoint_gun", "armes endpoint (weapon)" },
            { "hardpoint_missile", "missile (rack ?)" },
            { "missile", "missile" },
        };

        Components.Clear();
        if (value == null) return;

        // Utiliser le displayIcon présent dans (EntityUIDisplayParams)StaticEntityClassData[0].displayIcon ?
        var defaultLoadout = value.EntityClass.Components.OfType<SEntityComponentDefaultLoadoutParams>().FirstOrDefault();
        
        VisitLoadoutEntries(defaultLoadout?.loadout, loadoutEntry =>
        {
                var flag = "Unknown";
                var foundPrefix = prefix.FirstOrDefault(p => loadoutEntry.itemPortName.StartsWith(p.Key, StringComparison.InvariantCultureIgnoreCase));

                if (null != foundPrefix.Value)
                {
                    flag = foundPrefix.Value;
                }
                
                // TODO filtrer pour ne récupérer que les armes, les missiles et les composants internes
                // TODO gérer plusieurs listes pour avoir un affichage plus propre/pro !
                
                Components.Add(new P4kShipComponentModel
                {
                    PortName = loadoutEntry.itemPortName,
                    DisplayName = !String.IsNullOrEmpty(loadoutEntry.entityClassName) ? loadoutEntry.entityClassName : loadoutEntry.entityClassReference?.Category,
                    MinSize = "",
                    MaxSize = "",
                    Flags = flag
                });

                //loadoutEntry.inventoryContainer.inventoryItems.
        });
        
        //defaultLoadout.loadout.
        // var portContainer = value.EntityClass.Components.OfType<SItemPortContainerComponentParams>().FirstOrDefault();
        // if (portContainer != null)
        // {
        //     foreach (var port in portContainer.Ports)
        //     {
        //         Components.Add(new P4kShipComponentModel
        //         {
        //             PortName = port.Name,
        //             DisplayName = port.DisplayName,
        //             MinSize = port.MinSize.ToString(),
        //             MaxSize = port.MaxSize.ToString(),
        //             Flags = port.Flags
        //         });
        //     }
        // }
    }

    partial void OnShowOnlyVisibleChanged(bool value)
    {
        // Rafraîchir la liste lors du changement de la case à cocher
        // Sécuriser l'appel côté UI
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            ApplyShipFilter();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyShipFilter);
        }
    }
}
