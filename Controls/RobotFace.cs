using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SoundPanel.Services;

namespace SoundPanel.Controls;

public enum FifuMood { Normal, Working, Happy, Error, Loud, Roar, Sleepy, Listening }

/// <summary>
/// Fifu, the 1Flow robot: neon squircle head, dark visor, pill eyes, two-stripe emblem.
/// Idle: darting eyes, blinking, follows the mouse. Playing: happy eyes, head bobs with the loudness.
/// Moods (set by the window): Working, Happy, Error, Loud, Roar, Sleepy.
/// </summary>
public sealed class RobotFace : FrameworkElement
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(RobotFace), new PropertyMetadata(false));

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(RobotFace), new PropertyMetadata(null));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(RobotFace), new PropertyMetadata(0.0));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(RobotFace), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(FifuMood), typeof(RobotFace), new PropertyMetadata(FifuMood.Normal));

    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public float[]? Peaks { get => (float[]?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public FifuMood Mood { get => (FifuMood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(RobotFace), new PropertyMetadata(0.0));

    /// <summary>Microphone loudness 0..1 while Fifu is listening.</summary>
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private static readonly Pen ListenPen = MakePen(Solid(0xFF, 255, 255, 255), 1.4);

    private void DrawListenRings(DrawingContext dc)
    {
        for (var i = 0; i < 3; i++)
        {
            var t = (_time * 0.8 + i / 3.0) % 1.0;
            var scale = 1.0 + t * (0.28 + 0.2 * Level);

            dc.PushOpacity(Math.Clamp((1 - t) * (0.35 + 0.65 * Level), 0, 1));
            dc.PushTransform(new TranslateTransform(48, 49));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(null, ListenPen, OuterHead);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }
    }

    // ------------------------------------------------------------ frozen drawing resources

    private static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static StreamGeometry Build(Action<StreamGeometryContext> draw)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) draw(c);
        g.Freeze();
        return g;
    }

    /// <summary>Superellipse centred on the origin (n = 3 gives the soft "squircle" of the logo).</summary>
    private static StreamGeometry Squircle(double a, double b, double n = 3.0)
    {
        const int steps = 120;
        return Build(c =>
        {
            for (var i = 0; i < steps; i++)
            {
                var t = i * 2 * Math.PI / steps;
                var cos = Math.Cos(t);
                var sin = Math.Sin(t);
                var p = new Point(
                    a * Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2.0 / n),
                    b * Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2.0 / n));

                if (i == 0) c.BeginFigure(p, true, true);
                else c.LineTo(p, true, true);
            }
        });
    }

    private static readonly StreamGeometry OuterHead = Squircle(37, 29);
    private static readonly StreamGeometry InnerVisor = Squircle(30, 22);

    private static readonly Pen RimPen = MakePen(Frozen(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0xB0, 255, 255, 255), 0.00),
            new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.45),
            new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.80),
            new GradientStop(Color.FromArgb(0x60, 0xE0, 0xB0, 0xFF), 1.00)
        }
    }), 1.1);

    private static readonly Brush VisorBrush = Frozen(new RadialGradientBrush(
        Color.FromRgb(0x08, 0x0D, 0x34), Color.FromRgb(0x01, 0x03, 0x16)));

    private static readonly Brush EyeShine = Solid(0xB0, 255, 255, 255);
    private static readonly Brush MouthFill = Solid(0xFF, 0x01, 0x03, 0x16);
    private static readonly Brush SweatBrush = Solid(0xC8, 0x9A, 0xE0, 0xFF);
    private static readonly Pen ErrorPen = MakePen(Solid(0xFF, 0xFF, 0x6B, 0x81), 2.6);

    private static readonly StreamGeometry HappyArc = Build(c =>
    {
        c.BeginFigure(new Point(-4.4, 2.6), false, false);
        c.QuadraticBezierTo(new Point(0, -5.8), new Point(4.4, 2.6), true, false);
    });

    // 270 degree arc: the "loading" eye
    private static readonly StreamGeometry SpinnerArc = Build(c =>
    {
        c.BeginFigure(new Point(4.6, 0), false, false);
        c.ArcTo(new Point(0, -4.6), new Size(4.6, 4.6), 0, true, SweepDirection.Clockwise, true, false);
    });

    private static readonly StreamGeometry XMark = Build(c =>
    {
        c.BeginFigure(new Point(-3.4, -3.4), false, false);
        c.LineTo(new Point(3.4, 3.4), true, false);
        c.BeginFigure(new Point(3.4, -3.4), false, false);
        c.LineTo(new Point(-3.4, 3.4), true, false);
    });

    private static readonly StreamGeometry FlatLine = Build(c =>
    {
        c.BeginFigure(new Point(-4.6, 0), false, false);
        c.LineTo(new Point(4.6, 0), true, false);
    });

    private static readonly StreamGeometry SleepArc = Build(c =>
    {
        c.BeginFigure(new Point(-4.4, -1.2), false, false);
        c.QuadraticBezierTo(new Point(0, 4.2), new Point(4.4, -1.2), true, false);
    });

    // ------------------------------------------------------------ theme palette

    private Palette _pal = new(ThemeService.Robot);

    /// <summary>Colors of the current robot theme (rebuilt whenever the theme changes).</summary>
    private sealed class Palette
    {
        public readonly Brush Ring;
        public readonly Pen[] GlowPens;
        public readonly Brush Eye;
        public readonly Brush EyeGlow;
        public readonly Pen Happy;
        public readonly Pen Smile;
        public readonly Brush Bar1;
        public readonly Brush Bar2;
        public readonly Brush Shadow;

        public Palette(RobotTheme t)
        {
            Ring = Frozen(new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(t.Light, 0.00),
                    new GradientStop(t.Mid, 0.30),
                    new GradientStop(t.Deep, 0.68),
                    new GradientStop(t.Tail, 1.00)
                }
            });

            GlowPens = new[]
            {
                MakePen(Alpha(t.Mid, 0x0E), 15),
                MakePen(Alpha(t.Mid, 0x16), 10),
                MakePen(Alpha(t.Mid, 0x24), 6),
                MakePen(Alpha(t.Mid, 0x38), 3)
            };

            Eye = Frozen(new LinearGradientBrush(t.Eye, ThemeService.Mix(t.Eye, t.Mid, 0.55), 90));
            EyeGlow = Frozen(new RadialGradientBrush(
                Color.FromArgb(0x60, t.Eye.R, t.Eye.G, t.Eye.B), Color.FromArgb(0, t.Eye.R, t.Eye.G, t.Eye.B)));
            Happy = MakePen(Frozen(new SolidColorBrush(t.Eye)), 2.6);
            Smile = MakePen(Frozen(new SolidColorBrush(ThemeService.Mix(t.Eye, t.Mid, 0.4))), 1.7);

            Bar1 = Frozen(new LinearGradientBrush(t.Light, t.Mid, 0));
            Bar2 = Frozen(new LinearGradientBrush(t.Mid, t.Tail, 0));
            Shadow = Alpha(t.Mid, 0x30);
        }

        private static SolidColorBrush Alpha(Color c, byte a) =>
            Frozen(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
    }

    private void OnRobotChanged(object? sender, EventArgs e) => _pal = new Palette(ThemeService.Robot);

    // ------------------------------------------------------------ animation state

    private readonly Random _rng = new();
    private bool _hooked;
    private TimeSpan _last;
    private double _time;
    private double _phase;
    private double _playBlend;
    private double _energy = 0.5;
    private Point _gaze;
    private Point _gazeTarget;
    private double _nextGaze = 0.6;
    private double _blink = -1;
    private double _nextBlink = 1.5;
    private FormattedText? _noteA;
    private FormattedText? _noteB;
    private FormattedText? _starA;
    private FormattedText? _starB;
    private FormattedText? _zSmall;
    private FormattedText? _zBig;

    public RobotFace()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Hook();
            else Unhook();
        };
    }

    private void Hook()
    {
        if (_hooked || !IsVisible) return;
        _hooked = true;
        _last = TimeSpan.Zero;
        _pal = new Palette(ThemeService.Robot);
        ThemeService.RobotChanged += OnRobotChanged;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        ThemeService.RobotChanged -= OnRobotChanged;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = (now - _last).TotalSeconds;
        _last = now;
        if (dt <= 0) return;
        if (dt > 0.1) dt = 1.0 / 60;

        Step(dt);
        InvalidateVisual();
    }

    private void Step(double dt)
    {
        _time += dt;
        var playing = IsPlaying;

        _playBlend += ((playing ? 1.0 : 0.0) - _playBlend) * (1 - Math.Exp(-dt * 5));
        if (playing) _phase += dt * 6.2;

        // loudness of the current moment drives how strongly the head moves
        var target = 0.5;
        var peaks = Peaks;
        if (playing && peaks is { Length: > 0 } && Duration > 0)
        {
            var idx = (int)Math.Clamp(Position / Duration * (peaks.Length - 1), 0, peaks.Length - 1);
            target = Math.Max(0.25, peaks[idx]);
        }
        _energy += (target - _energy) * (1 - Math.Exp(-dt * 9));

        // gaze
        Point wanted;
        if (IsMouseOver)
        {
            var p = Mouse.GetPosition(this);
            var dx = (p.X - ActualWidth / 2) / Math.Max(1, ActualWidth / 2);
            var dy = (p.Y - ActualHeight / 2) / Math.Max(1, ActualHeight / 2);
            wanted = new Point(Math.Clamp(dx, -1, 1), Math.Clamp(dy, -1, 1));
        }
        else
        {
            if (_time >= _nextGaze)
            {
                _gazeTarget = _rng.NextDouble() < 0.25
                    ? new Point(0, 0)
                    : new Point(_rng.NextDouble() * 2 - 1, (_rng.NextDouble() * 2 - 1) * 0.7);
                _nextGaze = _time + 0.5 + _rng.NextDouble() * 2.0;
            }
            wanted = _gazeTarget;
        }

        var k = 1 - Math.Exp(-dt * 14);
        _gaze = new Point(_gaze.X + (wanted.X - _gaze.X) * k, _gaze.Y + (wanted.Y - _gaze.Y) * k);

        // blink
        if (_blink < 0 && _time >= _nextBlink) _blink = 0;
        if (_blink >= 0)
        {
            _blink += dt / 0.16;
            if (_blink >= 1)
            {
                _blink = -1;
                _nextBlink = _time + (_rng.NextDouble() < 0.2 ? 0.25 : 1.8 + _rng.NextDouble() * 3.5);
            }
        }
    }

    // ------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 8 || h < 8) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test area

        var s = Math.Min(w, h) / 96.0;
        dc.PushTransform(new TranslateTransform((w - 96 * s) / 2, (h - 96 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        DrawRobot(dc);
        dc.Pop();
        dc.Pop();
    }

    private void DrawRobot(DrawingContext dc)
    {
        var mood = Mood;
        var pb = _playBlend;
        var bounce = Math.Abs(Math.Sin(_phase)) * (1.5 + 3.5 * _energy) * pb;
        var sway = Math.Sin(_phase) * (2.0 + 4.0 * _energy) * pb;
        var breathe = Math.Sin(_time * 1.7) * 0.7 * (1 - pb);
        var jitterX = 0.0;

        switch (mood)
        {
            case FifuMood.Happy:
                bounce += Math.Abs(Math.Sin(_time * 9)) * 5;
                sway += Math.Sin(_time * 9) * 3;
                break;
            case FifuMood.Error:
                sway += Math.Sin(_time * 34) * 3.2;
                break;
            case FifuMood.Roar:
                jitterX = (_rng.NextDouble() - 0.5) * 2.4;
                sway += (_rng.NextDouble() - 0.5) * 3;
                break;
            case FifuMood.Sleepy:
                sway += 6 + Math.Sin(_time * 1.1) * 2;
                breathe += Math.Sin(_time * 1.1) * 1.6;
                break;
            case FifuMood.Working:
                sway += Math.Sin(_time * 3.0) * 1.5;
                break;
        }

        // soft floor glow, shrinks while the head is up
        dc.DrawEllipse(_pal.Shadow, null, new Point(48, 88),
            Math.Max(8, 24 - bounce * 1.2), Math.Max(1.5, 3 - bounce * 0.25));

        dc.PushTransform(new TranslateTransform(jitterX, breathe - bounce));
        dc.PushTransform(new RotateTransform(sway, 48, 52));

        // ---- head (drawn around its own centre)
        dc.PushTransform(new TranslateTransform(48, 49));

        var glow = Math.Clamp(0.65 + 0.35 * Math.Sin(_time * 2.0) * (1 - pb) + 0.45 * _energy * pb, 0, 1);
        if (mood == FifuMood.Roar) glow = 1;
        dc.PushOpacity(glow);
        foreach (var pen in _pal.GlowPens) dc.DrawGeometry(null, pen, OuterHead);
        dc.Pop();

        dc.DrawGeometry(_pal.Ring, RimPen, OuterHead);
        dc.DrawGeometry(VisorBrush, null, InnerVisor);

        dc.Pop();

        // ---- face (shifts slightly with the gaze for a 3D feel)
        var gx = _gaze.X;
        var gy = _gaze.Y;

        // emblem: two slanted stripes
        var emblemScale = 1 + 0.12 * pb * _energy * (0.5 + 0.5 * Math.Sin(_phase * 2));
        dc.PushTransform(new TranslateTransform(48 + gx * 1.4, 37.6 + gy * 1.0));
        dc.PushTransform(new ScaleTransform(emblemScale, emblemScale));
        dc.PushTransform(new RotateTransform(-27));
        dc.DrawRoundedRectangle(_pal.Bar1, null, new Rect(-7.0, -5.4, 14.0, 4.6), 1.8, 1.8);
        dc.DrawRoundedRectangle(_pal.Bar2, null, new Rect(-3.6, 0.6, 11.0, 4.6), 1.8, 1.8);
        dc.Pop();
        dc.Pop();
        dc.Pop();

        // eyes
        var open = _blink < 0 ? 1.0 : 1 - Math.Sin(Math.PI * _blink) * 0.92;
        for (var side = -1; side <= 1; side += 2)
        {
            var cx = 48 + side * 14.6 + gx * 3.2;
            var cy = 54.7 + gy * 2.4;

            dc.DrawEllipse(_pal.EyeGlow, null, new Point(cx, cy), 10, 11);

            if (DrawMoodEye(dc, side, cx, cy, gx)) continue;

            if (pb < 0.98)
            {
                dc.PushOpacity(1 - pb);
                var hh = 6.4 * open;
                var r = Math.Min(3.7, hh);
                dc.DrawRoundedRectangle(_pal.Eye, null, new Rect(cx - 3.7, cy - hh, 7.4, hh * 2), r, r);
                if (open > 0.5) dc.DrawEllipse(EyeShine, null, new Point(cx - 1.0, cy - 3.6), 1.1, 1.1);
                dc.Pop();
            }

            if (pb > 0.02)
            {
                dc.PushOpacity(pb);
                dc.PushTransform(new TranslateTransform(cx, cy + 1.2));
                dc.DrawGeometry(null, _pal.Happy, HappyArc);
                dc.Pop();
                dc.Pop();
            }
        }

        // mouth
        var sx = 48 + gx * 1.8;
        var sy = 62.5 + gy * 1.2;
        if (!DrawMoodMouth(dc, sx, sy))
        {
            var smile = 3.0 + pb * (2.2 + _energy * 1.8);
            var mouth = Build0(c =>
            {
                c.BeginFigure(new Point(sx - 3.9, sy), false, false);
                c.QuadraticBezierTo(new Point(sx, sy + smile), new Point(sx + 3.9, sy), true, false);
            });
            dc.DrawGeometry(null, _pal.Smile, mouth);
        }

        dc.Pop();
        dc.Pop();

        // ---- things floating around the head
        switch (mood)
        {
            case FifuMood.Happy:
                _starA ??= MakeText("\u2726", Color.FromRgb(0xFF, 0xE2, 0x7A), 13);
                _starB ??= MakeText("\u2727", Color.FromRgb(0xFF, 0xFF, 0xFF), 12);
                DrawFloaters(dc, 1.0, _starA, _starB, 0.9);
                break;
            case FifuMood.Sleepy:
                _zSmall ??= MakeText("z", Color.FromRgb(0xB9, 0xC6, 0xFF), 11, true);
                _zBig ??= MakeText("Z", Color.FromRgb(0xB9, 0xC6, 0xFF), 15, true);
                DrawSleepZ(dc);
                break;
            case FifuMood.Listening:
                DrawListenRings(dc);
                break;
            case FifuMood.Loud:
                DrawSweat(dc, 76, 36);
                break;
            case FifuMood.Roar:
                DrawSweat(dc, 76, 36);
                DrawSweat(dc, 20, 38);
                break;
            default:
                if (pb > 0.05)
                {
                    _noteA ??= MakeText("\u266A", Color.FromRgb(0x7F, 0xEF, 0xFF), 13);
                    _noteB ??= MakeText("\u266B", Color.FromRgb(0xB9, 0x9C, 0xFF), 13);
                    DrawFloaters(dc, pb, _noteA, _noteB, 0.45);
                }
                break;
        }
    }

    private static StreamGeometry Build0(Action<StreamGeometryContext> draw)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) draw(c);
        return g;
    }

    // ------------------------------------------------------------ moods

    private bool DrawMoodEye(DrawingContext dc, int side, double cx, double cy, double gx)
    {
        switch (Mood)
        {
            case FifuMood.Working:
                dc.PushTransform(new TranslateTransform(cx, cy + 0.6));
                dc.PushTransform(new RotateTransform(_time * 420 * (side < 0 ? 1 : -1)));
                dc.DrawGeometry(null, _pal.Happy, SpinnerArc);
                dc.Pop();
                dc.Pop();
                return true;

            case FifuMood.Error:
                dc.PushTransform(new TranslateTransform(cx, cy + 0.6));
                dc.DrawGeometry(null, ErrorPen, XMark);
                dc.Pop();
                return true;

            case FifuMood.Happy:
                dc.PushTransform(new TranslateTransform(cx, cy + 1.2));
                dc.DrawGeometry(null, _pal.Happy, HappyArc);
                dc.Pop();
                return true;

            case FifuMood.Loud:
                dc.PushTransform(new TranslateTransform(cx, cy + 1));
                dc.PushTransform(new RotateTransform(side * 9));
                dc.DrawGeometry(null, _pal.Happy, FlatLine);
                dc.Pop();
                dc.Pop();
                return true;

            case FifuMood.Listening:
            {
                var pulse = 1 + 0.35 * Level;
                dc.DrawEllipse(null, _pal.Happy, new Point(cx, cy), 4.6 * pulse, 5.8 * pulse);
                dc.DrawEllipse(_pal.Eye, null, new Point(cx + gx * 1.2, cy), 2.0, 2.0);
                return true;
            }

            case FifuMood.Roar:
                dc.DrawEllipse(null, _pal.Happy, new Point(cx, cy), 4.4, 5.6);
                dc.DrawEllipse(_pal.Eye, null, new Point(cx + gx * 1.2, cy), 1.5, 1.5);
                return true;

            case FifuMood.Sleepy:
                dc.PushTransform(new TranslateTransform(cx, cy + 1.4));
                dc.DrawGeometry(null, _pal.Happy, SleepArc);
                dc.Pop();
                return true;

            default:
                return false;
        }
    }

    private bool DrawMoodMouth(DrawingContext dc, double sx, double sy)
    {
        switch (Mood)
        {
            case FifuMood.Working:
                dc.DrawLine(_pal.Smile, new Point(sx - 2.8, sy + 1.6), new Point(sx + 2.8, sy + 1.6));
                return true;

            case FifuMood.Happy:
            {
                var g = Build0(c =>
                {
                    c.BeginFigure(new Point(sx - 5.2, sy - 0.2), true, true);
                    c.QuadraticBezierTo(new Point(sx, sy + 11), new Point(sx + 5.2, sy - 0.2), true, false);
                });
                dc.DrawGeometry(MouthFill, _pal.Smile, g);
                return true;
            }

            case FifuMood.Error:
            {
                var g = Build0(c =>
                {
                    c.BeginFigure(new Point(sx - 3.9, sy + 2.6), false, false);
                    c.QuadraticBezierTo(new Point(sx, sy - 2.4), new Point(sx + 3.9, sy + 2.6), true, false);
                });
                dc.DrawGeometry(null, _pal.Smile, g);
                return true;
            }

            case FifuMood.Loud:
            {
                var g = Build0(c =>
                {
                    c.BeginFigure(new Point(sx - 4.5, sy + 1.5), false, false);
                    c.QuadraticBezierTo(new Point(sx - 2.25, sy - 0.8), new Point(sx, sy + 1.5), true, false);
                    c.QuadraticBezierTo(new Point(sx + 2.25, sy + 3.8), new Point(sx + 4.5, sy + 1.5), true, false);
                });
                dc.DrawGeometry(null, _pal.Smile, g);
                return true;
            }

            case FifuMood.Listening:
                dc.DrawEllipse(null, _pal.Smile, new Point(sx, sy + 2), 1.6 + 1.6 * Level, 1.4 + 2.2 * Level);
                return true;

            case FifuMood.Roar:
                dc.DrawEllipse(MouthFill, _pal.Smile, new Point(sx, sy + 3.2), 3.3, 4.2);
                return true;

            case FifuMood.Sleepy:
            {
                var r = 1.3 + 0.5 * Math.Sin(_time * 1.1 + 1);
                dc.DrawEllipse(null, _pal.Smile, new Point(sx, sy + 1.8), r, r * 0.85);
                return true;
            }

            default:
                return false;
        }
    }

    private void DrawFloaters(DrawingContext dc, double strength, FormattedText a, FormattedText b, double speed)
    {
        for (var i = 0; i < 2; i++)
        {
            var t = (_time * speed + i * 0.5) % 1.0;
            var x = (i == 0 ? 78 : 8) + Math.Sin(t * 6 + i * 2) * 4 + (i == 0 ? t * 8 : -t * 6);
            var y = 30 - t * 26;

            dc.PushOpacity(Math.Clamp(Math.Sin(Math.PI * t) * strength, 0, 1));
            dc.DrawText(i == 0 ? a : b, new Point(x, y));
            dc.Pop();
        }
    }

    private void DrawSleepZ(DrawingContext dc)
    {
        for (var i = 0; i < 3; i++)
        {
            var t = (_time * 0.28 + i / 3.0) % 1.0;
            var x = 66 + t * 14 + Math.Sin(t * 5 + i) * 2;
            var y = 34 - t * 30;

            dc.PushOpacity(Math.Clamp(Math.Sin(Math.PI * t), 0, 1));
            dc.DrawText(i % 2 == 0 ? _zSmall! : _zBig!, new Point(x, y));
            dc.Pop();
        }
    }

    private void DrawSweat(DrawingContext dc, double x, double y)
    {
        var t = (_time * 0.9 + x * 0.013) % 1.0;
        dc.PushOpacity(1 - t * t);
        dc.DrawEllipse(SweatBrush, null, new Point(x, y + t * 12), 2.2, 3.2);
        dc.Pop();
    }

    private FormattedText MakeText(string text, Color color, double size, bool bold = false) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI Symbol"), FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
        size,
        new SolidColorBrush(color),
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}