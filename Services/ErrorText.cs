namespace SoundPanel.Services;

/// <summary>Turns raw yt-dlp errors into short Georgian messages.</summary>
public static class ErrorText
{
    private static readonly (string[] Keys, string Text)[] Map =
    {
        (new[] { "private video" }, "ვიდეო პირადია. წვდომა არ გაქვს"),
        (new[] { "confirm your age", "age-restricted", "age restricted", "inappropriate for some users" },
            "ვიდეო 18+ა. ჩართე „Cookies ბრაუზერიდან“ და ბრაუზერში შესული იყავი"),
        (new[] { "not a bot" },
            "YouTube-მა დადასტურება მოითხოვა. ჩართე „Cookies ბრაუზერიდან“ ან განაახლე yt-dlp"),
        (new[] { "members-only", "members only", "join this channel" }, "ვიდეო მხოლოდ არხის წევრებისთვისაა"),
        (new[] { "not available in your country", "geo-restrict", "geo restrict", "blocked it in your country" },
            "ვიდეო შენს რეგიონში დაბლოკილია"),
        (new[] { "copyright" }, "ვიდეო საავტორო უფლების გამო დაბლოკილია"),
        (new[] { "video unavailable", "this video is not available", "has been removed", "no longer available", "has been terminated" },
            "ვიდეო მიუწვდომელია ან წაშლილია"),
        (new[] { "live event will begin", "premieres in", "this live event" }, "ეს ლაივია ან ჯერ არ დაწყებულა"),
        (new[] { "unsupported url" }, "ეს ბმული ან საიტი არ არის მხარდაჭერილი"),
        (new[] { "http error 403" }, "საიტმა მოთხოვნა უარყო (403). სცადე yt-dlp-ის განახლება"),
        (new[] { "http error 404" }, "გვერდი ვერ მოიძებნა (404). შეამოწმე ბმული"),
        (new[] { "http error 429", "too many requests" }, "ძალიან ბევრი მოთხოვნა (429). ცოტა ხანში სცადე"),
        (new[] { "getaddrinfo", "unable to download webpage", "timed out", "connection reset", "temporary failure",
                 "network is unreachable", "name or service not known" },
            "ინტერნეტი არ მუშაობს ან საიტი ვერ გაიხსნა"),
        (new[] { "requested format is not available" }, "ფორმატი მიუწვდომელია. სცადე სხვა ფორმატი ან ხარისხი"),
        (new[] { "failed to decrypt", "could not copy", "cookies" },
            "ბრაუზერის cookies ვერ ამოვიღე. დახურე ბრაუზერი ან აირჩიე Firefox"),
        (new[] { "no space left", "disk full" }, "დისკზე ადგილი არ არის"),
        (new[] { "permission denied" }, "საქაღალდეში ჩაწერა ვერ მოხერხდა"),
        (new[] { "ffmpeg", "ffprobe" }, "ffmpeg-მა შეცდომა დააბრუნა. სცადე თავიდან"),
    };

    public static string Friendly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "უცნობი შეცდომა";
        raw = raw.Trim();

        foreach (var (keys, text) in Map)
            foreach (var key in keys)
                if (raw.Contains(key, StringComparison.OrdinalIgnoreCase))
                    return text;

        return raw.Length > 240 ? raw[..240] + "…" : raw;
    }
}