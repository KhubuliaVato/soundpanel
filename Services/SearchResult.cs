using SoundPanel.ViewModels;

namespace SoundPanel.Services;

/// <summary>One YouTube search hit shown under the link box.</summary>
public sealed class SearchResult : ObservableObject
{
    private string? _duplicate;

    public SearchResult(string url, string title, string? uploader, double? duration, string? thumbnailUrl)
    {
        Url = url;
        Title = title;
        Uploader = uploader ?? "";
        DurationText = duration is { } d && d > 0 ? LinkInfo.FormatTime(d) : "";
        ThumbnailUrl = thumbnailUrl;
    }

    public string Url { get; }
    public string Title { get; }
    public string Uploader { get; }
    public string DurationText { get; }
    public string? ThumbnailUrl { get; }

    public string Subtitle =>
        string.Join("  ·  ", new[] { Uploader, DurationText }.Where(s => !string.IsNullOrEmpty(s)));

    public string? DuplicateName
    {
        get => _duplicate;
        set
        {
            if (SetProperty(ref _duplicate, value)) OnPropertyChanged(nameof(IsDuplicate));
        }
    }

    public bool IsDuplicate => _duplicate != null;
}