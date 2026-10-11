using System.IO;
using System.Text.Json;

namespace SoundPanel.Services;

public sealed class AppSettings
{
    public string? RootFolder { get; set; }
    public double Volume { get; set; } = 0.8;
    public bool Shuffle { get; set; } = false;
    public bool Repeat { get; set; } = true;
    public string AccentTheme { get; set; } = "aurora";
    public string RobotTheme { get; set; } = "neon";
    public bool AlwaysOnTop { get; set; } = false;
    public bool MiniOnMinimize { get; set; } = true;
    public string QuickSearchHotkey { get; set; } = "Ctrl+Alt+F";
    public string OutputDevice { get; set; } = "";
    public string DownloadFormat { get; set; } = "mp3";
    public int DownloadQuality { get; set; } = 320;
    public bool DownloadPlaylist { get; set; } = false;
    public bool DownloadEmbed { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "1Flow", "SoundPanel", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupted settings -> defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-critical */ }
    }
}
