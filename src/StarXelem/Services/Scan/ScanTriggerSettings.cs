using System.Text;
using Avalonia.Input;
using StarXelem.Constants;

namespace StarXelem.Services.Scan;

public enum ScanTriggerKind
{
    Keyboard,
    Joystick,
}

/// <summary>Bouton d'un joystick/HOTAS identifié par l'instance DirectInput de l'appareil (stable sur une machine donnée).</summary>
public sealed record JoystickBinding(Guid InstanceGuid, string ProductName, int Button)
{
    public string ToDisplayString() => $"🕹 {ProductName} — Bouton {Button + 1}";
}

/// <summary>
/// Déclencheur du scan de signature : une combinaison clavier <b>ou</b> un bouton de joystick, jamais les deux.
/// Les paramètres clavier existants sont conservés tels quels ; l'absence de clé <c>ScanTriggerKind</c> en
/// registre équivaut à <see cref="ScanTriggerKind.Keyboard"/> (aucune migration nécessaire).
/// </summary>
public sealed record ScanTriggerSettings(bool Enabled, ScanTriggerKind Kind, Key Key, KeyModifiers Modifiers, JoystickBinding? Joystick)
{
    public static ScanTriggerSettings Default => new(true, ScanTriggerKind.Keyboard, Key.F9, KeyModifiers.Control, null);

    public string ToDisplayString()
    {
        if (Kind == ScanTriggerKind.Joystick)
        {
            return Joystick?.ToDisplayString() ?? "(aucun bouton)";
        }

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl + ");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt + ");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift + ");
        sb.Append(Key);
        return sb.ToString();
    }

    public static async Task<ScanTriggerSettings> LoadAsync(ISettingsService settings)
    {
        try
        {
            var enabledRaw = await settings.GetAsync(ScanConstants.SettingHotkeyEnabled).ConfigureAwait(false);
            var keyRaw = await settings.GetAsync(ScanConstants.SettingHotkeyKey).ConfigureAwait(false);
            var modifiersRaw = await settings.GetAsync(ScanConstants.SettingHotkeyModifiers).ConfigureAwait(false);
            var kindRaw = await settings.GetAsync(ScanConstants.SettingTriggerKind).ConfigureAwait(false);
            var guidRaw = await settings.GetAsync(ScanConstants.SettingJoystickInstanceGuid).ConfigureAwait(false);
            var productRaw = await settings.GetAsync(ScanConstants.SettingJoystickProductName).ConfigureAwait(false);
            var buttonRaw = await settings.GetAsync(ScanConstants.SettingJoystickButton).ConfigureAwait(false);

            var enabled = enabledRaw == null || bool.Parse(enabledRaw);
            var key = !string.IsNullOrEmpty(keyRaw) && Enum.TryParse<Key>(keyRaw, out var parsedKey) ? parsedKey : Default.Key;
            var modifiers = !string.IsNullOrEmpty(modifiersRaw) && Enum.TryParse<KeyModifiers>(modifiersRaw, out var parsedModifiers) ? parsedModifiers : Default.Modifiers;
            var kind = !string.IsNullOrEmpty(kindRaw) && Enum.TryParse<ScanTriggerKind>(kindRaw, out var parsedKind) ? parsedKind : ScanTriggerKind.Keyboard;

            JoystickBinding? joystick = null;
            if (Guid.TryParse(guidRaw, out var guid) && int.TryParse(buttonRaw, out var button) && button >= 0)
            {
                joystick = new JoystickBinding(guid, productRaw ?? "Joystick", button);
            }

            if (kind == ScanTriggerKind.Joystick && joystick == null)
            {
                kind = ScanTriggerKind.Keyboard;
            }

            return new ScanTriggerSettings(enabled, kind, key, modifiers, joystick);
        }
        catch
        {
            return Default;
        }
    }

    public async Task SaveAsync(ISettingsService settings)
    {
        await settings.SetAsync(ScanConstants.SettingHotkeyEnabled, Enabled.ToString()).ConfigureAwait(false);
        await settings.SetAsync(ScanConstants.SettingHotkeyKey, Key.ToString()).ConfigureAwait(false);
        await settings.SetAsync(ScanConstants.SettingHotkeyModifiers, Modifiers.ToString()).ConfigureAwait(false);
        await settings.SetAsync(ScanConstants.SettingTriggerKind, Kind.ToString()).ConfigureAwait(false);

        if (Joystick != null)
        {
            await settings.SetAsync(ScanConstants.SettingJoystickInstanceGuid, Joystick.InstanceGuid.ToString()).ConfigureAwait(false);
            await settings.SetAsync(ScanConstants.SettingJoystickProductName, Joystick.ProductName).ConfigureAwait(false);
            await settings.SetAsync(ScanConstants.SettingJoystickButton, Joystick.Button.ToString()).ConfigureAwait(false);
        }
        else
        {
            await settings.ClearAsync(ScanConstants.SettingJoystickInstanceGuid).ConfigureAwait(false);
            await settings.ClearAsync(ScanConstants.SettingJoystickProductName).ConfigureAwait(false);
            await settings.ClearAsync(ScanConstants.SettingJoystickButton).ConfigureAwait(false);
        }
    }
}
