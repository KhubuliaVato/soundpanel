using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace SoundPanel.Services;

/// <summary>Finds (or downloads) yt-dlp and ffmpeg.</summary>
public sealed class ToolsService
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip";

    private static readonly HttpClient Http = CreateClient();

    public static string ToolsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1Flow", "SoundPanel", "tools");

    public string? YtDlpPath
    {
        get
        {
            var own = Path.Combine(ToolsDir, "yt-dlp.exe");
            if (File.Exists(own)) return own;
            return PathDirs().Select(d => Path.Combine(d, "yt-dlp.exe")).FirstOrDefault(SafeExists);
        }
    }

    public string? FfmpegDir
    {
        get
        {
            if (HasBoth(ToolsDir)) return ToolsDir;
            return PathDirs().FirstOrDefault(HasBoth);
        }
    }

    public bool IsReady => YtDlpPath != null && FfmpegDir != null;

    public async Task EnsureAsync(IProgress<string>? status, IProgress<double>? percent, CancellationToken ct)
    {
        Directory.CreateDirectory(ToolsDir);

        if (YtDlpPath == null)
        {
            status?.Report("yt-dlp იტვირთება…");
            await DownloadAsync(YtDlpUrl, Path.Combine(ToolsDir, "yt-dlp.exe"), percent, ct);
        }

        if (FfmpegDir == null)
        {
            status?.Report("ffmpeg იტვირთება (დაახლოებით 150 MB, მხოლოდ ერთხელ)…");
            var zip = Path.Combine(ToolsDir, "ffmpeg.zip");
            await DownloadAsync(FfmpegUrl, zip, percent, ct);

            status?.Report("ffmpeg იშლება…");
            await Task.Run(() => ExtractFfmpeg(zip), ct);
            try { File.Delete(zip); } catch { /* leftover is harmless */ }
        }
    }

    /// <summary>Runs "yt-dlp -U" and returns its last output line.</summary>
    public async Task<string> UpdateYtDlpAsync(CancellationToken ct)
    {
        var path = YtDlpPath;
        if (path == null) return "yt-dlp ვერ მოიძებნა";

        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-U");

        using var proc = Process.Start(psi);
        if (proc == null) return "ვერ გაეშვა";

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        var error = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var text = (output + "\n" + error).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return text.LastOrDefault() ?? "მზადაა";
    }

    // ------------------------------------------------------------ helpers

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("1Flow-SoundPanel/1.0");
        return client;
    }

    private static async Task DownloadAsync(string url, string destination, IProgress<double>? percent, CancellationToken ct)
    {
        var temp = destination + ".download";

        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(temp);

            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) percent?.Report(done * 100.0 / total);
            }
        }

        File.Move(temp, destination, overwrite: true);
    }

    private static void ExtractFfmpeg(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name is "ffmpeg.exe" or "ffprobe.exe")
                entry.ExtractToFile(Path.Combine(ToolsDir, entry.Name), overwrite: true);
        }
    }

    private static IEnumerable<string> PathDirs() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool HasBoth(string dir) =>
        SafeExists(Path.Combine(dir, "ffmpeg.exe")) && SafeExists(Path.Combine(dir, "ffprobe.exe"));

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); }
        catch { return false; }
    }
}