using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoundPanel.Models;
using SoundPanel.ViewModels;

namespace SoundPanel;

public partial class MainWindow : Window
{
    private const string TrackFormat = "SoundPanel.Tracks";

    private readonly MainViewModel _vm;

    private Point _dragStart;
    private Track? _dragCandidate;
    private TrackGroup? _hoverGroup;
    private int _toastVersion;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.Notify += ShowToast;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SettingsPane.Close();
                DownloadPane.Close();
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        };
        _downloader = new DownloaderViewModel(_vm);
        DownloadPane.DataContext = _downloader;
        InitVoice();
        InitFifu();
        InitHotkey();
        InitFx();
        SettingsPane.DownloaderRequested += (_, _) => OpenDownloader();
        InitPages();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.RootFolder)) _downloader.NotifyLibraryChanged();
        };
        _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyWindowMode();
    }

    // ------------------------------------------------------------ window chrome

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private MiniPlayerWindow? _mini;
    private DownloaderViewModel _downloader = null!;

    private bool _fsActive;
    private Rect _normalBounds;
    private WindowState _stateBeforeFs;

    

    private void OpenDownloader()
    {
        SettingsPane.Close();
        DownloadPane.Open();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFullscreen) or nameof(MainViewModel.AlwaysOnTop))
            ApplyWindowMode();
    }

    private void ApplyWindowMode()
    {
        var wantFullscreen = _vm.IsFullscreen;

        if (wantFullscreen && !_fsActive)
        {
            _fsActive = true;
            _stateBeforeFs = WindowState;
            _normalBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        else if (!wantFullscreen && _fsActive)
        {
            _fsActive = false;
            ResizeMode = ResizeMode.CanResize;
            Left = _normalBounds.Left;
            Top = _normalBounds.Top;
            Width = _normalBounds.Width;
            Height = _normalBounds.Height;
            if (_stateBeforeFs == WindowState.Maximized) WindowState = WindowState.Maximized;
        }

        Topmost = _fsActive || _vm.AlwaysOnTop;
    }

    private void OnWindowStateChanged()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        if (WindowState == WindowState.Minimized && _vm.MiniOnMinimize)
        {
            _mini ??= new MiniPlayerWindow(_vm, RestoreFromMini);
            _mini.ShowWidget();
        }
        else
        {
            _mini?.HideWidget();
        }
    }

    private void RestoreFromMini()
    {
        SystemCommands.RestoreWindow(this); // back to the previous state (normal or maximized)
        Activate();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e) =>
        OnWindowStateChanged();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _vm.Dispose();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            _vm.IsFullscreen = !_vm.IsFullscreen;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && (SettingsPane.IsOpen || DownloadPane.IsOpen || _vm.IsFullscreen))
        {
            if (DownloadPane.IsOpen) DownloadPane.Close();
else if (SettingsPane.IsOpen) SettingsPane.Close();
            else _vm.IsFullscreen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox)
        {
            _vm.TogglePlay();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ buttons

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "აირჩიე საქაღალდე მუსიკით" };
        if (_vm.HasRoot) dialog.InitialDirectory = _vm.RootFolder;
        if (dialog.ShowDialog(this) == true)
            _vm.SetRootFolder(dialog.FolderName);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var error = _vm.CreateGroup(dialog.GroupName);
        ShowToast(error ?? $"კომპონენტი „{dialog.GroupName.Trim()}“ შეიქმნა");
    }

    private void VolumeKnob_Clicked(object? sender, EventArgs e) => _vm.ToggleMute();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.SearchText = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _vm.PlayFirstMatch();
            e.Handled = true;
        }
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        _vm.SearchText = "";
        SearchBox.Focus();
    }

    // ------------------------------------------------------------ waveform seek

    private void Waveform_SeekStarted(object? sender, EventArgs e) => _vm.IsSeeking = true;

    private void Waveform_SeekPreview(object? sender, double seconds) => _vm.PositionSeconds = seconds;

    private void Waveform_SeekCommitted(object? sender, double seconds)
    {
        _vm.SeekTo(seconds);
        _vm.IsSeeking = false;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _vm.TogglePlay();
    private void Next_Click(object sender, RoutedEventArgs e) => _vm.Next();
    private void Prev_Click(object sender, RoutedEventArgs e) => _vm.Previous();

    // ------------------------------------------------------------ track list: play + drag source

    private void TracksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is Track track)
            _vm.PlayTrack(track);
    }

    private void TracksList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Track;
    }

    private void TracksList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _dragCandidate = null;

    private void TracksList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var picked = new List<Track> { _dragCandidate };
        _dragCandidate = null;

        DragDrop.DoDragDrop(TracksList, new DataObject(TrackFormat, picked), DragDropEffects.Copy);
        SetHover(null);
    }

    // ------------------------------------------------------------ track list: accept files from Explorer

    private void TracksList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _vm.SelectedGroup != null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TracksList_Drop(object sender, DragEventArgs e)
    {
        if (_vm.SelectedGroup == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var (added, skipped) = _vm.AddFiles(_vm.SelectedGroup, files);
        ToastForAdd(_vm.SelectedGroup, added, skipped);
        e.Handled = true;
    }

    // ------------------------------------------------------------ sidebar: drop targets

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        // Clear the highlight as soon as the cursor leaves the component list.
        if (GroupAt(e.OriginalSource) == null) SetHover(null);
    }

    private void GroupsList_DragOver(object sender, DragEventArgs e)
    {
        var group = GroupAt(e.OriginalSource);
        var internalDrag = e.Data.GetDataPresent(TrackFormat);

        var ok = group != null &&
                 (internalDrag ? !group.IsAll : e.Data.GetDataPresent(DataFormats.FileDrop));

        SetHover(ok ? group : null);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupsList_Drop(object sender, DragEventArgs e)
    {
        var group = GroupAt(e.OriginalSource);
        SetHover(null);
        if (group == null) return;

        IEnumerable<string>? files = null;
        if (e.Data.GetData(TrackFormat) is List<Track> tracks)
        {
            if (!group.IsAll) files = tracks.Select(t => t.FilePath).ToList();
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            files = dropped;
        }

        if (files == null) return;

        var (added, skipped) = _vm.AddFiles(group, files);
        ToastForAdd(group, added, skipped);
        e.Handled = true;
    }

    private TrackGroup? GroupAt(object? source) =>
        FindAncestor<ListBoxItem>(source as DependencyObject)?.DataContext as TrackGroup;

    private void SetHover(TrackGroup? group)
    {
        if (ReferenceEquals(_hoverGroup, group)) return;
        if (_hoverGroup != null) _hoverGroup.IsDropTarget = false;
        _hoverGroup = group;
        if (group != null) group.IsDropTarget = true;
    }

    // ------------------------------------------------------------ toast

    private void ToastForAdd(TrackGroup group, int added, int skipped)
    {
        if (added == 0 && skipped == 0) return;

        var text = added > 0
            ? $"{added} სიმღერა დაემატა → {group.Name}"
            : $"უკვე არსებობს: {group.Name}";
        if (added > 0 && skipped > 0) text += $"  ({skipped} უკვე იყო)";
        ShowToast(text);
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        var version = ++_toastVersion;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        Toast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        ToastTr.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });

        var hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (version != _toastVersion) return; // a newer toast is showing
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)));
            ToastTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, TimeSpan.FromMilliseconds(380)));
        };
        hideTimer.Start();
    }

    // ------------------------------------------------------------ helpers

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T match) return match;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
