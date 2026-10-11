using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoundPanel.Controls;
using SoundPanel.Services;
using SoundPanel.ViewModels;

namespace SoundPanel;

public sealed class SearchHit
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public required string Folder { get; init; }
    public required string Query { get; init; }
    public ImageSource? Cover { get; init; }
}

/// <summary>Fifu's quick search: global hotkey -> type a few letters -> Enter plays the closest song.</summary>
public partial class QuickSearchWindow : Window
{
    private sealed record Entry(string Path, string Title, string Norm, string Folder);

    private readonly MainViewModel _vm;
    private readonly VoiceAssistant _voice;
    private readonly List<string> _paths;
    private readonly List<Entry> _entries;
    private bool _closing;

    public QuickSearchWindow(MainViewModel vm, VoiceAssistant voice)
    {
        InitializeComponent();
        _vm = vm;
        _voice = voice;
        HookVoice();

        _paths = vm.GetAllTrackFiles();
        _entries = _paths
            .Select(p =>
            {
                var title = System.IO.Path.GetFileNameWithoutExtension(p);
                var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p)) ?? "";
                return new Entry(p, title, FuzzySearch.Normalize(title), folder);
            })
            .ToList();

        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + work.Height * 0.16;

        Fifu.IsPlaying = vm.IsPlaying;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        CardTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });

        Activate();
        Box.Focus();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => CloseSoft();

    // ------------------------------------------------------------ voice

    private void HookVoice()
    {
        _voice.PhaseChanged += OnVoicePhase;
        _voice.LevelChanged += OnVoiceLevel;
        _voice.Heard += OnVoiceHeard;
        _voice.Finished += OnVoiceFinished;

        Closed += (_, _) =>
        {
            _voice.PhaseChanged -= OnVoicePhase;
            _voice.LevelChanged -= OnVoiceLevel;
            _voice.Heard -= OnVoiceHeard;
            _voice.Finished -= OnVoiceFinished;
            if (_voice.IsBusy) _voice.Cancel();
        };
    }

    public void ToggleVoice() => _ = _voice.RunOrCancelAsync();

    private void MicButton_Click(object sender, RoutedEventArgs e) => ToggleVoice();

    private void OnVoiceLevel(double level) => Fifu.Level = level;

    private void OnVoicePhase(VoicePhase phase)
    {
        switch (phase)
        {
            case VoicePhase.Downloading:
                Fifu.Mood = FifuMood.Working;
                Hint.Text = $"ვტვირთავ ხმის მოდელს… {_voice.DownloadPercent}% (მხოლოდ ერთხელ)";
                break;
            case VoicePhase.Listening:
                Fifu.Mood = FifuMood.Listening;
                Hint.Text = "ვუსმენ…  play სახელი · next · previous · stop · shuffle · volume 120";
                break;
            case VoicePhase.Thinking:
                Fifu.Mood = FifuMood.Working;
                Hint.Text = "ვფიქრობ…";
                break;
            default:
                Fifu.Level = 0;
                if (Fifu.Mood is FifuMood.Listening or FifuMood.Working) Fifu.Mood = FifuMood.Normal;
                break;
        }
    }

    private void OnVoiceHeard(string text) => Hint.Text = $"გავიგე: „{text}“";

    private void OnVoiceFinished(VoiceOutcome outcome)
    {
        Hint.Text = outcome.Message;
        Fifu.Mood = outcome.Ok ? FifuMood.Happy : FifuMood.Error;
        if (!outcome.Ok) return;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CloseSoft();
        };
        timer.Start();
    }

    private void CloseSoft()
    {
        if (_closing) return;
        _closing = true;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        Card.BeginAnimation(OpacityProperty, fade);
    }

    // ------------------------------------------------------------ search

    private void Box_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var query = Box.Text.Trim();
        Placeholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (query.Length == 0)
        {
            ShowHits(new List<SearchHit>(), "↑ ↓ არჩევა   ·   Enter დაკვრა   ·   Esc დახურვა");
            Fifu.Mood = FifuMood.Normal;
            return;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(FuzzySearch.Normalize)
            .Where(w => w.Length > 0)
            .ToArray();

        var hits = _entries
            .Select(en => (en, score: FuzzySearch.Score(words, en.Norm)))
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.en.Title.Length)
            .Take(6)
            .Select(x => new SearchHit
            {
                Path = x.en.Path,
                Title = x.en.Title,
                Folder = x.en.Folder,
                Query = query,
                Cover = CoverService.Load(x.en.Path, 84)
            })
            .ToList();

        Fifu.Mood = hits.Count == 0 ? FifuMood.Error : FifuMood.Normal;
        ShowHits(hits, hits.Count == 0
            ? $"„{query}“ ვერ ვიპოვე. სცადე სხვანაირად დაწერა"
            : "↑ ↓ არჩევა   ·   Enter დაკვრა   ·   Esc დახურვა");
    }

    private void ShowHits(List<SearchHit> hits, string hint)
    {
        List.ItemsSource = hits;
        List.SelectedIndex = hits.Count > 0 ? 0 : -1;
        List.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = List.Visibility;
        Hint.Text = hint;
    }

    // ------------------------------------------------------------ keyboard + mouse

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSoft();
                e.Handled = true;
                break;
            case Key.M when Keyboard.Modifiers == ModifierKeys.Control:
                ToggleVoice();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                PlaySelected();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var count = List.Items.Count;
        if (count == 0) return;

        List.SelectedIndex = (List.SelectedIndex + delta + count) % count;
        List.ScrollIntoView(List.SelectedItem);
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not ListBoxItem)
            d = VisualTreeHelper.GetParent(d);

        if (d is ListBoxItem { DataContext: SearchHit hit })
        {
            List.SelectedItem = hit;
            PlaySelected();
        }
    }

    private void PlaySelected()
    {
        if (List.SelectedItem is not SearchHit hit) return;

        _vm.PlayFromLibrary(hit.Path, _paths);
        CloseSoft();
    }
}