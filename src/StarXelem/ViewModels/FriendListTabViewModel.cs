using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

/// <summary>
/// Écran Amis : recherche, puces d'activité et tri par colonne.
/// Le filtrage reprend le fonctionnement de l'écran Objets : différé, calculé hors du thread d'UI et affiché par paquets.
/// </summary>
public partial class FriendListTabViewModel : PageViewModelBase
{
    private const string ConnectedChipKey = "connected";
    private const string AllChipKey = "all";

    // Délai d'inactivité de la saisie avant de filtrer : une frappe rapide ne déclenche qu'un seul filtrage.
    private static readonly TimeSpan SearchFilterDelay = TimeSpan.FromMilliseconds(300);
    // Puces et tri : délai réduit, suffisant pour absorber une série de clics rapprochés.
    private static readonly TimeSpan SelectionFilterDelay = TimeSpan.FromMilliseconds(120);
    // Entre deux paquets, le thread d'UI traite la saisie et le rendu : l'indicateur reste animé.
    private const int DisplayBatchSize = 20;

    // Ordre d'affichage des activités : les joueurs en jeu d'abord, les déconnectés en dernier.
    private static readonly string[] ActivityOrder = ["persistent_universe", "arena_commander", "menu", "offline"];

    private readonly IGrpcClientService _clientService;
    private readonly ILogger<FriendListTabViewModel> _logger;
    private IReadOnlyList<FriendViewModel> _allFriends = Array.Empty<FriendViewModel>();
    private ColumnHeaderViewModel? _sortColumn;
    private CancellationTokenSource? _filterCts;

    public override string Name => "Amis";
    protected override string NavIconData => "M20 21v-1a5 5 0 0 0-5-5H9a5 5 0 0 0-5 5v1M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    public override IVisualSourceViewModel Icon => new FluentIconVisualViewModel(FluentIcons.Common.Symbol.People);

    [ObservableProperty] private Task<List<FriendViewModel>?>? _friendList;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(HasNotLoadedFriends))] private bool _isLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BusyMessage))] private string _treatmentStatus = "";
    [ObservableProperty] private bool _onlyConnected = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<FriendViewModel> _displayedFriends = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary), nameof(ShowNoMatch))]
    private int _filteredCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(BusyMessage), nameof(ShowNoMatch))] private bool _isFiltering;

    public FriendListTabViewModel(IGrpcClientService clientService, ILogger<FriendListTabViewModel> logger)
    {
        _clientService = clientService;
        _logger = logger;

        NameColumn = new ColumnHeaderViewModel("Pseudo", OnSortRequested);
        StatusColumn = new ColumnHeaderViewModel("Statut", OnSortRequested);
        ShardColumn = new ColumnHeaderViewModel("Shard", OnSortRequested);
        RegionColumn = new ColumnHeaderViewModel("Région", OnSortRequested);
        ActivityChips =
        [
            new FilterChipViewModel(ConnectedChipKey, "Connectés", key => OnlyConnected = key == ConnectedChipKey),
            new FilterChipViewModel(AllChipKey, "Tous", key => OnlyConnected = key == ConnectedChipKey)
        ];

        _clientService.OnStatusChanged += (sender, _) => LoadFriendNotifyCanExecuteChanged();
    }

    /// <summary>Puces Connectés / Tous, exclusives l'une de l'autre.</summary>
    public IReadOnlyList<FilterChipViewModel> ActivityChips { get; }

    /// <summary>En-tête de la colonne Pseudo.</summary>
    public ColumnHeaderViewModel NameColumn { get; }

    /// <summary>En-tête de la colonne Statut.</summary>
    public ColumnHeaderViewModel StatusColumn { get; }

    /// <summary>En-tête de la colonne Shard.</summary>
    public ColumnHeaderViewModel ShardColumn { get; }

    /// <summary>En-tête de la colonne Région.</summary>
    public ColumnHeaderViewModel RegionColumn { get; }

    /// <summary>Nombre d'amis retenus par les filtres sur le total.</summary>
    public string ResultSummary => HasLoadedFriends ? $"{FilteredCount} sur {_allFriends.Count} amis" : string.Empty;

    /// <summary>Vrai une fois la liste d'amis chargée.</summary>
    public bool HasLoadedFriends => FriendList is { IsCompletedSuccessfully: true };

    /// <summary>Vrai tant qu'aucun chargement n'a eu lieu.</summary>
    public bool HasNotLoadedFriends => !HasLoadedFriends && !IsLoading;

    /// <summary>Vrai si les filtres ne laissent aucun ami.</summary>
    public bool ShowNoMatch => HasLoadedFriends && _allFriends.Count > 0 && FilteredCount == 0 && !IsFiltering;

    /// <summary>Vrai pendant un chargement ou un filtrage : affiche l'indicateur superposé au tableau.</summary>
    public bool IsBusy => IsLoading || IsFiltering;

    /// <summary>Texte de l'indicateur superposé au tableau.</summary>
    public string BusyMessage => IsLoading
        ? (string.IsNullOrWhiteSpace(TreatmentStatus) ? "Chargement des amis…" : TreatmentStatus)
        : "Filtrage en cours…";

    [RelayCommand(CanExecute = nameof(CanLoadFriendList))]
    public async Task LoadFriendList()
    {
        IsLoading = true;
        TreatmentStatus = "Appel RSI";
        try
        {
            var friendList = await _clientService.GetFriendList();
            FriendList = Task.FromResult(friendList.Select(f => new FriendViewModel(
                displayName: f.Account?.DisplayName ?? "Unknown",
                tokenName: f.Account?.Nickname ?? "Unknown",
                avatarUrl: f.Account?.AvatarUrl,
                isConnected: f.Presence != null,
                isInGame: f.Presence?.Activity?.PlayerId != null,
                activity: f.Presence?.Activity?.State ?? "Hors ligne",
                shardInfoLoader: f.Account?.AccountId != null && f.Presence?.Status != null ? () => _clientService.GetShardInfo((int)f.Account.AccountId)! : null
            )).ToList())!;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanLoadFriendList()
    {
        return _clientService.Status is GrpcConnectionStatus.Connected or GrpcConnectionStatus.InGame && !IsLoading;
    }

    partial void OnIsLoadingChanged(bool value) => LoadFriendNotifyCanExecuteChanged();

    partial void OnFriendListChanged(Task<List<FriendViewModel>?>? value)
    {
        _allFriends = value is { IsCompletedSuccessfully: true, Result: { } list } ? list : Array.Empty<FriendViewModel>();
        OnPropertyChanged(nameof(HasLoadedFriends));
        OnPropertyChanged(nameof(HasNotLoadedFriends));
        ScheduleFilters(TimeSpan.Zero);
    }

    partial void OnOnlyConnectedChanged(bool value) => ScheduleFilters(SelectionFilterDelay);

    partial void OnSearchTextChanged(string value) => ScheduleFilters(SearchFilterDelay);

    private void OnSortRequested(ColumnHeaderViewModel column)
    {
        var direction = column == _sortColumn && column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var header in new[] { NameColumn, StatusColumn, ShardColumn, RegionColumn })
        {
            header.SortDirection = header == column ? direction : null;
        }

        _sortColumn = column;
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
        var criteria = new FriendFilterCriteria(
            _allFriends,
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            OnlyConnected,
            GetSortColumn(),
            _sortColumn?.SortDirection == ListSortDirection.Descending);

        IsFiltering = true;
        _ = RunFilterAsync(criteria, delay, cts.Token);
    }

    private FriendSortColumn? GetSortColumn()
    {
        if (_sortColumn?.SortDirection == null)
        {
            return null;
        }

        if (_sortColumn == StatusColumn)
        {
            return FriendSortColumn.Status;
        }

        if (_sortColumn == ShardColumn)
        {
            return FriendSortColumn.Shard;
        }

        return _sortColumn == RegionColumn ? FriendSortColumn.Region : FriendSortColumn.Name;
    }

    private async Task RunFilterAsync(FriendFilterCriteria criteria, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            var outcome = await Task.Run(() => ComputeFilterOutcome(criteria, token), token);
            await Dispatcher.UIThread.InvokeAsync(() => DisplayResultAsync(outcome, token), DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Remplacée par une demande plus récente : comportement normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du filtrage de la liste d'amis");
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

    private static FriendFilterOutcome ComputeFilterOutcome(FriendFilterCriteria criteria, CancellationToken token)
    {
        var searched = criteria.Friends.Where(friend => criteria.Needle == null || MatchesSearch(friend, criteria.Needle)).ToList();
        token.ThrowIfCancellationRequested();

        var connectedCount = searched.Count(friend => friend.IsConnected);
        var rows = Sort(searched.Where(friend => !criteria.OnlyConnected || friend.IsConnected), criteria).ToList();

        return new FriendFilterOutcome(rows, connectedCount, searched.Count);
    }

    private async Task DisplayResultAsync(FriendFilterOutcome outcome, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        foreach (var chip in ActivityChips)
        {
            var isConnectedChip = chip.Key == ConnectedChipKey;
            chip.Count = isConnectedChip ? outcome.ConnectedCount : outcome.SearchedCount;
            chip.IsSelected = isConnectedChip == OnlyConnected;
        }

        FilteredCount = outcome.Friends.Count;

        // Reconstruire le tableau est l'opération coûteuse : on l'évite quand le résultat ne change pas.
        if (DisplayedFriends.SequenceEqual(outcome.Friends))
        {
            return;
        }

        var target = new ObservableCollection<FriendViewModel>();
        DisplayedFriends = target;

        for (var index = 0; index < outcome.Friends.Count; index += DisplayBatchSize)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var friend in outcome.Friends.Skip(index).Take(DisplayBatchSize))
            {
                target.Add(friend);
            }

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }
    }

    // Le shard se charge à l'affichage de la ligne : seul un shard déjà connu peut être trouvé par la recherche.
    private static bool MatchesSearch(FriendViewModel friend, string search)
    {
        return new[] { friend.DisplayName, friend.TokenName, friend.ActivityLabel, friend.KnownShardId, friend.KnownShardRegion }
            .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private static IEnumerable<FriendViewModel> Sort(IEnumerable<FriendViewModel> friends, FriendFilterCriteria criteria)
    {
        // Sans tri choisi : les amis en jeu d'abord, puis par pseudo.
        if (criteria.SortColumn is not { } column)
        {
            return friends.OrderBy(GetActivityRank).ThenBy(friend => friend.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        }

        Func<FriendViewModel, object> key = column switch
        {
            FriendSortColumn.Status => friend => GetActivityRank(friend),
            FriendSortColumn.Shard => friend => friend.KnownShardId ?? string.Empty,
            FriendSortColumn.Region => friend => friend.KnownShardRegion ?? string.Empty,
            _ => friend => friend.DisplayName
        };
        var comparer = Comparer<object>.Create((left, right) => left is string a && right is string b
            ? StringComparer.CurrentCultureIgnoreCase.Compare(a, b)
            : Comparer<object>.Default.Compare(left, right));

        return criteria.SortDescending ? friends.OrderByDescending(key, comparer) : friends.OrderBy(key, comparer);
    }

    private static int GetActivityRank(FriendViewModel friend)
    {
        var index = Array.IndexOf(ActivityOrder, friend.ActivityKey);
        return index < 0 ? ActivityOrder.Length - 1 : index;
    }

    private void LoadFriendNotifyCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LoadFriendListCommand?.NotifyCanExecuteChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(LoadFriendNotifyCanExecuteChanged);
        }
    }

    private enum FriendSortColumn
    {
        Name,
        Status,
        Shard,
        Region
    }

    /// <summary>État des filtres au moment de la demande, lu par le calcul hors du thread d'UI.</summary>
    private sealed record FriendFilterCriteria(
        IReadOnlyList<FriendViewModel> Friends,
        string? Needle,
        bool OnlyConnected,
        FriendSortColumn? SortColumn,
        bool SortDescending);

    /// <summary>Résultat d'un filtrage : lignes à afficher et compteurs des puces.</summary>
    private sealed record FriendFilterOutcome(IReadOnlyList<FriendViewModel> Friends, int ConnectedCount, int SearchedCount);
}
