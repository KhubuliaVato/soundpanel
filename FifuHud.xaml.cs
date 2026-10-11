using System.Windows;
using System.Windows.Threading;
using SoundPanel.Controls;

namespace SoundPanel;

/// <summary>Small bubble in the screen corner: shows what Fifu is doing while the app is minimized.</summary>
public partial class FifuHud : Window
{
    private readonly DispatcherTimer _hide = new();
    private DateTime _deadline;

    public FifuHud()
    {
        InitializeComponent();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
            Face.Level = 0;
        };
    }

    public void ShowState(FifuMood mood, string text)
    {
        _hide.Stop();
        Face.Mood = mood;
        Msg.Text = text;
        Place();
        if (!IsVisible) Show();
    }

    public void SetLevel(double level) => Face.Level = level;

    /// <summary>Hide after a while (never shortens a longer pending delay).</summary>
    public void HideSoon(int ms)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        if (_hide.IsEnabled && deadline < _deadline) return;

        _deadline = deadline;
        _hide.Interval = TimeSpan.FromMilliseconds(ms);
        _hide.Stop();
        _hide.Start();
    }

    private void Place()
    {
        if (Content is not FrameworkElement root) return;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var area = SystemParameters.WorkArea;
        Left = area.Right - root.DesiredSize.Width - 8;
        Top = area.Bottom - root.DesiredSize.Height - 8;
    }
}