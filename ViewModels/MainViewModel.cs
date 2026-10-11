using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using SoundPanel.Models;
using SoundPanel.Services;

namespace SoundPanel.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private readonly AudioPlayer _player = new();

    /// <summary>Sound-processing page (EQ, effects).</summary>
    public FxViewModel Fx { get; } = new();
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _durationCts;
    private CancellationTokenSource? _waveCts;
    private double _volumeBeforeMute = 1.0;
    private bool _shuffle;
    private bool _repeat = true;
    private readonly Random _rng = new();
    private readonly List<string> _history = new();
    private readonly HashSet<string> _shufflePlayed = new(StringComparer.OrdinalIgnoreCase);
    private bool _goingBack;
    private float[]? _peaks;
    private List<string> _queue = new();
    private int _queueIndex = -1;
    private string? _currentPath;

    private string? _rootFolder;
    private TrackGroup? _selectedGroup;
    private bool _isPlaying;
    private double _positionSeconds;
    private double _durationSeconds;
    private double _volume;
    private string _nowPlayingTitle = "აირჩიე სიმღერა";

    public MainViewModel()
    {
        _settings = _settingsService.Load();
        _volume = Math.Clamp(_settings.Volume, 0, 2);
        _volumeBeforeMute = _volume > 0 ? _volume : 1.0;
        _player.Volume = (float)_volume;
        _player.Fx = Fx.Params;
        _shuffle = _settings.Shuffle;
        _ = TracksView; // installs the search filter
        _repeat = _settings.Repeat;
        _outputDevices = LoadDevices();
        _player.DeviceNumber = ResolveDevice(_settings.OutputDevice);
        _player.Ended += (_, _) => Next(auto: true);

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            if (_player.IsPlaying && !IsSeeking)
                PositionSeconds = _player.Position;
        };
        _timer.Start();

        if (!string.IsNullOrWhiteSpace(_settings.RootFolder) && Directory.Exists(_settings.RootFolder))
        {
            _rootFolder = _settings.RootFolder;
            LoadLibrary();
        }
    }

    // ---------------------------------------------------------------- events

    /// <summary>Short messages for the UI toast.</summary>
    public event Action<string>? Notify;

    // ---------------------------------------------------------------- collections

    public ObservableCollection<TrackGroup> Groups { get; } = new();
    public ObservableCollection<Track> Tracks { get; } = new();

    // ---------------------------------------------------------------- state

    public bool IsSeeking { get; set; }

    private System.Windows.Media.ImageSource? _currentCover;

    public System.Windows.Media.ImageSource? CurrentCover
    {
        get => _currentCover;
        private set
        {
            if (SetProperty(ref _currentCover, value))
                OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => _currentCover != null;

    // ---------------------------------------------------------------- search

    private string _searchText = "";
    private ICollectionView? _tracksView;

    private ICollectionView TracksView => _tracksView ??= CreateView();

    private ICollectionView CreateView()
    {
        var view = CollectionViewSource.GetDefaultView(Tracks);
        view.Filter = o => o is Track t && MatchesSearch(t);
        return view;
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? "")) return;
            TracksView.Refresh();
            OnPropertyChanged(nameof(HasSearch));
            OnPropertyChanged(nameof(TracksSubtitle));
            OnPropertyChanged(nameof(ShowNoMatches));
        }
    }

    public bool HasSearch => _searchText.Length > 0;
    public bool ShowNoMatches => HasSearch && Tracks.Count > 0 && !Tracks.Any(MatchesSearch);

    /// <summary>Every typed word must appear in the title (any order, ignoring case).</summary>
    public bool MatchesSearch(Track track)
    {
        var words = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
            if (track.Title.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) < 0)
                return false;
        return true;
    }

    public void PlayFirstMatch()
    {
        if (Tracks.FirstOrDefault(MatchesSearch) is { } first) PlayTrack(first);
    }

    // ---------------------------------------------------------------- settings

    public IReadOnlyList<AccentTheme> AccentThemes => ThemeService.AccentThemes;
    public IReadOnlyList<RobotTheme> RobotThemes => ThemeService.RobotThemes;

    public string AccentThemeId
    {
        get => _settings.AccentTheme;
        set
        {
            if (string.IsNullOrEmpty(value) || value == _settings.AccentTheme) return;
            _settings.AccentTheme = value;
            ThemeService.ApplyAccent(value);
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string RobotThemeId
    {
        get => _settings.RobotTheme;
        set
        {
            if (string.IsNullOrEmpty(value) || value == _settings.RobotTheme) return;
            _settings.RobotTheme = value;
            ThemeService.ApplyRobot(value);
            OnPropertyChanged();
            SaveSettings();
        }
    }

    private bool _isFullscreen;

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (_settings.AlwaysOnTop == value) return;
            _settings.AlwaysOnTop = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool MiniOnMinimize
    {
        get => _settings.MiniOnMinimize;
        set
        {
            if (_settings.MiniOnMinimize == value) return;
            _settings.MiniOnMinimize = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool StartWithWindows
    {
        get => AutoStartService.IsEnabled();
        set
        {
            AutoStartService.Set(value);
            OnPropertyChanged();
        }
    }

    private List<AudioDevice> _outputDevices = new();
    public IReadOnlyList<AudioDevice> OutputDevices => _outputDevices;

    public string OutputDeviceKey
    {
        get => _settings.OutputDevice;
        set
        {
            if (value == null || value == _settings.OutputDevice) return;
            _settings.OutputDevice = value;
            OnPropertyChanged();
            SaveSettings();
            ApplyDevice();
        }
    }

    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void ResetSettings()
    {
        AccentThemeId = "aurora";
        RobotThemeId = "neon";
        AlwaysOnTop = false;
        MiniOnMinimize = true;
        IsFullscreen = false;
        OutputDeviceKey = "";
        Shuffle = false;
        Repeat = true;
    }

    public void SaveSettings() => _settingsService.Save(_settings);
    public AppSettings Settings => _settings;
    public void RefreshLibrary() => LoadLibrary();
    public void ShowMessage(string text) => Notify?.Invoke(text);

    /// <summary>Temporary volume multiplier (0..1) for fade-outs; does not touch the saved volume.</summary>
    public void SetFade(double factor) => _player.Volume = (float)(_volume * Math.Clamp(factor, 0, 1));

    // ---------------------------------------------------------------- quick search

    public string QuickSearchHotkey
    {
        get => _settings.QuickSearchHotkey;
        set
        {
            value ??= "";
            if (value == _settings.QuickSearchHotkey) return;
            _settings.QuickSearchHotkey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickSearchHotkeyDisplay));
            SaveSettings();
        }
    }

    public string QuickSearchHotkeyDisplay =>
        string.IsNullOrEmpty(_settings.QuickSearchHotkey) ? "გამორთულია" : _settings.QuickSearchHotkey;

    /// <summary>Every song in the library (main folder + all components); a song copied into a component appears once.</summary>
    public List<string> GetAllTrackFiles()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var group in Groups)
        {
            foreach (var file in LibraryService.GetTrackFiles(group.FolderPath))
            {
                if (names.Add(Path.GetFileName(file)))
                    result.Add(file);
            }
        }
        return result;
    }

    /// <summary>Plays a song picked from the quick search; next/previous continue through the whole library.</summary>
    public void PlayFromLibrary(string path, List<string> queue)
    {
        _queue = queue.ToList();
        _queueIndex = _queue.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _shufflePlayed.Clear();
        _history.Clear();
        PlayPath(path);
    }

    private static List<AudioDevice> LoadDevices()
    {
        var list = new List<AudioDevice> { new("Windows-ის ნაგულისხმევი", -1) };
        try
        {
            for (var i = 0; i < NAudio.Wave.WaveOut.DeviceCount; i++)
                list.Add(new AudioDevice(NAudio.Wave.WaveOut.GetCapabilities(i).ProductName, i));
        }
        catch { /* no audio devices */ }
        return list;
    }

    private int ResolveDevice(string key) =>
        _outputDevices.FirstOrDefault(d => d.Key == key)?.Number ?? -1;

    /// <summary>Switches the output device; the current track continues from the same position.</summary>
    private void ApplyDevice()
    {
        _player.DeviceNumber = ResolveDevice(_settings.OutputDevice);
        if (_currentPath == null) return;

        var position = _player.Position;
        var wasPlaying = _player.IsPlaying;
        try
        {
            _player.Load(_currentPath);
            _player.Seek(position);
            if (wasPlaying) _player.Play();
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            Notify?.Invoke($"ვერ გადაერთო: {ex.Message}");
        }
    }

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (!SetProperty(ref _shuffle, value)) return;
            _shufflePlayed.Clear();
            if (_currentPath != null) _shufflePlayed.Add(_currentPath);
            _settings.Shuffle = value;
        }
    }

    /// <summary>When on, the list starts over after the last track, so music never stops.</summary>
    public bool Repeat
    {
        get => _repeat;
        set
        {
            if (SetProperty(ref _repeat, value))
                _settings.Repeat = value;
        }
    }

    public string? RootFolder
    {
        get => _rootFolder;
        private set
        {
            if (!SetProperty(ref _rootFolder, value)) return;
            OnPropertyChanged(nameof(HasRoot));
            OnPropertyChanged(nameof(ShowNoRoot));
            OnPropertyChanged(nameof(RootFolderDisplay));
            OnPropertyChanged(nameof(ShowEmptyTracks));
        }
    }

    public bool HasRoot => !string.IsNullOrEmpty(_rootFolder);
    public bool ShowNoRoot => !HasRoot;
    public bool ShowEmptyTracks => HasRoot && Tracks.Count == 0;
    public string RootFolderDisplay => HasRoot ? _rootFolder! : "არ არის არჩეული";

    public TrackGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (value == null) return; // ListBox clears selection while the list is rebuilt
            if (!SetProperty(ref _selectedGroup, value)) return;
            _searchText = "";
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(HasSearch));
            ReloadTracks();
        }
    }

    public string TracksSubtitle
    {
        get
        {
            if (Tracks.Count == 0) return "ცარიელია";
            if (!HasSearch) return $"{Tracks.Count} სიმღერა";
            return $"{Tracks.Count(MatchesSearch)} / {Tracks.Count} სიმღერა";
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!SetProperty(ref _isPlaying, value)) return;
            RefreshTrackFlags();
        }
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (!SetProperty(ref _positionSeconds, value)) return;
            OnPropertyChanged(nameof(PositionText));
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (!SetProperty(ref _durationSeconds, value)) return;
            OnPropertyChanged(nameof(DurationText));
        }
    }

    public string PositionText => FormatTime(_positionSeconds);
    public string DurationText => FormatTime(_durationSeconds);

    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value)) return;
            _player.Volume = (float)value;
            if (value > 0) _settings.Volume = value; // never persist "muted"
        }
    }

    /// <summary>Loudness profile of the current track (0..1 per bucket); null while it is being computed.</summary>
    public float[]? Peaks
    {
        get => _peaks;
        private set => SetProperty(ref _peaks, value);
    }

    public string NowPlayingTitle
    {
        get => _nowPlayingTitle;
        private set => SetProperty(ref _nowPlayingTitle, value);
    }

    // ---------------------------------------------------------------- library

    public void SetRootFolder(string path)
    {
        _settings.RootFolder = path;
        _settingsService.Save(_settings);
        RootFolder = path;
        LoadLibrary();
    }

    private void LoadLibrary()
    {
        if (!HasRoot) return;

        var previous = _selectedGroup?.FolderPath;
        Groups.Clear();

        Groups.Add(new TrackGroup
        {
            Name = "მთელი ბიბლიოთეკა",
            FolderPath = _rootFolder!,
            IsAll = true,
            Count = LibraryService.CountTracks(_rootFolder!)
        });

        foreach (var dir in LibraryService.GetGroupFolders(_rootFolder!))
        {
            Groups.Add(new TrackGroup
            {
                Name = Path.GetFileName(dir),
                FolderPath = dir,
                Count = LibraryService.CountTracks(dir)
            });
        }

        _selectedGroup = null; // force a fresh selection + reload
        SelectedGroup = Groups.FirstOrDefault(g => g.FolderPath == previous) ?? Groups[0];
        OnPropertyChanged(nameof(SelectedGroup));
    }

    private void ReloadTracks()
    {
        _durationCts?.Cancel();
        _durationCts = new CancellationTokenSource();

        Tracks.Clear();
        if (_selectedGroup != null)
        {
            foreach (var file in LibraryService.GetTrackFiles(_selectedGroup.FolderPath))
            {
                var isCurrent = string.Equals(file, _currentPath, StringComparison.OrdinalIgnoreCase);
                Tracks.Add(new Track(file) { IsCurrent = isCurrent, IsPlaying = isCurrent && _isPlaying });
            }
            _selectedGroup.Count = Tracks.Count;
        }

        OnPropertyChanged(nameof(TracksSubtitle));
        OnPropertyChanged(nameof(ShowEmptyTracks));
        OnPropertyChanged(nameof(ShowNoMatches));
        _ = LoadDurationsAsync(Tracks.ToList(), _durationCts.Token);
        _ = LoadCoversAsync(Tracks.ToList(), _durationCts.Token);
    }

    private static async Task LoadCoversAsync(List<Track> tracks, CancellationToken ct)
    {
        foreach (var track in tracks)
        {
            if (ct.IsCancellationRequested) return;
            var cover = await Task.Run(() => CoverService.Load(track.FilePath, 96));
            if (ct.IsCancellationRequested) return;
            track.Cover = cover;
        }
    }

    private static async Task LoadDurationsAsync(List<Track> tracks, CancellationToken ct)
    {
        foreach (var track in tracks)
        {
            if (ct.IsCancellationRequested) return;
            var duration = await Task.Run(() => LibraryService.ReadDuration(track.FilePath), ct)
                .ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null, TaskScheduler.Default);
            if (ct.IsCancellationRequested) return;
            track.Duration = duration;

            var cover = await Task.Run(() => CoverService.Load(track.FilePath, 96));
            if (ct.IsCancellationRequested) return;
            track.Cover = cover;
        }
    }

    /// <summary>Creates a new component folder inside the main folder. Returns an error text or null.</summary>
    public string? CreateGroup(string name)
    {
        if (!HasRoot) return "ჯერ აირჩიე მთავარი საქაღალდე";
        var error = LibraryService.CreateGroup(_rootFolder!, name, out var folder);
        if (error != null) return error;

        LoadLibrary();
        var created = Groups.FirstOrDefault(g => g.FolderPath == folder);
        if (created != null) SelectedGroup = created;
        OnPropertyChanged(nameof(SelectedGroup));
        return null;
    }

    /// <summary>Copies audio files into a component. Dropping on "all" imports into the main folder.</summary>
    public (int Added, int Skipped) AddFiles(TrackGroup group, IEnumerable<string> files)
    {
        int added = 0, skipped = 0;
        foreach (var file in files)
        {
            if (!LibraryService.IsAudio(file)) continue;
            try
            {
                if (LibraryService.CopyInto(group.FolderPath, file)) added++;
                else skipped++;
            }
            catch (Exception ex)
            {
                Notify?.Invoke($"შეცდომა: {ex.Message}");
            }
        }

        group.Count = LibraryService.CountTracks(group.FolderPath);

        if (added > 0 && ReferenceEquals(group, _selectedGroup))
            ReloadTracks();

        return (added, skipped);
    }

    // ---------------------------------------------------------------- playback

    public void PlayTrack(Track track)
    {
        _queue = Tracks.Where(MatchesSearch).Select(t => t.FilePath).ToList();
        _queueIndex = _queue.IndexOf(track.FilePath);
        _shufflePlayed.Clear();
        _history.Clear();
        PlayPath(track.FilePath);
    }

    public void TogglePlay()
    {
        if (_currentPath == null)
        {
            if (Tracks.FirstOrDefault(MatchesSearch) is { } first) PlayTrack(first);
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            if (_player.Position >= _player.Duration - 0.2) _player.Seek(0);
            _player.Play();
            IsPlaying = true;
        }
    }

    public void Next(bool auto = false)
    {
        if (_queue.Count == 0) return;

        string? next;

        if (_shuffle)
        {
            next = PickShuffled(); // null = every track was played in this round
            if (next == null)
            {
                if (auto && !_repeat)
                {
                    StopAtEnd();
                    return;
                }

                _shufflePlayed.Clear();
                if (_currentPath != null) _shufflePlayed.Add(_currentPath);
                next = PickShuffled() ?? _queue[0];
            }
        }
        else
        {
            var i = _queueIndex + 1;
            if (i >= _queue.Count)
            {
                if (auto && !_repeat)
                {
                    StopAtEnd();
                    return;
                }
                i = 0;
            }
            next = _queue[i];
        }

        _queueIndex = _queue.IndexOf(next);
        PlayPath(next);
    }

    private string? PickShuffled()
    {
        var pool = _queue.Where(p => !_shufflePlayed.Contains(p)).ToList();
        return pool.Count == 0 ? null : pool[_rng.Next(pool.Count)];
    }

    private void StopAtEnd()
    {
        _player.Seek(0);
        PositionSeconds = 0;
        IsPlaying = false;
    }

    public void Previous()
    {
        if (_queue.Count == 0) return;

        if (_player.Position > 3)
        {
            SeekTo(0);
            return;
        }

        if (_shuffle && _history.Count > 0)
        {
            var prev = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            _goingBack = true;
            _queueIndex = _queue.IndexOf(prev);
            PlayPath(prev);
            _goingBack = false;
            return;
        }

        _queueIndex = _queueIndex <= 0 ? _queue.Count - 1 : _queueIndex - 1;
        PlayPath(_queue[_queueIndex]);
    }

    public void ToggleMute()
    {
        if (_volume > 0)
        {
            _volumeBeforeMute = _volume;
            Volume = 0;
        }
        else
        {
            Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1.0;
        }
    }

    public void SeekTo(double seconds)
    {
        _player.Seek(seconds);
        PositionSeconds = _player.Position;
    }

    private void PlayPath(string path)
    {
        try
        {
            _player.Load(path);
            _player.Play();
            if (!_goingBack && _currentPath != null &&
                !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _history.Add(_currentPath);
                if (_history.Count > 200) _history.RemoveAt(0);
            }
            _shufflePlayed.Add(path);
            _currentPath = path;
            NowPlayingTitle = Path.GetFileNameWithoutExtension(path);
            CurrentCover = CoverService.Load(path, 220);
            DurationSeconds = _player.Duration;
            PositionSeconds = 0;
            IsPlaying = true;
            RefreshTrackFlags();
            _ = LoadPeaksAsync(path);
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            Notify?.Invoke($"ვერ დაიკვრა: {ex.Message}");
        }
    }

    private async Task LoadPeaksAsync(string path)
    {
        _waveCts?.Cancel();
        var cts = _waveCts = new CancellationTokenSource();
        Peaks = null;

        try
        {
            var peaks = await Task.Run(() => WaveformService.ComputePeaks(path, 512, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested && string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
                Peaks = peaks;
        }
        catch
        {
            // cancelled or unreadable file: the bar simply stays in its idle state
        }
    }

    private void RefreshTrackFlags()
    {
        foreach (var t in Tracks)
        {
            var current = string.Equals(t.FilePath, _currentPath, StringComparison.OrdinalIgnoreCase);
            t.IsCurrent = current;
            t.IsPlaying = current && _isPlaying;
        }
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    public void Dispose()
    {
        _timer.Stop();
        _durationCts?.Cancel();
        _waveCts?.Cancel();
        _settingsService.Save(_settings);
        Fx.Flush();
        _player.Dispose();
    }
}
