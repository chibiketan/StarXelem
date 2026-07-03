using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.ViewModels.Popup;
using CommunityToolkit.Mvvm.Messaging;

namespace StarXelem.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalDatabaseService _localDatabaseService;
    private const string P4kFolderSettingName = "P4KFolder";
    private const string SelectedP4kSettingName = "SelectedP4KPath";

    [ObservableProperty]
    private Task<IList<P4kFileModel>> _installedEnvs;
    [ObservableProperty]
    private P4kFileModel? selectedP4kFile;
    
    [ObservableProperty]private string _p4kStatus = "";


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrpcStatusDisplay))]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsInGame))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private GrpcConnectionStatus _grpcStatus = GrpcConnectionStatus.Disconnected;
    [ObservableProperty] private string? _grpcErrorMessage;
    [ObservableProperty] private string? _currentShardName;
    
    public bool IsDisconnected => GrpcStatus == GrpcConnectionStatus.Disconnected;
    public bool IsConnecting => GrpcStatus == GrpcConnectionStatus.Connecting;
    public bool IsConnected => GrpcStatus == GrpcConnectionStatus.Connected;
    public bool IsInGame => GrpcStatus == GrpcConnectionStatus.InGame;
    public bool IsError => GrpcStatus == GrpcConnectionStatus.Error;

    public string? GrpcStatusDisplay
    {
        get
        {
            var statusText = GrpcStatus switch
            {
                GrpcConnectionStatus.Disconnected => "Jeu non détecté",
                GrpcConnectionStatus.Connecting => "Connexion en cours…",
                GrpcConnectionStatus.Connected => "Connecté",
                GrpcConnectionStatus.InGame => "En jeu",
                GrpcConnectionStatus.Error => "Erreur",
                _ => null,
            };

            return statusText;
        }
    }

    private void OnGrpcConnectionStatusChanged(object? sender, GrpcConnectionStatus status)
    {
        GrpcStatus = status;
        GrpcErrorMessage = status == GrpcConnectionStatus.Error ? _grpcClientService.ErrorMessage : null;
        CurrentShardName = status == GrpcConnectionStatus.InGame ? _grpcClientService.CurrentShard : null;
    }
    [ObservableProperty]
    private PopupViewModel _popupViewModel;

    public IAsyncRelayCommand OpenDataP4kCommand { get; }

    [RelayCommand]
    private void SwitchTheme()
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = app.RequestedThemeVariant == ThemeVariant.Light
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage());
    }
    
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IP4kService p4kService, IGrpcClientService grpcClientService, ISettingsService settingsService, PopupViewModel popupViewModel, ILocalDatabaseService localDatabaseService)
    {
        _logger = logger;
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
        _settingsService = settingsService;
        _localDatabaseService = localDatabaseService;
        PopupViewModel = popupViewModel;
        _installedEnvs = p4kService.FindInstalledFiles();
        InstalledEnvs.ContinueWith(async x =>
        {
            if (x.IsFaulted)
            {
                // On rethrow si le chargement a échoué
                throw x.Exception;
            }

            // On récupère la liste initiale
            var list = await x;

            // Charge le chemin depuis le registre au démarrage
            // TODO Modifier la méthode pour retourner l'élément, comme ça on le retourne dans la liste
            var localElement = await LoadP4kFromRegistryAsync();

            if (localElement != null)
            {
                var existing = list.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(localElement.Path), StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    list.Insert(0, localElement);
                }
            }

            return list;
        })
        .Unwrap()
        .ContinueWith(async x =>
        {
            var list = await x;
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(InstalledEnvs));
                var savedPath = _settingsService.GetAsync(SelectedP4kSettingName).Result;
                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    var saved = list?.FirstOrDefault(e => string.Equals(Path.GetFullPath(e.Path), Path.GetFullPath(savedPath), StringComparison.OrdinalIgnoreCase));
                    SelectedP4kFile = saved ?? list?.FirstOrDefault();
                }
                else
                {
                    SelectedP4kFile = list?.FirstOrDefault();
                }
            });
        });
        Pages =
        [
            App.Current.Services.GetRequiredService<ShipTabViewModel>(),
            App.Current.Services.GetRequiredService<ItemsTabViewModel>(),
            App.Current.Services.GetRequiredService<BlueprintListTabViewModel>(),
            App.Current.Services.GetRequiredService<FriendListTabViewModel>(),
            App.Current.Services.GetRequiredService<P4kShipTabViewModel>(),
            App.Current.Services.GetRequiredService<MissionsTabViewModel>(),
            App.Current.Services.GetRequiredService<ExtractionTabViewModel>(),
            App.Current.Services.GetRequiredService<ReputationTabViewModel>(),
            App.Current.Services.GetRequiredService<SettingsTabViewModel>()
        ];
        
        CurrentPage = _pages.First();

        OpenDataP4kCommand = new AsyncRelayCommand<object?>(OpenDataP4kAsync);
        if (null != _p4kService.SelectedP4KFile)
        {
            OnSelectedP4KFileChanged(null, _p4kService.SelectedP4KFile);
        }

        _p4kService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;

        // Propage les changements d'état de chargement vers la VM pour les bindings (ex: couleur d'icône)
        _p4kService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IP4kService.FileLoadState))
            {
                OnPropertyChanged(nameof(FileLoadState));
                OnPropertyChanged(nameof(P4kStatusTooltip));
            }
        };

        // Abonnement au statut de connexion gRPC
        _grpcClientService.OnStatusChanged += OnGrpcConnectionStatusChanged;

    }
    
    [ObservableProperty]
    private PageViewModelBase _currentPage;

    [ObservableProperty] 
    private ObservableCollection<PageViewModelBase> _pages;

    partial void OnCurrentPageChanged(PageViewModelBase value)
    {
        _ = value.LoadAsync();
    }

    partial void OnSelectedP4kFileChanged(P4kFileModel? value)
    {
        _p4kService.SelectedP4KFile = value;
        if (value != null)
        {
            SaveSelectedP4k(Path.GetFullPath(value.Path));
        }
    }

    private async Task OpenDataP4kAsync(object? parameter)
    {
        try
        {
            // Expect the Window (or TopLevel) as parameter to access StorageProvider
            TopLevel? topLevel = parameter switch
            {
                TopLevel tl => tl,
                Control ctl => TopLevel.GetTopLevel(ctl),
                _ => null
            };

            if (topLevel?.StorageProvider is null)
                return;

            var options = new FilePickerOpenOptions
            {
                Title = "Choisir le fichier Data.p4k",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("P4K") { Patterns = new[] { "*.p4k" } }
                }
            };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(false);
            var file = files?.FirstOrDefault();
            if (file == null)
                return;

            if (!string.Equals(file.Name, "Data.p4k", StringComparison.OrdinalIgnoreCase))
                return;

            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
                return;
            
            var infos = await _p4kService.GetInstallationInfo(localPath).ConfigureAwait(false);

            if (infos == null)
                return;

            try
            {
                var list = await InstalledEnvs.ConfigureAwait(false);
                list ??= new List<P4kFileModel>();

                var existing = list.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(infos.Path), StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    var newList = list.ToList();
                    newList.Add(infos);
                    InstalledEnvs = Task.FromResult<IList<P4kFileModel>>(newList);
                    SelectedP4kFile = infos;
                    // Sauvegarde du dossier dans le registre
                    SaveP4kFolderToRegistry(Path.GetFullPath(infos.Path));
                }
                else
                {
                    SelectedP4kFile = existing;
                }

            }
            catch
            {
                var list = await InstalledEnvs.ConfigureAwait(false);
                list ??= new List<P4kFileModel>();

                var newList = list.ToList();
                newList.Add(infos);
                InstalledEnvs = Task.FromResult<IList<P4kFileModel>>(newList);
                SelectedP4kFile = infos;
                // Sauvegarde du dossier dans le registre
                SaveP4kFolderToRegistry(Path.GetFullPath(infos.Path));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la sélection du fichier Data.p4k");
        }
    }

    private CancellationTokenSource? _cts;
    private LoadingPopupContentViewModel? _rebuildPopupContent;
    
    private async void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        if (null != _cts)
        {
            await _cts.CancelAsync();
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        if (e is null || string.IsNullOrWhiteSpace(e.Path))
            return;

        try
        {
            var initClientTask = _grpcClientService.InitClient(e).WaitAsync(ct);
            UpdateP4kStatus("Chargement du fichier Data.p4k...");
            await _p4kService.OpenP4k(e.Path, new Progress<double>(), new Progress<double>()).WaitAsync(ct).ConfigureAwait(false);
            UpdateP4kStatus("Mise en cache des donnnées...");
            await _p4kService.FillDataCache().WaitAsync(ct).ConfigureAwait(false);
            UpdateP4kStatus("Chargement terminé");
            // On termine par attendre l'initialisation du client gRPC
            await RunRebuildWithPopupAsync().ConfigureAwait(false);
            await initClientTask;
        }
        catch (OperationCanceledException)
        {
            UpdateP4kStatus("Chargement annulé");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement du P4K");
            UpdateP4kStatus("Erreur lors du chargement");
            // Affiche un message d'erreur à l'utilisateur avec le détail technique (sans stacktrace)
            var fullMessage = BuildExceptionMessage(ex);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    WeakReferenceMessenger.Default.Send(new ShowPopupMessage(
                        showCloseButton: true,
                        onClose: null,
                        viewModel: new MessagePopupContentViewModel
                        {
                            Title = "Une erreur est survenue lors du chargement du fichier P4k",
                            Message = fullMessage
                        }
                    ));
                });
            }
            catch
            {
                // En dernier recours si l'UI thread n'est pas disponible, ne rien faire de plus
            }
        }        
    }

    private async Task RunRebuildWithPopupAsync()
    {
        if (!await _localDatabaseService.NeedsRebuildCheckAsync().ConfigureAwait(false))
        {
            return;
        }

        _rebuildPopupContent = new LoadingPopupContentViewModel
        {
            PhaseLabel = "Préparation de la base de données…",
            ShowLoading = true
        };

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WeakReferenceMessenger.Default.Send(new ShowPopupMessage(
                showCloseButton: false,
                onClose: null,
                viewModel: _rebuildPopupContent
            ));
        });

        try
        {
            var rebuildProgress = new Progress<RebuildProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_rebuildPopupContent != null)
                    {
                        _rebuildPopupContent.PhaseLabel = p.PhaseName;
                        _rebuildPopupContent.Progress = (double)p.CurrentPhase / p.TotalPhases * 100;
                        _rebuildPopupContent.Message = $"Phase {p.CurrentPhase}/{p.TotalPhases}";
                    }
                });
            });

            await _localDatabaseService.EnsureDbAsync(rebuildProgress).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_rebuildPopupContent != null)
                {
                    _rebuildPopupContent.PhaseLabel = "Terminé !";
                    _rebuildPopupContent.Progress = 100;
                    _rebuildPopupContent.Message = "";
                }
            });

            await Task.Delay(300).ConfigureAwait(false);
        }
        finally
        {
            _rebuildPopupContent = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WeakReferenceMessenger.Default.Send(new ClosePopupMessage());
            });
        }
    }

    private static string BuildExceptionMessage(Exception ex)
    {
        // Concatène les messages de toutes les InnerException sans stacktrace
        var parts = new List<string>();
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(cur.Message))
            {
                parts.Add(cur.Message.Trim());
            }
        }

        return string.Join("\n → ", parts.Distinct());
    }
    
    private void OnSelectedP4KFileChangedDoNotUse(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, reconnexion
        _ = _grpcClientService.InitClient(e);
        // Ensuite on relance le chargement du fichier au global
        UpdateP4kStatus("Chargement du fichier Data.p4k...");
        _p4kService.OpenP4k(e.Path, new Progress<double>(), new Progress<double>())
            .ContinueWith((t) =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    return t;
                }
                
                UpdateP4kStatus("Mise en cache des donnnées...");
                // lancer le chargement des données de cache
                return _p4kService.FillDataCache();
            })
            .Unwrap()
            .ContinueWith((t) =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    _logger.LogError(t.Exception, "Erreur lors du chargement du fichier Data.p4k");
                    UpdateP4kStatus("Chargement en erreur");
                }
                else
                {
                    UpdateP4kStatus("Chargement terminé");
                }
            });
    }

    private void SaveP4kFolderToRegistry(string folderPath)
    {
        try
        {
            _ = _settingsService.SetAsync(P4kFolderSettingName, folderPath);
        }
        catch (Exception ex)
        {
            // Ignorer les erreurs d'accès au registre pour ne pas bloquer l'utilisateur
            _logger.LogWarning(ex, "Impossible de sauvegarder le dossier P4K dans les paramètres");
        }
    }

    private void SaveSelectedP4k(string p4kPath)
    {
        try
        {
            _ = _settingsService.SetAsync(SelectedP4kSettingName, p4kPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de sauvegarder le P4K sélectionné");
        }
    }

    private async Task<P4kFileModel?> LoadP4kFromRegistryAsync()
    {
        try
        {
            var dataP4kPath = await _settingsService.GetAsync(P4kFolderSettingName).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(dataP4kPath))
                return null;

            var infos = await _p4kService.GetInstallationInfo(dataP4kPath).ConfigureAwait(false);
            if (infos?.Manifest is not null)
            {
                return infos;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger le dossier P4K depuis le registre");
        }

        return null;
    }
    
    private void UpdateP4kStatus(string status)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            P4kStatus = status;
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateP4kStatus(status));
        }
    }

    // Texte affiché dans l'infobulle au survol de l'icône d'état
    public string P4kStatusTooltip
    {
        get
        {
            var state = _p4kService.FileLoadState;
            return state switch
            {
                P4kService.P4kFileLoadState.NotLoaded => "P4K: Non chargé",
                P4kService.P4kFileLoadState.Loading => "P4K: En cours de chargement...",
                P4kService.P4kFileLoadState.Loaded => "P4K: Chargé",
                P4kService.P4kFileLoadState.CacheLoading => "P4K: Cache en cours de chargement...",
                P4kService.P4kFileLoadState.CacheLoaded => "P4K: Cache chargé",
                P4kService.P4kFileLoadState.Cancelled => "P4K: Chargement annulé",
                P4kService.P4kFileLoadState.Error =>
                    string.IsNullOrWhiteSpace(_p4kService.GetLastErrorMessage())
                        ? "P4K: En erreur"
                        : $"P4K: En erreur\n{_p4kService.GetLastErrorMessage()}",
                _ => "P4K: Inconnu"
            };
        }
    }

    // Expose l'état du service pour les bindings UI (ex: couleur de l'icône dans MainWindow)
    public P4kService.P4kFileLoadState FileLoadState => _p4kService.FileLoadState;
}