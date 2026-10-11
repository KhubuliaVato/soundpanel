using NAudio.Wave;

namespace SoundPanel.Services;

/// <summary>Sound-processing settings shared between the UI and the audio thread. Change fields, then call Touch().</summary>
public sealed class FxParams
{
    public static readonly double[] BandFreqs = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public volatile bool Enabled = true;
    public float PreampDb;
    public float BassDb;
    public float TrebleDb;
    public readonly float[] Eq = new float[10];
    public volatile bool Normalize;
    public volatile bool Limiter = true;
    public volatile bool Bypass;          // A/B: play the original, level-matched
    public float TargetLufs = -16f;       // normalize target

    // meters: written by the audio thread, read by the UI
    public volatile float PeakL;
    public volatile float PeakR;
    public volatile float RmsDb = -100f;
    public volatile float LufsM = -100f;
    public volatile float LufsS = -100f;
    public volatile float LufsI = -100f;
    public volatile float GrLin = 1f;     // lowest limiter gain since the UI last looked
    public float EchoMix;      // 0..1
    public float EchoMs = 320;
    public float ReverbMix;    // 0..1
    public float ReverbSize = 0.5f;
    public float Width = 1f;   // 0..2

    private int _version;
    public int Version => Volatile.Read(ref _version);
    public void Touch() => Interlocked.Increment(ref _version);
}

internal struct Coef
{
    public float B0, B1, B2, A1, A2;
    public bool Active;
}

internal static class Dsp
{
    public static Coef Peak(double fs, double f0, double gainDb, double q)
    {
        if (Math.Abs(gainDb) < 0.05) return default;
        f0 = Math.Min(f0, fs * 0.45);
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * f0 / fs;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / (2 * q);
        return Make(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public static Coef LowShelf(double fs, double f0, double gainDb)
    {
        if (Math.Abs(gainDb) < 0.05) return default;
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * f0 / fs;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / 2 * Math.Sqrt(2);
        var sq = 2 * Math.Sqrt(a) * alpha;
        return Make(
            a * ((a + 1) - (a - 1) * cos + sq),
            2 * a * ((a - 1) - (a + 1) * cos),
            a * ((a + 1) - (a - 1) * cos - sq),
            (a + 1) + (a - 1) * cos + sq,
            -2 * ((a - 1) + (a + 1) * cos),
            (a + 1) + (a - 1) * cos - sq);
    }

    public static Coef HighShelf(double fs, double f0, double gainDb)
    {
        if (Math.Abs(gainDb) < 0.05) return default;
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * f0 / fs;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / 2 * Math.Sqrt(2);
        var sq = 2 * Math.Sqrt(a) * alpha;
        return Make(
            a * ((a + 1) + (a - 1) * cos + sq),
            -2 * a * ((a - 1) + (a + 1) * cos),
            a * ((a + 1) + (a - 1) * cos - sq),
            (a + 1) - (a - 1) * cos + sq,
            2 * ((a - 1) - (a + 1) * cos),
            (a + 1) - (a - 1) * cos - sq);
    }

    private static Coef Make(double b0, double b1, double b2, double a0, double a1, double a2) => new()
    {
        B0 = (float)(b0 / a0), B1 = (float)(b1 / a0), B2 = (float)(b2 / a0),
        A1 = (float)(a1 / a0), A2 = (float)(a2 / a0),
        Active = true
    };

    public static double MagnitudeDb(in Coef c, double fs, double f)
    {
        if (!c.Active) return 0;
        var w = 2 * Math.PI * f / fs;
        double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        var nr = c.B0 + c.B1 * c1 + c.B2 * c2;
        var ni = -(c.B1 * s1 + c.B2 * s2);
        var dr = 1 + c.A1 * c1 + c.A2 * c2;
        var di = -(c.A1 * s1 + c.A2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }
}

/// <summary>Frequency response used by the UI curve.</summary>
public static class FxCurve
{
    public static double ResponseDb(FxParams p, double f)
    {
        const double fs = 44100;
        double db = 0;
        for (var i = 0; i < 10; i++)
            db += Dsp.MagnitudeDb(Dsp.Peak(fs, FxParams.BandFreqs[i], p.Eq[i], FxChain.BandQ), fs, f);
        db += Dsp.MagnitudeDb(Dsp.LowShelf(fs, FxChain.BassHz, p.BassDb), fs, f);
        db += Dsp.MagnitudeDb(Dsp.HighShelf(fs, FxChain.TrebleHz, p.TrebleDb), fs, f);
        return db;
    }
}

/// <summary>
/// Real-time chain: preamp, 10-band EQ, bass/treble shelves, stereo width, echo, reverb,
/// auto-level (normalize) and limiter.
/// </summary>
public sealed class FxChain : ISampleProvider
{
    public const double BandQ = 1.2;
    public const double BassHz = 110;
    public const double TrebleHz = 9000;

    private const int Stages = 12; // 10 EQ + bass + treble
    private const float Ceiling = 0.97f;
    private const float TargetRms = 0.14f;
    private const float RvbDamp = 0.25f;

    private readonly ISampleProvider _src;
    private readonly FxParams _p;
    private readonly int _ch;
    private readonly int _fs;
    private int _ver = -1;

    private readonly Coef[] _coef = new Coef[Stages];
    private readonly int[] _active = new int[Stages];
    private int _activeCount;
    private readonly float[] _z1;
    private readonly float[] _z2;
    private float _pre = 1f;
    private float _width = 1f;

    private readonly float[][] _echoBuf;
    private int _echoPos;
    private int _echoLen = 1;
    private float _echoMix;

    private readonly Comb[][] _combs;
    private readonly AllPass[][] _allpass;
    private float _rvbMix;
    private float _rvbFb = 0.84f;
    private bool _rvbHot;

    private float _env = TargetRms * TargetRms;

    private readonly LoudnessMeter _inM;
    private readonly LoudnessMeter _outM;
    private readonly LoudnessMeter _preM;
    private int _outHops;
    private int _preHops;
    private float _agcTarget = 1f;
    private float _grMin = 1f;
    private float _inPeak;
    private float _matchDb;
    private float _matchLin = 1f;
    private float _byp = 1f;
    private float _agc = 1f;
    private float _lim = 1f;
    private readonly float _envCoef;
    private readonly float _agcCoef;
    private readonly float _relCoef;

    public FxChain(ISampleProvider source, FxParams p)
    {
        _src = source;
        _p = p;
        _ch = source.WaveFormat.Channels;
        _fs = source.WaveFormat.SampleRate;

        _z1 = new float[_ch * Stages];
        _z2 = new float[_ch * Stages];

        _echoBuf = new float[_ch][];
        for (var c = 0; c < _ch; c++) _echoBuf[c] = new float[_fs + 4];

        int[] combTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        int[] allpassTuning = { 556, 441, 341, 225 };
        _combs = new Comb[_ch][];
        _allpass = new AllPass[_ch][];
        for (var c = 0; c < _ch; c++)
        {
            var spread = c * 23;
            _combs[c] = combTuning.Select(t => new Comb(Scale(t + spread))).ToArray();
            _allpass[c] = allpassTuning.Select(t => new AllPass(Scale(t + spread))).ToArray();
        }

        _inM = new LoudnessMeter(_ch, _fs);
        _outM = new LoudnessMeter(_ch, _fs);
        _preM = new LoudnessMeter(_ch, _fs);

        _envCoef = (float)(1 - Math.Exp(-1.0 / (_fs * 1.5)));
        _agcCoef = (float)(1 - Math.Exp(-1.0 / (_fs * 1.0)));
        _relCoef = (float)(1 - Math.Exp(-1.0 / (_fs * 0.12)));
    }

    public WaveFormat WaveFormat => _src.WaveFormat;

    private int Scale(int n) => Math.Max(8, (int)(n * _fs / 44100.0));

    /// <summary>Clears echo / reverb / filter memory (used after a seek).</summary>
    public void Reset()
    {
        Array.Clear(_z1);
        Array.Clear(_z2);
        foreach (var b in _echoBuf) Array.Clear(b);
        ClearReverb();
        _env = TargetRms * TargetRms;
        _agcTarget = 1f;
    }

    private void ClearReverb()
    {
        foreach (var set in _combs) foreach (var c in set) c.Clear();
        foreach (var set in _allpass) foreach (var a in set) a.Clear();
    }

    private void Update(int ver)
    {
        _ver = ver;
        double fs = _fs;
        _pre = MathF.Pow(10f, _p.PreampDb / 20f);

        for (var b = 0; b < 10; b++)
            _coef[b] = Dsp.Peak(fs, FxParams.BandFreqs[b], _p.Eq[b], BandQ);
        _coef[10] = Dsp.LowShelf(fs, BassHz, _p.BassDb);
        _coef[11] = Dsp.HighShelf(fs, TrebleHz, _p.TrebleDb);

        _activeCount = 0;
        for (var s = 0; s < Stages; s++)
            if (_coef[s].Active) _active[_activeCount++] = s;

        _width = _p.Width;
        _echoMix = _p.EchoMix;
        _echoLen = Math.Clamp((int)(_p.EchoMs * _fs / 1000.0), 1, _fs);
        _rvbMix = _p.ReverbMix;
        _rvbFb = 0.70f + 0.28f * Math.Clamp(_p.ReverbSize, 0f, 1f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _src.Read(buffer, offset, count);
        if (read <= 0) return read;

        // input loudness and peak (before any processing)
        var chn = _ch;
        var frames0 = read / chn;
        var pk = 0f;
        for (var f = 0; f < frames0; f++)
        {
            var i = offset + f * chn;
            for (var c = 0; c < chn; c++) pk = MathF.Max(pk, MathF.Abs(buffer[i + c]));
            _inM.AddFrame(buffer, i);
        }
        _inPeak = MathF.Max(_inPeak * MathF.Pow(0.5f, frames0 / (3f * _fs)), pk);

        if (_p.Enabled && !_p.Bypass) Process(buffer, offset, read);
        else Bypassed(buffer, offset, read);

        MeasureOut(buffer, offset, read);
        return read;
    }

    /// <summary>Original sound. While A/B is held the level follows the processed loudness (never clipping).</summary>
    private void Bypassed(float[] buffer, int offset, int read)
    {
        var target = 1f;
        if (_p.Bypass && _p.Enabled)
            target = MathF.Min(_matchLin, 0.98f / MathF.Max(_inPeak, 1e-4f));

        if (MathF.Abs(_byp - 1f) < 1e-4f && MathF.Abs(target - 1f) < 1e-4f)
        {
            _byp = 1f;
            return;
        }

        for (var k = 0; k < read; k++)
        {
            _byp += (target - _byp) * 0.002f;
            buffer[offset + k] *= _byp;
        }
    }

    private void MeasureOut(float[] buffer, int offset, int read)
    {
        var ch = _ch;
        var frames = read / ch;
        float pl = 0, pr = 0;
        double sq = 0;

        for (var f = 0; f < frames; f++)
        {
            var i = offset + f * ch;
            var l = MathF.Abs(buffer[i]);
            var r = ch > 1 ? MathF.Abs(buffer[i + 1]) : l;
            if (l > pl) pl = l;
            if (r > pr) pr = r;
            for (var c = 0; c < ch; c++) sq += buffer[i + c] * buffer[i + c];
            _outM.AddFrame(buffer, i);
        }

        var p = _p;
        if (pl > p.PeakL) p.PeakL = pl;
        if (pr > p.PeakR) p.PeakR = pr;
        if (frames > 0)
            p.RmsDb = (float)(10 * Math.Log10(Math.Max(sq / (frames * ch), 1e-10)));

        if (_outM.Hops == _outHops) return;
        _outHops = _outM.Hops;

        p.LufsM = _outM.Momentary;
        p.LufsS = _outM.ShortTerm;
        p.LufsI = _outM.Integrated;

        // how much louder (or quieter) the processed sound is than the original
        if (p.Enabled && !p.Bypass && _inM.ShortTerm > -60 && _outM.ShortTerm > -60)
        {
            var d = Math.Clamp(_outM.ShortTerm - _inM.ShortTerm, -12f, 12f);
            _matchDb += (d - _matchDb) * 0.15f;
            _matchLin = MathF.Pow(10f, _matchDb / 20f);
        }
    }

    private void Process(float[] buffer, int offset, int read)
    {
        _byp = 1f;
        var ver = _p.Version;
        if (ver != _ver) Update(ver);

        var ch = _ch;
        var frames = read / ch;
        var doWidth = ch == 2 && Math.Abs(_width - 1f) > 0.01f;
        var doEcho = _echoMix > 0.001f;
        var doRvb = _rvbMix > 0.001f;
        var normalize = _p.Normalize;
        var limiter = _p.Limiter;
        var pre = _pre;
        var bufLen = _echoBuf[0].Length;

        if (!doRvb && _rvbHot)
        {
            ClearReverb();
            _rvbHot = false;
        }
        if (doRvb) _rvbHot = true;

        for (var f = 0; f < frames; f++)
        {
            var i = offset + f * ch;

            // preamp + EQ + bass/treble
            for (var c = 0; c < ch; c++)
            {
                var x = buffer[i + c] * pre;
                var zi = c * Stages;
                for (var k = 0; k < _activeCount; k++)
                {
                    var s = _active[k];
                    var q = _coef[s];
                    var y = q.B0 * x + _z1[zi + s];
                    _z1[zi + s] = q.B1 * x - q.A1 * y + _z2[zi + s];
                    _z2[zi + s] = q.B2 * x - q.A2 * y;
                    x = y;
                }
                buffer[i + c] = x;
            }

            // stereo width (mid/side)
            if (doWidth)
            {
                var l = buffer[i];
                var r = buffer[i + 1];
                var m = (l + r) * 0.5f;
                var sd = (l - r) * 0.5f * _width;
                buffer[i] = m + sd;
                buffer[i + 1] = m - sd;
            }

            // echo
            var ri = _echoPos - _echoLen;
            if (ri < 0) ri += bufLen;
            for (var c = 0; c < ch; c++)
            {
                var buf = _echoBuf[c];
                var x = buffer[i + c];
                if (doEcho)
                {
                    var d = buf[ri];
                    buf[_echoPos] = x + d * 0.4f;
                    buffer[i + c] = x + d * _echoMix;
                }
                else
                {
                    buf[_echoPos] = 0f;
                }
            }
            if (++_echoPos >= bufLen) _echoPos = 0;

            // reverb
            if (doRvb)
            {
                var inp = 0f;
                for (var c = 0; c < ch; c++) inp += buffer[i + c];
                inp = inp / ch * 0.015f;

                for (var c = 0; c < ch; c++)
                {
                    var acc = 0f;
                    var cb = _combs[c];
                    for (var j = 0; j < cb.Length; j++)
                        acc += cb[j].Process(inp, _rvbFb, RvbDamp, 1f - RvbDamp);
                    var ap = _allpass[c];
                    for (var j = 0; j < ap.Length; j++)
                        acc = ap[j].Process(acc);
                    buffer[i + c] += acc * _rvbMix;
                }
            }

            // normalize: loudness measured before the gain steers it toward the target LUFS
            _preM.AddFrame(buffer, i);
            if (_preM.Hops != _preHops)
            {
                _preHops = _preM.Hops;
                var loud = _preM.ShortTerm;
                if (loud > -50f)
                    _agcTarget = MathF.Pow(10f, Math.Clamp(_p.TargetLufs - loud, -12f, 9f) / 20f);
            }

            if (normalize) _agc += (_agcTarget - _agc) * _agcCoef;
            else _agc += (1f - _agc) * _agcCoef;

            var g = _agc;

            // limiter
            if (limiter)
            {
                var peak = 0f;
                for (var c = 0; c < ch; c++) peak = MathF.Max(peak, MathF.Abs(buffer[i + c]));
                peak *= g;
                var need = peak > Ceiling ? Ceiling / peak : 1f;
                _lim = need < _lim
                    ? _lim + (need - _lim) * 0.5f
                    : MathF.Min(need, _lim + (1f - _lim) * _relCoef);
                g *= _lim;
                _grMin = MathF.Min(_grMin, _lim);
            }
            else
            {
                _lim += (1f - _lim) * _relCoef;
            }

            if (MathF.Abs(g - 1f) > 1e-4f || limiter)
            {
                for (var c = 0; c < ch; c++)
                {
                    var y = buffer[i + c] * g;
                    buffer[i + c] = limiter ? Math.Clamp(y, -0.999f, 0.999f) : y;
                }
            }
        }

        _p.GrLin = MathF.Min(_p.GrLin, _grMin);
        _grMin = 1f;
    }

    private sealed class Comb
    {
        private readonly float[] _b;
        private int _i;
        private float _store;

        public Comb(int n) => _b = new float[n];

        public float Process(float x, float fb, float d1, float d2)
        {
            var o = _b[_i];
            _store = o * d2 + _store * d1;
            if (MathF.Abs(_store) < 1e-18f) _store = 0f;
            _b[_i] = x + _store * fb;
            if (++_i >= _b.Length) _i = 0;
            return o;
        }

        public void Clear()
        {
            Array.Clear(_b);
            _store = 0f;
        }
    }

    private sealed class AllPass
    {
        private readonly float[] _b;
        private int _i;

        public AllPass(int n) => _b = new float[n];

        public float Process(float x)
        {
            var bo = _b[_i];
            var o = -x + bo;
            _b[_i] = x + bo * 0.5f;
            if (++_i >= _b.Length) _i = 0;
            return o;
        }

        public void Clear() => Array.Clear(_b);
    }
}