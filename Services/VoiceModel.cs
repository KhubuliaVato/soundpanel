using System.IO;
using System.Net.Http;

namespace SoundPanel.Services;

/// <summary>The offline speech model (downloaded once, ~150 MB).</summary>
public static class VoiceModel
{
    // "ggml-small.en.bin" (~470 MB) is more accurate but slower: change the name here if you want it.
    private const string FileName = "ggml-base.en.bin";
    private static readonly string Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + FileName;

    public static string ModelPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "1Flow", "SoundPanel", "tools", FileName);

    public static bool IsReady => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 50_000_000;

    public static async Task EnsureAsync(IProgress<int> progress, CancellationToken ct)
    {
        if (IsReady) return;

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ModelPath)!);
        var part = ModelPath + ".part";

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        {
            var buffer = new byte[81920];
            long read = 0;
            var last = -1;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                {
                    var pct = (int)(read * 100 / total);
                    if (pct != last)
                    {
                        last = pct;
                        progress.Report(pct);
                    }
                }
            }
        }

        File.Move(part, ModelPath, true);
    }
}