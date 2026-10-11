using NAudio.Wave;

namespace SoundPanel.Services;

/// <summary>
/// Applies gain (0..2 = 0..200%). Above 100% a soft clipper keeps peaks from hard-clipping
/// so the boost sounds loud but not harsh. At or below 100% it is a pure gain.
/// </summary>
public sealed class BoostSampleProvider : ISampleProvider
{
    private const float Knee = 0.9f;

    private readonly ISampleProvider _source;

    public BoostSampleProvider(ISampleProvider source) => _source = source;

    public float Gain { get; set; } = 1f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var gain = Gain;

        if (gain <= 1f)
        {
            for (var i = 0; i < read; i++)
                buffer[offset + i] *= gain;
        }
        else
        {
            for (var i = 0; i < read; i++)
                buffer[offset + i] = SoftClip(buffer[offset + i] * gain);
        }

        return read;
    }

    private static float SoftClip(float x)
    {
        var a = MathF.Abs(x);
        if (a <= Knee) return x;

        var over = (a - Knee) / (1f - Knee);
        var y = Knee + (1f - Knee) * MathF.Tanh(over);
        return x < 0 ? -y : y;
    }
}
