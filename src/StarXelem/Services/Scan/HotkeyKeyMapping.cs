using Avalonia.Input;

namespace StarXelem.Services.Scan;

/// <summary>Table de conversion des touches <see cref="Key"/> Avalonia prises en charge par le raccourci global vers leur code Virtual-Key Win32.</summary>
public static class HotkeyKeyMapping
{
    public static bool TryGetVirtualKey(Key key, out uint vk)
    {
        vk = key switch
        {
            >= Key.A and <= Key.Z => (uint)('A' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
            >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Pause => 0x13,
            Key.Scroll => 0x91,
            _ => 0,
        };
        return vk != 0;
    }

    public static bool IsSupported(Key key) => TryGetVirtualKey(key, out _);
}
