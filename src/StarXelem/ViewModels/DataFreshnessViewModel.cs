using System.Globalization;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StarXelem.ViewModels;

/// <summary>
/// Date de récupération des données affichées par un écran, et leur provenance : chargées depuis le jeu pendant cette session,
/// ou relues depuis le cache local. Le libellé relatif (« il y a 3 h ») se met à jour chaque minute.
/// </summary>
public partial class DataFreshnessViewModel : ObservableObject
{
    // Au-delà, des données relues du cache sont signalées comme anciennes.
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly DispatcherTimer _timer;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasData), nameof(Label), nameof(ToolTip), nameof(IsUpToDate), nameof(IsRecentCache), nameof(IsStale))]
    private DateTimeOffset? _fetchedAt;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label), nameof(ToolTip), nameof(IsUpToDate), nameof(IsRecentCache), nameof(IsStale))]
    private bool _isFromCache;

    /// <summary>
    /// Initialise un indicateur sans données.
    /// </summary>
    public DataFreshnessViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => RefreshRelativeTexts();
    }

    /// <summary>Vrai quand des données sont affichées.</summary>
    public bool HasData => FetchedAt.HasValue;

    /// <summary>Vrai pour des données chargées depuis le jeu pendant cette session.</summary>
    public bool IsUpToDate => HasData && !IsFromCache;

    /// <summary>Vrai pour des données relues du cache, récupérées il y a moins de 24 h.</summary>
    public bool IsRecentCache => HasData && IsFromCache && !IsOld;

    /// <summary>Vrai pour des données relues du cache, récupérées il y a plus de 24 h.</summary>
    public bool IsStale => HasData && IsFromCache && IsOld;

    /// <summary>Texte court du badge.</summary>
    public string Label
    {
        get
        {
            if (FetchedAt is not { } fetchedAt)
            {
                return string.Empty;
            }

            var local = fetchedAt.ToLocalTime();
            return IsFromCache
                ? $"Données du {local.ToString("dd/MM à HH:mm", French)} · {FormatAge(DateTimeOffset.Now - fetchedAt)}"
                : $"À jour · {local.ToString("HH:mm", French)}";
        }
    }

    /// <summary>Explication détaillée affichée au survol du badge.</summary>
    public string ToolTip
    {
        get
        {
            if (FetchedAt is not { } fetchedAt)
            {
                return string.Empty;
            }

            var date = fetchedAt.ToLocalTime().ToString("dddd d MMMM yyyy à HH:mm", French);
            return IsFromCache
                ? $"Données récupérées le {date}, relues depuis le dernier chargement enregistré. Actualisez pour les recharger depuis le jeu."
                : $"Données récupérées depuis le jeu le {date}.";
        }
    }

    private bool IsOld => FetchedAt is { } fetchedAt && DateTimeOffset.Now - fetchedAt > StaleThreshold;

    /// <summary>
    /// Indique les données affichées.
    /// </summary>
    /// <param name="fetchedAt">Date de récupération des données auprès du serveur.</param>
    /// <param name="isFromCache">Vrai si les données sont relues depuis le cache local.</param>
    public void Update(DateTimeOffset fetchedAt, bool isFromCache)
    {
        IsFromCache = isFromCache;
        FetchedAt = fetchedAt;
        _timer.Start();
    }

    /// <summary>
    /// Indique qu'aucune donnée n'est affichée.
    /// </summary>
    public void Clear()
    {
        _timer.Stop();
        FetchedAt = null;
        IsFromCache = false;
    }

    private void RefreshRelativeTexts()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsRecentCache));
        OnPropertyChanged(nameof(IsStale));
    }

    private static string FormatAge(TimeSpan age)
    {
        return age switch
        {
            { TotalMinutes: < 1 } => "à l'instant",
            { TotalHours: < 1 } => $"il y a {(int)age.TotalMinutes} min",
            { TotalDays: < 1 } => $"il y a {(int)age.TotalHours} h",
            _ => $"il y a {(int)age.TotalDays} j"
        };
    }
}
