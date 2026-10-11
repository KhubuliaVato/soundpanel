using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;

namespace SoundPanel.Services;

public sealed record FxPreset(string Name, float[] Eq, bool IsUser = false, FxData? Data = null);

public sealed class EqBand : INotifyPropertyChanged
{
    private double _gain;

    public EqBand(string label) => Label = label;

    public string Label { get; }

    public double Gain
    {
        get => _gain;
        set
        {
            value = Math.Clamp(value, -12, 12);
            if (Math.Abs(value - _gain) < 0.001) return;
            _gain = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gain)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GainText)));
        }
    }

    public string GainText => _gain > 0.001 ? $"+{_gain:0.#}" : _gain < -0.001 ? $"{_gain:0.#}" : "0";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FxData
{
    public bool Enabled { get; set; } = true;
    public double[]? Eq { get; set; }
    public double Preamp { get; set; }
    public double Bass { get; set; }
    public double Treble { get; set; }
    public bool Normalize { get; set; }
    public bool Limiter { get; set; } = true;
    public double Echo { get; set; }
    public double EchoMs { get; set; } = 320;
    public double Reverb { get; set; }
    public double ReverbSize { get; set; } = 50;
    public double Width { get; set; } = 100;
    public double Target { get; set; } = -16;
    public string? Preset { get; set; }
}

/// <summary>Sound-processing page state: sliders, presets, persistence. Pushes values into <see cref="Params"/>.</summary>
public sealed class FxViewModel : INotifyPropertyChanged
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1Flow", "SoundPanel", "fx.json");

    private static readonly string PresetsPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "fx-presets.json");

    public static readonly IReadOnlyList<FxPreset> PresetList = new FxPreset[]
    {
        new("ბრტყელი",       new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        new("ბასი",          new float[] { 6, 5, 4, 2.5f, 1, 0, 0, 0, 0, 0 }),
        new("მაღალი ხმები",  new float[] { 0, 0, 0, 0, 0, 1, 2.5f, 4, 5, 6 }),
        new("ვოკალი",        new float[] { -2, -2, -1, 1, 3, 4, 3.5f, 2, 0, -1 }),
        new("საუბარი",       new float[] { -4, -3, -1, 1, 3, 4, 4, 2, 0, -2 }),
        new("როკი",          new float[] { 4, 3, 1.5f, -1, -2, -1, 1.5f, 3, 4, 4.5f }),
        new("პოპი",          new float[] { -1.5f, -1, 0, 2, 3.5f, 3.5f, 2, 0, -1, -1.5f }),
        new("ელექტრონული",   new float[] { 5, 4.5f, 1.5f, 0, -2, 1.5f, 0.5f, 1.5f, 4, 5 }),
        new("ჯაზი",          new float[] { 3, 2, 1, 2, -1.5f, -1.5f, 0, 1, 2, 3 }),
        new("კლასიკური",     new float[] { 3, 2.5f, 2, 1.5f, -0.5f, -0.5f, 0, 1.5f, 2.5f, 3 }),
        new("Loudness",      new float[] { 6, 4.5f, 2, 0, -1, -1, 0, 1.5f, 4, 5.5f }),
    };

    private static readonly string[] BandLabels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    private readonly DispatcherTimer _saveTimer;
    private readonly List<FxData> _user = new();
    private bool _applying;

    private bool _enabled = true;
    private bool _bypass;
    private bool _normalize;
    private bool _limiter = true;
    private double _preamp, _bass, _treble, _echo, _reverb;
    private double _echoMs = 320, _size = 50, _width = 100;
    private string _preset = "ბრტყელი";
    private bool _naming;
    private string _newName = "";
    private double _target = -16;

    private static readonly IReadOnlyList<LufsTarget> TargetList = new LufsTarget[]
    {
        new(-23, "−23 · TV"),
        new(-16, "−16"),
        new(-14, "−14 · სტრიმინგი"),
    };

    public FxViewModel()
    {
        foreach (var label in BandLabels)
        {
            var band = new EqBand(label);
            band.PropertyChanged += BandChanged;
            Bands.Add(band);
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };

        LoadUser();
        ReloadPresets();
        Load();
        Push();
    }

    public FxParams Params { get; } = new();
    public ObservableCollection<EqBand> Bands { get; } = new();

    /// <summary>Built-in presets followed by the user's own.</summary>
    public ObservableCollection<FxPreset> Presets { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CurveChanged;

    // ---------------------------------------------------------------- properties

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Normalize { get => _normalize; set => Set(ref _normalize, value); }
    public bool Limiter { get => _limiter; set => Set(ref _limiter, value); }

    /// <summary>Hold-to-compare: true plays the original sound. Never saved.</summary>
    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (_bypass == value) return;
            _bypass = value;
            Raise();
            Push();
        }
    }

    /// <summary>-12..+12 dB</summary>
    public double Preamp { get => _preamp; set => Set(ref _preamp, Math.Clamp(value, -12, 12)); }
    public double Bass { get => _bass; set => Set(ref _bass, Math.Clamp(value, -12, 12)); }
    public double Treble { get => _treble; set => Set(ref _treble, Math.Clamp(value, -12, 12)); }

    /// <summary>0..100 %</summary>
    public double Echo { get => _echo; set => Set(ref _echo, Math.Clamp(value, 0, 100)); }
    public double EchoMs { get => _echoMs; set => Set(ref _echoMs, Math.Clamp(value, 100, 800)); }
    public double Reverb { get => _reverb; set => Set(ref _reverb, Math.Clamp(value, 0, 100)); }
    public double ReverbSize { get => _size; set => Set(ref _size, Math.Clamp(value, 0, 100)); }

    /// <summary>0..200 %</summary>
    public double Width { get => _width; set => Set(ref _width, Math.Clamp(value, 0, 200)); }

    public string PresetName
    {
        get => _preset;
        set
        {
            value ??= "";
            if (value == _preset) return;
            _preset = value;
            Raise();
            Raise(nameof(IsUserPreset));
            if (_applying || value.Length == 0) return;

            var p = Presets.FirstOrDefault(x => x.Name == value);
            if (p != null) ApplyPreset(p);
        }
    }

    /// <summary>The selected preset is one of the user's own (can be deleted).</summary>
    public bool IsUserPreset => _user.Any(x => x.Preset == _preset);

    public int UserPresetCount => _user.Count;

    public bool IsNaming
    {
        get => _naming;
        private set
        {
            if (_naming == value) return;
            _naming = value;
            Raise();
        }
    }

    public string NewPresetName
    {
        get => _newName;
        set
        {
            value ??= "";
            if (value == _newName) return;
            _newName = value;
            Raise();
        }
    }

    /// <summary>Loudness the normalizer aims for (LUFS).</summary>
    public double TargetLufs { get => _target; set => Set(ref _target, Math.Clamp(value, -40, -5)); }

    public IReadOnlyList<LufsTarget> Targets => TargetList;

    // ---------------------------------------------------------------- logic

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
        Changed();
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void BandChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || e.PropertyName != nameof(EqBand.Gain)) return;
        PresetName = "";
        Changed();
    }

    private void ApplyPreset(FxPreset p)
    {
        if (p.Data != null)
        {
            ApplyData(p.Data);
            return;
        }

        _applying = true;
        for (var i = 0; i < Bands.Count && i < p.Eq.Length; i++)
            Bands[i].Gain = p.Eq[i];
        _applying = false;
        Changed();
    }

    /// <summary>Applies everything a user preset stores (the on/off switch stays as it is).</summary>
    private void ApplyData(FxData d)
    {
        _applying = true;
        Preamp = d.Preamp;
        Bass = d.Bass;
        Treble = d.Treble;
        Normalize = d.Normalize;
        Limiter = d.Limiter;
        Echo = d.Echo;
        EchoMs = d.EchoMs;
        Reverb = d.Reverb;
        ReverbSize = d.ReverbSize;
        Width = d.Width;
        if (d.Eq != null)
            for (var i = 0; i < Bands.Count && i < d.Eq.Length; i++)
                Bands[i].Gain = d.Eq[i];
        _applying = false;
        Changed();
    }

    private void Changed()
    {
        if (_applying) return;
        Push();
        _saveTimer.Stop();
        _saveTimer.Start();
        CurveChanged?.Invoke();
    }

    private void Push()
    {
        var p = Params;
        p.Enabled = _enabled;
        p.Bypass = _bypass;
        p.TargetLufs = (float)_target;
        p.PreampDb = (float)_preamp;
        p.BassDb = (float)_bass;
        p.TrebleDb = (float)_treble;
        for (var i = 0; i < Bands.Count; i++) p.Eq[i] = (float)Bands[i].Gain;
        p.Normalize = _normalize;
        p.Limiter = _limiter;
        p.EchoMix = (float)(_echo / 100);
        p.EchoMs = (float)_echoMs;
        p.ReverbMix = (float)(_reverb / 100);
        p.ReverbSize = (float)(_size / 100);
        p.Width = (float)(_width / 100);
        p.Touch();
    }

    public void ResetAll()
    {
        _applying = true;
        Enabled = true;
        Preamp = 0;
        Bass = 0;
        Treble = 0;
        Normalize = false;
        Limiter = true;
        Echo = 0;
        EchoMs = 320;
        Reverb = 0;
        ReverbSize = 50;
        Width = 100;
        foreach (var b in Bands) b.Gain = 0;
        PresetName = "ბრტყელი";
        _applying = false;
        Changed();
    }

    // ---------------------------------------------------------------- user presets

    private FxData Snapshot() => new()
    {
        Enabled = _enabled,
        Eq = Bands.Select(b => b.Gain).ToArray(),
        Preamp = _preamp,
        Bass = _bass,
        Treble = _treble,
        Normalize = _normalize,
        Limiter = _limiter,
        Echo = _echo,
        EchoMs = _echoMs,
        Reverb = _reverb,
        ReverbSize = _size,
        Width = _width,
        Target = _target,
        Preset = _preset
    };

    private static FxPreset ToPreset(FxData d) =>
        new(d.Preset ?? "", (d.Eq ?? new double[10]).Select(x => (float)x).ToArray(), true, d);

    /// <summary>Rebuilds the chip list; the current selection is kept.</summary>
    private void ReloadPresets()
    {
        var keep = _preset;
        var was = _applying;
        _applying = true;

        Presets.Clear();
        foreach (var p in PresetList) Presets.Add(p);
        foreach (var d in _user) Presets.Add(ToPreset(d));

        _preset = keep;
        _applying = was;
        Raise(nameof(PresetName));
        Raise(nameof(IsUserPreset));
        Raise(nameof(UserPresetCount));
    }

    private static string UniqueName(string name) =>
        PresetList.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? name + " 2" : name;

    public void BeginNaming()
    {
        NewPresetName = IsUserPreset ? _preset : "";
        IsNaming = true;
    }

    public void CancelNaming() => IsNaming = false;

    /// <summary>Saves the current sound as a user preset (same name = overwrite).</summary>
    public bool SaveUserPreset()
    {
        var name = (_newName ?? "").Trim();
        if (name.Length == 0) return false;
        if (name.Length > 30) name = name[..30];
        name = UniqueName(name);

        _preset = name;
        var data = Snapshot();
        data.Preset = name;

        var i = _user.FindIndex(x => string.Equals(x.Preset, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _user[i] = data;
        else _user.Add(data);

        SaveUser();
        ReloadPresets();
        IsNaming = false;
        Changed();
        return true;
    }

    public void DeleteCurrentPreset()
    {
        var i = _user.FindIndex(x => x.Preset == _preset);
        if (i < 0) return;

        _user.RemoveAt(i);
        _preset = "";
        SaveUser();
        ReloadPresets();
        Changed();
    }

    public void ExportPresets(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(_user, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Adds presets from a file made by <see cref="ExportPresets"/>; returns how many were added.</summary>
    public int ImportPresets(string path)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<FxData>>(File.ReadAllText(path));
            if (list == null) return 0;

            var count = 0;
            foreach (var d in list)
            {
                var name = (d.Preset ?? "").Trim();
                if (name.Length == 0 || d.Eq == null || d.Eq.Length != 10) continue;
                if (name.Length > 30) name = name[..30];
                name = UniqueName(name);
                d.Preset = name;

                var i = _user.FindIndex(x => string.Equals(x.Preset, name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) _user[i] = d;
                else _user.Add(d);
                count++;
            }

            if (count > 0)
            {
                SaveUser();
                ReloadPresets();
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private void LoadUser()
    {
        try
        {
            if (!File.Exists(PresetsPath)) return;
            var list = JsonSerializer.Deserialize<List<FxData>>(File.ReadAllText(PresetsPath));
            if (list == null) return;
            _user.AddRange(list.Where(d => !string.IsNullOrWhiteSpace(d.Preset) && d.Eq is { Length: 10 }));
        }
        catch
        {
            // corrupt file: start with none
        }
    }

    private void SaveUser()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath)!);
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(_user));
        }
        catch
        {
            // best-effort
        }
    }

    // ---------------------------------------------------------------- persistence

    public void Flush()
    {
        _saveTimer.Stop();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Snapshot()));
        }
        catch
        {
            // settings are best-effort
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<FxData>(File.ReadAllText(FilePath));
            if (d == null) return;

            _applying = true;
            _enabled = d.Enabled;
            _normalize = d.Normalize;
            _limiter = d.Limiter;
            _preamp = Math.Clamp(d.Preamp, -12, 12);
            _bass = Math.Clamp(d.Bass, -12, 12);
            _treble = Math.Clamp(d.Treble, -12, 12);
            _echo = Math.Clamp(d.Echo, 0, 100);
            _echoMs = Math.Clamp(d.EchoMs, 100, 800);
            _reverb = Math.Clamp(d.Reverb, 0, 100);
            _size = Math.Clamp(d.ReverbSize, 0, 100);
            _width = Math.Clamp(d.Width, 0, 200);
            _target = Math.Clamp(d.Target, -40, -5);
            _preset = d.Preset ?? "";
            if (d.Eq != null)
                for (var i = 0; i < Bands.Count && i < d.Eq.Length; i++)
                    Bands[i].Gain = d.Eq[i];
        }
        catch
        {
            // corrupt file: keep defaults
        }
        finally
        {
            _applying = false;
        }
    }
}