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
        InstalledEnvs = p4kService.FindInstalledFiles();
        InstalledEnvs.ContinueWith(async x =>
        {
            // Charge le chemin depuis le registre au démarrage
            await LoadP4kFromRegistryAsync();

            var list = await x;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
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
        }        
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

    private async Task LoadP4kFromRegistryAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryBasePath);
            var dataP4kPath = key?.GetValue(RegistryValueName) as string;
            if (string.IsNullOrWhiteSpace(dataP4kPath))
                return;

            var infos = await _p4kService.GetInstallationInfo(dataP4kPath).ConfigureAwait(false);
            if (infos == null || infos.Manifest == null)
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
                }
                else
                {
                    SelectedP4kFile = existing;
                }
            }
            catch
            {
                SelectedP4kFile = infos;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger le dossier P4K depuis le registre");
        }
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
}