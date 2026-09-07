using Microsoft.Extensions.Logging;
using StarXelem.Constants;
using Vortice.DirectInput;

namespace StarXelem.Services.Scan;

/// <summary>
/// Déclencheur par bouton de joystick/HOTAS via DirectInput 8 : lecture de l'état matériel en mode
/// <c>Background | NonExclusive</c> sur un thread dédié, donc fonctionnelle même quand le jeu a le focus.
/// Contrairement au raccourci clavier (<see cref="Win32GlobalHotkeyService"/>), la pression n'est pas
/// interceptée : le jeu la reçoit aussi. Détection sur front montant avec anti-rebond.
/// </summary>
public class DirectInputJoystickService : IJoystickTriggerService
{
    private readonly ILogger<DirectInputJoystickService> _logger;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

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

    public DirectInputJoystickService(ILogger<DirectInputJoystickService> logger)
    {
        _logger = logger;
    }

    public bool TryApply(ScanTriggerSettings settings, out string? error)
    {
        Stop();
        error = null;

        if (!settings.Enabled || settings.Kind != ScanTriggerKind.Joystick || settings.Joystick == null)
        {
            Status = ScanTriggerStatus.Disabled;
            return true;
        }

        var binding = settings.Joystick;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() => RunPollLoop(binding, token)) { IsBackground = true, Name = "StarXelem.Joystick" };
        _thread.Start();
        return true;
    }

    public void Stop()
    {
        if (_thread == null) return;

        _cts?.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
        Status = ScanTriggerStatus.Disabled;
    }

    private void RunPollLoop(JoystickBinding binding, CancellationToken token)
    {
        IDirectInput8? directInput = null;
        IDirectInputDevice8? device = null;
        var lastPressed = false;
        var lastTrigger = DateTime.MinValue;
        var consecutiveFailures = 0;

        try
        {
            directInput = DInput.DirectInput8Create();

            while (!token.IsCancellationRequested)
            {
                if (device == null)
                {
                    device = TryOpenDevice(directInput, binding.InstanceGuid);
                    if (device == null)
                    {
                        Status = ScanTriggerStatus.DeviceNotConnected;
                        if (token.WaitHandle.WaitOne(ScanConstants.JoystickReenumerateIntervalMs)) break;
                        continue;
                    }

                    Status = ScanTriggerStatus.Active;
                    lastPressed = false;
                    consecutiveFailures = 0;
                    _logger.LogInformation("Joystick « {Name} » connecté, bouton {Button} surveillé.", binding.ProductName, binding.Button + 1);
                }

                try
                {
                    if (device.Poll().Failure && device.Acquire().Failure)
                    {
                        consecutiveFailures++;
                    }
                    else
                    {
                        consecutiveFailures = 0;
                        var state = device.GetCurrentJoystickState();
                        var pressed = binding.Button < state.Buttons.Length && state.Buttons[binding.Button];

                        if (pressed && !lastPressed && (DateTime.UtcNow - lastTrigger).TotalMilliseconds >= ScanConstants.JoystickDebounceMs)
                        {
                            lastTrigger = DateTime.UtcNow;
                            RaiseTriggered();
                        }

                        lastPressed = pressed;
                    }
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    _logger.LogDebug(ex, "Lecture du joystick en échec ({Count}).", consecutiveFailures);
                }

                if (consecutiveFailures >= 10)
                {
                    _logger.LogWarning("Joystick « {Name} » perdu (débranché ?), nouvelle tentative périodique.", binding.ProductName);
                    ReleaseDevice(ref device);
                    continue;
                }

                if (token.WaitHandle.WaitOne(ScanConstants.JoystickPollIntervalMs)) break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur fatale du thread joystick.");
            Status = ScanTriggerStatus.Error;
        }
        finally
        {
            ReleaseDevice(ref device);
            directInput?.Dispose();
        }
    }

    private void RaiseTriggered()
    {
        try
        {
            Triggered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur dans le gestionnaire Triggered (joystick).");
        }
    }

    private IDirectInputDevice8? TryOpenDevice(IDirectInput8 directInput, Guid instanceGuid)
    {
        try
        {
            if (!directInput.IsDeviceAttached(instanceGuid))
                return null;

            var device = directInput.CreateDevice(instanceGuid);
            device.SetDataFormat<RawJoystickState>();
            device.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            if (device.Acquire().Failure)
            {
                device.Dispose();
                return null;
            }
            return device;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible d'ouvrir le joystick {Guid}.", instanceGuid);
            return null;
        }
    }

    private static void ReleaseDevice(ref IDirectInputDevice8? device)
    {
        if (device == null) return;
        try
        {
            device.Unacquire();
        }
        catch
        {
            // ignoré : l'appareil peut déjà être débranché
        }
        device.Dispose();
        device = null;
    }

    public Task<JoystickBinding?> CaptureNextButtonAsync(CancellationToken cancellationToken)
    {
        // Instance DirectInput et appareils dédiés à la capture : indépendants du thread de polling,
        // qui peut continuer à surveiller le bouton actuellement configuré pendant la saisie.
        return Task.Run<JoystickBinding?>(() =>
        {
            using var directInput = DInput.DirectInput8Create();
            var devices = new List<(DeviceInstance Instance, IDirectInputDevice8 Device, bool[] Baseline)>();

            try
            {
                foreach (var instance in directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    try
                    {
                        var device = directInput.CreateDevice(instance.InstanceGuid);
                        device.SetDataFormat<RawJoystickState>();
                        device.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        if (device.Acquire().Failure)
                        {
                            device.Dispose();
                            continue;
                        }

                        device.Poll();
                        // Boutons déjà enfoncés au début de la capture : ignorés jusqu'à relâchement.
                        var baseline = (bool[])device.GetCurrentJoystickState().Buttons.Clone();
                        devices.Add((instance, device, baseline));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Joystick {Name} ignoré pour la capture.", instance.ProductName);
                    }
                }

                if (devices.Count == 0)
                {
                    _logger.LogInformation("Aucun joystick branché pour la capture.");
                    return null;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var (instance, device, baseline) in devices)
                    {
                        try
                        {
                            if (device.Poll().Failure && device.Acquire().Failure) continue;
                            var buttons = device.GetCurrentJoystickState().Buttons;
                            for (var i = 0; i < buttons.Length; i++)
                            {
                                if (buttons[i] && !baseline[i])
                                {
                                    return new JoystickBinding(instance.InstanceGuid, instance.ProductName, i);
                                }
                                if (!buttons[i]) baseline[i] = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Lecture de {Name} en échec pendant la capture.", instance.ProductName);
                        }
                    }

                    if (cancellationToken.WaitHandle.WaitOne(ScanConstants.JoystickPollIntervalMs)) break;
                }

                return null;
            }
            finally
            {
                foreach (var entry in devices)
                {
                    var d = entry.Device;
                    ReleaseDevice(ref d!);
                }
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        Stop();
    }
}
