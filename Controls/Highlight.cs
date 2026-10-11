using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SoundPanel.Controls;

/// <summary>Attached properties for a TextBlock: shows Text with every Query word highlighted.</summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string v) => d.SetValue(TextProperty, v);
    public static string GetQuery(DependencyObject d) => (string)d.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject d, string v) => d.SetValue(QueryProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        var text = GetText(tb) ?? "";
        var words = (GetQuery(tb) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        tb.Inlines.Clear();
        if (words.Length == 0 || text.Length == 0)
        {
            tb.Inlines.Add(new Run(text));
            return;
        }

        var mask = new bool[text.Length];
        foreach (var w in words)
        {
            var i = 0;
            while ((i = text.IndexOf(w, i, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                for (var k = i; k < i + w.Length && k < text.Length; k++) mask[k] = true;
                i += Math.Max(1, w.Length);
            }
        }

        var brush = Application.Current?.TryFindResource("AccentTextBrush") as Brush ?? Brushes.White;

        var pos = 0;
        while (pos < text.Length)
        {
            var on = mask[pos];
            var end = pos;
            while (end < text.Length && mask[end] == on) end++;

            var run = new Run(text[pos..end]);
            if (on)
            {
                run.Foreground = brush;
                run.FontWeight = FontWeights.Bold;
            }
            tb.Inlines.Add(run);
            pos = end;
        }
    }
}