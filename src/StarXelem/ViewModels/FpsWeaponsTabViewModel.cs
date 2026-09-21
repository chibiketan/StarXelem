using System.Collections.ObjectModel;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.ViewModels;

/// <summary>
/// ViewModel de l'écran des armes FPS : tableau filtrable par nom, par plage de dégâts
/// et par plage de portée d'efficacité. Les données proviennent de la base locale
/// reconstruite depuis les fichiers du jeu.
/// </summary>
public partial class FpsWeaponsTabViewModel : PageViewModelBase
{
    private const string AllClassesLabel = "Toutes les classes";
    private readonly IFpsWeaponRepository _fpsWeaponRepository;
    private readonly ILogger<FpsWeaponsTabViewModel> _logger;
    private List<FpsWeaponModel> _allWeapons = new();
    private bool _isApplyingBounds;
    private CancellationTokenSource? _filterCts;

    // Délai d'inactivité de la saisie avant de filtrer : assez court pour rester réactif,
    // assez long pour qu'une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan NameFilterDelay = TimeSpan.FromMilliseconds(300);
    // Curseurs, classe et case à cocher : délai réduit, suffisant pour absorber le glissement d'un curseur.
    private static readonly TimeSpan ControlFilterDelay = TimeSpan.FromMilliseconds(120);

    public override string Name => "Armes FPS";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.Target);

    [ObservableProperty] private ObservableCollection<FpsWeaponModel> _weapons = new();
    [ObservableProperty] private FpsWeaponModel? _selectedWeapon;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _nameFilter = string.Empty;
    // Collection créée une fois pour toutes et complétée en place : remplacer l'instance ferait
    // perdre sa sélection au ComboBox, qui afficherait un champ vide.
    [ObservableProperty] private ObservableCollection<string> _weaponClassList = [AllClassesLabel];
    [ObservableProperty] private string _selectedWeaponClass = AllClassesLabel;

    /* ---- Bornes du filtre de dégâts, déduites des données chargées ---- */

    [ObservableProperty] private double _damageLowerBound;
    [ObservableProperty] private double _damageUpperBound = 100;
    [ObservableProperty] private double _damageMin;
    [ObservableProperty] private double _damageMax = 100;

    /* ---- Bornes du filtre de portée d'efficacité ---- */

    [ObservableProperty] private double _effectiveRangeLowerBound;
    [ObservableProperty] private double _effectiveRangeUpperBound = 100;
    [ObservableProperty] private double _effectiveRangeMin;
    [ObservableProperty] private double _effectiveRangeMax = 100;

    /// <summary>
    /// Conserve dans les résultats les armes dont aucune munition n'a de chute de dégâts,
    /// et donc aucune portée d'efficacité exploitable : sans cette option, le curseur de portée
    /// les ferait disparaître silencieusement.
    /// </summary>
    [ObservableProperty] private bool _includeWeaponsWithoutEffectiveRange = true;

    public FpsWeaponsTabViewModel(IFpsWeaponRepository fpsWeaponRepository, ILogger<FpsWeaponsTabViewModel> logger)
    {
        _fpsWeaponRepository = fpsWeaponRepository;
        _logger = logger;
    }

    protected override async Task OnFirstShowAsync()
    {
        if (IsLoading)
        {
            return;
        }

        UpdateIsLoading(true);

        try
        {
            var entities = await _fpsWeaponRepository.GetAllAsync().ConfigureAwait(false);
            var weapons = entities.Select(MapToModel).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allWeapons = weapons;

                foreach (var label in BuildClassLabels(weapons))
                {
                    WeaponClassList.Add(label);
                }

                ResetBounds(weapons);
                ApplyFilters();
            }, DispatcherPriority.Default);

            _logger.LogInformation("Loaded {Count} FPS weapons from the local database.", weapons.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des armes FPS depuis la base locale");
        }
        finally
        {
            UpdateIsLoading(false);
        }
    }

    // Le chargement se poursuit hors du thread d'UI après ConfigureAwait(false) : IsLoading
    // ne doit être écrit que depuis le thread d'UI.
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

    private static FpsWeaponModel MapToModel(FpsWeaponEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.LocalizedName,
        TechnicalName = entity.TechnicalName,
        WeaponClass = entity.WeaponClass,
        Manufacturer = entity.Manufacturer?.Name,
        Size = entity.Size,
        Grade = entity.Grade,
        VariantCount = entity.VariantCount,
        VariantNames = entity.VariantNames,
        MagazineSize = entity.MagazineSize,
        RepoolUnstowDuration = entity.RepoolUnstowDuration,
        RepoolBulletsPerSecond = entity.RepoolBulletsPerSecond,
        RepoolFullMagMergeDuration = entity.RepoolFullMagMergeDuration,
        PrimaryModeName = entity.PrimaryModeName,
        PrimaryDamageType = entity.PrimaryDamageType,
        PrimaryDamagePerShot = entity.PrimaryDamagePerShot,
        PrimaryExplosiveDamage = entity.PrimaryExplosiveDamage,
        PrimaryPelletCount = entity.PrimaryPelletCount,
        PrimaryFireRate = entity.PrimaryFireRate,
        PrimaryProjectileSpeed = entity.PrimaryProjectileSpeed,
        PrimaryMaxRange = entity.PrimaryMaxRange,
        PrimaryEffectiveRange = entity.PrimaryEffectiveRange,
        PrimaryDamageFloorRange = entity.PrimaryDamageFloorRange,
        HasSecondaryFire = entity.HasSecondaryFire,
        SecondaryModeName = entity.SecondaryModeName,
        SecondaryDamageType = entity.SecondaryDamageType,
        SecondaryDamagePerShot = entity.SecondaryDamagePerShot,
        SecondaryExplosiveDamage = entity.SecondaryExplosiveDamage,
        SecondaryPelletCount = entity.SecondaryPelletCount,
        SecondaryFireRate = entity.SecondaryFireRate,
        SecondaryProjectileSpeed = entity.SecondaryProjectileSpeed,
        SecondaryMaxRange = entity.SecondaryMaxRange,
        SecondaryEffectiveRange = entity.SecondaryEffectiveRange,
        SecondaryDamageFloorRange = entity.SecondaryDamageFloorRange
    };

    /// <summary>
    /// Libellés de classe présents dans les données, triés, sans l'entrée « toutes les classes »
    /// qui occupe déjà le premier rang de la collection.
    /// </summary>
    private static List<string> BuildClassLabels(List<FpsWeaponModel> weapons) => weapons
        .Select(w => w.WeaponClassLabel)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(label => label, StringComparer.CurrentCulture)
        .ToList();

    /// <summary>
    /// Recalcule les bornes des curseurs à partir des valeurs réellement présentes, et remet
    /// les curseurs à pleine amplitude. Les bornes ne sont jamais codées en dur : une mise à jour
    /// du jeu qui ferait apparaître une arme plus puissante reste filtrable.
    /// </summary>
    private void ResetBounds(List<FpsWeaponModel> weapons)
    {
        _isApplyingBounds = true;

        try
        {
            var damages = weapons
                .SelectMany(w => new[]
                {
                    w.PrimaryDamagePerShot, w.SecondaryDamagePerShot,
                    w.PrimaryExplosiveDamage, w.SecondaryExplosiveDamage
                })
                .Where(d => d is > 0f)
                .Select(d => (double)d!.Value)
                .ToList();

            DamageLowerBound = damages.Count > 0 ? Math.Floor(damages.Min()) : 0;
            DamageUpperBound = damages.Count > 0 ? Math.Ceiling(damages.Max()) : 100;
            DamageMin = DamageLowerBound;
            DamageMax = DamageUpperBound;

            var ranges = weapons
                .SelectMany(w => new[] { w.PrimaryEffectiveRange, w.SecondaryEffectiveRange })
                .Where(r => r is > 0f)
                .Select(r => (double)r!.Value)
                .ToList();

            EffectiveRangeLowerBound = ranges.Count > 0 ? Math.Floor(ranges.Min()) : 0;
            EffectiveRangeUpperBound = ranges.Count > 0 ? Math.Ceiling(ranges.Max()) : 100;
            EffectiveRangeMin = EffectiveRangeLowerBound;
            EffectiveRangeMax = EffectiveRangeUpperBound;
        }
        finally
        {
            _isApplyingBounds = false;
        }
    }

    /// <summary>
    /// Applique les filtres immédiatement, de façon synchrone. Réservé au chargement initial,
    /// où la liste doit être remplie avant que l'indicateur de chargement ne disparaisse.
    /// </summary>
    private void ApplyFilters()
    {
        if (_isApplyingBounds)
        {
            return;
        }

        _filterCts?.Cancel();
        Weapons = new ObservableCollection<FpsWeaponModel>(FilterWeapons(_allWeapons, CaptureCriteria()));
    }

    /// <summary>
    /// Planifie un filtrage différé : toute demande encore en attente ou en cours est annulée,
    /// de sorte qu'une rafale de modifications ne produit qu'un seul recalcul, après la dernière.
    /// Le calcul s'exécute hors du thread d'UI ; seule l'affectation du résultat y revient.
    /// </summary>
    private void ScheduleFilters(TimeSpan delay)
    {
        if (_isApplyingBounds)
        {
            return;
        }

        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        _ = RunFilterAsync(_allWeapons, CaptureCriteria(), delay, cts.Token);
    }

    private async Task RunFilterAsync(List<FpsWeaponModel> source, FilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var result = await Task.Run(() => FilterWeapons(source, criteria), token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Une saisie plus récente a pu annuler cette demande pendant le calcul ou l'attente du thread d'UI.
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
                if (!Weapons.SequenceEqual(result))
                {
                    Weapons = new ObservableCollection<FpsWeaponModel>(result);
                }
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des armes FPS");
        }
    }

    /// <summary>
    /// Copie l'état des filtres. À appeler depuis le thread d'UI : le calcul qui suit s'exécute
    /// sur un autre thread et ne doit lire aucune propriété du ViewModel.
    /// </summary>
    private FilterCriteria CaptureCriteria() => new(
        Needle: string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
        WeaponClass: !string.Equals(SelectedWeaponClass, AllClassesLabel, StringComparison.Ordinal)
                     && !string.IsNullOrEmpty(SelectedWeaponClass)
            ? SelectedWeaponClass
            : null,
        // Un filtre à pleine amplitude ne doit écarter personne, y compris les armes dont les dégâts sont inconnus.
        DamageActive: DamageMin > DamageLowerBound || DamageMax < DamageUpperBound,
        DamageMin: DamageMin,
        DamageMax: DamageMax,
        RangeActive: EffectiveRangeMin > EffectiveRangeLowerBound || EffectiveRangeMax < EffectiveRangeUpperBound,
        RangeMin: EffectiveRangeMin,
        RangeMax: EffectiveRangeMax,
        IncludeWithoutRange: IncludeWeaponsWithoutEffectiveRange);

    private static List<FpsWeaponModel> FilterWeapons(List<FpsWeaponModel> source, FilterCriteria criteria)
    {
        IEnumerable<FpsWeaponModel> filtered = source;

        if (criteria.Needle is { } needle)
        {
            filtered = filtered.Where(w =>
                w.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || w.TechnicalName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (w.VariantNames?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        if (criteria.WeaponClass is { } weaponClass)
        {
            filtered = filtered.Where(w =>
                string.Equals(w.WeaponClassLabel, weaponClass, StringComparison.CurrentCultureIgnoreCase));
        }

        if (criteria.DamageActive)
        {
            filtered = filtered.Where(w => w.MatchesDamageRange(criteria.DamageMin, criteria.DamageMax));
        }

        if (criteria.RangeActive)
        {
            filtered = filtered.Where(w =>
                w.MatchesEffectiveRange(criteria.RangeMin, criteria.RangeMax)
                || (criteria.IncludeWithoutRange && w.HasNoKnownEffectiveRange));
        }

        return filtered.ToList();
    }

    private sealed record FilterCriteria(
        string? Needle,
        string? WeaponClass,
        bool DamageActive,
        double DamageMin,
        double DamageMax,
        bool RangeActive,
        double RangeMin,
        double RangeMax,
        bool IncludeWithoutRange);

    partial void OnNameFilterChanged(string value) => ScheduleFilters(NameFilterDelay);

    partial void OnSelectedWeaponClassChanged(string value) => ScheduleFilters(ControlFilterDelay);

    partial void OnIncludeWeaponsWithoutEffectiveRangeChanged(bool value) => ScheduleFilters(ControlFilterDelay);

    partial void OnDamageMinChanged(double value)
    {
        if (value > DamageMax)
        {
            DamageMax = value;
        }

        ScheduleFilters(ControlFilterDelay);
    }

    partial void OnDamageMaxChanged(double value)
    {
        if (value < DamageMin)
        {
            DamageMin = value;
        }

        ScheduleFilters(ControlFilterDelay);
    }

    partial void OnEffectiveRangeMinChanged(double value)
    {
        if (value > EffectiveRangeMax)
        {
            EffectiveRangeMax = value;
        }

        ScheduleFilters(ControlFilterDelay);
    }

    partial void OnEffectiveRangeMaxChanged(double value)
    {
        if (value < EffectiveRangeMin)
        {
            EffectiveRangeMin = value;
        }

        ScheduleFilters(ControlFilterDelay);
    }
}
