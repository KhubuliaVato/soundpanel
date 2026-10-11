using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SoundPanel.Services;

/// <summary>Finds songs that are already in the library (ignores junk words, case, punctuation and word order).</summary>
public sealed class LibraryIndex
{
    private sealed record Entry(string Norm, HashSet<string> Tokens, string Name);

    private readonly List<Entry> _items = new();

    public LibraryIndex(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var n = Norm(name);
            if (n.Length >= 3) _items.Add(new Entry(n, Tokens(n), name));
        }
    }

    /// <summary>Name of the matching library song, or null.</summary>
    public string? Find(string title)
    {
        var n = Norm(title);
        if (n.Length < 3) return null;

        foreach (var it in _items)
            if (it.Norm == n) return it.Name;

        if (n.Length >= 8)
            foreach (var it in _items)
                if (it.Norm.Length >= 8 && (it.Norm.Contains(n) || n.Contains(it.Norm))) return it.Name;

        var tokens = Tokens(n);
        if (tokens.Count >= 2)
        {
            foreach (var it in _items)
            {
                if (it.Tokens.Count < 2) continue;
                var inter = tokens.Count(t => it.Tokens.Contains(t));
                var union = tokens.Count + it.Tokens.Count - inter;
                if (union > 0 && (double)inter / union >= 0.8) return it.Name;
            }
        }

        return null;
    }

    private static string Norm(string s)
    {
        var t = TitleCleaner.Normalize(s).ToLowerInvariant();
        var sb = new StringBuilder(t.Length);
        foreach (var c in t) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static HashSet<string> Tokens(string n) =>
        n.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
}