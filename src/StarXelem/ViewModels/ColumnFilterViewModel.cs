using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarXelem.ViewModels;

/// <summary>
/// État du filtre par valeurs d'une colonne de tableau. Aucune valeur cochée signifie aucun filtre.
/// Le filtrage lui-même reste à la charge de l'écran, prévenu par <see cref="SelectionChanged"/>.
/// </summary>
public partial class ColumnFilterViewModel : ObservableObject
{
    private readonly Dictionary<string, ColumnFilterOptionViewModel> _options = new();
    private bool _isBatchUpdate;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoVisibleOption))] private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoVisibleOption), nameof(AreAllVisibleSelected))]
    private IReadOnlyList<ColumnFilterOptionViewModel> _visibleOptions = Array.Empty<ColumnFilterOptionViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private int _selectedCount;

    /// <summary>Levé quand l'utilisateur modifie les valeurs cochées.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Vrai si au moins une valeur est cochée.</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>Vrai si la recherche ou les autres filtres ne laissent aucune valeur à proposer.</summary>
    public bool HasNoVisibleOption => VisibleOptions.Count == 0 && StatusText == null;

    /// <summary>Coche ou décoche d'un coup toutes les valeurs actuellement visibles.</summary>
    public bool AreAllVisibleSelected
    {
        get => VisibleOptions.Count > 0 && VisibleOptions.All(option => option.IsSelected);
        set => ApplyBatch(() =>
        {
            foreach (var option in VisibleOptions)
            {
                option.IsSelected = value;
            }
        });
    }

    /// <summary>
    /// Remplace les valeurs proposées, en conservant la coche de celles qui existent encore.
    /// </summary>
    /// <param name="values">Couples identifiant, libellé.</param>
    public void SetOptions(IEnumerable<KeyValuePair<string, string>> values)
    {
        var incoming = values.ToDictionary(value => value.Key, value => value.Value);

        foreach (var key in _options.Keys.Where(key => !incoming.ContainsKey(key)).ToList())
        {
            _options[key].PropertyChanged -= OnOptionPropertyChanged;
            _options.Remove(key);
        }

        foreach (var (key, label) in incoming.Where(value => !_options.ContainsKey(value.Key)))
        {
            var option = new ColumnFilterOptionViewModel(key, label);
            option.PropertyChanged += OnOptionPropertyChanged;
            _options.Add(key, option);
        }

        SelectedCount = _options.Values.Count(option => option.IsSelected);
        RefreshVisibleOptions();
    }

    /// <summary>
    /// Met à jour le nombre de lignes de chaque valeur. Une valeur à zéro disparaît, sauf si elle est cochée.
    /// </summary>
    /// <param name="counts">Nombre de lignes par identifiant de valeur.</param>
    public void UpdateCounts(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var option in _options.Values)
        {
            option.Count = counts.GetValueOrDefault(option.Key);
        }

        RefreshVisibleOptions();
    }

    /// <summary>Retourne les identifiants cochés, ou <c>null</c> si le filtre est inactif.</summary>
    public HashSet<string>? GetSelectedKeys()
    {
        return HasSelection
            ? _options.Values.Where(option => option.IsSelected).Select(option => option.Key).ToHashSet()
            : null;
    }

    /// <summary>Indique si une valeur est cochée.</summary>
    /// <param name="key">Identifiant de la valeur.</param>
    public bool IsSelected(string key)
    {
        return _options.TryGetValue(key, out var option) && option.IsSelected;
    }

    /// <summary>Inverse la coche d'une valeur.</summary>
    /// <param name="key">Identifiant de la valeur.</param>
    public void Toggle(string key)
    {
        if (_options.TryGetValue(key, out var option))
        {
            option.IsSelected = !option.IsSelected;
        }
    }

    /// <summary>Décoche toutes les valeurs.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Reset()
    {
        ApplyBatch(() =>
        {
            foreach (var option in _options.Values)
            {
                option.IsSelected = false;
            }
        });
    }

    partial void OnSearchTextChanged(string value) => RefreshVisibleOptions();

    private void ApplyBatch(Action change)
    {
        _isBatchUpdate = true;
        try
        {
            change();
        }
        finally
        {
            _isBatchUpdate = false;
        }

        OnSelectionEdited();
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColumnFilterOptionViewModel.IsSelected) && !_isBatchUpdate)
        {
            OnSelectionEdited();
        }
    }

    private void OnSelectionEdited()
    {
        SelectedCount = _options.Values.Count(option => option.IsSelected);
        OnPropertyChanged(nameof(AreAllVisibleSelected));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // La liste n'est remplacée que si son contenu change : cocher une valeur ne doit pas reconstruire les lignes du panneau.
    private void RefreshVisibleOptions()
    {
        var search = SearchText.Trim();
        var visible = _options.Values
            .Where(option => option.Count > 0 || option.IsSelected)
            .Where(option => search.Length == 0 || option.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!visible.SequenceEqual(VisibleOptions))
        {
            VisibleOptions = visible;
        }
    }
}
