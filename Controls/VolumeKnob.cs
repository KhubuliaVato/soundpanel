using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SoundPanel.Services;

namespace SoundPanel.Controls;

/// <summary>
/// Rotary volume knob, 0..2 (0%..200%). Drag up/down or use the wheel; double-click resets to 100%;
/// a plain click raises <see cref="Clicked"/> (used for mute). The part above 100% glows pink/orange.
/// </summary>
public sealed class VolumeKnob : FrameworkElement
{
    public const double MaxValue = 2.0;

    private const double StartAngle = 135;   // bottom-left
    private const double SweepAngle = 270;
    private const double PixelsForFullRange = 340;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(VolumeKnob),
        new FrameworkPropertyMetadata(0.8, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged, CoerceValue));

    private static DropShadowEffect NormalGlow =>
        Freeze(new DropShadowEffect { Color = ThemeService.Accent.A, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45 });
    private static readonly DropShadowEffect BoostGlow = Freeze(new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x5C, 0xA8), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.75 });

    private readonly ScaleTransform _scale = new(1, 1);
    private bool _dragging;
    private bool _pressedCenter;
    private bool _moved;
    private double _startX;
    private double _startY;
    private double _startValue;

    public VolumeKnob()
    {
        Width = 68;
        Height = 68;
        Cursor = Cursors.Hand;
        Focusable = false;
        ToolTip = "ხმა — დააჭირე რგოლს და გადაათრიე · ბორბალი · ორმაგი კლიკი: 100% · ცენტრზე კლიკი: mute";
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        Effect = NormalGlow;
        ThemeService.AccentChanged += (_, _) =>
        {
            Effect = Value > 1.0 ? BoostGlow : NormalGlow;
            InvalidateVisual();
        };

        MouseEnter += (_, _) => AnimateScale(1.07);
        MouseLeave += (_, _) => { if (!_dragging) AnimateScale(1.0); };
    }

    public event EventHandler? Clicked;

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static object CoerceValue(DependencyObject d, object baseValue) =>
        Math.Clamp((double)baseValue, 0, MaxValue);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (VolumeKnob)d;
        knob.Effect = (double)e.NewValue > 1.0 ? BoostGlow : NormalGlow;
    }

    private static DropShadowEffect Freeze(DropShadowEffect e)
    {
        e.Freeze();
        return e;
    }

    private void AnimateScale(double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // ------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.ClickCount == 2)
        {
            SetCurrentValue(ValueProperty, 1.0);
            _dragging = false;
            _moved = true; // swallow the click
            e.Handled = true;
            return;
        }

        var p = e.GetPosition(this);
        var cx = p.X - ActualWidth / 2;
        var cy = p.Y - ActualHeight / 2;
        var r = Math.Min(ActualWidth, ActualHeight) / 2 - 8;

        _pressedCenter = Math.Sqrt(cx * cx + cy * cy) < r * 0.65; // center = mute button
        _dragging = true;
        CaptureMouse();
        AnimateScale(0.97);

        if (!_pressedCenter) SetFromMouse(p);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        if (!_pressedCenter) SetFromMouse(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        AnimateScale(IsMouseOver ? 1.07 : 1.0);

        if (_pressedCenter && IsMouseOver) Clicked?.Invoke(this, EventArgs.Empty);
        _pressedCenter = false;
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 0.01 : 0.05;
        var v = Math.Clamp(Value + (e.Delta > 0 ? step : -step), 0, MaxValue);
        if (Math.Abs(v - 1.0) < step / 2) v = 1.0;
        SetCurrentValue(ValueProperty, Math.Round(v, 3));
        e.Handled = true;
    }

    // ------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        var center = new Point(w / 2, h / 2);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test area
        const double thickness = 6;
        var radius = Math.Min(w, h) / 2 - thickness - 2;
        var fraction = Math.Clamp(Value / MaxValue, 0, 1);
        var boosting = Value > 1.0;

        // soft inner disc
        dc.DrawEllipse(
            new RadialGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), 1),
            center, radius - thickness / 2 - 3, radius - thickness / 2 - 3);

        // track
        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), thickness)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        DrawArc(dc, trackPen, center, radius, 0, 1);

        // normal part (0..100%)
        var normalBrush = new LinearGradientBrush(ThemeService.Accent.A, ThemeService.Accent.B,
            new Point(0, 1), new Point(1, 0));
        var normalPen = new Pen(normalBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        DrawArc(dc, normalPen, center, radius, 0, Math.Min(fraction, 0.5));

        // boost part (100%..200%)
        if (boosting)
        {
            var boostBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0x5C, 0xA8), Color.FromRgb(0xFF, 0xA0, 0x4D),
                new Point(0, 0), new Point(1, 1));
            var boostPen = new Pen(boostBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            DrawArc(dc, boostPen, center, radius, 0.5, fraction);
        }

        // 100% marker (top)
        var marker = PointOnCircle(center, radius + thickness / 2 + 3, AngleAt(0.5));
        var markerInner = PointOnCircle(center, radius + thickness / 2 + 0.5, AngleAt(0.5));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(boosting ? (byte)0xCC : (byte)0x66, 0xFF, 0xFF, 0xFF)), 2)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, markerInner, marker);

        // pointer dot
        if (fraction > 0.001)
        {
            var tip = PointOnCircle(center, radius, AngleAt(fraction));
            dc.DrawEllipse(Brushes.White, null, tip, 3.2, 3.2);
        }

        // percentage text
        var percent = (int)Math.Round(Value * 100);
        var text = new FormattedText(
            percent + "%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            percent >= 100 ? 14 : 15,
            boosting ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xD6)) : Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static double AngleAt(double fraction) => StartAngle + SweepAngle * fraction;

    /// <summary>The pointer follows the cursor's angle around the knob centre.</summary>
    private void SetFromMouse(Point p)
    {
        var dx = p.X - ActualWidth / 2;
        var dy = p.Y - ActualHeight / 2;
        if (dx * dx + dy * dy < 4) return;

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var rel = ((angle - StartAngle) % 360 + 360) % 360;

        // cursor in the gap at the bottom: stick to the nearest end (0% or 200%)
        var fraction = rel <= SweepAngle ? rel / SweepAngle : (Value >= 1.0 ? 1.0 : 0.0);

        var v = Math.Round(fraction * MaxValue, 2);
        if (Math.Abs(v - 1.0) < 0.03) v = 1.0; // magnetic 100%
        SetCurrentValue(ValueProperty, v);
    }

    private static Point PointOnCircle(Point c, double r, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }

    private static void DrawArc(DrawingContext dc, Pen pen, Point center, double radius, double from, double to)
    {
        if (to - from < 0.002) return;

        var p0 = PointOnCircle(center, radius, AngleAt(from));
        var p1 = PointOnCircle(center, radius, AngleAt(to));
        var sweep = SweepAngle * (to - from);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
