using SoundPanel.Controls;

namespace SoundPanel.Services;

/// <summary>Lets any window (main, mini widget) show Fifu's current voice mood.</summary>
public static class FifuBus
{
    /// <summary>null = back to the normal face.</summary>
    public static event Action<FifuMood?>? MoodChanged;
    public static event Action<double>? LevelChanged;

    public static void SetMood(FifuMood? mood) => MoodChanged?.Invoke(mood);
    public static void SetLevel(double level) => LevelChanged?.Invoke(level);
}