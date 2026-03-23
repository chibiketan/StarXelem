using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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
    private const string RegistryBasePath = "Software\\StarXelem";
    private const string RegistryValueName = "P4KFolder";

    [ObservableProperty]
    private Task<IList<P4kFileModel>> _installedEnvs;
    [ObservableProperty]
    private P4kFileModel? selectedP4kFile;
    
    [ObservableProperty]private string _p4kStatus = "";

    [ObservableProperty]
    private PopupViewModel _popupViewModel;

    public IAsyncRelayCommand OpenDataP4kCommand { get; }
    
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IP4kService p4kService, IGrpcClientService grpcClientService, PopupViewModel popupViewModel)
    {
        _logger = logger;
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
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
                SelectedP4kFile = list?.FirstOrDefault();
            });
        });
        Pages =
        [
            App.Current.Services.GetRequiredService<ShipTabViewModel>(),
            App.Current.Services.GetRequiredService<P4kShipTabViewModel>(),
            App.Current.Services.GetRequiredService<ItemsTabViewModel>(),
            App.Current.Services.GetRequiredService<ContainerTabViewModel>(),
            App.Current.Services.GetRequiredService<FriendListTabViewModel>(),
            App.Current.Services.GetRequiredService<ExtractionTabViewModel>(),
            App.Current.Services.GetRequiredService<MissionsTabViewModel>()
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
        // On met à jour le fichier sélectionné dans le service
        _p4kService.SelectedP4KFile = value;
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
            using var key = Registry.CurrentUser.CreateSubKey(RegistryBasePath);
            key?.SetValue(RegistryValueName, folderPath, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            // Ignorer les erreurs d'accès au registre pour ne pas bloquer l'utilisateur
            _logger.LogWarning(ex, "Impossible de charger le dossier P4K depuis le registre");

        }
    }

    private async Task<P4kFileModel?> LoadP4kFromRegistryAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryBasePath);
            var dataP4kPath = key?.GetValue(RegistryValueName) as string;
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