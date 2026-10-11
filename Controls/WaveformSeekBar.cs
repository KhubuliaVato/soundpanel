using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoundPanel.Services;

namespace SoundPanel.Controls;

/// <summary>
/// Mirrored-bar waveform that doubles as the seek bar: the played part is filled with the accent
/// gradient, hovering previews the target position, click/drag seeks. Bars sweep in when peaks arrive.
/// </summary>
public sealed class WaveformSeekBar : FrameworkElement
{
    private const double BarWidth = 3;
    private const double BarGap = 2;
    private const double MinBarHeight = 3;

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformSeekBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPeaksChanged));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(WaveformSeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(WaveformSeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(WaveformSeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverBlendProperty = DependencyProperty.Register(
        nameof(HoverBlend), typeof(double), typeof(WaveformSeekBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _dragging;
    private double _dragSeconds;
    private double _hoverX = -1;

    private LinearGradientBrush? _playedBrush;
    private double _brushWidth;

    public WaveformSeekBar()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        ClipToBounds = true;
        ThemeService.AccentChanged += (_, _) =>
        {
            _playedBrush = null; // rebuilt with the new colors on the next render
            InvalidateVisual();
        };
    }

    /// <summary>Raised when the user presses the bar (stop the playback timer from overwriting the position).</summary>
    public event EventHandler? SeekStarted;

    /// <summary>Raised while the user drags / on press, with the target position in seconds.</summary>
    public event EventHandler<double>? SeekPreview;

    /// <summary>Raised on release with the final position in seconds.</summary>
    public event EventHandler<double>? SeekCommitted;

    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double Reveal
    {
        get => (double)GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    /// <summary>0..1, animated; how strongly the hover preview is shown.</summary>
    public double HoverBlend
    {
        get => (double)GetValue(HoverBlendProperty);
        set => SetValue(HoverBlendProperty, value);
    }

    private static void OnPeaksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (WaveformSeekBar)d;
        if (e.NewValue is float[])
        {
            bar.BeginAnimation(RevealProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        else
        {
            bar.BeginAnimation(RevealProperty, null);
            bar.Reveal = 0;
        }
    }

    // ------------------------------------------------------------ input

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        BeginAnimation(HoverBlendProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = -1;
        BeginAnimation(HoverBlendProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0) return;

        _dragging = true;
        CaptureMouse();
        SeekStarted?.Invoke(this, EventArgs.Empty);
        UpdateDrag(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hoverX = e.GetPosition(this).X;
        if (_dragging) UpdateDrag(_hoverX);
        else InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        SeekCommitted?.Invoke(this, _dragSeconds);
        InvalidateVisual();
        e.Handled = true;
    }

    private void UpdateDrag(double x)
    {
        var w = Math.Max(1, ActualWidth);
        _dragSeconds = Math.Clamp(x / w, 0, 1) * Duration;
        SeekPreview?.Invoke(this, _dragSeconds);
        InvalidateVisual();
    }

    // ------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 6) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test area

        EnsureBrush(w);

        var peaks = Peaks;
        var step = BarWidth + BarGap;
        var count = Math.Max(1, (int)(w / step));
        var offsetX = (w - count * step + BarGap) / 2;
        var mid = h / 2;
        var maxHalf = h / 2 - 1;

        var seconds = _dragging ? _dragSeconds : Position;
        var progressX = Duration > 0 ? Math.Clamp(seconds / Duration, 0, 1) * w : 0;

        var hovering = _hoverX >= 0 && HoverBlend > 0.01 && Duration > 0;
        var hoverX = hovering ? Math.Clamp(_hoverX, 0, w) : -1;

        var unplayed = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        var preview = new SolidColorBrush(Color.FromArgb((byte)(0x70 * Math.Clamp(HoverBlend, 0, 1)), 0xC4, 0xB5, 0xFF));
        unplayed.Freeze();
        preview.Freeze();

        var reveal = Reveal;
        var radius = BarWidth / 2;

        for (var i = 0; i < count; i++)
        {
            var x = offsetX + i * step;
            var centerX = x + BarWidth / 2;
            var t = count > 1 ? i / (double)(count - 1) : 0;

            double amp;
            if (peaks is { Length: > 0 })
            {
                // max over the slice of peaks that belongs to this bar
                var a = (int)(t * (peaks.Length - 1));
                var b = Math.Min(peaks.Length - 1, (int)Math.Ceiling((i + 1) / (double)count * (peaks.Length - 1)));
                float m = 0;
                for (var p = a; p <= Math.Max(a, b); p++)
                    if (peaks[p] > m) m = peaks[p];
                amp = m;

                // sweep-in: left bars finish first
                var local = Math.Clamp(reveal * 1.6 - t * 0.6, 0, 1);
                var eased = 1 - Math.Pow(1 - local, 3);
                amp *= eased;
            }
            else
            {
                amp = 0.04; // idle: dotted line
            }

            var half = Math.Max(MinBarHeight / 2, amp * maxHalf);
            var rect = new Rect(x, mid - half, BarWidth, half * 2);

            Brush brush;
            if (centerX <= progressX) brush = _playedBrush!;
            else if (hovering && centerX <= hoverX) brush = preview;
            else brush = unplayed;

            dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
        }

        // playhead
        if (Duration > 0)
        {
            var accentB = ThemeService.Accent.B;
            var glow = new SolidColorBrush(Color.FromArgb(0x40, accentB.R, accentB.G, accentB.B));
            dc.DrawRoundedRectangle(glow, null, new Rect(progressX - 3, 0, 6, h), 3, 3);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(progressX - 1, 1, 2, h - 2), 1, 1);
        }

        // hover marker
        if (hovering)
        {
            var line = new SolidColorBrush(Color.FromArgb((byte)(0x80 * Math.Clamp(HoverBlend, 0, 1)), 0xFF, 0xFF, 0xFF));
            dc.DrawRectangle(line, null, new Rect(hoverX - 0.5, 4, 1, h - 8));
        }
    }

    private void EnsureBrush(double width)
    {
        if (_playedBrush != null && Math.Abs(_brushWidth - width) < 0.5) return;

        _playedBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(width, 0),
            GradientStops =
            {
                new GradientStop(ThemeService.Accent.A, 0),
                new GradientStop(ThemeService.Mix(ThemeService.Accent.A, ThemeService.Accent.B, 0.5), 0.5),
                new GradientStop(ThemeService.Accent.B, 1)
            }
        };
        _playedBrush.Freeze();
        _brushWidth = width;
    }
}
