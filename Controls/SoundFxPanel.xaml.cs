using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoundPanel.Services;
using SoundPanel.ViewModels;

namespace SoundPanel.Controls;

public partial class SoundFxPanel : UserControl
{
    private FxViewModel? _hooked;
    private readonly DispatcherTimer _meterTimer;

    private double _shownL = -100, _shownR = -100, _holdL = -100, _holdR = -100, _rms = -100, _grShown;
    private int _holdTL, _holdTR;

    public SoundFxPanel()
    {
        InitializeComponent();
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => MeterTick();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) =>
        {
            Hook();
            DrawCurve();
        };
    }

    public bool IsOpen { get; private set; }

    public event EventHandler? OpenChanged;

    // ------------------------------------------------------------ open / close

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visibility = Visibility.Visible;

        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)));
        SurfaceTr.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        DrawCurve();
        _meterTimer.Start();
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (!IsOpen) Visibility = Visibility.Collapsed;
        };
        Surface.BeginAnimation(OpacityProperty, fade);
        SurfaceTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(160)));
        _meterTimer.Stop();

        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ EQ curve

    private void Hook()
    {
        var fx = (DataContext as MainViewModel)?.Fx;
        if (ReferenceEquals(fx, _hooked)) return;

        if (_hooked != null) _hooked.CurveChanged -= DrawCurve;
        _hooked = fx;
        if (fx != null) fx.CurveChanged += DrawCurve;
        DrawCurve();
    }

    private void Curve_SizeChanged(object sender, SizeChangedEventArgs e) => DrawCurve();

    private void DrawCurve()
    {
        if (_hooked == null) return;
        var w = CurveHost.ActualWidth;
        var h = CurveHost.ActualHeight;
        if (w < 20 || h < 20) return;

        const double range = 15; // dB shown each way
        var mid = h / 2;

        var grid = new StreamGeometry();
        using (var c = grid.Open())
        {
            foreach (var db in new[] { -12.0, -6.0, 0.0, 6.0, 12.0 })
            {
                var y = mid - db / range * mid;
                c.BeginFigure(new Point(0, y), false, false);
                c.LineTo(new Point(w, y), true, false);
            }
        }
        grid.Freeze();
        CurveGrid.Data = grid;

        const int n = 140;
        var pts = new Point[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var f = 20 * Math.Pow(1000, i / (double)n); // 20 Hz .. 20 kHz, log scale
            var db = Math.Clamp(FxCurve.ResponseDb(_hooked.Params, f), -range, range);
            pts[i] = new Point(i * w / n, mid - db / range * mid);
        }

        var rest = pts.Skip(1).ToList();

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(pts[0], false, false);
            c.PolyLineTo(rest, true, true);
        }
        line.Freeze();
        CurveLine.Data = line;

        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(0, mid), true, true);
            c.LineTo(pts[0], false, false);
            c.PolyLineTo(rest, false, false);
            c.LineTo(new Point(w, mid), false, false);
        }
        fill.Freeze();
        CurveFill.Data = fill;
    }

    // ------------------------------------------------------------ level meters

    private static readonly Brush Amber = Frozen("#FBBF24");
    private static readonly Brush Red = Frozen("#F87171");
    private static readonly Brush Green = Frozen("#34D399");

    private static Brush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    private static double ToDb(float lin) => lin > 1e-5f ? 20 * Math.Log10(lin) : -100;

    private static string Fmt(double v) =>
        v <= -99 ? "—" : v.ToString("0.0", CultureInfo.InvariantCulture);

    private static void Ballistics(ref double shown, ref double hold, ref int holdTicks, double db)
    {
        shown = db > shown ? db : Math.Max(db, shown - 0.8); // falls about 24 dB/s
        if (db >= hold)
        {
            hold = db;
            holdTicks = 45; // 1.5 s
        }
        else if (holdTicks > 0)
        {
            holdTicks--;
        }
        else
        {
            hold = Math.Max(shown, hold - 0.6);
        }
    }

    private void Place(Border bar, Border tick, double width, double shown, double hold)
    {
        const double range = 60;
        var w = Math.Max(0, width);
        bar.Width = Math.Clamp((shown + range) / range, 0, 1) * w;
        tick.Margin = new Thickness(Math.Max(0, Math.Clamp((hold + range) / range, 0, 1) * w - 2), 0, 0, 0);
        tick.Visibility = hold > -59 ? Visibility.Visible : Visibility.Hidden;

        bar.Background = shown > -0.5 ? Red
            : shown > -6 ? Amber
            : (Brush)FindResource("AccentBrushH");
    }

    private void MeterTick()
    {
        if (FxVm is not { } vm) return;
        var p = vm.Params;

        var dbL = ToDb(p.PeakL);
        p.PeakL = 0;
        var dbR = ToDb(p.PeakR);
        p.PeakR = 0;

        Ballistics(ref _shownL, ref _holdL, ref _holdTL, dbL);
        Ballistics(ref _shownR, ref _holdR, ref _holdTR, dbR);
        Place(BarL, HoldL, HostL.ActualWidth, _shownL, _holdL);
        Place(BarR, HoldR, HostR.ActualWidth, _shownR, _holdR);

        _rms += (p.RmsDb - _rms) * 0.2;
        var peak = Math.Max(_holdL, _holdR);
        LevelText.Text = $"Peak {Fmt(peak)} dBFS    RMS {Fmt(_rms)} dB" + (vm.Bypass ? "    · ორიგინალი" : "");

        LufsMText.Text = Fmt(p.LufsM);
        LufsSText.Text = Fmt(p.LufsS);
        LufsIText.Text = Fmt(p.LufsI);

        if (p.LufsI <= -99)
        {
            LufsIText.Foreground = (Brush)FindResource("TextPrimary");
        }
        else
        {
            var diff = Math.Abs(p.LufsI - vm.TargetLufs);
            LufsIText.Foreground = diff <= 1 ? Green : diff <= 3 ? Amber : Red;
        }

        var gr = p.GrLin;
        p.GrLin = 1f;
        var grDb = gr < 0.999f ? 20 * Math.Log10(gr) : 0;
        _grShown = grDb < _grShown ? grDb : Math.Min(0, _grShown + 0.4);
        GrText.Text = _grShown > -0.05 ? "0.0" : Fmt(_grShown);
        GrText.Foreground = _grShown < -3 ? Amber : (Brush)FindResource("TextPrimary");
    }

    // ------------------------------------------------------------ misc

    /// <summary>Double click on a slider puts it back to its default (stored in Uid).</summary>
    private void Slider_Reset(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s &&
            double.TryParse(s.Uid, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            s.Value = value;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.Fx.ResetAll();

    // ------------------------------------------------------------ original sound (hold)

    private void Bypass_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el) el.CaptureMouse();
        if (FxVm != null) FxVm.Bypass = true;
    }

    private void Bypass_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el) el.ReleaseMouseCapture();
        if (FxVm != null) FxVm.Bypass = false;
    }

    private void Bypass_Lost(object sender, MouseEventArgs e)
    {
        if (FxVm != null) FxVm.Bypass = false;
    }

    // ------------------------------------------------------------ user presets

    private FxViewModel? FxVm => (DataContext as MainViewModel)?.Fx;

    private void Say(string text) => (DataContext as MainViewModel)?.ShowMessage(text);

    private void SaveNew_Click(object sender, RoutedEventArgs e)
    {
        FxVm?.BeginNaming();
        Dispatcher.BeginInvoke(() =>
        {
            PresetNameBox.Focus();
            PresetNameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        if (FxVm != null && FxVm.SaveUserPreset()) Say("პრესეტი შენახულია");
    }

    private void CancelName_Click(object sender, RoutedEventArgs e) => FxVm?.CancelNaming();

    private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSave_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FxVm?.CancelNaming();
            e.Handled = true;
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (FxVm is not { IsUserPreset: true }) return;
        FxVm.DeleteCurrentPreset();
        Say("პრესეტი წაიშალა");
    }

    private void ExportPresets_Click(object sender, RoutedEventArgs e)
    {
        if (FxVm == null) return;
        if (FxVm.UserPresetCount == 0)
        {
            Say("ჯერ შეინახე საკუთარი პრესეტი");
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "1Flow პრესეტები (*.json)|*.json",
            FileName = "1flow-presets.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            FxVm.ExportPresets(dlg.FileName);
            Say("პრესეტები ექსპორტირებულია");
        }
        catch (Exception ex)
        {
            Say("ვერ შეინახა: " + ex.Message);
        }
    }

    private void ImportPresets_Click(object sender, RoutedEventArgs e)
    {
        if (FxVm == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "1Flow პრესეტები (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        var n = FxVm.ImportPresets(dlg.FileName);
        Say(n > 0 ? $"დაემატა {n} პრესეტი" : "ფაილში პრესეტები ვერ ვიპოვე");
    }
}