using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using StarXelem.ViewModels;
using System.Net.Http;

namespace StarXelem.Style;

/// <summary>
/// Custom control unifié pour afficher n'importe quelle source visuelle.
///
/// Utilisation directe :
///   <controls:VisualSourceControl Source="{Binding Visual}" Width="30" Height="30" CornerRadius="7"/>
///
/// Utilisation via ContentPresenter (dispatch automatique) :
///   <ContentPresenter Content="{Binding Visual}"/>
///   → nécessite que les DataTemplates soient déclarées dans App.axaml
///
/// États gérés :
///   - Idle   : affiche la source
///   - Loading: spinner pendant le chargement d'une image réseau
///   - Error  : bascule sur le Fallback ou un placeholder neutre
/// </summary>
public class VisualSourceControl : TemplatedControl
{
    // =========================================================================
    // Styled Properties
    // =========================================================================

    public static readonly StyledProperty<IVisualSourceViewModel?> SourceProperty =
        AvaloniaProperty.Register<VisualSourceControl, IVisualSourceViewModel?>(
            nameof(Source));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<VisualSourceControl, CornerRadius>(
            nameof(CornerRadius), new CornerRadius(7));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<VisualSourceControl, bool>(
            nameof(IsLoading), defaultValue: false);

    // Propriété interne exposée au template pour le dispatch
    internal static readonly StyledProperty<IVisualSourceViewModel?> ResolvedSourceProperty =
        AvaloniaProperty.Register<VisualSourceControl, IVisualSourceViewModel?>(
            nameof(ResolvedSource));

    // =========================================================================
    // Propriétés CLR
    // =========================================================================

    public IVisualSourceViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public new CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        private set => SetValue(IsLoadingProperty, value);
    }

    internal IVisualSourceViewModel? ResolvedSource
    {
        get => GetValue(ResolvedSourceProperty);
        private set => SetValue(ResolvedSourceProperty, value);
    }

    // =========================================================================
    // HttpClient partagé pour le chargement d'images réseau
    // =========================================================================

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // =========================================================================
    // Surcharge OnPropertyChanged
    // =========================================================================

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
            OnSourceChanged(change.NewValue as IVisualSourceViewModel);
    }

    // =========================================================================
    // Logique de résolution de la source
    // =========================================================================

    private CancellationTokenSource? _loadCts;

    private void OnSourceChanged(IVisualSourceViewModel? source)
    {
        // Annuler tout chargement en cours
        _loadCts?.Cancel();
        _loadCts = null;
        IsLoading = false;

        switch (source)
        {
            case null:
                ResolvedSource = null;
                break;

            // Ces types sont synchrones : dispatch direct
            case InitialsVisualViewModel:
            case GeometryIconVisualViewModel:
            case FluentIconVisualViewModel:
                ResolvedSource = source;
                break;

            // Chargement asynchrone depuis une URI
            case UriImageVisualViewModel uriVm:
                _ = LoadUriImageAsync(uriVm);
                break;

            // Chargement depuis un chemin fichier
            case PathImageVisualViewModel pathVm:
                _ = LoadPathImageAsync(pathVm);
                break;

            default:
                ResolvedSource = source;
                break;
        }
    }

    private async Task LoadUriImageAsync(UriImageVisualViewModel vm)
    {
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        // Afficher le fallback pendant le chargement
        ResolvedSource = vm.Fallback;
        IsLoading      = true;

        try
        {
            Bitmap? bitmap;

            if (vm.Source.IsFile)
            {
                // URI de type file:// — chargement synchrone via stream
                bitmap = await Task.Run(() =>
                {
                    var path = vm.Source.LocalPath;
                    return File.Exists(path) ? new Bitmap(path) : null;
                }, cts.Token);
            }
            else if (vm.Source.Scheme is "avares" or "resm")
            {
                // Resource embarquée Avalonia
//                var assetLoader = Avalonia.Platform.AssetLoader;
                // Note : AssetLoader est accessible via AvaloniaLocator en Avalonia 11
                await using var stream = Avalonia.Platform.AssetLoader.Open(vm.Source);
                bitmap = new Bitmap(stream);
            }
            else
            {
                // HTTP/HTTPS
                var bytes = await SharedHttpClient.GetByteArrayAsync(vm.Source, cts.Token);
                using var ms = new MemoryStream(bytes);
                bitmap = new Bitmap(ms);
            }

            if (cts.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                IsLoading      = false;
                ResolvedSource = bitmap is not null
                    ? new BitmapVisualViewModel(bitmap)
                    : (vm.Fallback ?? FallbackPlaceholder(vm));
            });
        }
        catch (OperationCanceledException) { /* chargement annulé, ne rien faire */ }
        catch
        {
            if (cts.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading      = false;
                ResolvedSource = vm.Fallback ?? FallbackPlaceholder(vm);
            });
        }
    }

    private async Task LoadPathImageAsync(PathImageVisualViewModel vm)
    {
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        ResolvedSource = vm.Fallback;
        IsLoading      = true;

        try
        {
            var bitmap = await Task.Run(() =>
                File.Exists(vm.FilePath) ? new Bitmap(vm.FilePath) : null,
                cts.Token);

            if (cts.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                IsLoading      = false;
                ResolvedSource = bitmap is not null
                    ? new BitmapVisualViewModel(bitmap)
                    : (vm.Fallback ?? FallbackPlaceholder(vm));
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (cts.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading      = false;
                ResolvedSource = vm.Fallback ?? FallbackPlaceholder(vm);
            });
        }
    }

    // Placeholder neutre quand aucun Fallback n'est défini
    private static InitialsVisualViewModel FallbackPlaceholder(IVisualSourceViewModel _)
        => new("?", Color.Parse("#338B5CF6")); // violet teinté du design system

    // Helper pour accéder à l'AssetLoader Avalonia 11
    private static Avalonia.Platform.IAssetLoader AssetLoader =>
        App.Current!.Services?
            .GetService(typeof(Avalonia.Platform.IAssetLoader))
            as Avalonia.Platform.IAssetLoader
        ?? throw new InvalidOperationException("IAssetLoader non disponible.");
}

// =============================================================================
// ViewModel interne : Bitmap résolu (non exposé publiquement)
// =============================================================================

/// <summary>
/// ViewModel interne créé après résolution du chargement asynchrone d'une image.
/// Le template XAML correspondant affiche un <see cref="Image"/> Avalonia.
/// </summary>
internal sealed class BitmapVisualViewModel : IVisualSourceViewModel
{
    public Bitmap Bitmap { get; }
    public BitmapVisualViewModel(Bitmap bitmap) => Bitmap = bitmap;
}
