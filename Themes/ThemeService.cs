using System.Windows;
using System.Windows.Media;

namespace SoundPanel.Services;

public sealed class AccentTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Color A { get; init; }
    public required Color B { get; init; }
    public required Color Hot { get; init; }
    public required Color BgTop { get; init; }
    public required Color BgMid { get; init; }
    public required Color BgBottom { get; init; }

    public Color Mid => ThemeService.Mix(A, B, 0.5);

    private Brush? _preview;
    public Brush Preview => _preview ??= ThemeService.Freeze(new LinearGradientBrush(A, B, 45));
}

public sealed class RobotTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Color Light { get; init; }
    public required Color Mid { get; init; }
    public required Color Deep { get; init; }
    public required Color Tail { get; init; }
    public required Color Eye { get; init; }

    private Brush? _preview;
    public Brush Preview => _preview ??= ThemeService.Freeze(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Light, 0.0),
            new GradientStop(Mid, 0.45),
            new GradientStop(Tail, 1.0)
        }
    });
}

public static class ThemeService
{
    public static IReadOnlyList<AccentTheme> AccentThemes { get; } = new[]
    {
        new AccentTheme { Id = "aurora", Name = "ავრორა", A = C("#7C5CFF"), B = C("#22D3EE"), Hot = C("#FF5CA8"),
            BgTop = C("#090912"), BgMid = C("#120E26"), BgBottom = C("#08111F") },
        new AccentTheme { Id = "ocean", Name = "ოკეანე", A = C("#3B82F6"), B = C("#2DD4BF"), Hot = C("#8B5CF6"),
            BgTop = C("#060B16"), BgMid = C("#0A1530"), BgBottom = C("#061418") },
        new AccentTheme { Id = "sunset", Name = "მზის ჩასვლა", A = C("#FF5E7E"), B = C("#FFA64D"), Hot = C("#C04DFF"),
            BgTop = C("#12080E"), BgMid = C("#22101C"), BgBottom = C("#160D0A") },
        new AccentTheme { Id = "emerald", Name = "ზურმუხტი", A = C("#10B981"), B = C("#22D3EE"), Hot = C("#A3E635"),
            BgTop = C("#07110D"), BgMid = C("#0B1E18"), BgBottom = C("#08121A") },
        new AccentTheme { Id = "rose", Name = "ვარდი", A = C("#EC4899"), B = C("#8B5CF6"), Hot = C("#F59E0B"),
            BgTop = C("#140812"), BgMid = C("#220C26"), BgBottom = C("#0E0A1C") },
        new AccentTheme { Id = "graphite", Name = "გრაფიტი", A = C("#8A94A6"), B = C("#E2E8F0"), Hot = C("#94A3B8"),
            BgTop = C("#0B0C10"), BgMid = C("#15171D"), BgBottom = C("#0B0D12") }
    };

    public static IReadOnlyList<RobotTheme> RobotThemes { get; } = new[]
    {
        new RobotTheme { Id = "neon", Name = "ნეონი", Light = C("#8FEFFF"), Mid = C("#3EA8FF"), Deep = C("#3050F5"), Tail = C("#B26CFF"), Eye = C("#2DF5FF") },
        new RobotTheme { Id = "violet", Name = "იისფერი", Light = C("#D9C8FF"), Mid = C("#8B6CFF"), Deep = C("#5B3CFF"), Tail = C("#FF6CC8"), Eye = C("#C8B4FF") },
        new RobotTheme { Id = "pink", Name = "ვარდისფერი", Light = C("#FFC9F0"), Mid = C("#FF6AD5"), Deep = C("#B04CFF"), Tail = C("#FF9A6A"), Eye = C("#FFD0F5") },
        new RobotTheme { Id = "emerald", Name = "ზურმუხტი", Light = C("#B6FFE0"), Mid = C("#2EE6A6"), Deep = C("#1AA6B8"), Tail = C("#3E8BFF"), Eye = C("#7CFFD4") },
        new RobotTheme { Id = "sun", Name = "მზე", Light = C("#FFF0B0"), Mid = C("#FFB52E"), Deep = C("#FF6A2E"), Tail = C("#FF4D6D"), Eye = C("#FFE27A") },
        new RobotTheme { Id = "silver", Name = "ვერცხლი", Light = C("#FFFFFF"), Mid = C("#C7D2FE"), Deep = C("#8E9BD6"), Tail = C("#B6A8FF"), Eye = C("#EAF0FF") }
    };

    public static AccentTheme Accent { get; private set; } = AccentThemes[0];
    public static RobotTheme Robot { get; private set; } = RobotThemes[0];

    public static event EventHandler? AccentChanged;
    public static event EventHandler? RobotChanged;

    public static void ApplyAccent(string? id)
    {
        var t = AccentThemes.FirstOrDefault(x => x.Id == id) ?? AccentThemes[0];
        Accent = t;

        var res = Application.Current?.Resources;
        if (res != null)
        {
            res["AccentColorA"] = t.A;
            res["AccentColorB"] = t.B;
            res["AccentBrush"] = Gradient(t.A, t.B, new Point(0, 0), new Point(1, 1));
            res["AccentBrushH"] = Gradient(t.A, t.B, new Point(0, 0), new Point(1, 0));
            res["AccentBrushV"] = Gradient(t.A, t.B, new Point(0, 0), new Point(0, 1));
            res["HotBrush"] = Gradient(t.Hot, t.A, new Point(0, 0), new Point(1, 1));
            res["WindowBackground"] = Freeze(new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(t.BgTop, 0.0),
                    new GradientStop(t.BgMid, 0.55),
                    new GradientStop(t.BgBottom, 1.0)
                }
            });

            res["AccentSoftBrush"] = Solid(t.A, 0x2A);
            res["AccentBorderBrush"] = Solid(t.A, 0x66);
            res["AccentTextBrush"] = Freeze(new SolidColorBrush(Mix(t.A, Colors.White, 0.55)));

            res["AmbA1"] = WithAlpha(t.A, 0x55);
            res["AmbA0"] = WithAlpha(t.A, 0x00);
            res["AmbB1"] = WithAlpha(t.B, 0x44);
            res["AmbB0"] = WithAlpha(t.B, 0x00);
            res["AmbC1"] = WithAlpha(t.Hot, 0x26);
            res["AmbC0"] = WithAlpha(t.Hot, 0x00);
        }

        AccentChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyRobot(string? id)
    {
        Robot = RobotThemes.FirstOrDefault(x => x.Id == id) ?? RobotThemes[0];
        RobotChanged?.Invoke(null, EventArgs.Empty);
    }

    // ------------------------------------------------------------ helpers

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static SolidColorBrush Solid(Color c, byte alpha) => Freeze(new SolidColorBrush(WithAlpha(c, alpha)));

    private static LinearGradientBrush Gradient(Color a, Color b, Point start, Point end) =>
        Freeze(new LinearGradientBrush(a, b, 0) { StartPoint = start, EndPoint = end });
}