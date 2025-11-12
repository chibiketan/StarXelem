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
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;

    [ObservableProperty]
    private Task<IList<P4kFileModel>> _installedEnvs;
    [ObservableProperty]
    private P4kFileModel? selectedP4kFile;

    public IAsyncRelayCommand OpenDataP4kCommand { get; }
    
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IP4kService p4kService, IGrpcClientService grpcClientService)
    {
        _logger = logger;
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
        InstalledEnvs = p4kService.FindInstalledFiles();
        InstalledEnvs.ContinueWith(async x =>
        {
            var list = await x;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedP4kFile = list?.FirstOrDefault();
            });
        });
        Pages =
        [
            App.Current.Services.GetRequiredService<ShipTabViewModel>(),
            App.Current.Services.GetRequiredService<ItemsTabViewModel>(),
            App.Current.Services.GetRequiredService<ContainerTabViewModel>(),
            App.Current.Services.GetRequiredService<FriendListTabViewModel>()
        ];
        
        CurrentPage = _pages.First();

        OpenDataP4kCommand = new AsyncRelayCommand<object?>(OpenDataP4kAsync);
        if (null != _p4kService.SelectedP4KFile)
        {
            _grpcClientService.InitClient(_p4kService.SelectedP4KFile);
        }

        _p4kService.SelectedP4KFileChanged += OnSelectedP4KFileChanged;

    }
    
    [ObservableProperty]
    private PageViewModelBase _currentPage;

    [ObservableProperty] 
    private ObservableCollection<PageViewModelBase> _pages;

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

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            var file = files?.FirstOrDefault();
            if (file == null)
                return;

            if (!string.Equals(file.Name, "Data.p4k", StringComparison.OrdinalIgnoreCase))
                return;

            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
                return;
            
            var infos = await _p4kService.GetInstallationInfo(localPath);

            if (infos == null)
                return;

            try
            {
                var list = await InstalledEnvs;
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
            _logger.LogError(ex, "Erreur lors de la sélection du fichier Data.p4k");
        }
    }
    
    private void OnSelectedP4KFileChanged(Object? sender, P4kFileModel? e)
    {
        // Le fichier a été modifié, reconnexion
        _grpcClientService.InitClient(e);
    }
}