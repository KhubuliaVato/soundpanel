using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SoundPanel.Services;

/// <summary>System-wide hotkeys (work while the app is minimized or another app is focused).</summary>
public static class HotkeyService
{
    public const int WmHotkey = 0x0312;

    private const uint ModAlt = 1;
    private const uint ModCtrl = 2;
    private const uint ModShift = 4;
    private const uint ModWin = 8;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool Register(IntPtr hwnd, int id, string text)
    {
        if (hwnd == IntPtr.Zero || !TryParse(text, out var mods, out var vk)) return false;
        return RegisterHotKey(hwnd, id, mods | ModNoRepeat, vk);
    }

    public static void Unregister(IntPtr hwnd, int id)
    {
        if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, id);
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None or Key.DeadCharProcessed or Key.ImeProcessed;

    /// <summary>"Ctrl+Alt+F" for the pressed combination, or null if it is not a valid hotkey.</summary>
    public static string? Format(Key key, ModifierKeys mods)
    {
        if (IsModifierKey(key)) return null;

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return null; // a modifier is required

        parts.Add(key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModCtrl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                default: return false;
            }
        }

        var name = parts[^1];
        Key key;
        if (name.Length == 1 && char.IsDigit(name[0]))
            key = (Key)((int)Key.D0 + (name[0] - '0'));
        else if (!Enum.TryParse(name, true, out key))
            return false;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }
}