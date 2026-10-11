using System.Text.RegularExpressions;

namespace SoundPanel.Services;

public enum VoiceKind
{
    Unknown, Play, Resume, Pause, Next, Previous, Shuffle, Repeat,
    VolumeSet, VolumeUp, VolumeDown, Mute, Unmute
}

public sealed record VoiceCommand(VoiceKind Kind, string Arg = "", int Number = 0, bool? Flag = null);

/// <summary>Turns "play next", "volume 120", "shuffle off", "play sawadika"... into a command.</summary>
public static class VoiceCommands
{
    private static readonly Regex WakeRx = new(
        @"^(?:hey |ok |okay |hi )?(?:fifu|fifo|fifi|fiffu|fifa|fee foo|fee fu|feefoo|fi fu|fifoo)\b\s*",
        RegexOptions.Compiled);

    private static readonly Regex FillerRx = new(@"\b(?:please|could you|can you|would you|just)\b", RegexOptions.Compiled);

    private static readonly Regex NextRx = new(
        @"^(?:play |go to |skip to )?(?:the )?next(?: song| track| one)?$|^skip(?: song| track| this)?$",
        RegexOptions.Compiled);

    private static readonly Regex PrevRx = new(
        @"^(?:play |go to |go )?(?:the )?(?:previous|prev|last|back)(?: song| track| one)?$",
        RegexOptions.Compiled);

    private static readonly Regex PauseRx = new(
        @"^(?:stop|pause|halt|silence)(?: the)?(?: music| song| playing| playback)?$", RegexOptions.Compiled);

    private static readonly Regex ResumeRx = new(
        @"^(?:start|play|resume|continue|unpause)(?: the)?(?: music| song| playing| playback)?$", RegexOptions.Compiled);

    private static readonly Regex PlayRx = new(
        @"^(?:play|start|put on|listen to)\s+(?:the )?(?:song |track |music )?(.+)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Words = new()
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
        ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    /// <summary>Removes a leading "Fifu"; woke tells whether it was there.</summary>
    public static string StripWake(string raw, out bool woke)
    {
        var t = Regex.Replace(raw.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        var m = WakeRx.Match(t);
        woke = m.Success;
        return m.Success ? t.Substring(m.Length).Trim() : t;
    }

    public static VoiceCommand Parse(string raw)
    {
        var t = Regex.Replace(raw.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        t = WakeRx.Replace(t, "").Trim();
        t = Regex.Replace(FillerRx.Replace(t, " "), @"\s+", " ").Trim();
        if (t.Length == 0) return new VoiceCommand(VoiceKind.Unknown);

        if (Regex.IsMatch(t, @"\bun ?mute\b")) return new VoiceCommand(VoiceKind.Unmute);
        if (Regex.IsMatch(t, @"\bmute\b")) return new VoiceCommand(VoiceKind.Mute);

        if (Regex.IsMatch(t, @"\b(volume|louder|quieter|softer)\b"))
        {
            var up = Regex.IsMatch(t, @"\b(up|increase|raise|higher|louder)\b");
            var down = Regex.IsMatch(t, @"\b(down|decrease|lower|reduce|quieter|softer)\b");
            var number = ExtractNumber(t);

            if (up || down)
                return new VoiceCommand(up ? VoiceKind.VolumeUp : VoiceKind.VolumeDown, Number: number ?? 10);
            if (number is { } n)
                return new VoiceCommand(VoiceKind.VolumeSet, Number: n);
            if (Regex.IsMatch(t, @"\b(max|maximum|full)\b"))
                return new VoiceCommand(VoiceKind.VolumeSet, Number: 200);
        }

        var flag = FlagOf(t);
        if (t.Contains("shuffle") || Regex.IsMatch(t, @"\brandom\b"))
            return new VoiceCommand(VoiceKind.Shuffle, Flag: flag);
        if (Regex.IsMatch(t, @"\b(autoplay|auto play|repeat|loop)\b"))
            return new VoiceCommand(VoiceKind.Repeat, Flag: flag);

        if (NextRx.IsMatch(t)) return new VoiceCommand(VoiceKind.Next);
        if (PrevRx.IsMatch(t)) return new VoiceCommand(VoiceKind.Previous);
        if (PauseRx.IsMatch(t)) return new VoiceCommand(VoiceKind.Pause);
        if (ResumeRx.IsMatch(t)) return new VoiceCommand(VoiceKind.Resume);

        var play = PlayRx.Match(t);
        if (play.Success)
        {
            var title = Regex.Replace(play.Groups[1].Value, @"\bplease$", "").Trim();
            return title.Length > 0
                ? new VoiceCommand(VoiceKind.Play, title)
                : new VoiceCommand(VoiceKind.Resume);
        }

        // just a name: treat it as a song title
        return new VoiceCommand(VoiceKind.Play, t);
    }

    private static bool? FlagOf(string t)
    {
        if (Regex.IsMatch(t, @"\b(off|disable|stop|no|deactivate)\b")) return false;
        if (Regex.IsMatch(t, @"\b(on|enable|start|activate)\b")) return true;
        return null;
    }

    private static int? ExtractNumber(string t)
    {
        var m = Regex.Match(t, @"\b(\d{1,3})\b");
        if (m.Success) return int.Parse(m.Groups[1].Value);

        var current = 0;
        var found = false;
        foreach (var w in t.Split(' '))
        {
            if (Words.TryGetValue(w, out var v))
            {
                current += v;
                found = true;
            }
            else if (w == "hundred")
            {
                current = (current == 0 ? 1 : current) * 100;
                found = true;
            }
        }
        return found ? current : null;
    }
}