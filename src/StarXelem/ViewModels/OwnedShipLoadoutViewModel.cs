using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StarXelem.Models;
using StarXelem.Services.ShipLoadout;

namespace StarXelem.ViewModels;

/// <summary>
/// Fiche loadout d'un vaisseau possédé, ouverte depuis Mon hangar : liste des vaisseaux à gauche, composants, armes et missiles du vaisseau
/// sélectionné à droite. Les écarts avec l'équipement d'origine sont signalés et peuvent être isolés.
/// </summary>
public partial class OwnedShipLoadoutViewModel : ViewModelBase
{
    private readonly IShipLoadoutService _loadoutService;
    private readonly ILogger? _logger;
    private readonly Action _close;
    private CancellationTokenSource? _loadCts;

    /// <summary>Vaisseaux du hangar, dans l'ordre du tableau au moment de l'ouverture.</summary>
    public IReadOnlyList<SpaceshipModel> Ships { get; }

    [ObservableProperty] private SpaceshipModel? _selectedShip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadout), nameof(IsRealSource), nameof(IsOriginSource), nameof(SourceLabel), nameof(ComponentCount),
        nameof(WeaponCount), nameof(MissileCount), nameof(ModifiedCount), nameof(HasModified), nameof(ShowNoEquipment))]
    private OwnedShipLoadoutModel? _loadout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoEquipment))]
    private bool _isLoadingLoadout;

    /// <summary>N'affiche que les composants et armes qui diffèrent de l'origine.</summary>
    [ObservableProperty] private bool _showDifferencesOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoEquipment))]
    private IReadOnlyList<OwnedShipComponentSection> _componentSections = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWeapons), nameof(ShowNoEquipment))]
    private IReadOnlyList<OwnedShipWeaponRow> _weapons = [];

    public OwnedShipLoadoutViewModel(IShipLoadoutService loadoutService, IReadOnlyList<SpaceshipModel> ships, SpaceshipModel selectedShip, Action close,
        ILogger? logger = null)
    {
        _loadoutService = loadoutService;
        _logger = logger;
        _close = close;
        Ships = ships;
        SelectedShip = selectedShip;
    }

    public bool HasLoadout => Loadout != null;
    public bool IsRealSource => Loadout?.Source == OwnedShipLoadoutSource.Real;
    public bool IsOriginSource => Loadout?.Source == OwnedShipLoadoutSource.Origin;
    public string SourceLabel => IsRealSource ? "Loadout réel" : "Loadout d'origine";
    public int ComponentCount => Loadout?.ComponentCount ?? 0;
    public int WeaponCount => Loadout?.WeaponCount ?? 0;
    public int MissileCount => Loadout?.MissileCount ?? 0;
    public int ModifiedCount => Loadout?.ModifiedCount ?? 0;
    public bool HasModified => ModifiedCount > 0;
    public bool HasWeapons => Weapons.Count > 0;

    /// <summary>Vrai si, une fois chargé, rien n'est à afficher (aucun équipement, ou aucun écart avec le filtre actif).</summary>
    public bool ShowNoEquipment => HasLoadout && !IsLoadingLoadout && ComponentSections.Count == 0 && Weapons.Count == 0;

    /// <summary>Message affiché quand la liste est vide.</summary>
    public string NoEquipmentMessage => ShowDifferencesOnly ? "Aucun écart avec l'équipement d'origine." : "Aucun équipement trouvé pour ce vaisseau.";

    /// <summary>Nom principal du vaisseau : celui donné par le joueur, sinon le modèle.</summary>
    public string ShipTitle => SelectedShip is { HasCustomName: true } ship ? ship.Name : SelectedShip?.Shipname ?? string.Empty;

    /// <summary>Modèle et nom technique, sous le titre.</summary>
    public string ShipSubtitle => SelectedShip is not { } ship
        ? string.Empty
        : string.Join(" · ", new[] { ship.HasCustomName ? ship.Shipname : null, ship.TechnicalName }.Where(part => !string.IsNullOrEmpty(part)));

    [RelayCommand]
    private void Close()
    {
        _loadCts?.Cancel();
        _close();
    }

    partial void OnSelectedShipChanged(SpaceshipModel? value)
    {
        OnPropertyChanged(nameof(ShipTitle));
        OnPropertyChanged(nameof(ShipSubtitle));
        _ = LoadAsync(value);
    }

    partial void OnShowDifferencesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(NoEquipmentMessage));
        ApplyFilter();
    }

    partial void OnLoadoutChanged(OwnedShipLoadoutModel? value) => ApplyFilter();

    private async Task LoadAsync(SpaceshipModel? ship)
    {
        _loadCts?.Cancel();
        Loadout = null;
        if (ship == null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        IsLoadingLoadout = true;

        try
        {
            var loadout = await _loadoutService.GetLoadoutAsync(ship, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Loadout = loadout;
            }
        }
        catch (OperationCanceledException)
        {
            // Un autre vaisseau a été sélectionné entre-temps.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erreur lors du chargement du loadout de {Ship}", ship.Shipname);
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoadingLoadout = false;
            }
        }
    }

    private void ApplyFilter()
    {
        if (Loadout == null)
        {
            ComponentSections = [];
            Weapons = [];
            return;
        }

        if (!ShowDifferencesOnly)
        {
            ComponentSections = Loadout.ComponentSections;
            Weapons = Loadout.Weapons;
            return;
        }

        ComponentSections = Loadout.ComponentSections
            .Select(section => section with { Components = section.Components.Where(component => component.IsModified).ToList() })
            .Where(section => section.Components.Count > 0)
            .ToList();
        Weapons = Loadout.Weapons.Where(row => row.IsModified).ToList();
    }
}
