using Avalonia.Controls;
using StarXelem.Services.Scan.Win32;

namespace StarXelem.Views.Overlay;

public partial class SignatureOverlayWindow : Window
{
    public SignatureOverlayWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        var exStyle = NativeMethods.GetWindowExStyle(handle).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowExStyle(handle, new IntPtr(exStyle));
    }
}
