using System.Text;

namespace SoundPanel.Services;

/// <summary>Forgiving title matching: substring, small typos, then loose letter order.</summary>
public static class FuzzySearch
{
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Higher is better; -1 = no match. Every query word must match.</summary>
    public static int Score(string[] words, string title)
    {
        if (words.Length == 0) return -1;

        var total = 0;
        foreach (var w in words)
        {
            var s = ScoreWord(w, title);
            if (s < 0) return -1;
            total += s;
        }
        return total / words.Length;
    }

    private static int ScoreWord(string w, string t)
    {
        var idx = t.IndexOf(w, StringComparison.Ordinal);
        if (idx >= 0) return 1000 - Math.Min(idx, 200) + (idx == 0 ? 150 : 0);

        if (w.Length >= 4)
        {
            var allowed = w.Length <= 5 ? 1 : 2;
            var dist = ApproxSubstring(w, t);
            if (dist <= allowed) return 600 - dist * 100;
        }

        if (w.Length >= 3 && IsSubsequence(w, t)) return 200;
        return -1;
    }

    /// <summary>Smallest edit distance between the pattern and any substring of the text (Sellers).</summary>
    private static int ApproxSubstring(string p, string t)
    {
        var m = p.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var i = 0; i <= m; i++) prev[i] = i;

        var best = m;
        for (var j = 1; j <= t.Length; j++)
        {
            curr[0] = 0;
            for (var i = 1; i <= m; i++)
            {
                var cost = p[i - 1] == t[j - 1] ? 0 : 1;
                curr[i] = Math.Min(Math.Min(curr[i - 1] + 1, prev[i] + 1), prev[i - 1] + cost);
            }
            best = Math.Min(best, curr[m]);
            (prev, curr) = (curr, prev);
        }
        return best;
    }

    private static bool IsSubsequence(string p, string t)
    {
        var i = 0;
        foreach (var ch in t)
            if (i < p.Length && ch == p[i]) i++;
        return i == p.Length;
    }
}