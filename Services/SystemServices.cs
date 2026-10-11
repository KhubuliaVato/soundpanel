using Microsoft.Win32;

namespace SoundPanel.Services;

public sealed record AudioDevice(string Name, int Number)
{
    /// <summary>Stable key saved in settings ("" = Windows default).</summary>
    public string Key => Number < 0 ? "" : Name;
}

public static class AutoStartService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "1Flow SoundPanel";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key == null) return;

            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* non-critical */ }
    }
}