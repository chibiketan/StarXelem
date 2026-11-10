using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarXelem.ViewModels;

/// <summary>
/// En-tête d'une colonne triable, éventuellement filtrable par valeurs.
/// </summary>
public partial class ColumnHeaderViewModel : ObservableObject
{
    private readonly Action<ColumnHeaderViewModel> _onSortRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSorted), nameof(IsSortedAscending), nameof(IsSortedDescending))]
    private ListSortDirection? _sortDirection;

    /// <summary>
    /// Initialise un en-tête de colonne.
    /// </summary>
    /// <param name="title">Titre affiché.</param>
    /// <param name="onSortRequested">Appelé quand l'utilisateur clique sur le titre.</param>
    /// <param name="filter">Filtre par valeurs de la colonne, ou <c>null</c> si elle n'en a pas.</param>
    public ColumnHeaderViewModel(string title, Action<ColumnHeaderViewModel> onSortRequested, ColumnFilterViewModel? filter = null)
    {
        Title = title;
        _onSortRequested = onSortRequested;
        Filter = filter;
    }

    /// <summary>Titre affiché.</summary>
    public string Title { get; }

    /// <summary>Filtre par valeurs de la colonne, ou <c>null</c> si elle n'en a pas.</summary>
    public ColumnFilterViewModel? Filter { get; }

    /// <summary>Vrai si la colonne propose un filtre par valeurs.</summary>
    public bool HasFilter => Filter != null;

    /// <summary>Vrai si le tableau est trié sur cette colonne.</summary>
    public bool IsSorted => SortDirection != null;

    /// <summary>Vrai si le tableau est trié sur cette colonne en ordre croissant.</summary>
    public bool IsSortedAscending => SortDirection == ListSortDirection.Ascending;

    /// <summary>Vrai si le tableau est trié sur cette colonne en ordre décroissant.</summary>
    public bool IsSortedDescending => SortDirection == ListSortDirection.Descending;

    [RelayCommand]
    private void RequestSort() => _onSortRequested(this);
}
