using System.IO;
using System.Text.Json;

namespace SoundPanel.Services;

public sealed class DownloaderPrefs
{
    public string? TargetFolder { get; set; }
    public int MaxParallel { get; set; } = 2;
    public bool CleanNames { get; set; } = true;
    public string Cookies { get; set; } = "";
}

public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Group { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Format { get; set; } = "mp3";
    public int Quality { get; set; }
    public bool Playlist { get; set; }
    public bool Embed { get; set; }
    public string? FileName { get; set; }
    public string Cookies { get; set; } = "";
    public double? Start { get; set; }
    public double? End { get; set; }
    public string Status { get; set; } = "Done";
    public string? Error { get; set; }
    public string? ErrorRaw { get; set; }
    public string? FilePath { get; set; }
    public List<string> Files { get; set; } = new();
    public double Progress { get; set; }
}

/// <summary>Downloader preferences and queue history, stored next to the other app data.</summary>
public static class DownloaderStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1Flow", "SoundPanel");

    private static string PrefsFile => Path.Combine(Dir, "downloader.json");
    private static string HistoryFile => Path.Combine(Dir, "downloads.json");

    public static DownloaderPrefs LoadPrefs()
    {
        try
        {
            if (File.Exists(PrefsFile))
                return JsonSerializer.Deserialize<DownloaderPrefs>(File.ReadAllText(PrefsFile)) ?? new DownloaderPrefs();
        }
        catch { /* corrupt file: defaults */ }
        return new DownloaderPrefs();
    }

    public static void SavePrefs(DownloaderPrefs prefs) => Write(PrefsFile, prefs);

    public static List<HistoryEntry> LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryFile))
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new();
        }
        catch { /* corrupt file: empty history */ }
        return new List<HistoryEntry>();
    }

    public static void SaveHistory(List<HistoryEntry> list) => Write(HistoryFile, list);

    private static void Write<T>(string path, T data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(data));
        }
        catch { /* best effort */ }
    }
}