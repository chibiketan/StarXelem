using System.Runtime.InteropServices;
using Avalonia.Input;
using Microsoft.Extensions.Logging;
using StarXelem.Services.Scan.Win32;

namespace StarXelem.Services.Scan;

/// <summary>
/// Enregistre un raccourci clavier global via <c>RegisterHotKey</c> Win32, sur un thread dédié muni de sa
/// propre boucle de messages (<c>GetMessage</c>) : ne nécessite aucune fenêtre et fonctionne même quand
/// une autre application (le jeu) a le focus. Le raccourci est consommé : il n'est pas transmis au jeu.
/// </summary>
public class Win32GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0xB00C; // arbitraire, unique au sein du process

    private readonly ILogger<Win32GlobalHotkeyService> _logger;
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _registered = new(false);
    private bool _lastRegisterResult;
    private string? _lastRegisterError;

    public event EventHandler? Triggered;
    public event EventHandler? StatusChanged;

    private ScanTriggerStatus _status = ScanTriggerStatus.Disabled;
    public ScanTriggerStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Win32GlobalHotkeyService(ILogger<Win32GlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public bool TryApply(ScanTriggerSettings settings, out string? error)
    {
        Stop();
        error = null;

        if (!settings.Enabled || settings.Kind != ScanTriggerKind.Keyboard)
        {
            Status = ScanTriggerStatus.Disabled;
            return true;
        }

        if (!HotkeyKeyMapping.TryGetVirtualKey(settings.Key, out var vk))
        {
            error = $"La touche '{settings.Key}' n'est pas prise en charge pour le raccourci de scan.";
            return false;
        }

        var mods = ToModifierFlags(settings.Modifiers);
        if (mods == 0)
        {
            error = "Choisissez au moins un modificateur (Ctrl, Alt ou Shift).";
            return false;
        }

        _registered.Reset();
        _thread = new Thread(() => RunMessageLoop(mods, vk)) { IsBackground = true, Name = "StarXelem.Hotkey" };
        _thread.Start();

        // Attend que le thread ait tenté RegisterHotKey (ou échoué à démarrer sa boucle) avant de répondre.
        if (!_registered.Wait(TimeSpan.FromSeconds(5)))
        {
            error = "Le service de raccourci n'a pas répondu à temps.";
            Status = ScanTriggerStatus.Error;
            return false;
        }

        error = _lastRegisterError;
        Status = _lastRegisterResult ? ScanTriggerStatus.Active : ScanTriggerStatus.Error;
        return _lastRegisterResult;
    }

    public void Stop()
    {
        if (_thread == null) return;

        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        Status = ScanTriggerStatus.Disabled;
    }

    private void RunMessageLoop(uint mods, uint vk)
    {
        _threadId = GetCurrentThreadId();

        _lastRegisterResult = NativeMethods.RegisterHotKey(IntPtr.Zero, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk);
        if (!_lastRegisterResult)
        {
            var err = Marshal.GetLastPInvokeError();
            _lastRegisterError = "Ce raccourci est déjà utilisé par une autre application (code Windows " + err + ").";
            _logger.LogWarning("RegisterHotKey a échoué : {Error}", _lastRegisterError);
            _registered.Set();
            return;
        }

        _registered.Set();

        try
        {
            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == NativeMethods.WM_HOTKEY)
                {
                    try
                    {
                        Triggered?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur dans le gestionnaire Triggered.");
                    }
                }
            }
        }
        finally
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static uint ToModifierFlags(KeyModifiers modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(KeyModifiers.Control)) flags |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Alt)) flags |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Shift)) flags |= NativeMethods.MOD_SHIFT;
        return flags;
    }

    public void Dispose()
    {
        Stop();
        _registered.Dispose();
    }
}
