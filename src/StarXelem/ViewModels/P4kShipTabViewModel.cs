using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarXelem.Data;
using StarXelem.Models;
using StarXelem.Services;
using Microsoft.Extensions.Logging;

namespace StarXelem.ViewModels;

public partial class P4kShipTabViewModel : PageViewModelBase
{
    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);

    private readonly ILocalDatabaseService _localDatabaseService;
    private readonly ILogger<P4kShipTabViewModel> _logger;
    private readonly HashSet<string> _selectedManufacturers = new(StringComparer.Ordinal);
    private List<P4kShipModel> _allShips = new();
    private CancellationTokenSource? _filterCts;

    public override string Name => "Loadout vaisseaux";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Airplane);

    [ObservableProperty] private ObservableCollection<P4kShipModel> _ships = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] private P4kShipModel? _selectedShip;
    [ObservableProperty] private ObservableCollection<MissionEntity> _missionsForSelectedShip = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PortsToggleLabel))] private ObservableCollection<P4kShipComponentModel> _components = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;
    [ObservableProperty] private bool _includeHidden;
    [ObservableProperty] private string _search = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))] private int _filteredCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PortsToggleLabel))] private bool _showAllPorts;
    [ObservableProperty] private List<P4kShipComponentModel> _coolerList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _shieldList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _powerplantList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumdriveList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _radarList = new();
    [ObservableProperty] private List<P4kShipComponentModel> _quantumjumpList = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasWeapons))] private List<P4kShipComponentModel> _weaponList = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMissiles))] private List<P4kShipComponentModel> _missileList = new();
    [ObservableProperty] private List<P4kShipComponentSection> _componentSections = new();
    [ObservableProperty] private String _tagList = "";

    public P4kShipTabViewModel(ILocalDatabaseService localDatabaseService, ILogger<P4kShipTabViewModel> logger)
    {
        _localDatabaseService = localDatabaseService;
        _logger = logger;
        AllManufacturersChip = new FilterChipViewModel(null, "Tous", ToggleManufacturer) { IsSelected = true };
    }

    /// <summary>Puce « Tous » : retire le filtre constructeur.</summary>
    public FilterChipViewModel AllManufacturersChip { get; }

    /// <summary>Puce « Tous » puis puces des constructeurs, cumulables ; aucune cochée = tous les constructeurs.</summary>
    public ObservableCollection<FilterChipViewModel> ManufacturerChips { get; } = new();

    public string ResultSummary => $"{FilteredCount} sur {_allShips.Count(ship => IncludeHidden || ship.IsVisible)} vaisseaux";

    public bool ShowNoMatch => _allShips.Count > 0 && FilteredCount == 0 && !IsFiltering;

    public bool IsBusy => IsLoading || IsFiltering;

    public string BusyMessage => IsLoading ? "Chargement des vaisseaux…" : "Filtrage en cours…";

    public bool HasSelection => SelectedShip != null;

    public bool HasWeapons => WeaponList.Count > 0;

    public bool HasMissiles => MissileList.Count > 0;

    public string PortsToggleLabel => ShowAllPorts ? "Masquer les points d'emport" : $"Afficher les {Components.Count} points d'emport";

    protected override async Task OnFirstShowAsync()
    {
        if (IsLoading) return;

        UpdateIsLoading(true);
        try
        {
            var ships = await _localDatabaseService.GetShipsAsync().ConfigureAwait(false);
            var shipList = new List<P4kShipModel>();

            foreach (var ship in ships)
            {
                // Tri alphabétique : deux vaisseaux se comparent tag à tag, toujours dans le même ordre.
                var tagNames = ship.ShipTags
                    .Select(st => st.Tag?.Name ?? st.TagSelfId)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                shipList.Add(new P4kShipModel
                {
                    Name = ship.LocalizedName,
                    TechnicalName = ship.TechnicalName,
                    Guid = ship.EntityClassGuid,
                    Manufacturer = ship.Manufacturer?.Name,
                    Tags = string.Join(", ", tagNames.Select(tag => $"[{tag}]")),
                    TagNames = tagNames,
                    IsVisible = ship.IsVisible
                });
            }

            shipList.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allShips = shipList;
                BuildManufacturerChips();
                ScheduleFilters(TimeSpan.Zero);
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

    partial void OnSelectedShipChanged(P4kShipModel? value)
    {
        MissionsForSelectedShip.Clear();
        Components = new ObservableCollection<P4kShipComponentModel>();
        ComponentSections = new List<P4kShipComponentSection>();
        WeaponList = new List<P4kShipComponentModel>();
        MissileList = new List<P4kShipComponentModel>();

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

            // Une carte par type de composant présent, dans l'ordre de la maquette.
            var sections = new List<P4kShipComponentSection>();
            void AddSection(string singular, string plural, List<P4kShipComponentModel> list)
            {
                if (list.Count > 0)
                {
                    sections.Add(new P4kShipComponentSection(list.Count > 1 ? plural : singular, list));
                }
            }

            AddSection("Générateur", "Générateurs", powerplantList);
            AddSection("Refroidisseur", "Refroidisseurs", coolerList);
            AddSection("Bouclier", "Boucliers", shieldList);
            AddSection("Radar", "Radars", radarList);
            AddSection("Réacteur quantique", "Réacteurs quantiques", quantumdriveList);
            AddSection("Moteur de saut", "Moteurs de saut", jumpdriveList);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // L'utilisateur a pu choisir un autre vaisseau pendant le chargement.
                if (!ReferenceEquals(SelectedShip, value))
                {
                    return;
                }

                Components = new ObservableCollection<P4kShipComponentModel>(componentsList);
                QuantumdriveList = quantumdriveList;
                QuantumjumpList = jumpdriveList;
                RadarList = radarList;
                CoolerList = coolerList;
                PowerplantList = powerplantList;
                ShieldList = shieldList;
                WeaponList = weaponList;
                MissileList = missileList;
                ComponentSections = sections;
                TagList = value.Tags;
            });
        });
    }

    private static ComponentClass ParseComponentClass(string className)
    {
        if (Enum.TryParse<ComponentClass>(className, out var result))
            return result;
        return ComponentClass.Unknown;
    }

    [RelayCommand]
    private void TogglePorts() => ShowAllPorts = !ShowAllPorts;

    partial void OnSearchChanged(string value) => ScheduleFilters(SearchFilterDelay);

    partial void OnIncludeHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(ResultSummary));
        ScheduleFilters(SelectionFilterDelay);
    }

    private void BuildManufacturerChips()
    {
        ManufacturerChips.Clear();
        ManufacturerChips.Add(AllManufacturersChip);
        var manufacturers = _allShips
            .Select(ship => ship.Manufacturer)
            .Where(manufacturer => !string.IsNullOrEmpty(manufacturer))
            .Distinct()
            .Order(StringComparer.CurrentCultureIgnoreCase);

        foreach (var manufacturer in manufacturers)
        {
            ManufacturerChips.Add(new FilterChipViewModel(manufacturer, manufacturer!, ToggleManufacturer));
        }

        _selectedManufacturers.RemoveWhere(manufacturer => ManufacturerChips.All(chip => chip.Key != manufacturer));
    }

    private void ToggleManufacturer(string? key)
    {
        if (key == null)
        {
            _selectedManufacturers.Clear();
        }
        else if (!_selectedManufacturers.Remove(key))
        {
            _selectedManufacturers.Add(key);
        }

        ScheduleFilters(SelectionFilterDelay);
    }

    /// <summary>
    /// Planifie un filtrage différé : toute demande encore en attente ou en cours est annulée, de sorte qu'une rafale de
    /// modifications ne produit qu'un seul recalcul, après la dernière. À appeler depuis le thread d'UI.
    /// </summary>
    private void ScheduleFilters(TimeSpan delay)
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        // Copie de l'état des filtres : le calcul qui suit ne doit lire aucune propriété du ViewModel.
        var criteria = new ShipFilterCriteria(
            _allShips,
            string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            IncludeHidden,
            _selectedManufacturers.Count == 0 ? null : _selectedManufacturers.ToHashSet(StringComparer.Ordinal));

        IsFiltering = true;
        _ = RunFilterAsync(criteria, delay, cts.Token);
    }

    private async Task RunFilterAsync(ShipFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => ComputeFilterOutcome(criteria), token);
            await Dispatcher.UIThread.InvokeAsync(() => DisplayResult(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des vaisseaux");
        }
        finally
        {
            // Une demande annulée a forcément une remplaçante, qui reste propriétaire de l'indicateur.
            if (!token.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() => IsFiltering = false, DispatcherPriority.MaxValue);
            }
        }
    }

    private static ShipFilterOutcome ComputeFilterOutcome(ShipFilterCriteria criteria)
    {
        // La recherche porte sur le nom et le constructeur, comme l'annonce le champ.
        var searched = criteria.Ships
            .Where(ship => criteria.IncludeHidden || ship.IsVisible)
            .Where(ship => criteria.Needle == null
                           || ship.Name.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase)
                           || (ship.Manufacturer?.Contains(criteria.Needle, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToList();

        var manufacturerCounts = searched
            .Where(ship => !string.IsNullOrEmpty(ship.Manufacturer))
            .GroupBy(ship => ship.Manufacturer!)
            .ToDictionary(group => group.Key, group => group.Count());

        var rows = searched
            .Where(ship => criteria.Manufacturers == null || (ship.Manufacturer != null && criteria.Manufacturers.Contains(ship.Manufacturer)))
            .ToList();

        return new ShipFilterOutcome(rows, searched.Count, manufacturerCounts);
    }

    private void DisplayResult(ShipFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        AllManufacturersChip.Count = outcome.SearchedCount;
        AllManufacturersChip.IsSelected = _selectedManufacturers.Count == 0;
        foreach (var chip in ManufacturerChips.Where(chip => chip.Key != null))
        {
            chip.Count = outcome.ManufacturerCounts.GetValueOrDefault(chip.Key!);
            chip.IsSelected = _selectedManufacturers.Contains(chip.Key!);
        }

        FilteredCount = outcome.Ships.Count;

        if (!Ships.SequenceEqual(outcome.Ships))
        {
            var selected = SelectedShip;
            Ships = new ObservableCollection<P4kShipModel>(outcome.Ships);
            SelectedShip = selected != null && outcome.Ships.Contains(selected) ? selected : null;
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

    /// <summary>État des filtres au moment de la demande, lu par le calcul hors du thread d'UI.</summary>
    private sealed record ShipFilterCriteria(IReadOnlyList<P4kShipModel> Ships, string? Needle, bool IncludeHidden, HashSet<string>? Manufacturers);

    /// <summary>Résultat d'un filtrage : lignes à afficher et compteurs des puces.</summary>
    private sealed record ShipFilterOutcome(IReadOnlyList<P4kShipModel> Ships, int SearchedCount, Dictionary<string, int> ManufacturerCounts);
}
