using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.RegularExpressions;
using Avalonia.Threading;
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
    private readonly IP4kService _p4kService;
    private readonly ILogger<P4kShipTabViewModel> _logger;
    private bool _isLoaded = false;
    private List<P4kShipModel> _allShips = new();

    public override string Name => "Loadout vaisseaux";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Target);

    [ObservableProperty] private ObservableCollection<P4kShipModel> _ships = new();
    [ObservableProperty] private P4kShipModel? _selectedShip;
    [ObservableProperty] private ObservableCollection<P4kShipComponentModel> _components = new();
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _showOnlyVisible = true;
    [ObservableProperty] private List<P4kShipManufacturerModel> _allManufacturer = new();
    [ObservableProperty] private P4kShipManufacturerModel? _selectedManufacturer;
    [ObservableProperty] private List<P4kShipComponentModel> _coolerList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _shieldList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _powerplantList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumdriveList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _radarList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumjumpList = new();
    [ObservableProperty] private String _tagList = "";
    private Dictionary<String, CigGuid> _componentGuidMap = new(); 

    public P4kShipTabViewModel(IP4kService p4kService, ILogger<P4kShipTabViewModel> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
    }

    protected override async Task OnFirstShowAsync()
    {
        if (IsLoading || _p4kService.SelectedP4KFile == null) return;

        UpdateIsLoading(true);
        try
        {
            var shipList = new List<P4kShipModel>();
            var uniqueManufacturerSet = new HashSet<SCItemManufacturer>(20);
            var componentGuidMap = new Dictionary<string, CigGuid>(200);

            var records = _p4kService.GetAllEntityClassDefinition(1);

            await foreach (var record in records.ConfigureAwait(false))
            {
                var entityDef = (EntityClassDefinition)record.Data;
                
                // Traitement des véhicules
                var vehicleParams = entityDef.Components.OfType<VehicleComponentParams>().FirstOrDefault();
                if (vehicleParams != null)
                {
                    var eaEntityDataParams = entityDef.StaticEntityClassData.OfType<EAEntityDataParams>().FirstOrDefault();
                    var technicalName = record.RecordName;
                    var displayName = await _p4kService.GetLocaleValue(vehicleParams.vehicleName).ConfigureAwait(false) ?? technicalName;
                    var manufacturer = await _p4kService.GetLocaleValue(vehicleParams.manufacturer?.Localization.Name).ConfigureAwait(false) ?? vehicleParams.manufacturer?.Localization.Name;
                    var tags = String.Join(", ", (entityDef.tags?.Where(t => null != t?.tagName).Select(t => $"[{t.tagName}]") ?? Enumerable.Empty<string>())
                        .Concat(eaEntityDataParams?.inclusionParams.tags.tags.Where(t => null != t).Select(t => $"[{t.tagName}]") ?? Enumerable.Empty<string>() ));

                    if (vehicleParams.manufacturer != null)
                    {
                        uniqueManufacturerSet.Add(vehicleParams.manufacturer);
                    }
                    
                    // var entitlementEntityParams = entityDef.StaticEntityClassData.OfType<DefaultEntitlementEntityParams>().FirstOrDefault();
                    var isVisible = !technicalName.Contains("_ai_", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.Contains("_unmanned_", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.Contains("salvageabledebris", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.Contains("_pu_", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.Contains("_ea_", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.Contains("_fleetweek", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_temp", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_template", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_tutorial", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_advocacy", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_indestructible", StringComparison.InvariantCultureIgnoreCase)
                                    && !technicalName.EndsWith("_pu", StringComparison.InvariantCultureIgnoreCase);
                
                    shipList.Add(new P4kShipModel
                    {
                        Name = displayName,
                        TechnicalName = technicalName,
                        EntityClass = entityDef,
                        Manufacturer = manufacturer,
                        Tags = tags,
                        // Pour être visible il doit être "inclus" et avoir un entitlement
                        // IsVisible = null != entitlementEntityParams && eaEntityDataParams?.inclusionMode == EAEntityInclusionMode.ReadyToInclude
                        IsVisible = isVisible
                    });
                }
                
                // Traitement des composants
                // On prépare un mapping entre nom du type de l'objet et son id pour le récupérer vite plus tard
                var attachableComponent = entityDef.Components.OfType<SAttachableComponentParams>().FirstOrDefault();
                
                if (null != attachableComponent)
                {
                    switch (attachableComponent.AttachDef.Type)
                    {
                        case EItemType.QuantumDrive:
                        case EItemType.Cooler:
                        case EItemType.Shield:
                        case EItemType.PowerPlant:
                        case EItemType.JumpDrive:
                        case EItemType.Radar:
                            componentGuidMap.Add(record.RecordName.Split(".", 2).Last(), record.RecordId);
                            break;
                    }
                }
                
                _componentGuidMap = componentGuidMap;
            }
            
            _allShips = shipList;
            // Mise à jour de la liste des fabricants
            var allManufacturer = new List<P4kShipManufacturerModel>(uniqueManufacturerSet.Count);
            
            foreach (var manufacturer in uniqueManufacturerSet)
            {
                allManufacturer.Add(new P4kShipManufacturerModel
                {
                    Manufacturer = manufacturer,
                    Name = await _p4kService.GetLocaleValue(manufacturer.Localization.Name) ?? manufacturer.Localization.Name
                });
                
            }
            
            allManufacturer = allManufacturer.OrderBy(m => m.Name).ToList();
            allManufacturer.Insert(0, new P4kShipManufacturerModel { Name = "Tous" });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AllManufacturer = allManufacturer;
                SelectedManufacturer = allManufacturer.First();
                ApplyShipFilter();
            }, DispatcherPriority.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ships from P4K");
        }
        finally
        {
            UpdateIsLoading(false);
        }
    }

    private void ApplyShipFilter()
    {
        IEnumerable<P4kShipModel> query = _allShips;
        if (ShowOnlyVisible)
            query = query.Where(s => s.IsVisible);

        if (null != SelectedManufacturer?.Manufacturer)
        {
            query = query.Where(s => SelectedManufacturer.Name == s.Manufacturer);
        }

        Ships = new ObservableCollection<P4kShipModel>(query.OrderBy(s => s.Name));
    }

    private void VisitLoadoutEntries(SItemPortLoadoutBaseParams? loadout, Action<SItemPortLoadoutEntryParams> visitor)
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
        var quantumdriveList = new List<P4kShipComponentModel>(); 
        var jumpdriveList = new List<P4kShipComponentModel>(); 
        var coolerList = new List<P4kShipComponentModel>(); 
        var powerplantList = new List<P4kShipComponentModel>(); 
        var shieldList = new List<P4kShipComponentModel>(); 
        var radarList = new List<P4kShipComponentModel>(); 
        
        VisitLoadoutEntries(defaultLoadout?.loadout, async loadoutEntry =>
        {
            var flag = "Unknown";
            var foundPrefix = prefix.FirstOrDefault(p => loadoutEntry.itemPortName.StartsWith(p.Key, StringComparison.InvariantCultureIgnoreCase));

            if (null != foundPrefix.Value)
            {
                flag = foundPrefix.Value;
            }

            EntityClassDefinition entityClass = null;
            
            // TODO filtrer pour ne récupérer que les armes, les missiles et les composants internes
            // TODO gérer plusieurs listes pour avoir un affichage plus propre/pro !
            if (!String.IsNullOrEmpty(loadoutEntry.entityClassName))
            {
                // On a une classe, on recherche l'objet réel
                if (_componentGuidMap.TryGetValue(loadoutEntry.entityClassName, out var guid))
                {
                    // On a un id, on récupère l'objet
                    var record = await _p4kService.GetEntityType(Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([guid]))).ConfigureAwait(false);

                    entityClass = (EntityClassDefinition)record!.Data;
                }
            }
            else if (null != loadoutEntry.entityClassReference)
            {
                entityClass = loadoutEntry.entityClassReference;
            }
            
            Components.Add(new P4kShipComponentModel
            {
                PortName = loadoutEntry.itemPortName,
                DisplayName = !String.IsNullOrEmpty(loadoutEntry.entityClassName) ? loadoutEntry.entityClassName : loadoutEntry.entityClassReference?.Category,
                Grade = "",
                Size = 0,
                Class = ComponentClass.Unknown
            });

            if (entityClass?.Components.OfType<SAttachableComponentParams>().FirstOrDefault() is { } test)
            {
                var size = test.AttachDef.Size;
                var grade = new String((char)(test.AttachDef.Grade - 1 + 'A'), 1);
                var eClass = ComponentClass.Unknown;

                var component = new P4kShipComponentModel()
                {
                    Grade = grade,
                    Size = size,
                    DisplayName = await _p4kService.GetLocaleValue(test.AttachDef.Localization.Name).ConfigureAwait(false) ?? "NOT FOUND",
                    PortName = loadoutEntry.itemPortName,
                    Class = await TranslateToComponentClass(test.AttachDef.Localization.Description).ConfigureAwait(false)
                };

                
                
                switch (test.AttachDef.Type)
                {
                    case EItemType.QuantumDrive:
                        quantumdriveList.Add(component);
                        break;
                    case EItemType.Cooler:
                        coolerList.Add(component);
                        break;
                    case EItemType.Shield:
                        shieldList.Add(component);
                        break;
                    case EItemType.PowerPlant:
                        powerplantList.Add(component);
                        break;
                    case EItemType.JumpDrive:
                        jumpdriveList.Add(component);
                        break;
                    case EItemType.Radar:
                        radarList.Add(component);
                        break;
                }
            }
        });

        QuantumdriveList = quantumdriveList;
        QuantumjumpList = jumpdriveList;
        RadarList = radarList;
        CoolerList = coolerList;
        PowerplantList = powerplantList;
        ShieldList = shieldList;
        TagList = String.Join(", ", value.Tags.Split(", "));
    }

    async Task<ComponentClass> TranslateToComponentClass(string name)
    {
        var description = await _p4kService.GetLocaleValue(name);
        var searchRegex = new Regex(@"Class:\s*(\w+)");
        var cClass = ComponentClass.Unknown;
        // On retire le '@' devant la description
        var searchResult = searchRegex.Match(description);
        if (searchResult.Success)
        {
            // On a trouvé la classe, on l'extrait
            if (!ComponentClass.TryParse(searchResult.Groups[1].Value, out cClass))
            {
                cClass = ComponentClass.Unknown;
            }
        }

        return cClass;
    }
    
    partial void OnSelectedManufacturerChanged(P4kShipManufacturerModel? value)
    {
        // La liste n'est pas encore chargée, on ne fait rien
        if (IsLoading || _allShips.Count == 0)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyShipFilter();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyShipFilter);
        }
    }

    partial void OnShowOnlyVisibleChanged(bool value)
    {
        // Rafraîchir la liste lors du changement de la case à cocher
        // Sécuriser l'appel côté UI
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyShipFilter();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyShipFilter);
        }
    }
    
    private void UpdateIsLoading(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsLoading = value;
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateIsLoading(value), DispatcherPriority.MaxValue);
        }
    }
}

