using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarXelem.Models;
using StarXelem.Services;

namespace StarXelem.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IP4kService _p4kService;
    [ObservableProperty]
    private Task<IList<P4kFileModel>> _installedEnvs;
    [ObservableProperty]
    private P4kFileModel? selectedP4kFile;
    
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IP4kService p4kService)
    {
        _logger = logger;
        _p4kService = p4kService;
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
            App.Current.Services.GetRequiredService<ContainerTabViewModel>()
        ];
        
        CurrentPage = _pages.First();
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

}