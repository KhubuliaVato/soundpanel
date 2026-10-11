using System.Collections.ObjectModel;
using System.IO;
using SoundPanel.Models;
using SoundPanel.Services;

namespace SoundPanel.ViewModels;

/// <summary>Search by name, "already in library" check and title cleanup.</summary>
public sealed partial class DownloaderViewModel
{
    private const int PageSize = 8;
    private const int MaxResults = 40;

    private CancellationTokenSource? _searchCts;
    private bool _isSearching;
    private bool _isLoadingMore;
    private bool _canLoadMore;
    private string? _searchError;
    private string? _dupName;
    private string _lastQuery = "";
    private int _searchCount = PageSize;

    private sealed record CleanResult(string? FileName, string? Artist, string? TitleTag, string? Display);

    // ------------------------------------------------------------ properties

    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public bool HasSearchResults => SearchResults.Count > 0;

    public bool IsSearching { get => _isSearching; private set => SetProperty(ref _isSearching, value); }

    public bool IsLoadingMore { get => _isLoadingMore; private set => SetProperty(ref _isLoadingMore, value); }

    public bool CanLoadMore { get => _canLoadMore; private set => SetProperty(ref _canLoadMore, value); }

    public string? SearchError
    {
        get => _searchError;
        private set
        {
            if (SetProperty(ref _searchError, value)) OnPropertyChanged(nameof(HasSearchError));
        }
    }

    public bool HasSearchError => !string.IsNullOrEmpty(_searchError);

    public bool CleanNames
    {
        get => _prefs.CleanNames;
        set
        {
            if (_prefs.CleanNames == value) return;
            _prefs.CleanNames = value;
            OnPropertyChanged();
            DownloaderStore.SavePrefs(_prefs);
        }
    }

    public bool HasDuplicate => _dupName != null;
    public string DuplicateText => _dupName == null ? "" : $"უკვე გაქვს ბიბლიოთეკაში: {_dupName}";

    private void SetDuplicate(string? name)
    {
        _dupName = name;
        OnPropertyChanged(nameof(HasDuplicate));
        OnPropertyChanged(nameof(DuplicateText));
    }

    // ------------------------------------------------------------ search by name

    private static bool IsSearchQuery(string text)
    {
        var t = text.Trim();
        return t.Length is >= 3 and <= 120
               && !t.Contains('\n')
               && !t.Contains("://")
               && !t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    private void OnNoSingleUrl(int urlCount)
    {
        if (urlCount == 0 && IsSearchQuery(_urlText))
        {
            _searchCts?.Cancel();
            _searchCts = null;
            IsSearching = false;
            _debounce.Start(); // the tick starts the search
        }
        else
        {
            ClearSearch();
        }
    }

    private void ClearSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        IsSearching = false;
        IsLoadingMore = false;
        CanLoadMore = false;
        SearchError = null;
        _lastQuery = "";
        _searchCount = PageSize;
        if (SearchResults.Count > 0)
        {
            SearchResults.Clear();
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    /// <summary>"Show more": asks for the next page and appends only the new hits.</summary>
    public void LoadMore()
    {
        if (!CanLoadMore || IsLoadingMore || IsSearching || _lastQuery.Length == 0) return;
        _ = SearchAsync(_lastQuery, more: true);
    }

    private async Task SearchAsync(string query, bool more = false)
    {
        var cts = _searchCts = new CancellationTokenSource();
        if (more)
        {
            IsLoadingMore = true;
        }
        else
        {
            IsSearching = true;
            CanLoadMore = false;
            _lastQuery = query;
            _searchCount = PageSize;
        }
        SearchError = null;

        try
        {
            if (!await EnsureToolsAsync() || _tools.YtDlpPath == null)
            {
                if (!cts.IsCancellationRequested)
                    SearchError = string.IsNullOrEmpty(ToolsStatus) ? "yt-dlp ვერ მოიძებნა" : ToolsStatus;
                return;
            }

            if (cts.IsCancellationRequested) return;

            var count = more ? Math.Min(_searchCount + PageSize, MaxResults) : PageSize;
            var results = await _linkInfo.SearchAsync(query, count, _tools.YtDlpPath, cts.Token);
            if (cts.IsCancellationRequested) return;

            var files = _main.GetAllTrackFiles();
            var index = await Task.Run(() => new LibraryIndex(files), cts.Token);
            foreach (var r in results)
            {
                var (artist, title) = TitleCleaner.Clean(r.Title, r.Uploader);
                r.DuplicateName = index.Find(TitleCleaner.FileName(artist, title));
            }

            if (cts.IsCancellationRequested) return;

            if (more)
            {
                var known = SearchResults.Select(r => r.Url).ToHashSet();
                foreach (var r in results)
                    if (known.Add(r.Url)) SearchResults.Add(r);
            }
            else
            {
                SearchResults.Clear();
                foreach (var r in results) SearchResults.Add(r);
                if (results.Count == 0) SearchError = "ვერაფერი მოიძებნა";
            }

            _searchCount = count;
            CanLoadMore = results.Count >= count && count < MaxResults;
            OnPropertyChanged(nameof(HasSearchResults));
        }
        catch (OperationCanceledException)
        {
            // a newer query replaced this one
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) SearchError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
                IsLoadingMore = false;
            }
        }
    }

    /// <summary>A search hit was clicked: put its link in the box, the normal preview takes over.</summary>
    public void UseResult(SearchResult result) => UrlText = result.Url;

    /// <summary>The "+" on a search hit: queue it right away with the current settings.</summary>
    public void QuickAdd(SearchResult result)
    {
        if (!_main.HasRoot)
        {
            _main.ShowMessage("ჯერ აირჩიე მთავარი საქაღალდე");
            return;
        }

        var folder = _targetFolder ?? _main.RootFolder!;
        var group = _main.Groups.FirstOrDefault(g => SamePath(g.FolderPath, folder));
        var groupName = group?.Name ?? Path.GetFileName(folder);
        var cookie = string.IsNullOrEmpty(_cookie.Id) ? null : _cookie.Id;

        var clean = CleanFor(result.Title, null, result.Uploader);
        var options = new DownloadOptions(result.Url, folder, _format.Id, _quality.Kbps, false, EmbedThumbnail,
            null, null, clean.FileName, cookie, clean.Artist, clean.TitleTag);
        var item = new DownloadItem(options, groupName, clean.Display ?? result.Title);

        Items.Insert(0, item);
        Enqueue(item);
        _main.ShowMessage(result.IsDuplicate ? "დაემატა (ეს სიმღერა უკვე გქონდა)" : "დაემატა რიგში");
        ScheduleSave();
    }

    // ------------------------------------------------------------ after a link preview

    private async Task AfterPreviewAsync(LinkInfo info, CancellationToken ct)
    {
        SetDuplicate(null);

        // suggested file name (editable in the preview card)
        if (!info.IsPlaylist && CleanNames)
        {
            var (a, t) = TitleCleaner.Clean(info.Title, info.Uploader);
            FileName = TitleCleaner.FileName(a, t);
        }

        var files = _main.GetAllTrackFiles();
        var index = await Task.Run(() => new LibraryIndex(files), ct);
        if (ct.IsCancellationRequested || !ReferenceEquals(Preview, info)) return;

        if (info.IsPlaylist)
        {
            foreach (var entry in info.Entries)
            {
                var (a, t) = TitleCleaner.Clean(entry.Title, null);
                var dup = index.Find(TitleCleaner.FileName(a, t));
                if (dup == null) continue;
                entry.DuplicateName = dup;
                entry.IsSelected = false; // already have it: unchecked by default
            }
        }
        else
        {
            var (a, t) = TitleCleaner.Clean(info.Title, info.Uploader);
            SetDuplicate(index.Find(TitleCleaner.FileName(a, t)));
        }
    }

    // ------------------------------------------------------------ title cleanup

    private CleanResult CleanFor(string? title, string? userName, string? uploader)
    {
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var name = userName.Trim();
            var (a, t) = TitleCleaner.Split(name);
            return new CleanResult(name, a, a == null ? name : t, name);
        }

        if (!CleanNames || string.IsNullOrWhiteSpace(title))
            return new CleanResult(null, null, null, null);

        var (artist, song) = TitleCleaner.Clean(title, uploader);
        var file = TitleCleaner.FileName(artist, song);
        return new CleanResult(file, artist, song, file);
    }
}