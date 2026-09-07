using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StarXelem.Services.Scan;

namespace StarXelem.Components;

/// <summary>
/// Zone de saisie d'un raccourci clavier : clic puis pression d'une combinaison (au moins un modificateur +
/// une touche prise en charge par <see cref="HotkeyKeyMapping"/>) capture <see cref="Key"/> et <see cref="Modifiers"/>.
/// </summary>
public class HotkeyBox : TextBox
{
    public static readonly StyledProperty<Key> KeyProperty =
        AvaloniaProperty.Register<HotkeyBox, Key>(nameof(Key), Key.F9, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<KeyModifiers> ModifiersProperty =
        AvaloniaProperty.Register<HotkeyBox, KeyModifiers>(nameof(Modifiers), KeyModifiers.Control, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public Key Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public KeyModifiers Modifiers
    {
        get => GetValue(ModifiersProperty);
        set => SetValue(ModifiersProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public HotkeyBox()
    {
        IsReadOnly = true;
        Focusable = true;
        UpdateDisplayText();
    }

    static HotkeyBox()
    {
        KeyProperty.Changed.AddClassHandler<HotkeyBox>((box, _) => box.UpdateDisplayText());
        ModifiersProperty.Changed.AddClassHandler<HotkeyBox>((box, _) => box.UpdateDisplayText());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key is Key.Escape)
        {
            UpdateDisplayText();
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

        Key = e.Key;
        Modifiers = e.KeyModifiers;
        ToolTip.SetTip(this, null);
    }

    private void UpdateDisplayText()
    {
        Text = new ScanHotkeySettings(true, Key, Modifiers).ToDisplayString();
    }
}
