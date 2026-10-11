using SoundPanel.ViewModels;

namespace SoundPanel.Models;

/// <summary>
/// A "component": either the whole library (IsAll) or a sub-folder of the main music folder.
/// </summary>
public sealed class TrackGroup : ObservableObject
{
    private int _count;
    private bool _isDropTarget;

    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public bool IsAll { get; init; }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }
}
