using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace SoundPanel.Services;

/// <summary>Reads the embedded cover picture (ID3 APIC / MP4 covr / FLAC picture) from an audio file.</summary>
public static class CoverService
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a frozen, downscaled bitmap or null when the file has no cover. Safe to call from any thread.</summary>
    public static BitmapSource? Load(string path, int decodeWidth)
    {
        var key = decodeWidth + "|" + path;
        if (Cache.TryGetValue(key, out var hit)) return hit;

        BitmapSource? image = null;
        try
        {
            using var file = TagLib.File.Create(path);
            var pictures = file.Tag.Pictures;
            var pic = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures.FirstOrDefault();

            if (pic != null && pic.Data.Count > 0)
            {
                using var ms = new MemoryStream(pic.Data.Data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                image = bmp;
            }
        }
        catch
        {
            // unreadable tag or unsupported picture: fall back to the note icon
        }

        Cache[key] = image;
        return image;
    }
}