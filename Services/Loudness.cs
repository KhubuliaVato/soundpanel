namespace SoundPanel.Services;

public sealed record LufsTarget(double Value, string Name);

/// <summary>ITU-R BS.1770 / EBU R128 loudness: K-weighting, momentary (0.4 s), short-term (3 s), gated integrated.</summary>
internal sealed class LoudnessMeter
{
    private struct Bq
    {
        public double B0, B1, B2, A1, A2, Z1, Z2;

        public double Run(double x)
        {
            var y = B0 * x + Z1;
            Z1 = B1 * x - A1 * y + Z2;
            Z2 = B2 * x - A2 * y;
            return y;
        }
    }

    private readonly int _ch;
    private readonly int _hopLen;
    private readonly Bq[] _shelf;
    private readonly Bq[] _rlb;
    private readonly double[] _ring = new double[30];
    private readonly List<double> _blocks = new();

    private double _acc;
    private int _n;
    private int _pos;
    private int _filled;

    public int Hops { get; private set; }
    public float Momentary { get; private set; } = -100;
    public float ShortTerm { get; private set; } = -100;
    public float Integrated { get; private set; } = -100;

    public LoudnessMeter(int channels, int sampleRate)
    {
        _ch = Math.Max(1, channels);
        _hopLen = Math.Max(1, sampleRate / 10);
        _shelf = new Bq[_ch];
        _rlb = new Bq[_ch];

        // stage 1: head-related high shelf
        {
            const double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var vh = Math.Pow(10, g / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + k / q + k * k;
            for (var c = 0; c < _ch; c++)
                _shelf[c] = new Bq
                {
                    B0 = (vh + vb * k / q + k * k) / a0,
                    B1 = 2 * (k * k - vh) / a0,
                    B2 = (vh - vb * k / q + k * k) / a0,
                    A1 = 2 * (k * k - 1) / a0,
                    A2 = (1 - k / q + k * k) / a0
                };
        }

        // stage 2: RLB high-pass
        {
            const double f0 = 38.13547087602444, q = 0.5003270373238773;
            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var a0 = 1 + k / q + k * k;
            for (var c = 0; c < _ch; c++)
                _rlb[c] = new Bq
                {
                    B0 = 1, B1 = -2, B2 = 1,
                    A1 = 2 * (k * k - 1) / a0,
                    A2 = (1 - k / q + k * k) / a0
                };
        }
    }

    public void AddFrame(float[] buf, int i)
    {
        for (var c = 0; c < _ch; c++)
        {
            var y = _rlb[c].Run(_shelf[c].Run(buf[i + c]));
            _acc += y * y;
        }

        if (++_n >= _hopLen) EndHop();
    }

    private void EndHop()
    {
        var ms = _acc / _n * (_ch == 1 ? 2.0 : 1.0); // a mono file plays through both speakers
        _acc = 0;
        _n = 0;

        _ring[_pos] = ms;
        _pos = (_pos + 1) % _ring.Length;
        if (_filled < _ring.Length) _filled++;
        Hops++;

        if (_filled < 4) return;

        var block = Mean(4);
        Momentary = (float)ToLufs(block);
        ShortTerm = (float)ToLufs(Mean(_filled));

        _blocks.Add(block);
        if (Hops % 10 == 0) Integrated = (float)ComputeIntegrated();
    }

    private double Mean(int count)
    {
        double sum = 0;
        for (var j = 0; j < count; j++)
            sum += _ring[(_pos - 1 - j + _ring.Length * 2) % _ring.Length];
        return sum / count;
    }

    private double ComputeIntegrated()
    {
        double sum = 0;
        var n = 0;
        foreach (var b in _blocks)
            if (ToLufs(b) > -70)
            {
                sum += b;
                n++;
            }
        if (n == 0) return -100;

        var rel = ToLufs(sum / n) - 10;
        sum = 0;
        n = 0;
        foreach (var b in _blocks)
        {
            var l = ToLufs(b);
            if (l > -70 && l > rel)
            {
                sum += b;
                n++;
            }
        }
        return n == 0 ? -100 : ToLufs(sum / n);
    }

    private static double ToLufs(double power) => power > 1e-10 ? -0.691 + 10 * Math.Log10(power) : -100;
}