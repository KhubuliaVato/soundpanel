using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoundPanel.Controls;
using SoundPanel.Services;
using SoundPanel.ViewModels;

namespace SoundPanel;

public partial class MiniPlayerWindow : Window
{
    private const double CollapsedWidth = 108;
    private const double ExpandedWidth = 420;
    private const double AutoCollapseSeconds = 3;

    private readonly MainViewModel _vm;
    private readonly Action _restore;
    private readonly DispatcherTimer _collapseTimer;
    private bool _expanded;

    public MiniPlayerWindow(MainViewModel vm, Action restore)
    {
        InitializeComponent();
        _vm = vm;
        _restore = restore;
        DataContext = vm;

        Action<FifuMood?> onMood = mood => MiniRobot.Mood = mood ?? FifuMood.Normal;
        Action<double> onLevel = level => MiniRobot.Level = level;
        FifuBus.MoodChanged += onMood;
        FifuBus.LevelChanged += onLevel;
        Closed += (_, _) =>
        {
            FifuBus.MoodChanged -= onMood;
            FifuBus.LevelChanged -= onLevel;
        };

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoCollapseSeconds) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver) SetExpanded(false);
        };

        MouseEnter += (_, _) => _collapseTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (_expanded) _collapseTimer.Start();
        };
    }

    public void ShowWidget()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Bottom - Height;

        SetExpanded(false, animate: false);
        if (!IsVisible) Show();
    }

    public void HideWidget()
    {
        _collapseTimer.Stop();
        Hide();
    }

    private void SetExpanded(bool expand, bool animate = true)
    {
        _expanded = expand;
        var width = expand ? ExpandedWidth : CollapsedWidth;
        var opacity = expand ? 1.0 : 0.0;
        Controls.IsHitTestVisible = expand;

        if (!animate)
        {
            Card.BeginAnimation(WidthProperty, null);
            Card.Width = width;
            Controls.BeginAnimation(OpacityProperty, null);
            Controls.Opacity = opacity;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(WidthProperty,
            new DoubleAnimation(width, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        Controls.BeginAnimation(OpacityProperty,
            new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(expand ? 240 : 120))
            {
                BeginTime = TimeSpan.FromMilliseconds(expand ? 140 : 0)
            });

        if (expand && !IsMouseOver) _collapseTimer.Start();
    }

    private void Robot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SetExpanded(!_expanded);
        e.Handled = true;
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _vm.Previous();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => _vm.TogglePlay();
    private void Next_Click(object sender, RoutedEventArgs e) => _vm.Next();
    private void Restore_Click(object sender, RoutedEventArgs e) => _restore();

    private void Waveform_SeekStarted(object? sender, EventArgs e) => _vm.IsSeeking = true;
    private void Waveform_SeekPreview(object? sender, double seconds) => _vm.PositionSeconds = seconds;

    private void Waveform_SeekCommitted(object? sender, double seconds)
    {
        _vm.SeekTo(seconds);
        _vm.IsSeeking = false;
    }
}