using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.ViewModels;

/// <summary>
/// ViewModel de l'écran des armes FPS : tableau filtrable par nom, par puces de classe et de type de dégâts,
/// et par plages de dégâts et de portée d'efficacité. Les données proviennent de la base locale
/// reconstruite depuis les fichiers du jeu.
/// </summary>
public partial class FpsWeaponsTabViewModel : PageViewModelBase
{
    private readonly IFpsWeaponRepository _fpsWeaponRepository;
    private readonly ILogger<FpsWeaponsTabViewModel> _logger;
    private readonly HashSet<string> _selectedClasses = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _selectedDamageTypes = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<ColumnHeaderViewModel, WeaponSortColumn> _sortColumns;
    private List<FpsWeaponModel> _allWeapons = new();
    private bool _isApplyingBounds;
    private CancellationTokenSource? _filterCts;
    private ColumnHeaderViewModel? _activeSortColumn;

    // Délai d'inactivité de la saisie avant de filtrer : assez court pour rester réactif,
    // assez long pour qu'une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan NameFilterDelay = TimeSpan.FromMilliseconds(300);
    // Curseurs, puces, tri et case à cocher : délai réduit, suffisant pour absorber le glissement d'un curseur.
    private static readonly TimeSpan ControlFilterDelay = TimeSpan.FromMilliseconds(120);
    // Le tableau est alimenté par paquets : entre deux paquets, le thread d'UI traite la saisie et le rendu,
    // ce qui garde l'indicateur animé et la fenêtre réactive pendant la construction des lignes.
    private const int DisplayBatchSize = 20;

    public override string Name => "Armes FPS";
    protected override string NavIconData => "M3 10h15v5H3zM18 11h3v3h-3M6 15v5h5v-5";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.VideoSettings);

    [ObservableProperty] private ObservableCollection<FpsWeaponModel> _weapons = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedWeapon))] private FpsWeaponModel? _selectedWeapon;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage))] private bool _isLoading;
    /// <summary>Vrai du moment où un filtre est demandé jusqu'à ce que la liste correspondante soit entièrement affichée.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))] private int _filteredCount;
    [ObservableProperty] private string _nameFilter = string.Empty;

    /// <summary>Vrai quand la ligne des curseurs de plage est dépliée.</summary>
    [ObservableProperty] private bool _isRangePanelOpen;

    public bool IsBusy => IsLoading || IsFiltering;
    public string BusyMessage => IsLoading ? "Chargement des armes…" : "Filtrage en cours…";

    /// <summary>Nombre de modèles retenus par les filtres sur le total.</summary>
    public string ResultSummary => $"{FilteredCount} sur {_allWeapons.Count} modèles";

    /// <summary>Vrai si les filtres ne laissent aucune arme.</summary>
    public bool ShowNoMatch => _allWeapons.Count > 0 && FilteredCount == 0 && !IsFiltering;

    /// <summary>Vrai si une arme est sélectionnée : affiche son détail.</summary>
    public bool HasSelectedWeapon => SelectedWeapon != null;

    /// <summary>Puces de classe d'arme, cumulables ; « Toutes » retire le filtre.</summary>
    public ObservableCollection<FilterChipViewModel> ClassChips { get; } = new();

    /// <summary>Puces de type de dégâts, cumulables ; « Tous » retire le filtre.</summary>
    public ObservableCollection<FilterChipViewModel> DamageTypeChips { get; } = new();

    public ColumnHeaderViewModel NameColumn { get; }
    public ColumnHeaderViewModel ClassColumn { get; }
    public ColumnHeaderViewModel ManufacturerColumn { get; }
    public ColumnHeaderViewModel MagazineColumn { get; }
    public ColumnHeaderViewModel DamageColumn { get; }
    public ColumnHeaderViewModel FireRateColumn { get; }
    public ColumnHeaderViewModel MaxRangeColumn { get; }

    /* ---- Bornes du filtre de dégâts, déduites des données chargées ---- */

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _damageLowerBound;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _damageUpperBound = 100;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _damageMin;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _damageMax = 100;

    /* ---- Bornes du filtre de portée d'efficacité ---- */

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _effectiveRangeLowerBound;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _effectiveRangeUpperBound = 100;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _effectiveRangeMin;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RangeButtonLabel))] private double _effectiveRangeMax = 100;

    /// <summary>
    /// Conserve dans les résultats les armes dont aucune munition n'a de chute de dégâts,
    /// et donc aucune portée d'efficacité exploitable : sans cette option, le curseur de portée
    /// les ferait disparaître silencieusement.
    /// </summary>
    [ObservableProperty] private bool _includeWeaponsWithoutEffectiveRange = true;

    private bool IsDamageRangeActive => DamageMin > DamageLowerBound || DamageMax < DamageUpperBound;

    private bool IsEffectiveRangeActive => EffectiveRangeMin > EffectiveRangeLowerBound || EffectiveRangeMax < EffectiveRangeUpperBound;

    /// <summary>Libellé du bouton Plages, avec le nombre de plages actives : un filtre replié ne doit pas passer inaperçu.</summary>
    public string RangeButtonLabel
    {
        get
        {
            var active = (IsDamageRangeActive ? 1 : 0) + (IsEffectiveRangeActive ? 1 : 0);
            return active == 0 ? "Plages" : $"Plages · {active}";
        }
    }

    public FpsWeaponsTabViewModel(IFpsWeaponRepository fpsWeaponRepository, ILogger<FpsWeaponsTabViewModel> logger)
    {
        _fpsWeaponRepository = fpsWeaponRepository;
        _logger = logger;

        NameColumn = new ColumnHeaderViewModel("Nom", OnSortRequested);
        ClassColumn = new ColumnHeaderViewModel("Classe", OnSortRequested);
        ManufacturerColumn = new ColumnHeaderViewModel("Fabricant", OnSortRequested);
        MagazineColumn = new ColumnHeaderViewModel("Chargeur", OnSortRequested);
        DamageColumn = new ColumnHeaderViewModel("Dégâts", OnSortRequested);
        FireRateColumn = new ColumnHeaderViewModel("Cadence", OnSortRequested);
        MaxRangeColumn = new ColumnHeaderViewModel("Portée max", OnSortRequested);
        _sortColumns = new Dictionary<ColumnHeaderViewModel, WeaponSortColumn>
        {
            [NameColumn] = WeaponSortColumn.Name,
            [ClassColumn] = WeaponSortColumn.Class,
            [ManufacturerColumn] = WeaponSortColumn.Manufacturer,
            [MagazineColumn] = WeaponSortColumn.Magazine,
            [DamageColumn] = WeaponSortColumn.Damage,
            [FireRateColumn] = WeaponSortColumn.FireRate,
            [MaxRangeColumn] = WeaponSortColumn.MaxRange
        };
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

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                _allWeapons = weapons;
                BuildChips(weapons);
                ResetBounds(weapons);

                // L'indicateur de chargement reste affiché jusqu'à ce que toutes les lignes soient à l'écran.
                await ScheduleFilters(TimeSpan.Zero);
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

    [RelayCommand]
    private void ToggleRangePanel() => IsRangePanelOpen = !IsRangePanelOpen;

    /// <summary>Remet les deux curseurs à pleine amplitude.</summary>
    [RelayCommand]
    private void ResetRanges()
    {
        _isApplyingBounds = true;
        try
        {
            DamageMin = DamageLowerBound;
            DamageMax = DamageUpperBound;
            EffectiveRangeMin = EffectiveRangeLowerBound;
            EffectiveRangeMax = EffectiveRangeUpperBound;
        }
        finally
        {
            _isApplyingBounds = false;
        }

        ScheduleFilters(ControlFilterDelay);
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
    /// Crée les puces à partir des valeurs réellement présentes dans les données.
    /// </summary>
    private void BuildChips(List<FpsWeaponModel> weapons)
    {
        ClassChips.Clear();
        ClassChips.Add(new FilterChipViewModel(null, "Toutes", key => ToggleChip(_selectedClasses, key)));
        foreach (var label in weapons.Select(w => w.WeaponClassLabel)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .Order(StringComparer.CurrentCulture))
        {
            ClassChips.Add(new FilterChipViewModel(label, label, key => ToggleChip(_selectedClasses, key)));
        }

        DamageTypeChips.Clear();
        DamageTypeChips.Add(new FilterChipViewModel(null, "Tous", key => ToggleChip(_selectedDamageTypes, key)));
        foreach (var label in weapons.SelectMany(w => w.DamageTypeLabels)
                     .Where(label => label != "—")
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .Order(StringComparer.CurrentCulture))
        {
            DamageTypeChips.Add(new FilterChipViewModel(label, label, key => ToggleChip(_selectedDamageTypes, key)));
        }
    }

    private void ToggleChip(HashSet<string> selection, string? key)
    {
        if (key == null)
        {
            selection.Clear();
        }
        else if (!selection.Remove(key))
        {
            selection.Add(key);
        }

        ScheduleFilters(ControlFilterDelay);
    }

    private void OnSortRequested(ColumnHeaderViewModel column)
    {
        var direction = column == _activeSortColumn && column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var header in _sortColumns.Keys)
        {
            header.SortDirection = header == column ? direction : null;
        }

        _activeSortColumn = column;
        ScheduleFilters(ControlFilterDelay);
    }

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
    /// Planifie un filtrage différé : toute demande encore en attente ou en cours est annulée,
    /// de sorte qu'une rafale de modifications ne produit qu'un seul recalcul, après la dernière.
    /// Le calcul s'exécute hors du thread d'UI, et l'affichage du résultat se fait par paquets.
    /// La tâche retournée se termine quand la liste est entièrement affichée, ou dès que la demande
    /// est remplacée par une plus récente.
    /// </summary>
    private Task ScheduleFilters(TimeSpan delay)
    {
        if (_isApplyingBounds)
        {
            return Task.CompletedTask;
        }

        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        UpdateIsFiltering(true);
        return RunFilterAsync(_allWeapons, CaptureCriteria(), delay, cts.Token);
    }

    private async Task RunFilterAsync(List<FpsWeaponModel> source, FilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => FilterWeapons(source, criteria), token);

            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage des armes FPS");
        }
        finally
        {
            // Une demande annulée a forcément une remplaçante, qui reste propriétaire de l'indicateur.
            if (!token.IsCancellationRequested)
            {
                UpdateIsFiltering(false);
            }
        }
    }

    /// <summary>
    /// Affiche le résultat en alimentant une nouvelle collection par paquets : construire toutes les lignes
    /// d'un coup bloquerait le thread d'UI. S'interrompt dès que la demande est annulée.
    /// </summary>
    private async Task DisplayResultAsync(FilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        UpdateChips(ClassChips, _selectedClasses, outcome.ClassCounts);
        UpdateChips(DamageTypeChips, _selectedDamageTypes, outcome.DamageTypeCounts);
        FilteredCount = outcome.Weapons.Count;

        var result = outcome.Weapons;

        // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (Weapons.SequenceEqual(result))
        {
            return;
        }

        // Le tableau perd sa sélection quand sa collection est remplacée : on la rétablit si l'arme reste affichée,
        // sinon on montre la première pour que le panneau de détail ne reste pas vide.
        var selected = SelectedWeapon != null && result.Contains(SelectedWeapon) ? SelectedWeapon : result.FirstOrDefault();
        var target = new ObservableCollection<FpsWeaponModel>();
        Weapons = target;

        for (var index = 0; index < result.Count; index += DisplayBatchSize)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var weapon in result.Skip(index).Take(DisplayBatchSize))
            {
                target.Add(weapon);
            }

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        SelectedWeapon = selected;
    }

    private static void UpdateChips(IEnumerable<FilterChipViewModel> chips, HashSet<string> selection, IReadOnlyDictionary<string, int> counts)
    {
        foreach (var chip in chips)
        {
            chip.Count = chip.Key == null ? counts.GetValueOrDefault(string.Empty) : counts.GetValueOrDefault(chip.Key);
            chip.IsSelected = chip.Key == null ? selection.Count == 0 : selection.Contains(chip.Key);
        }
    }

    // Écrit depuis RunFilterAsync, qui peut reprendre hors du thread d'UI : IsFiltering ne doit y être écrit que là.
    private void UpdateIsFiltering(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsFiltering = value;
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateIsFiltering(value), DispatcherPriority.MaxValue);
        }
    }

    /// <summary>
    /// Copie l'état des filtres. À appeler depuis le thread d'UI : le calcul qui suit s'exécute
    /// sur un autre thread et ne doit lire aucune propriété du ViewModel.
    /// </summary>
    private FilterCriteria CaptureCriteria() => new(
        Needle: string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
        Classes: _selectedClasses.Count == 0 ? null : _selectedClasses.ToHashSet(StringComparer.CurrentCultureIgnoreCase),
        DamageTypes: _selectedDamageTypes.Count == 0 ? null : _selectedDamageTypes.ToHashSet(StringComparer.CurrentCultureIgnoreCase),
        // Un filtre à pleine amplitude ne doit écarter personne, y compris les armes dont les dégâts sont inconnus.
        DamageActive: IsDamageRangeActive,
        DamageMin: DamageMin,
        DamageMax: DamageMax,
        RangeActive: IsEffectiveRangeActive,
        RangeMin: EffectiveRangeMin,
        RangeMax: EffectiveRangeMax,
        IncludeWithoutRange: IncludeWeaponsWithoutEffectiveRange,
        SortColumn: _activeSortColumn is { IsSorted: true } ? _sortColumns[_activeSortColumn] : null,
        SortDescending: _activeSortColumn?.IsSortedDescending ?? false);

    /// <summary>
    /// Calcule les lignes à afficher et le nombre de modèles de chaque puce. Les compteurs d'une rangée de puces
    /// tiennent compte de tous les autres filtres, pas des siens : cocher une classe ne fait pas tomber les autres à zéro.
    /// La clé vide porte le total de la puce « Toutes » ou « Tous ».
    /// </summary>
    private static FilterOutcome FilterWeapons(List<FpsWeaponModel> source, FilterCriteria criteria)
    {
        bool MatchesClass(FpsWeaponModel w) => criteria.Classes == null || criteria.Classes.Contains(w.WeaponClassLabel);
        bool MatchesDamageType(FpsWeaponModel w) => criteria.DamageTypes == null || w.DamageTypeLabels.Any(criteria.DamageTypes.Contains);

        var candidates = source.Where(w => MatchesSearch(w, criteria) && MatchesRanges(w, criteria)).ToList();

        var classPool = candidates.Where(MatchesDamageType).ToList();
        var classCounts = classPool.GroupBy(w => w.WeaponClassLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);
        classCounts[string.Empty] = classPool.Count;

        var damagePool = candidates.Where(MatchesClass).ToList();
        var damageCounts = damagePool.SelectMany(w => w.DamageTypeLabels)
            .GroupBy(label => label, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);
        damageCounts[string.Empty] = damagePool.Count;

        var rows = Sort(candidates.Where(w => MatchesClass(w) && MatchesDamageType(w)), criteria).ToList();
        return new FilterOutcome(rows, classCounts, damageCounts);
    }

    private static bool MatchesSearch(FpsWeaponModel w, FilterCriteria criteria) =>
        criteria.Needle is not { } needle
        || w.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
        || w.TechnicalName.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || (w.Manufacturer?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (w.VariantNames?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static bool MatchesRanges(FpsWeaponModel w, FilterCriteria criteria) =>
        (!criteria.DamageActive || w.MatchesDamageRange(criteria.DamageMin, criteria.DamageMax))
        && (!criteria.RangeActive
            || w.MatchesEffectiveRange(criteria.RangeMin, criteria.RangeMax)
            || (criteria.IncludeWithoutRange && w.HasNoKnownEffectiveRange));

    private static IEnumerable<FpsWeaponModel> Sort(IEnumerable<FpsWeaponModel> weapons, FilterCriteria criteria)
    {
        if (criteria.SortColumn is not { } column)
        {
            return weapons;
        }

        Func<FpsWeaponModel, object?> key = column switch
        {
            WeaponSortColumn.Class => w => w.WeaponClassLabel,
            WeaponSortColumn.Manufacturer => w => w.Manufacturer,
            WeaponSortColumn.Magazine => w => w.MagazineSize,
            WeaponSortColumn.Damage => w => w.BestDamagePerShot,
            WeaponSortColumn.FireRate => w => w.PrimaryFireRate,
            WeaponSortColumn.MaxRange => w => w.PrimaryMaxRange,
            _ => w => w.Name
        };

        // Les valeurs inconnues restent en fin de liste, quel que soit le sens du tri.
        var known = weapons.Where(w => key(w) != null);
        var unknown = weapons.Where(w => key(w) == null);
        var comparer = Comparer<object?>.Create((left, right) => left is string a && right is string b
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a, b)
            : Comparer<object?>.Default.Compare(left, right));
        var sorted = criteria.SortDescending ? known.OrderByDescending(key, comparer) : known.OrderBy(key, comparer);
        return sorted.Concat(unknown);
    }

    private enum WeaponSortColumn
    {
        Name,
        Class,
        Manufacturer,
        Magazine,
        Damage,
        FireRate,
        MaxRange
    }

    private sealed record FilterCriteria(
        string? Needle,
        HashSet<string>? Classes,
        HashSet<string>? DamageTypes,
        bool DamageActive,
        double DamageMin,
        double DamageMax,
        bool RangeActive,
        double RangeMin,
        double RangeMax,
        bool IncludeWithoutRange,
        WeaponSortColumn? SortColumn,
        bool SortDescending);

    private sealed record FilterOutcome(
        List<FpsWeaponModel> Weapons,
        IReadOnlyDictionary<string, int> ClassCounts,
        IReadOnlyDictionary<string, int> DamageTypeCounts);

    partial void OnNameFilterChanged(string value) => ScheduleFilters(NameFilterDelay);

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
