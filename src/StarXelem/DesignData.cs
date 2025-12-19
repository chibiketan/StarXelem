using Microsoft.Extensions.DependencyInjection;
using StarXelem.Models;
using StarXelem.ViewModels;
using StarXelem.ViewModels.Popup;

namespace StarXelem;

public static class DesignData
{
    public static MainWindowViewModel MainWindowViewModel { get; } = App.Current.Services.GetRequiredService<MainWindowViewModel>();
    public static ShipTabViewModel ShipTabViewModel { get; } = App.Current.Services.GetRequiredService<ShipTabViewModel>();
    public static ItemsTabViewModel ItemsTabViewModel { get; } = App.Current.Services.GetRequiredService<ItemsTabViewModel>();
    public static ContainerTabViewModel ContainerTabViewModel { get; } = App.Current.Services.GetRequiredService<ContainerTabViewModel>();
    public static FriendListTabViewModel FriendListTabViewModel { get; } = App.Current.Services.GetRequiredService<FriendListTabViewModel>();

    public static PopupViewModel ComparisonPopupViewModel { get; } = App.Current.Services.GetRequiredService<PopupViewModel>();
    
    public static ItemComparisonPopupContentViewModel ItemComparisonPopupContentViewModel { get; } = App.Current.Services.GetRequiredService<ItemComparisonPopupContentViewModel>();


    static DesignData()
    {
        // Initialisation de ItemComparisonPopupContentViewModel pour le design
        var list = new List<ItemTypeComparisonResult>
        {
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 1", SourceCountSum = 1, TargetStackSum = 1, Status = ComparisonDiffType.Equal
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 2", SourceCountSum = 1, TargetStackSum = 10, Status = ComparisonDiffType.Gain
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 3", SourceCountSum = 10, TargetStackSum = 1, Status = ComparisonDiffType.Loss
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type only left", SourceCountSum = 1, TargetStackSum = 0, Status = ComparisonDiffType.OnlySource
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type only right", SourceCountSum = 0, TargetStackSum = 1, Status = ComparisonDiffType.OnlyTarget
            },
        };
        
        // Add 200 items to the list
        list.AddRange(Enumerable.Repeat(new ItemTypeComparisonResult{TechnicalType = "Test", SourceCountSum = 0, TargetStackSum = 0, Status = ComparisonDiffType.Equal}, 200));
        
        ItemComparisonPopupContentViewModel.FilteredResults = list;
        
        // comparison popup
        ComparisonPopupViewModel.ContentViewModel = ItemComparisonPopupContentViewModel;
        ComparisonPopupViewModel.IsVisible = true;
        ComparisonPopupViewModel.IsCloseButtonVisible = true;

    }
}