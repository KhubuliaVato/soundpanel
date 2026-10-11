using System.IO;
using NAudio.Wave;

namespace SoundPanel.Services;

/// <summary>Which microphone Fifu listens to (remembered between runs).</summary>
public static class VoiceMic
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "1Flow", "SoundPanel", "tools", "mic.txt");

    private static string? _selected;

    public static string Selected
    {
        get
        {
            if (_selected == null)
            {
                try { _selected = File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : ""; }
                catch { _selected = ""; }
            }
            return _selected;
        }
        set
        {
            _selected = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, value);
            }
            catch { /* not critical */ }
        }
    }

    private static readonly string HandsFreeFile = Path.Combine(Path.GetDirectoryName(FilePath)!, "handsfree.txt");

    /// <summary>"Fifu" wake-word listening, remembered between runs.</summary>
    public static bool HandsFree
    {
        get
        {
            try { return File.Exists(HandsFreeFile) && File.ReadAllText(HandsFreeFile).Trim() == "1"; }
            catch { return false; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HandsFreeFile)!);
                File.WriteAllText(HandsFreeFile, value ? "1" : "0");
            }
            catch { /* not critical */ }
        }
    }

    public static List<string> Devices()
    {
        var list = new List<string>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            list.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return list;
    }

    /// <summary>-1 = Windows default.</summary>
    public static int DeviceNumber()
    {
        if (Selected.Length == 0) return -1;
        var index = Devices().IndexOf(Selected);
        return index < 0 ? -1 : index;
    }
}