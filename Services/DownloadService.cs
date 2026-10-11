using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SoundPanel.Services;

public sealed record DownloadOptions(
    string Url, string FolderPath, string Format, int QualityKbps, bool Playlist, bool EmbedThumbnail,
    double? StartSec = null, double? EndSec = null, string? FileName = null, string? CookiesBrowser = null,
    string? Artist = null, string? TitleTag = null);

public enum DownloadPhase { Preparing, Downloading, Converting }

public sealed record DownloadProgress(
    DownloadPhase Phase, double? Percent = null, string? Speed = null, string? Eta = null,
    string? Title = null, string? Position = null, string? Size = null);

public sealed record DownloadResult(
    bool Success, bool Cancelled, string? Error, IReadOnlyList<string> Files, string? RawError = null);

public sealed class DownloadService
{
    private static readonly Regex ProgressRx = new(
        @"^\[download\]\s+(?<p>\d+(?:\.\d+)?)%(?:\s+of\s+~?\s*(?<size>\S+))?(?:\s+at\s+(?<speed>\S+))?(?:\s+ETA\s+(?<eta>\S+))?",
        RegexOptions.Compiled);
    private static readonly Regex DestinationRx = new(@"^\[download\] Destination: (?<f>.+)$", RegexOptions.Compiled);
    private static readonly Regex AlreadyRx = new(@"^\[download\] (?<f>.+) has already been downloaded", RegexOptions.Compiled);
    private static readonly Regex ItemRx = new(@"^\[download\] Downloading item (?<i>\d+) of (?<n>\d+)", RegexOptions.Compiled);
    private static readonly Regex ExtractRx = new(@"^\[ExtractAudio\] Destination: (?<f>.+)$", RegexOptions.Compiled);
    private static readonly Regex NoConvertRx = new(@"^\[ExtractAudio\] Not converting audio (?<f>.+?);", RegexOptions.Compiled);
    private static readonly Regex PostRx = new(
        @"^\[(ExtractAudio|Metadata|EmbedThumbnail|ThumbnailsConvertor|FixupM4a|FixupM3u8)\]", RegexOptions.Compiled);
    private static readonly Regex ErrorRx = new(@"^ERROR:\s*(?<m>.+)$", RegexOptions.Compiled);

    public async Task<DownloadResult> RunAsync(
        DownloadOptions options, string ytDlpPath, string ffmpegDir,
        IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var files = new List<string>();
        string? lastError = null;
        string? position = null;
        var gate = new object();

        try { Directory.CreateDirectory(options.FolderPath); }
        catch (Exception ex) { return new DownloadResult(false, false, ErrorText.Friendly(ex.Message), files, ex.Message); }

        var psi = new ProcessStartInfo(ytDlpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in BuildArgs(options, ffmpegDir)) psi.ArgumentList.Add(arg);

        void Handle(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            line = line.Trim();

            lock (gate)
            {
                Match m;

                if ((m = ErrorRx.Match(line)).Success) { lastError = m.Groups["m"].Value; return; }

                if ((m = ItemRx.Match(line)).Success)
                {
                    position = $"{m.Groups["i"].Value}/{m.Groups["n"].Value}";
                    progress.Report(new DownloadProgress(DownloadPhase.Preparing, 0, Position: position));
                    return;
                }

                if ((m = ProgressRx.Match(line)).Success)
                {
                    progress.Report(new DownloadProgress(
                        DownloadPhase.Downloading,
                        double.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture),
                        Clean(m.Groups["speed"].Value), Clean(m.Groups["eta"].Value),
                        Position: position, Size: Clean(m.Groups["size"].Value)));
                    return;
                }

                if ((m = DestinationRx.Match(line)).Success)
                {
                    progress.Report(new DownloadProgress(
                        DownloadPhase.Downloading, 0,
                        Title: Path.GetFileNameWithoutExtension(m.Groups["f"].Value), Position: position));
                    return;
                }

                if ((m = AlreadyRx.Match(line)).Success)
                {
                    files.Add(m.Groups["f"].Value);
                    return;
                }

                if ((m = ExtractRx.Match(line)).Success)
                {
                    files.Add(m.Groups["f"].Value);
                    progress.Report(new DownloadProgress(DownloadPhase.Converting, 100,
                        Title: Path.GetFileNameWithoutExtension(m.Groups["f"].Value), Position: position));
                    return;
                }

                if ((m = NoConvertRx.Match(line)).Success)
                {
                    files.Add(m.Groups["f"].Value.Trim('"'));
                    return;
                }

                if (PostRx.IsMatch(line))
                    progress.Report(new DownloadProgress(DownloadPhase.Converting, 100, Position: position));
            }
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => Handle(e.Data);
        proc.ErrorDataReceived += (_, e) => Handle(e.Data);

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, false, $"yt-dlp ვერ გაეშვა: {ex.Message}", files, ex.Message);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        progress.Report(new DownloadProgress(DownloadPhase.Preparing));

        using var registration = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* already gone */ }
        });

        await proc.WaitForExitAsync(CancellationToken.None);

        if (ct.IsCancellationRequested)
            return new DownloadResult(false, true, null, files);

        if (proc.ExitCode != 0)
        {
            var raw = lastError ?? $"yt-dlp დასრულდა კოდით {proc.ExitCode}";
            return new DownloadResult(false, false, ErrorText.Friendly(raw), files, raw);
        }

        lock (gate)
            return new DownloadResult(true, false, null, files.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IEnumerable<string> BuildArgs(DownloadOptions o, string ffmpegDir)
    {
        var args = new List<string>
        {
            "--newline", "--no-colors", "--ignore-config", "--no-mtime",
            "--encoding", "utf-8",
            "--windows-filenames", "--trim-filenames", "150",
            "--ffmpeg-location", ffmpegDir,
            "-x", "--audio-format", o.Format,
            "-o", Path.Combine(o.FolderPath.Replace("%", "%%"), OutputName(o) + ".%(ext)s"),
            o.Playlist ? "--yes-playlist" : "--no-playlist"
        };

        // bitrate only makes sense for lossy formats
        if (o.Format is "mp3" or "m4a" or "opus")
        {
            args.Add("--audio-quality");
            args.Add(o.QualityKbps > 0 ? $"{o.QualityKbps}K" : "0");
        }

        if (o.EmbedThumbnail)
        {
            args.Add("--embed-metadata");

            // clean tags (artist / title) instead of the raw video title
            if (!string.IsNullOrWhiteSpace(o.TitleTag))
            {
                args.Add("--parse-metadata");
                args.Add($"{Lit(o.TitleTag)}%(x|)s:%(meta_title)s");
            }
            if (!string.IsNullOrWhiteSpace(o.Artist))
            {
                args.Add("--parse-metadata");
                args.Add($"{Lit(o.Artist)}%(x|)s:%(meta_artist)s");
            }
            if (o.Format is "mp3" or "m4a" or "flac" or "opus")
            {
                args.Add("--embed-thumbnail");
                args.Add("--convert-thumbnails");
                args.Add("jpg");
            }
        }

        // only a part of the video
        if (o.StartSec != null || o.EndSec != null)
        {
            args.Add("--download-sections");
            args.Add($"*{Hms(o.StartSec ?? 0)}-{(o.EndSec is { } end ? Hms(end) : "inf")}");
            args.Add("--force-keyframes-at-cuts");
        }

        if (!string.IsNullOrEmpty(o.CookiesBrowser))
        {
            args.Add("--cookies-from-browser");
            args.Add(o.CookiesBrowser);
        }

        args.Add("--");
        args.Add(o.Url);
        return args;
    }

    /// <summary>Literal text for a yt-dlp template / --parse-metadata value.</summary>
    private static string Lit(string text) => text.Replace("%", "%%").Replace(":", "\\:");

    private static string OutputName(DownloadOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.FileName))
        {
            var name = o.FileName.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim(' ', '.');
            if (name.Length > 150) name = name[..150];
            if (name.Length > 0) return name.Replace("%", "%%");
        }

        if (o.StartSec != null || o.EndSec != null)
            return $"%(title)s [{Label(o.StartSec ?? 0)}-{(o.EndSec is { } end ? Label(end) : "end")}]";

        return "%(title)s";
    }

    private static string Hms(double seconds) =>
        TimeSpan.FromSeconds(Math.Floor(Math.Max(0, seconds))).ToString(@"hh\:mm\:ss");

    private static string Label(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\.mm\.ss") : t.ToString(@"m\.ss");
    }

    private static string? Clean(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : value;
}