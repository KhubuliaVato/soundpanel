using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SoundPanel.Services;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoundPanel.Controls;
using SoundPanel.Models;
using SoundPanel.ViewModels;

namespace SoundPanel;

/// <summary>Fifu's behaviour: moods, link offer, sleep timer, greeting.</summary>
public partial class MainWindow
{
    private static readonly Regex UrlRx = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] SupportedHosts =
    {
        "youtube.com", "youtu.be", "soundcloud.com", "tiktok.com", "facebook.com", "fb.watch",
        "instagram.com", "x.com", "twitter.com", "vimeo.com", "bandcamp.com", "twitch.tv"
    };

    private const double IdleSleepSeconds = 300; // Fifu falls asleep after 5 quiet minutes

    private DispatcherTimer _fifuTimer = null!;
    private DateTime _lastActivity = DateTime.UtcNow;
    private FifuMood _shownMood = FifuMood.Normal;
    private FifuMood? _transient;
    private DateTime _transientUntil;
    private DateTime? _sleepAt;
    private long _menuClosedTick;
    private string? _lastOffered;
    private string? _offerLink;
    private int _offerVersion;
    private DateTime _lastRoarWarn = DateTime.MinValue;

    private void InitFifu()
    {

        // any activity wakes Fifu up
        PreviewMouseMove += (_, _) => FifuWake();
        PreviewMouseDown += (_, _) => FifuWake();
        PreviewKeyDown += (_, _) => FifuWake();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Volume) or nameof(MainViewModel.IsPlaying))
                UpdateFifuMood();
        };

        foreach (var item in _downloader.Items) HookItem(item);
        if (_downloader.Items is INotifyCollectionChanged items)
        {
            items.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                    foreach (DownloadItem item in e.NewItems) HookItem(item);
                UpdateFifuMood();
            };
        }

        FifuRobot.MouseLeftButtonUp += FifuRobot_Click;
        Activated += (_, _) => CheckClipboard();

        _fifuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _fifuTimer.Tick += FifuTick;
        _fifuTimer.Start();

        var greet = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        greet.Tick += (_, _) =>
        {
            greet.Stop();
            ShowToast(Greeting());
        };
        greet.Start();
    }

    private static string Greeting() => DateTime.Now.Hour switch
    {
        >= 5 and < 12 => "დილა მშვიდობისა! მე Fifu ვარ, მზად ვარ მუსიკისთვის",
        >= 12 and < 18 => "გამარჯობა! Fifu აქაა, რას ვუსმენთ?",
        >= 18 and < 23 => "საღამო მშვიდობისა! Fifu-ს ყურები გაუშალა",
        _ => "ამ დროს ვინ არ იძინებს? მე და მუსიკა მზად ვართ"
    };

    // ------------------------------------------------------------ moods

    private void HookItem(DownloadItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DownloadItem.Status)) return;

            if (item.IsDone)
            {
                FifuReact(FifuMood.Happy, 3200);
            }
            else if (item.Status.ToString() == "Failed")
            {
                FifuReact(FifuMood.Error, 3800);
                ShowToast("ჩამოტვირთვა ვერ მოხერხდა · მიზეზი რიგში ჩანს");
            }

            UpdateFifuMood();
        };
    }

    private void FifuReact(FifuMood mood, int milliseconds)
    {
        _transient = mood;
        _transientUntil = DateTime.UtcNow.AddMilliseconds(milliseconds);
        UpdateFifuMood();
    }

    private void FifuWake()
    {
        _lastActivity = DateTime.UtcNow;
        if (_shownMood != FifuMood.Sleepy) return;

        if (_transient == FifuMood.Sleepy) _transient = null;
        UpdateFifuMood();
    }

    private void UpdateFifuMood()
    {
        var now = DateTime.UtcNow;
        FifuMood mood;

        if (_voice.Phase == VoicePhase.Listening) mood = FifuMood.Listening;
        else if (_voice.Phase is VoicePhase.Thinking or VoicePhase.Downloading) mood = FifuMood.Working;
        else if (_transient is { } forced && now < _transientUntil) mood = forced;
        else if (_downloader.Items.Any(i => i.IsActive)) mood = FifuMood.Working;
        else if (_vm.Volume >= 1.95) mood = FifuMood.Roar;
        else if (_vm.Volume > 1.0) mood = FifuMood.Loud;
        else if (!_vm.IsPlaying && (now - _lastActivity).TotalSeconds >= IdleSleepSeconds) mood = FifuMood.Sleepy;
        else mood = FifuMood.Normal;

        if (mood == _shownMood) return;

        var previous = _shownMood;
        _shownMood = mood;
        FifuRobot.Mood = mood;

        if (mood == FifuMood.Roar && previous != FifuMood.Roar && (now - _lastRoarWarn).TotalSeconds > 20)
        {
            _lastRoarWarn = now;
            ShowToast("ოოო, ეს უკვე ძალიან ხმამაღალია!");
        }
    }

    private void FifuTick(object? sender, EventArgs e)
    {
        UpdateFifuMood();

        if (_sleepAt is not { } at)
        {
            FifuRobot.ToolTip = "Fifu";
            return;
        }

        var left = at - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            FinishSleepTimer();
            return;
        }

        FifuRobot.ToolTip = $"Fifu · ძილამდე {FormatLeft(left)}";
        if (SettingsPane.IsOpen) RefreshSleepUi();

        // last 20 seconds: the music fades out smoothly
        if (left.TotalSeconds <= 20) _vm.SetFade(left.TotalSeconds / 20);
    }

    // ------------------------------------------------------------ Fifu menu + sleep timer

    private void FifuRobot_Click(object sender, MouseButtonEventArgs e)
    {
        DownloadPane.Close();
        SettingsPane.Open();
        SettingsPane.ShowFifu();
        RefreshSleepUi();
    }

    private void SleepPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var minutes))
            StartSleepTimer(minutes);
    }

    private void SleepCancel_Click(object sender, RoutedEventArgs e)
    {
        _sleepAt = null;
        _vm.SetFade(1);
        RefreshSleepUi();
        ShowToast("კარგი, ტაიმერი გავაუქმე");
    }

    private void StartSleepTimer(int minutes)
    {
        _sleepAt = DateTime.UtcNow.AddMinutes(minutes);
        _vm.SetFade(1);
        RefreshSleepUi();
        ShowToast($"კარგი! {minutes} წუთში მუსიკას ჩავაქრობ და დავიძინებ");
    }

    private void FinishSleepTimer()
    {
        _sleepAt = null;
        if (_vm.IsPlaying) _vm.TogglePlay();
        _vm.SetFade(1);
        RefreshSleepUi();
        FifuReact(FifuMood.Sleepy, 15000);
        ShowToast("ღამე მშვიდობისა! მუსიკა გამოვრთე");
    }

    private void RefreshSleepUi()
    {
        FifuSleepActive.Visibility = _sleepAt != null ? Visibility.Visible : Visibility.Collapsed;
        if (_sleepAt is { } at)
            FifuSleepInfo.Text = $"გამოირთვება: {FormatLeft(at - DateTime.UtcNow)}";
    }

    private static string FormatLeft(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ------------------------------------------------------------ link offer

    private void CheckClipboard()
    {
        if (!_vm.HasRoot || DownloadPane.IsOpen || SettingsPane.IsOpen) return;

        string? text = null;
        try
        {
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch
        {
            return; // clipboard is locked by another app
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return;

        var match = UrlRx.Match(text);
        if (!match.Success || !Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return;

        var host = uri.Host.ToLowerInvariant();
        if (!SupportedHosts.Any(h => host == h || host.EndsWith("." + h))) return;

        var link = match.Value;
        if (link == _lastOffered) return;
        if (_downloader.Items.Any(i => i.Options.Url == link)) return;

        _lastOffered = link;
        _offerLink = link;
        OfferLink.Text = link;
        ShowOffer();
    }

    private void ShowOffer()
    {
        var version = ++_offerVersion;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        Offer.Visibility = Visibility.Visible;
        Offer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        OfferTr.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });

        FifuReact(FifuMood.Happy, 1200); // Fifu perks up

        var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
        hide.Tick += (_, _) =>
        {
            hide.Stop();
            if (version == _offerVersion) HideOffer();
        };
        hide.Start();
    }

    private void HideOffer()
    {
        _offerVersion++;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (Offer.Opacity <= 0.01) Offer.Visibility = Visibility.Collapsed;
        };
        Offer.BeginAnimation(OpacityProperty, fade);
        OfferTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(220)));
    }

    private void OfferYes_Click(object sender, RoutedEventArgs e)
    {
        HideOffer();
        if (_offerLink != null) _downloader.UrlText = _offerLink;
        SettingsPane.Close();
        DownloadPane.Open();
    }

    private void OfferNo_Click(object sender, RoutedEventArgs e) => HideOffer();
}