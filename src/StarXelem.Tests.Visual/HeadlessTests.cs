using Avalonia;
using Avalonia.Headless.XUnit;
using StarXelem;
using Xunit;

namespace StarXelem.Tests.Visual;

public class HeadlessTests
{
    [AvaloniaFact]
    public void Test_App_Starts_In_Headless_Mode()
    {
        // Arrange & Act
        // Le framework Avalonia.Headless.XUnit gère le démarrage de l'application via [AvaloniaFact]

        // Assert
        Assert.True(true); // Si on arrive ici sans crash, c'est que le setup est OK
    }
}
