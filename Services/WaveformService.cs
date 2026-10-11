using NAudio.Wave;

namespace SoundPanel.Services;

public static class WaveformService
{
    /// <summary>
    /// Decodes the file and returns <paramref name="buckets"/> loudness values normalised to 0..1
    /// (RMS per bucket, so the shape follows the music's dynamics).
    /// </summary>
    public static float[] ComputePeaks(string path, int buckets, CancellationToken ct)
    {
        using var reader = new AudioFileReader(path);

        var channels = Math.Max(1, reader.WaveFormat.Channels);
        var totalFrames = Math.Max(1L, reader.Length / sizeof(float) / channels);
        var framesPerBucket = Math.Max(1.0, (double)totalFrames / buckets);

        var sumSquares = new double[buckets];
        var counts = new int[buckets];
        var buffer = new float[4096 * channels];

        long frameIndex = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            for (var i = 0; i + channels <= read; i += channels)
            {
                float mono = 0;
                for (var c = 0; c < channels; c++) mono += buffer[i + c];
                mono /= channels;

                var b = (int)(frameIndex / framesPerBucket);
                if (b >= buckets) b = buckets - 1;

                sumSquares[b] += mono * mono;
                counts[b]++;
                frameIndex++;
            }
        }

        var result = new float[buckets];
        float max = 0;
        for (var i = 0; i < buckets; i++)
        {
            result[i] = counts[i] > 0 ? (float)Math.Sqrt(sumSquares[i] / counts[i]) : 0f;
            if (result[i] > max) max = result[i];
        }

        if (max <= 0) return result;

        for (var i = 0; i < buckets; i++)
            result[i] = (float)Math.Pow(result[i] / max, 0.85);

        return result;
    }
}
