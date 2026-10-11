using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SoundPanel.Services;

public sealed class AudioPlayer : IDisposable
{
    public const float MaxVolume = 2f; // 200%

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private BoostSampleProvider? _boost;
    private FxChain? _fx;

    /// <summary>Sound-processing settings (EQ, effects); applied to every loaded track.</summary>
    public FxParams? Fx { get; set; }
    private float _volume = 0.8f;

    /// <summary>-1 = Windows default output.</summary>
    public int DeviceNumber { get; set; } = -1;

    /// <summary>Raised on the UI thread when a track finishes by itself (not by Stop/Load).</summary>
    public event EventHandler? Ended;

    public bool IsLoaded => _reader != null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public double Position => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

    /// <summary>0..2 (0% .. 200%).</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, MaxVolume);
            if (_boost != null) _boost.Gain = _volume;
        }
    }

    public void Load(string path)
    {
        Unload();
        try
        {
            _reader = new AudioFileReader(path);
            ISampleProvider source = _reader;
            if (Fx != null)
            {
                _fx = new FxChain(_reader, Fx);
                source = _fx;
            }
            _boost = new BoostSampleProvider(source) { Gain = _volume };
            _output = new WaveOutEvent { DesiredLatency = 120, DeviceNumber = DeviceNumber };
            _output.Init(new SampleToWaveProvider16(_boost));
            _output.PlaybackStopped += OnPlaybackStopped;
        }
        catch
        {
            Unload();
            throw;
        }
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();

    public void Seek(double seconds)
    {
        if (_reader == null) return;
        _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, Duration - 0.05)));
        _fx?.Reset();
    }

    private void Unload()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }
        _boost = null;
        _fx = null;
        _reader?.Dispose();
        _reader = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) =>
        Ended?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Unload();
}
