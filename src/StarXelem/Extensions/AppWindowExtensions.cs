using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace StarXelem.Extensions;

public static class AppWindowExtensions
{
    public static void EnableMicaTransparency(this Window window)
    {
        //return;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        
        window.TransparencyLevelHint = [WindowTransparencyLevel.Mica];
        window.Background = new SolidColorBrush(new Color(64, 0, 0, 0));
    }
}