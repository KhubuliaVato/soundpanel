using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoundPanel.Models;
using SoundPanel.Services;

namespace SoundPanel.ViewModels;

public sealed record FormatOption(string Id, string Name, bool HasBitrate);
public sealed record QualityOption(int Kbps, string Name);
public sealed record CookieOption(string Id, string Name);

public sealed partial class DownloaderViewModel : ObservableObject
{
    private static readonly Regex UrlRx = new(@"https?://[^\s""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly MainViewModel _main;
    private readonly ToolsService _tools = new();
    private readonly DownloadService _service = new();
    private readonly LinkInfoService _linkInfo = new();
    private readonly DownloaderPrefs _prefs;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _stallTimer;
    private readonly List<DownloadItem> _waiting = new();
    private int _running;
    private Task<bool>? _toolsTask;

    private CancellationTokenSource? _analyzeCts;
    private string? _analyzedUrl;
    private ICollectionView? _itemsView;

    private string _urlText = "";
    private string? _targetFolder;
    private FormatOption _format;
    private QualityOption _quality;
    private CookieOption _cookie;
    private int _maxParallel;
    private bool _isInstallingTools;
    private string _toolsStatus = "";
    private double _toolsProgress;
    private bool _isUpdatingTools;

    private bool _isAnalyzing;
    private string? _analyzeError;
    private LinkInfo? _preview;
    private ImageSource? _previewThumb;
    private string _fileName = "";
    private bool _trimEnabled;
    private string _trimStart = "";
    private string _trimEnd = "";
    private string _queueSearch = "";

    public DownloaderViewModel(MainViewModel main)
    {
        _main = main;
        var s = main.Settings;
        _format = Formats.FirstOrDefault(f => f.Id == s.DownloadFormat) ?? Formats[0];
        _quality = Qualities.FirstOrDefault(q => q.Kbps == s.DownloadQuality) ?? Qualities[1];

        _prefs = DownloaderStore.LoadPrefs();
        _maxParallel = Math.Clamp(_prefs.MaxParallel, 1, 4);
        _cookie = CookieBrowsers.FirstOrDefault(c => c.Id == _prefs.Cookies) ?? CookieBrowsers[0];
        if (!string.IsNullOrEmpty(_prefs.TargetFolder) && Directory.Exists(_prefs.TargetFolder) &&
            main.Groups.Any(g => SamePath(g.FolderPath, _prefs.TargetFolder)))
            _targetFolder = _prefs.TargetFolder;

        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(QueueTitle));
            OnPropertyChanged(nameof(ShowNoMatches));
        };
        _ = ItemsView; // installs the search filter

        LoadHistory();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            var urls = ExtractUrls(_urlText);
            if (urls.Count == 1) _ = AnalyzeAsync(urls[0]);
            else if (urls.Count == 0 && IsSearchQuery(_urlText)) _ = SearchAsync(_urlText.Trim());
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };

        _stallTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _stallTimer.Tick += (_, _) => CheckStalled();
        _stallTimer.Start();

        if (Application.Current != null) Application.Current.Exit += (_, _) => OnExit();
    }

    // ------------------------------------------------------------ lists

    public IReadOnlyList<FormatOption> Formats { get; } = new[]
    {
        new FormatOption("mp3", "MP3", true),
        new FormatOption("m4a", "M4A (AAC)", true),
        new FormatOption("flac", "FLAC", false),
        new FormatOption("wav", "WAV", false),
        new FormatOption("opus", "Opus", true)
    };

    public IReadOnlyList<QualityOption> Qualities { get; } = new[]
    {
        new QualityOption(0, "საუკეთესო"),
        new QualityOption(320, "320 kbps"),
        new QualityOption(256, "256 kbps"),
        new QualityOption(192, "192 kbps"),
        new QualityOption(128, "128 kbps")
    };

    public IReadOnlyList<CookieOption> CookieBrowsers { get; } = new[]
    {
        new CookieOption("", "არა"),
        new CookieOption("firefox", "Firefox"),
        new CookieOption("chrome", "Chrome"),
        new CookieOption("edge", "Edge"),
        new CookieOption("brave", "Brave")
    };

    public IReadOnlyList<int> ParallelOptions { get; } = new[] { 1, 2, 3, 4 };

    public ObservableCollection<DownloadItem> Items { get; } = new();
    public ObservableCollection<TrackGroup> Groups => _main.Groups;
    public bool HasItems => Items.Count > 0;
    public bool HasRoot => _main.HasRoot;
    public string QueueTitle => Items.Count == 0 ? "რიგი" : $"რიგი · {Items.Count}";

    private ICollectionView ItemsView => _itemsView ??= CreateView();

    private ICollectionView CreateView()
    {
        var view = CollectionViewSource.GetDefaultView(Items);
        view.Filter = o => o is DownloadItem i && MatchesQueue(i);
        return view;
    }

    private bool MatchesQueue(DownloadItem item)
    {
        foreach (var w in _queueSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item.Title.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                item.Options.Url.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                item.GroupName.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) < 0)
                return false;
        }
        return true;
    }

    public string QueueSearch
    {
        get => _queueSearch;
        set
        {
            if (!SetProperty(ref _queueSearch, value ?? "")) return;
            ItemsView.Refresh();
            OnPropertyChanged(nameof(ShowNoMatches));
        }
    }

    public bool ShowNoMatches => HasItems && _queueSearch.Length > 0 && !Items.Any(MatchesQueue);

    // ------------------------------------------------------------ form

    public string UrlText
    {
        get => _urlText;
        set
        {
            value ??= "";
            if (!SetProperty(ref _urlText, value)) return;
            OnUrlChanged();
        }
    }

    public FormatOption SelectedFormat
    {
        get => _format;
        set
        {
            if (value == null || !SetProperty(ref _format, value)) return;
            OnPropertyChanged(nameof(ShowQuality));
            _main.Settings.DownloadFormat = value.Id;
            _main.SaveSettings();
        }
    }

    public QualityOption SelectedQuality
    {
        get => _quality;
        set
        {
            if (value == null || !SetProperty(ref _quality, value)) return;
            _main.Settings.DownloadQuality = value.Kbps;
            _main.SaveSettings();
        }
    }

    public bool ShowQuality => _format.HasBitrate;

    public CookieOption SelectedCookie
    {
        get => _cookie;
        set
        {
            if (value == null || !SetProperty(ref _cookie, value)) return;
            _prefs.Cookies = value.Id;
            DownloaderStore.SavePrefs(_prefs);
        }
    }

    public int MaxParallel
    {
        get => _maxParallel;
        set
        {
            value = Math.Clamp(value, 1, 4);
            if (!SetProperty(ref _maxParallel, value)) return;
            _prefs.MaxParallel = value;
            DownloaderStore.SavePrefs(_prefs);
            Pump();
        }
    }

    public bool Playlist
    {
        get => _main.Settings.DownloadPlaylist;
        set
        {
            if (_main.Settings.DownloadPlaylist == value) return;
            _main.Settings.DownloadPlaylist = value;
            OnPropertyChanged();
            _main.SaveSettings();

            // the same link can look different with / without playlist mode
            var urls = ExtractUrls(_urlText);
            if (urls.Count == 1)
            {
                CancelAnalyze();
                ClearPreview();
                _ = AnalyzeAsync(urls[0]);
            }
        }
    }

    public bool EmbedThumbnail
    {
        get => _main.Settings.DownloadEmbed;
        set
        {
            if (_main.Settings.DownloadEmbed == value) return;
            _main.Settings.DownloadEmbed = value;
            OnPropertyChanged();
            _main.SaveSettings();
        }
    }

    /// <summary>Folder path of the chosen component; null = main folder.</summary>
    public string? TargetFolder
    {
        get => _targetFolder ?? _main.RootFolder;
        set
        {
            if (value == null) return; // the list is rebuilt after each import; ignore the transient null
            if (!SetProperty(ref _targetFolder, value)) return;
            _prefs.TargetFolder = value;
            DownloaderStore.SavePrefs(_prefs);
        }
    }

    public bool IsInstallingTools { get => _isInstallingTools; private set => SetProperty(ref _isInstallingTools, value); }
    public string ToolsStatus { get => _toolsStatus; private set => SetProperty(ref _toolsStatus, value); }
    public double ToolsProgress { get => _toolsProgress; private set => SetProperty(ref _toolsProgress, value); }
    public bool IsUpdatingTools { get => _isUpdatingTools; private set => SetProperty(ref _isUpdatingTools, value); }

    // ------------------------------------------------------------ link preview

    public bool IsAnalyzing { get => _isAnalyzing; private set => SetProperty(ref _isAnalyzing, value); }

    public string? AnalyzeError
    {
        get => _analyzeError;
        private set
        {
            if (SetProperty(ref _analyzeError, value)) OnPropertyChanged(nameof(HasAnalyzeError));
        }
    }

    public bool HasAnalyzeError => !string.IsNullOrEmpty(_analyzeError);

    public LinkInfo? Preview
    {
        get => _preview;
        private set
        {
            if (ReferenceEquals(_preview, value)) return;
            _preview = value;

            if (value is { IsPlaylist: true })
                foreach (var e in value.Entries)
                    e.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(PlaylistEntry.IsSelected)) RaiseSelection();
                    };

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(ShowSingle));
            OnPropertyChanged(nameof(ShowPlaylistList));
            RaiseSelection();
        }
    }

    public ImageSource? PreviewThumb { get => _previewThumb; private set => SetProperty(ref _previewThumb, value); }

    public bool HasPreview => _preview != null;
    public bool ShowSingle => _preview is { IsPlaylist: false };
    public bool ShowPlaylistList => _preview is { IsPlaylist: true };

    public string SelectedCountText => _preview is { IsPlaylist: true } p
        ? $"{p.Entries.Count(e => e.IsSelected)} / {p.Entries.Count} არჩეულია"
        : "";

    public string AddButtonText => _preview is { IsPlaylist: true } p
        ? $"ჩამოტვირთვა ({p.Entries.Count(e => e.IsSelected)})"
        : "დამატება და ჩამოტვირთვა";

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(AddButtonText));
    }

    public string FileName { get => _fileName; set => SetProperty(ref _fileName, value ?? ""); }

    public bool TrimEnabled { get => _trimEnabled; set => SetProperty(ref _trimEnabled, value); }
    public string TrimStart { get => _trimStart; set => SetProperty(ref _trimStart, value ?? ""); }
    public string TrimEnd { get => _trimEnd; set => SetProperty(ref _trimEnd, value ?? ""); }

    public void SelectAllEntries(bool selected)
    {
        if (_preview is not { IsPlaylist: true } p) return;
        foreach (var e in p.Entries) e.IsSelected = selected;
    }

    private static List<string> ExtractUrls(string text) =>
        UrlRx.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')')).Distinct().ToList();

    private void OnUrlChanged()
    {
        _debounce.Stop();
        var urls = ExtractUrls(_urlText);

        if (urls.Count != 1)
        {
            CancelAnalyze();
            ClearPreview();
            OnNoSingleUrl(urls.Count);
            return;
        }

        ClearSearch();

        if (urls[0] == _analyzedUrl && (_preview != null || _isAnalyzing)) return;

        CancelAnalyze();
        ClearPreview();
        _debounce.Start();
    }

    private void CancelAnalyze()
    {
        _analyzeCts?.Cancel();
        _analyzeCts = null;
        _analyzedUrl = null;
        IsAnalyzing = false;
    }

    private void ClearPreview()
    {
        Preview = null;
        PreviewThumb = null;
        AnalyzeError = null;
        FileName = "";
        TrimEnabled = false;
        TrimStart = "";
        TrimEnd = "";
    }

    private async Task AnalyzeAsync(string url)
    {
        var cts = _analyzeCts = new CancellationTokenSource();
        _analyzedUrl = url;
        IsAnalyzing = true;
        AnalyzeError = null;
        Preview = null;
        PreviewThumb = null;

        try
        {
            if (!await EnsureToolsAsync() || _tools.YtDlpPath == null)
            {
                if (!cts.IsCancellationRequested)
                    AnalyzeError = string.IsNullOrEmpty(ToolsStatus) ? "yt-dlp ვერ მოიძებნა" : ToolsStatus;
                return;
            }

            if (cts.IsCancellationRequested) return;

            var cookie = string.IsNullOrEmpty(_cookie.Id) ? null : _cookie.Id;
            var info = await _linkInfo.AnalyzeAsync(url, Playlist, _tools.YtDlpPath, cookie, cts.Token);
            if (cts.IsCancellationRequested) return;

            Preview = info;
            _ = LoadThumbAsync(info, cts.Token);
            await AfterPreviewAsync(info, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // a newer link replaced this one
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) AnalyzeError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_analyzeCts, cts)) IsAnalyzing = false;
        }
    }

    private async Task LoadThumbAsync(LinkInfo info, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(info.ThumbnailUrl)) return;
        try
        {
            var bytes = await Http.GetByteArrayAsync(info.ThumbnailUrl, ct);
            if (ct.IsCancellationRequested) return;

            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 320;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();

            if (ReferenceEquals(_preview, info)) PreviewThumb = bmp;
        }
        catch
        {
            // no thumbnail: the placeholder stays
        }
    }

    // ------------------------------------------------------------ actions

    public void NotifyLibraryChanged()
    {
        if (_targetFolder != null && !Groups.Any(g => SamePath(g.FolderPath, _targetFolder)))
            _targetFolder = null;

        OnPropertyChanged(nameof(HasRoot));
        OnPropertyChanged(nameof(TargetFolder));
    }

    public void Add()
    {
        if (!_main.HasRoot)
        {
            _main.ShowMessage("ჯერ აირჩიე მთავარი საქაღალდე");
            return;
        }

        var jobs = new List<(string Url, string? Title)>();
        var playlistMode = false;
        double? start = null, end = null;
        string? name = null;

        if (_preview is { IsPlaylist: true } pl)
        {
            foreach (var e in pl.Entries.Where(x => x.IsSelected))
                jobs.Add((e.Url, e.Title));
            if (jobs.Count == 0)
            {
                _main.ShowMessage("აირჩიე ერთი ვიდეო მაინც");
                return;
            }
            playlistMode = true;
        }
        else
        {
            var urls = ExtractUrls(_urlText);
            if (urls.Count == 0)
            {
                _main.ShowMessage("ჩასვი ბმული ან აირჩიე ძიების შედეგი");
                return;
            }

            if (_preview is { IsPlaylist: false } single && urls.Count == 1)
            {
                name = string.IsNullOrWhiteSpace(_fileName) ? null : _fileName.Trim();

                if (_trimEnabled)
                {
                    if (!TryParseTime(_trimStart, out start) || !TryParseTime(_trimEnd, out end))
                    {
                        _main.ShowMessage("დრო ჩაწერე ასე: 1:20 ან 80");
                        return;
                    }
                    if (start != null && end != null && end <= start)
                    {
                        _main.ShowMessage("დასასრული დასაწყისზე გვიან უნდა იყოს");
                        return;
                    }
                    if (start != null && single.Duration is { } dur && dur > 0 && start >= dur)
                    {
                        _main.ShowMessage("დასაწყისი ვიდეოს ხანგრძლივობაზე მეტია");
                        return;
                    }
                }

                jobs.Add((urls[0], name ?? single.Title));
            }
            else
            {
                foreach (var u in urls) jobs.Add((u, null));
            }
        }

        var folder = _targetFolder ?? _main.RootFolder!;
        var group = _main.Groups.FirstOrDefault(g => SamePath(g.FolderPath, folder));
        var groupName = group?.Name ?? Path.GetFileName(folder);
        var cookie = string.IsNullOrEmpty(_cookie.Id) ? null : _cookie.Id;

        var index = 0;
        foreach (var (url, title) in jobs)
        {
            var clean = CleanFor(title, name, jobs.Count == 1 ? _preview?.Uploader : null);
            var options = new DownloadOptions(url, folder, _format.Id, _quality.Kbps,
                playlistMode ? false : Playlist, EmbedThumbnail, start, end, clean.FileName, cookie,
                clean.Artist, clean.TitleTag);
            var item = new DownloadItem(options, groupName, clean.Display ?? title);
            Items.Insert(index++, item);
            Enqueue(item);
        }

        if (jobs.Count > 1) _main.ShowMessage($"{jobs.Count} ჩანაწერი დაემატა რიგში");

        UrlText = "";
        ScheduleSave();
    }

    private static bool TryParseTime(string text, out double? seconds)
    {
        seconds = null;
        text = text.Trim();
        if (text.Length == 0) return true; // empty = not set

        var parts = text.Split(':');
        if (parts.Length > 3) return false;

        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
                return false;
            total = total * 60 + v;
        }

        seconds = total;
        return true;
    }

    public void Cancel(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Queued)
        {
            _waiting.Remove(item);
            item.Cts.Cancel();
            item.Status = DownloadStatus.Cancelled;
            ScheduleSave();
            return;
        }

        item.PauseRequested = false;
        item.Cts.Cancel();
    }

    public void Pause(DownloadItem item)
    {
        if (!item.CanPause) return;
        item.PauseRequested = true;
        item.Cts.Cancel();
    }

    public void Resume(DownloadItem item)
    {
        if (!item.CanResume) return;
        item.PrepareResume();
        Enqueue(item);
    }

    public void Retry(DownloadItem item)
    {
        if (!item.CanRetry) return;
        item.ResetForRetry();
        Enqueue(item);
    }

    public void Remove(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Queued) _waiting.Remove(item);
        if (item.IsRunning) item.Cts.Cancel();
        Items.Remove(item);
        ScheduleSave();
    }

    public void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.IsFinished).ToList())
            Items.Remove(item);
        ScheduleSave();
    }

    public void Play(DownloadItem item)
    {
        if (!item.HasFile) return;
        var path = item.FilePath!;
        var queue = _main.GetAllTrackFiles();
        if (!queue.Contains(path, StringComparer.OrdinalIgnoreCase)) queue.Insert(0, path);
        _main.PlayFromLibrary(path, queue);
    }

    public void ShowInFolder(DownloadItem item)
    {
        try
        {
            if (item.HasFile)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true });
            else if (Directory.Exists(item.Options.FolderPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Options.FolderPath}\"") { UseShellExecute = true });
        }
        catch
        {
            _main.ShowMessage("საქაღალდე ვერ გავხსენი");
        }
    }

    public void Move(DownloadItem item, TrackGroup group)
    {
        var moved = new List<string>();
        foreach (var file in item.Files.Where(File.Exists))
        {
            try
            {
                var dest = Path.Combine(group.FolderPath, Path.GetFileName(file));
                if (SamePath(file, dest))
                {
                    moved.Add(file);
                    continue;
                }

                dest = UniquePath(dest);
                File.Move(file, dest);
                moved.Add(dest);
            }
            catch
            {
                // skip a file that cannot be moved
            }
        }

        if (moved.Count == 0)
        {
            _main.ShowMessage("ფაილი ვერ გადავიტანე");
            return;
        }

        item.Files = moved;
        item.FilePath = moved[0];
        item.GroupName = group.Name;
        _main.RefreshLibrary();
        _main.ShowMessage($"გადავიტანე → {group.Name}");
        ScheduleSave();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task UpdateToolsAsync()
    {
        if (IsUpdatingTools) return;
        IsUpdatingTools = true;
        try
        {
            if (!await EnsureToolsAsync()) return;
            _main.ShowMessage(await _tools.UpdateYtDlpAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _main.ShowMessage($"განახლება ვერ მოხერხდა: {ex.Message}");
        }
        finally
        {
            IsUpdatingTools = false;
        }
    }

    // ------------------------------------------------------------ queue engine

    private void Enqueue(DownloadItem item)
    {
        item.Status = DownloadStatus.Queued;
        _waiting.Add(item);
        Pump();
    }

    private void Pump()
    {
        while (_running < _maxParallel && _waiting.Count > 0)
        {
            var next = _waiting[0];
            _waiting.RemoveAt(0);
            _running++;
            _ = RunSlotAsync(next);
        }
    }

    private async Task RunSlotAsync(DownloadItem item)
    {
        try
        {
            await RunItemAsync(item);
        }
        finally
        {
            _running--;
            Pump();
        }
    }

    private Task<bool> EnsureToolsAsync()
    {
        if (_tools.IsReady) return Task.FromResult(true);
        return _toolsTask ??= InstallToolsAsync();
    }

    private async Task<bool> InstallToolsAsync()
    {
        try
        {
            IsInstallingTools = true;
            ToolsProgress = 0;
            var status = new Progress<string>(s => ToolsStatus = s);
            var percent = new Progress<double>(p => ToolsProgress = p);
            await _tools.EnsureAsync(status, percent, CancellationToken.None);
            ToolsStatus = "";
            return true;
        }
        catch (Exception ex)
        {
            ToolsStatus = $"ვერ ჩამოიტვირთა: {ex.Message}";
            return false;
        }
        finally
        {
            IsInstallingTools = false;
            _toolsTask = null;
        }
    }

    private async Task RunItemAsync(DownloadItem item)
    {
        try
        {
            if (item.Cts.IsCancellationRequested)
            {
                item.Status = DownloadStatus.Cancelled;
                return;
            }

            item.Status = DownloadStatus.Preparing;
            item.LastProgressAt = DateTime.UtcNow;
            if (item.IsTrimmed) item.Detail = "მოჭრილი ნაწილი იტვირთება…";

            if (!await EnsureToolsAsync() || _tools.YtDlpPath == null || _tools.FfmpegDir == null)
            {
                item.Error = string.IsNullOrEmpty(ToolsStatus) ? "yt-dlp / ffmpeg ვერ მოიძებნა" : ToolsStatus;
                item.Status = DownloadStatus.Failed;
                return;
            }

            if (item.Cts.IsCancellationRequested)
            {
                item.Status = item.PauseRequested ? DownloadStatus.Paused : DownloadStatus.Cancelled;
                return;
            }

            var progress = new Progress<DownloadProgress>(p => Apply(item, p));
            var result = await _service.RunAsync(item.Options, _tools.YtDlpPath, _tools.FfmpegDir, progress, item.Cts.Token);

            if (result.Cancelled)
            {
                if (item.PauseRequested)
                {
                    item.Status = DownloadStatus.Paused;
                    item.Detail = "გასაგრძელებლად დააჭირე ▶";
                }
                else
                {
                    item.Status = DownloadStatus.Cancelled;
                    item.Detail = "";
                }
            }
            else if (result.Success)
            {
                item.Progress = 100;
                item.Detail = "";
                item.IsStalled = false;
                item.Files = result.Files.ToList();
                item.FilePath = result.Files.FirstOrDefault(File.Exists) ?? result.Files.FirstOrDefault();
                if (result.Files.Count == 1) item.Title = Path.GetFileNameWithoutExtension(result.Files[0]);
                item.Status = DownloadStatus.Done;

                _main.RefreshLibrary();
                var count = Math.Max(1, result.Files.Count);
                _main.ShowMessage($"{count} სიმღერა ჩამოიტვირთა → {item.GroupName}");

                var window = Application.Current?.MainWindow;
                if (window != null && !window.IsActive) TaskbarFlash.Flash(window);
            }
            else
            {
                item.Error = result.Error;
                item.ErrorRaw = result.RawError;
                item.Detail = "";
                item.Status = DownloadStatus.Failed;
            }
        }
        catch (Exception ex)
        {
            item.Error = ErrorText.Friendly(ex.Message);
            item.ErrorRaw = ex.Message;
            item.Status = DownloadStatus.Failed;
        }
        finally
        {
            ScheduleSave();
        }
    }

    private static void Apply(DownloadItem item, DownloadProgress p)
    {
        if (item.Status is DownloadStatus.Done or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Paused)
            return;

        item.Status = p.Phase switch
        {
            DownloadPhase.Downloading => DownloadStatus.Downloading,
            DownloadPhase.Converting => DownloadStatus.Converting,
            _ => DownloadStatus.Preparing
        };

        item.LastProgressAt = DateTime.UtcNow;
        item.IsStalled = false;

        if (p.Percent is { } percent) item.Progress = percent;
        if (!string.IsNullOrEmpty(p.Title)) item.Title = p.Title;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(p.Position)) parts.Add(p.Position);
        if (!string.IsNullOrEmpty(p.Size)) parts.Add(p.Size);
        if (!string.IsNullOrEmpty(p.Speed)) parts.Add(p.Speed);
        if (!string.IsNullOrEmpty(p.Eta)) parts.Add($"დარჩა {p.Eta}");
        if (parts.Count > 0) item.Detail = string.Join("  ·  ", parts);
    }

    private void CheckStalled()
    {
        var now = DateTime.UtcNow;
        foreach (var item in Items)
        {
            // trimmed downloads report no percentage, so they are never "stalled"
            item.IsStalled = item.Status == DownloadStatus.Downloading && !item.IsTrimmed &&
                             (now - item.LastProgressAt).TotalSeconds > 45;
        }
    }

    // ------------------------------------------------------------ history

    private void LoadHistory()
    {
        foreach (var h in DownloaderStore.LoadHistory())
        {
            var options = new DownloadOptions(h.Url, h.Folder, h.Format, h.Quality, h.Playlist, h.Embed,
                h.Start, h.End, h.FileName, string.IsNullOrEmpty(h.Cookies) ? null : h.Cookies);

            var item = new DownloadItem(options, h.Group, h.Title)
            {
                Progress = h.Progress,
                FilePath = h.FilePath,
                Files = h.Files ?? new List<string>(),
                Error = h.Error,
                ErrorRaw = h.ErrorRaw
            };

            item.Status = h.Status switch
            {
                "Done" => DownloadStatus.Done,
                "Failed" => DownloadStatus.Failed,
                _ => DownloadStatus.Paused
            };
            if (item.Status == DownloadStatus.Paused) item.Detail = "გასაგრძელებლად დააჭირე ▶";

            Items.Add(item);
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        var list = Items
            .Where(i => i.Status != DownloadStatus.Cancelled)
            .Take(300)
            .Select(i => new HistoryEntry
            {
                Url = i.Options.Url,
                Title = i.Title,
                Group = i.GroupName,
                Folder = i.Options.FolderPath,
                Format = i.Options.Format,
                Quality = i.Options.QualityKbps,
                Playlist = i.Options.Playlist,
                Embed = i.Options.EmbedThumbnail,
                FileName = i.Options.FileName,
                Cookies = i.Options.CookiesBrowser ?? "",
                Start = i.Options.StartSec,
                End = i.Options.EndSec,
                Status = i.Status switch
                {
                    DownloadStatus.Done => "Done",
                    DownloadStatus.Failed => "Failed",
                    _ => "Paused" // anything unfinished can be resumed next time
                },
                Error = i.Error,
                ErrorRaw = i.ErrorRaw,
                FilePath = i.FilePath,
                Files = i.Files,
                Progress = i.Progress
            })
            .ToList();

        DownloaderStore.SaveHistory(list);
    }

    private void OnExit()
    {
        // stop running yt-dlp processes; they are saved as paused and can be resumed next time
        foreach (var item in Items.Where(i => i.IsRunning).ToList())
        {
            item.PauseRequested = true;
            try { item.Cts.Cancel(); } catch { /* ignore */ }
        }

        _saveTimer.Stop();
        SaveNow();
    }
}