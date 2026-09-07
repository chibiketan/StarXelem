using System.Text;
using Avalonia.Input;
using StarXelem.Constants;

namespace StarXelem.Services.Scan;

public sealed record ScanHotkeySettings(bool Enabled, Key Key, KeyModifiers Modifiers)
{
    public static ScanHotkeySettings Default => new(true, Key.F9, KeyModifiers.Control);

    public string ToDisplayString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl + ");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt + ");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift + ");
        sb.Append(Key);
        return sb.ToString();
    }

    public static async Task<ScanHotkeySettings> LoadAsync(ISettingsService settings)
    {
        try
        {
            var enabledRaw = await settings.GetAsync(ScanConstants.SettingHotkeyEnabled).ConfigureAwait(false);
            var keyRaw = await settings.GetAsync(ScanConstants.SettingHotkeyKey).ConfigureAwait(false);
            var modifiersRaw = await settings.GetAsync(ScanConstants.SettingHotkeyModifiers).ConfigureAwait(false);

            var enabled = enabledRaw == null || bool.Parse(enabledRaw);
            var key = !string.IsNullOrEmpty(keyRaw) && Enum.TryParse<Key>(keyRaw, out var parsedKey) ? parsedKey : Default.Key;
            var modifiers = !string.IsNullOrEmpty(modifiersRaw) && Enum.TryParse<KeyModifiers>(modifiersRaw, out var parsedModifiers) ? parsedModifiers : Default.Modifiers;

            return new ScanHotkeySettings(enabled, key, modifiers);
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
    }
}
