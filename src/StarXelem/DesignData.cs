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
    public static P4kShipTabViewModel P4kShipTabViewModel { get; } = App.Current.Services.GetRequiredService<P4kShipTabViewModel>();


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
        
        P4kShipTabViewModel.Ships.Add(new P4kShipModel
        {
            EntityClass = null,
            Name = "Test ship",
            TechnicalName = "test_ship",
            Manufacturer = "Test Manufacturer"
        });

        // Coolers
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "A",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler A1",
            PortName = "CoolerA1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "B",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler B1",
            PortName = "CoolerB1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "C",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler C1",
            PortName = "CoolerC1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "D",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler D1",
            PortName = "CoolerD1"
        });
        
        // Powerplants
        P4kShipTabViewModel.PowerplantList.AddRange([
            new ()
            {
                Grade = "A",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant A0",
                PortName = "PowerplantA0"
            },
            new ()
            {
                Grade = "B",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant B0",
                PortName = "PowerplantB0"
            },
            new ()
            {
                Grade = "C",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant C0",
                PortName = "PowerplantC0"
            },
            new ()
            {
                Grade = "D",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant D0",
                PortName = "PowerplantD0"
            }
        ]);
    }
}