using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using StarXelem.Data;
using StarXelem.Models;
using StarXelem.Services;
using Microsoft.Extensions.Logging;

namespace StarXelem.ViewModels;

public partial class P4kShipTabViewModel : PageViewModelBase
{
    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly ILogger<P4kShipTabViewModel> _logger;
    private bool _isLoaded = false;
    private List<P4kShipModel> _allShips = new();

    public override string Name => "Loadout vaisseaux";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Target);

    [ObservableProperty] private ObservableCollection<P4kShipModel> _ships = new();
    [ObservableProperty] private P4kShipModel? _selectedShip;
    [ObservableProperty] private ObservableCollection<MissionEntity> _missionsForSelectedShip = new();
    [ObservableProperty] private ObservableCollection<P4kShipComponentModel> _components = new();
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _showOnlyVisible = true;
    [ObservableProperty] private List<P4kShipManufacturerModel> _allManufacturer = new();
    [ObservableProperty] private P4kShipManufacturerModel? _selectedManufacturer;
    private List<P4kShipManufacturerModel> _manufacturersWithShips = new();
    [ObservableProperty] private List<P4kShipComponentModel> _coolerList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _shieldList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _powerplantList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumdriveList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _radarList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumjumpList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _weaponList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _missileList = new();
    [ObservableProperty] private String _tagList = "";

    public P4kShipTabViewModel(ILocalDatabaseService localDatabaseService, ILogger<P4kShipTabViewModel> logger)
    {
        _localDatabaseService = localDatabaseService;
        _logger = logger;
    }

    protected override async Task OnFirstShowAsync()
    {
        if (IsLoading) return;

        UpdateIsLoading(true);
        try
        {
            // Load manufacturers from DB
            var manufacturers = await _localDatabaseService.GetManufacturersAsync().ConfigureAwait(false);
            var allManufacturerList = new List<P4kShipManufacturerModel>();
            var manufacturerNames = new HashSet<string>();

            foreach (var m in manufacturers)
            {
                if (manufacturerNames.Add(m.Name))
                {
                    allManufacturerList.Add(new P4kShipManufacturerModel { Name = m.Name });
                }
            }

            allManufacturerList = allManufacturerList.OrderBy(m => m.Name).ToList();

            // Load ships from DB
            var ships = await _localDatabaseService.GetShipsAsync().ConfigureAwait(false);
            var shipList = new List<P4kShipModel>();

            foreach (var ship in ships)
            {
                // Build tags string from ShipTags
                var tags = ship.ShipTags
                    .Select(st => $"[{st.Tag?.Name ?? st.TagSelfId}]")
                    .ToList();

                shipList.Add(new P4kShipModel
                {
                    Name = ship.LocalizedName,
                    TechnicalName = ship.TechnicalName,
                    Guid = ship.EntityClassGuid,
                    Manufacturer = ship.Manufacturer?.Name,
                    Tags = string.Join(", ", tags),
                    IsVisible = ship.IsVisible
                });
            }

            _allShips = shipList;

            // Filter manufacturers to only those with at least one ship
            var manufacturersWithShipsNames = shipList
                .Select(s => s.Manufacturer)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .ToHashSet();

            _manufacturersWithShips = allManufacturerList
                .Where(m => manufacturersWithShipsNames.Contains(m.Name))
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyManufacturerFilter();
                SelectedManufacturer = AllManufacturer.FirstOrDefault();
                ApplyShipFilter();
            }, DispatcherPriority.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ships from database");
        }
        finally
        {
            UpdateIsLoading(false);
        }
    }

    private void ApplyManufacturerFilter()
    {
        IEnumerable<string> visibleManufacturers = _allShips
            .Where(s => !string.IsNullOrEmpty(s.Manufacturer))
            .Select(s => s.Manufacturer!);

        if (ShowOnlyVisible)
            visibleManufacturers = visibleManufacturers.Where(m => _allShips.Any(s => s.Manufacturer == m && s.IsVisible));

        var filtered = _manufacturersWithShips
            .Where(m => visibleManufacturers.Contains(m.Name))
            .ToList();

        filtered.Insert(0, new P4kShipManufacturerModel { Name = "Tous" });
        AllManufacturer = filtered;
    }

    private void ApplyShipFilter()
    {
        IEnumerable<P4kShipModel> query = _allShips;
        if (ShowOnlyVisible)
            query = query.Where(s => s.IsVisible);

        if (SelectedManufacturer?.Name != "Tous" && !string.IsNullOrEmpty(SelectedManufacturer?.Name))
        {
            query = query.Where(s => SelectedManufacturer.Name == s.Manufacturer);
        }

        Ships = new ObservableCollection<P4kShipModel>(query.OrderBy(s => s.Name));
    }

    partial void OnSelectedShipChanged(P4kShipModel? value)
    {
        MissionsForSelectedShip.Clear();
        Components.Clear();

        if (value == null) return;

        // Update missions for selected ship
        _ = Task.Run(async () =>
        {
            var missions = await _localDatabaseService.GetMissionsForShipAsync(value.Guid).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MissionsForSelectedShip.Clear();
                foreach (var mission in missions)
                {
                    MissionsForSelectedShip.Add(mission);
                }
            });
        });

        // Load loadout from DB
        _ = Task.Run(async () =>
        {
            var loadoutEntries = await _localDatabaseService.GetShipLoadoutAsync(value.Guid).ConfigureAwait(false);

            var quantumdriveList = new List<P4kShipComponentModel>();
            var jumpdriveList = new List<P4kShipComponentModel>();
            var coolerList = new List<P4kShipComponentModel>();
            var powerplantList = new List<P4kShipComponentModel>();
            var shieldList = new List<P4kShipComponentModel>();
            var radarList = new List<P4kShipComponentModel>();
            var weaponList = new List<P4kShipComponentModel>();
            var missileList = new List<P4kShipComponentModel>();
            var componentsList = new List<P4kShipComponentModel>();

            foreach (var entry in loadoutEntries)
            {
                var componentClass = ParseComponentClass(entry.ComponentClass);
                var component = new P4kShipComponentModel
                {
                    PortName = entry.PortName,
                    DisplayName = entry.DisplayName,
                    Class = componentClass,
                    Size = entry.Size,
                    Grade = entry.Grade,
                    WeaponType = entry.WeaponType,
                    GuidanceType = entry.GuidanceType,
                    AlphaDamage = entry.AlphaDamage
                };

                componentsList.Add(component);

                switch (entry.ComponentType)
                {
                    case "QuantumDrive":
                        quantumdriveList.Add(component);
                        break;
                    case "Cooler":
                        coolerList.Add(component);
                        break;
                    case "Shield":
                        shieldList.Add(component);
                        break;
                    case "PowerPlant":
                        powerplantList.Add(component);
                        break;
                    case "JumpDrive":
                        jumpdriveList.Add(component);
                        break;
                    case "Radar":
                        radarList.Add(component);
                        break;
                    case "WeaponGun":
                    case "WeaponDefensive":
                    case "WeaponMining":
                    case "WeaponMount":
                    case "WeaponController":
                    case "Turret":
                    case "TurretBase":
                    case "UtilityTurret":
                    case "Bomb":
                    case "BombLauncher":
                        weaponList.Add(component);
                        break;
                    case "Missile":
                    case "MissileLauncher":
                    case "MissileController":
                        missileList.Add(component);
                        break;
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Components.Clear();
                foreach (var c in componentsList)
                    Components.Add(c);

                QuantumdriveList = quantumdriveList;
                QuantumjumpList = jumpdriveList;
                RadarList = radarList;
                CoolerList = coolerList;
                PowerplantList = powerplantList;
                ShieldList = shieldList;
                WeaponList = weaponList;
                MissileList = missileList;
                TagList = string.Join(", ", value.Tags.Split(", "));
            });
        });
    }

    private static ComponentClass ParseComponentClass(string className)
    {
        if (Enum.TryParse<ComponentClass>(className, out var result))
            return result;
        return ComponentClass.Unknown;
    }

    partial void OnSelectedManufacturerChanged(P4kShipManufacturerModel? value)
    {
        if (IsLoading || _allShips.Count == 0)
            return;

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
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyManufacturerFilter();
            ApplyShipFilter();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyManufacturerFilter();
                ApplyShipFilter();
            });
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
