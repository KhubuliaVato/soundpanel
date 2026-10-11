using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SoundPanel.ViewModels;

namespace SoundPanel.Services;

public sealed class PlaylistEntry : ObservableObject
{
    private bool _selected = true;
    private string? _duplicate;

    public PlaylistEntry(string url, string title, double? duration)
    {
        Url = url;
        Title = title;
        DurationText = duration is { } d && d > 0 ? LinkInfo.FormatTime(d) : "";
    }

    public string Url { get; }
    public string Title { get; }
    public string DurationText { get; }

    public bool IsSelected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>Name of the library song this entry matches, or null.</summary>
    public string? DuplicateName
    {
        get => _duplicate;
        set
        {
            if (SetProperty(ref _duplicate, value)) OnPropertyChanged(nameof(IsDuplicate));
        }
    }

    public bool IsDuplicate => _duplicate != null;
}

public sealed class LinkInfo
{
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string Uploader { get; init; } = "";
    public double? Duration { get; init; }
    public string? ThumbnailUrl { get; init; }
    public bool IsPlaylist { get; set; }
    public List<PlaylistEntry> Entries { get; init; } = new();

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Uploader)) parts.Add(Uploader);
            if (Duration is { } d && d > 0) parts.Add(FormatTime(d));
            if (IsPlaylist) parts.Add($"{Entries.Count} ვიდეო");
            return string.Join("  ·  ", parts);
        }
    }

    public static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}

/// <summary>Asks yt-dlp what a link is (title, length, thumbnail, playlist entries) without downloading.</summary>
public sealed class LinkInfoService
{
    public async Task<LinkInfo> AnalyzeAsync(string url, bool allowPlaylist, string ytDlpPath,
        string? cookiesBrowser, CancellationToken ct)
    {
        var args = new List<string>
        {
            "--ignore-config", "--no-warnings", "--encoding", "utf-8", "--socket-timeout", "15",
            "-J", "--skip-download",
            allowPlaylist ? "--flat-playlist" : "--no-playlist"
        };

        if (!string.IsNullOrWhiteSpace(cookiesBrowser))
        {
            args.Add("--cookies-from-browser");
            args.Add(cookiesBrowser);
        }

        args.Add("--");
        args.Add(url);

        var json = await RunJsonAsync(ytDlpPath, args, ct);
        return Parse(url, json);
    }

    /// <summary>YouTube search (no download): the first <paramref name="count"/> hits.</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int count, string ytDlpPath, CancellationToken ct)
    {
        var args = new[]
        {
            "--ignore-config", "--no-warnings", "--encoding", "utf-8", "--socket-timeout", "15",
            "-J", "--flat-playlist", "--skip-download", "--", $"ytsearch{count}:{query}"
        };

        var json = await RunJsonAsync(ytDlpPath, args, ct);
        var list = new List<SearchResult>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;

                var id = Str(e, "id");
                var url = Str(e, "url") ?? Str(e, "webpage_url");
                if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (id == null) continue;
                    url = $"https://www.youtube.com/watch?v={id}";
                }

                var thumb = PickThumb(e) ?? (id != null ? $"https://i.ytimg.com/vi/{id}/mqdefault.jpg" : null);
                list.Add(new SearchResult(url, Str(e, "title") ?? url, Str(e, "channel") ?? Str(e, "uploader"),
                    Num(e, "duration"), thumb));
            }
        }

        return list;
    }

    private static async Task<string> RunJsonAsync(string ytDlpPath, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ytDlpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var err = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (err) err.AppendLine(e.Data);
        };

        proc.Start();
        proc.BeginErrorReadLine();

        using var registration = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
        });

        var json = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();

        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
        {
            string text;
            lock (err) text = err.ToString();
            throw new InvalidOperationException(ErrorText.Friendly(LastError(text)));
        }

        return json;
    }

    private static string LastError(string stderr)
    {
        string? last = null;
        foreach (var raw in stderr.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("ERROR:", StringComparison.Ordinal)) last = line[6..].Trim();
        }
        if (last != null) return last;

        return stderr.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
    }

    private static LinkInfo Parse(string url, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var thumb = PickThumb(root);
        if (thumb == null && string.Equals(Str(root, "extractor_key"), "Youtube", StringComparison.OrdinalIgnoreCase)
                          && Str(root, "id") is { } vid && Str(root, "_type") != "playlist")
            thumb = $"https://i.ytimg.com/vi/{vid}/hqdefault.jpg";

        var info = new LinkInfo
        {
            Url = url,
            Title = Str(root, "title") ?? url,
            Uploader = Str(root, "uploader") ?? Str(root, "channel") ?? Str(root, "playlist_uploader") ?? "",
            Duration = Num(root, "duration"),
            ThumbnailUrl = thumb
        };

        if (Str(root, "_type") == "playlist" &&
            root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;

                var entryUrl = Str(e, "url") ?? Str(e, "webpage_url");
                if (string.IsNullOrEmpty(entryUrl) || !entryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var id = Str(e, "id");
                    if (id != null && string.Equals(Str(e, "ie_key"), "Youtube", StringComparison.OrdinalIgnoreCase))
                        entryUrl = $"https://www.youtube.com/watch?v={id}";
                    else
                        continue;
                }

                info.Entries.Add(new PlaylistEntry(entryUrl, Str(e, "title") ?? entryUrl, Num(e, "duration")));
            }

            info.IsPlaylist = info.Entries.Count > 0;
        }

        return info;
    }

    private static string? PickThumb(JsonElement root)
    {
        if (root.TryGetProperty("thumbnails", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            string? best = null;
            foreach (var t in list.EnumerateArray())
            {
                var u = Str(t, "url");
                if (u == null) continue;
                var clean = u.Split('?')[0];
                if (clean.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    clean.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    best = u; // later entries are better
            }
            if (best != null) return best;
        }

        var single = Str(root, "thumbnail");
        return single != null && !single.Split('?')[0].EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? single : null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}