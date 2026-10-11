using System.Text.RegularExpressions;

namespace SoundPanel.Services;

/// <summary>Strips "(Official Video)", "[HD]", "| Channel" and similar junk; splits "Artist - Title".</summary>
public static class TitleCleaner
{
    private static readonly Regex JunkBracket = new(
        @"\s*[\(\[\{【][^\)\]\}】]*?(official|video|audio|lyric|visuali[sz]er|\bhd\b|\bhq\b|\b4k\b|1080p|720p|\bm/?v\b|clip|remaster|ვიდეო|клип)[^\)\]\}】]*?[\)\]\}】]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Hashtag = new(@"\s*#\S+", RegexOptions.Compiled);
    private static readonly Regex PipeTail = new(@"\s*[|｜]\s*.*$", RegexOptions.Compiled);
    private static readonly Regex TrailingJunk = new(
        @"\s*[-–—:]?\s*\b(official\s+(music\s+)?(video|audio)|music\s+video|lyric\s+video|lyrics?|official|audio|hd|hq|4k)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Dash = new(@"^(?<a>.{1,80}?)\s+[-–—]\s+(?<t>.+)$", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Normalize(string s)
    {
        var t = PipeTail.Replace(s, "");
        t = Hashtag.Replace(t, "");

        for (var i = 0; i < 3; i++)
        {
            var before = t;
            t = JunkBracket.Replace(t, "");
            t = TrailingJunk.Replace(t, "");
            if (t == before) break;
        }

        t = Spaces.Replace(t, " ").Trim(' ', '-', '–', '—', '|', '_', '.');
        return t.Length == 0 ? s.Trim() : t;
    }

    public static (string? Artist, string Title) Split(string name)
    {
        var m = Dash.Match(name);
        return m.Success ? (m.Groups["a"].Value.Trim(), m.Groups["t"].Value.Trim()) : (null, name.Trim());
    }

    public static (string? Artist, string Title) Clean(string title, string? uploader)
    {
        var (artist, song) = Split(Normalize(title));

        // channels named "X - Topic" / "XVEVO" are reliably the artist
        if (artist == null && !string.IsNullOrWhiteSpace(uploader))
        {
            var u = uploader.Trim();
            if (u.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) artist = u[..^8].Trim();
            else if (u.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase)) artist = u[..^4].Trim();
        }

        return (string.IsNullOrWhiteSpace(artist) ? null : artist, song);
    }

    public static string FileName(string? artist, string title) =>
        string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
}