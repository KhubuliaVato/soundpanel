using System.IO;
using SoundPanel.Services;
using SoundPanel.ViewModels;

namespace SoundPanel.Models;

public enum DownloadStatus { Queued, Preparing, Downloading, Converting, Done, Failed, Cancelled, Paused }

public sealed class DownloadItem : ObservableObject
{
    private string _title;
    private string _groupName;
    private DownloadStatus _status = DownloadStatus.Queued;
    private double _progress;
    private string _detail = "";
    private string? _error;
    private string? _filePath;
    private bool _stalled;

    public DownloadItem(DownloadOptions options, string groupName, string? title = null)
    {
        Options = options;
        _groupName = groupName;
        _title = string.IsNullOrWhiteSpace(title) ? options.Url : title;
    }

    public DownloadOptions Options { get; }
    public CancellationTokenSource Cts { get; private set; } = new();

    /// <summary>Set when the user pauses: the cancelled process is then treated as "paused", not "cancelled".</summary>
    public bool PauseRequested { get; set; }

    public DateTime LastProgressAt { get; set; } = DateTime.UtcNow;
    public bool IsTrimmed => Options.StartSec != null || Options.EndSec != null;

    public string? ErrorRaw { get; set; }
    public List<string> Files { get; set; } = new();

    public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value)) OnPropertyChanged(nameof(HasFile));
        }
    }

    public bool IsStalled { get => _stalled; set => SetProperty(ref _stalled, value); }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(HasFile));
        }
    }

    public string StatusText => _status switch
    {
        DownloadStatus.Queued => "რიგშია",
        DownloadStatus.Preparing => "მზადდება…",
        DownloadStatus.Downloading => "იტვირთება",
        DownloadStatus.Converting => "გარდაიქმნება…",
        DownloadStatus.Done => "მზადაა",
        DownloadStatus.Failed => "შეცდომა",
        DownloadStatus.Paused => "პაუზაზეა",
        _ => "გაუქმდა"
    };

    public bool IsActive => _status is DownloadStatus.Preparing or DownloadStatus.Downloading or DownloadStatus.Converting;
    public bool IsRunning => _status == DownloadStatus.Queued || IsActive;
    public bool IsFinished => !IsRunning && _status != DownloadStatus.Paused;
    public bool IsDone => _status == DownloadStatus.Done;
    public bool CanRetry => _status is DownloadStatus.Failed or DownloadStatus.Cancelled;
    public bool CanPause => _status is DownloadStatus.Preparing or DownloadStatus.Downloading;
    public bool CanResume => _status == DownloadStatus.Paused;
    public bool CanRemove => !IsRunning;
    public bool ShowProgress => IsRunning || _status == DownloadStatus.Paused;

    public bool HasFile => IsDone && !string.IsNullOrEmpty(_filePath) && File.Exists(_filePath);

    public void ResetForRetry()
    {
        Cts.Dispose();
        Cts = new CancellationTokenSource();
        PauseRequested = false;
        Progress = 0;
        Detail = "";
        Error = null;
        ErrorRaw = null;
        IsStalled = false;
        Status = DownloadStatus.Queued;
    }

    /// <summary>Continues a paused item (yt-dlp keeps the partial file and picks up where it stopped).</summary>
    public void PrepareResume()
    {
        Cts.Dispose();
        Cts = new CancellationTokenSource();
        PauseRequested = false;
        Detail = "";
        Error = null;
        ErrorRaw = null;
        IsStalled = false;
        LastProgressAt = DateTime.UtcNow;
        Status = DownloadStatus.Queued;
    }
}