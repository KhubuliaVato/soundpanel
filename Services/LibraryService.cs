using System.IO;
using NAudio.Wave;

namespace SoundPanel.Services;

public static class LibraryService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".aif"
    };

    public static bool IsAudio(string path) =>
        File.Exists(path) && AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>Audio files directly inside the folder (not recursive).</summary>
    public static IEnumerable<string> GetTrackFiles(string folder)
    {
        if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    public static int CountTracks(string folder) => GetTrackFiles(folder).Count();

    public static IEnumerable<string> GetGroupFolders(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    /// <summary>Creates the group folder inside the root. Returns an error text or null on success.</summary>
    public static string? CreateGroup(string root, string rawName, out string? folderPath)
    {
        folderPath = null;
        var name = string.Concat(rawName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim('.', ' ');
        if (name.Length == 0) return "სახელი ცარიელია";

        var path = Path.Combine(root, name);
        if (Directory.Exists(path)) return "ასეთი კომპონენტი უკვე არსებობს";

        try
        {
            Directory.CreateDirectory(path);
            folderPath = path;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Copies a file into the target folder. Returns true if copied, false if skipped (already there).</summary>
    public static bool CopyInto(string targetFolder, string sourceFile)
    {
        var dest = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
        if (string.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return false;
        if (File.Exists(dest)) return false;
        File.Copy(sourceFile, dest);
        return true;
    }

    public static TimeSpan? ReadDuration(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime;
        }
        catch { return null; }
    }
}
