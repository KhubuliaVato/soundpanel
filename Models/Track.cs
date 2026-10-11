using System.IO;
using SoundPanel.ViewModels;

namespace SoundPanel.Models;

public sealed class Track : ObservableObject
{
    private TimeSpan? _duration;
    private bool _isCurrent;
    private bool _isPlaying;

    public Track(string filePath)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
        Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
    }

    public string FilePath { get; }
    public string Title { get; }
    public string Extension { get; }

    private System.Windows.Media.ImageSource? _cover;

    public System.Windows.Media.ImageSource? Cover
    {
        get => _cover;
        set
        {
            if (SetProperty(ref _cover, value))
                OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => _cover != null;

    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
                OnPropertyChanged(nameof(DurationText));
        }
    }

    public string DurationText => _duration is { } d
        ? (d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss"))
        : "";

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }
}
