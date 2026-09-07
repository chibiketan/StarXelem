using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StarXelem.Services.Scan;

namespace StarXelem.Components;

/// <summary>
/// Zone de saisie du déclencheur de scan : on clique, puis on appuie <b>soit</b> sur une combinaison clavier
/// (au moins un modificateur + une touche prise en charge par <see cref="HotkeyKeyMapping"/>), <b>soit</b> sur un
/// bouton de n'importe quel joystick branché. La première entrée reçue devient le déclencheur.
/// La capture joystick est déléguée via <see cref="JoystickCaptureProvider"/> pour ne pas coupler le contrôle au service.
/// </summary>
public class TriggerCaptureBox : TextBox
{
    private const string CapturePrompt = "Appuyez sur une touche (avec Ctrl, Alt ou Shift) ou sur un bouton du joystick…";

    public static readonly StyledProperty<ScanTriggerSettings?> TriggerProperty =
        AvaloniaProperty.Register<TriggerCaptureBox, ScanTriggerSettings?>(nameof(Trigger), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<Func<CancellationToken, Task<JoystickBinding?>>?> JoystickCaptureProviderProperty =
        AvaloniaProperty.Register<TriggerCaptureBox, Func<CancellationToken, Task<JoystickBinding?>>?>(nameof(JoystickCaptureProvider));

    public ScanTriggerSettings? Trigger
    {
        get => GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public Func<CancellationToken, Task<JoystickBinding?>>? JoystickCaptureProvider
    {
        get => GetValue(JoystickCaptureProviderProperty);
        set => SetValue(JoystickCaptureProviderProperty, value);
    }

    private CancellationTokenSource? _captureCts;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public TriggerCaptureBox()
    {
        IsReadOnly = true;
        Focusable = true;
        UpdateDisplayText();
    }

    static TriggerCaptureBox()
    {
        TriggerProperty.Changed.AddClassHandler<TriggerCaptureBox>((box, _) => box.UpdateDisplayText());
    }

    private ScanTriggerSettings Current => Trigger ?? ScanTriggerSettings.Default;

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Text = CapturePrompt;
        StartJoystickCapture();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        CancelJoystickCapture();
        UpdateDisplayText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key is Key.Escape)
        {
            CancelJoystickCapture();
            UpdateDisplayText();
            ClearFocus();
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            Text = "(aucun)";
            return;
        }

        // Touches de modificateur seules : ignorées, on attend la touche finale.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (!HotkeyKeyMapping.IsSupported(e.Key))
        {
            ToolTip.SetTip(this, $"Touche '{e.Key}' non prise en charge.");
            return;
        }

        CancelJoystickCapture();
        ToolTip.SetTip(this, null);
        Trigger = Current with { Kind = ScanTriggerKind.Keyboard, Key = e.Key, Modifiers = e.KeyModifiers };
        UpdateDisplayText();
        ClearFocus();
    }

    /// <summary>
    /// Retire le focus du champ une fois la saisie terminée (touche choisie, bouton capturé, ou Échap) :
    /// permet de recliquer immédiatement pour recommencer une capture avec un autre déclencheur.
    /// </summary>
    private void ClearFocus()
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
    }

    private async void StartJoystickCapture()
    {
        var provider = JoystickCaptureProvider;
        if (provider == null) return;

        CancelJoystickCapture();
        var cts = new CancellationTokenSource();
        _captureCts = cts;

        try
        {
            var binding = await provider(cts.Token);
            if (binding == null || cts.IsCancellationRequested || !ReferenceEquals(_captureCts, cts)) return;

            // La session de capture doit être terminée AVANT d'affecter le déclencheur : le rafraîchissement
            // du texte est ignoré tant qu'une capture est en cours (le champ garde le focus à ce moment-là).
            _captureCts = null;
            Trigger = Current with { Kind = ScanTriggerKind.Joystick, Joystick = binding };
            UpdateDisplayText();
            ToolTip.SetTip(this, null);
            ClearFocus();
        }
        catch (OperationCanceledException)
        {
            // capture annulée (perte de focus, Échap, saisie clavier)
        }
        finally
        {
            if (ReferenceEquals(_captureCts, cts))
            {
                _captureCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelJoystickCapture()
    {
        var cts = _captureCts;
        _captureCts = null;
        cts?.Cancel();
    }

    private void UpdateDisplayText()
    {
        if (IsFocused && _captureCts != null) return;
        Text = Current.ToDisplayString();
    }
}
