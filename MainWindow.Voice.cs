using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SoundPanel.Controls;
using SoundPanel.Services;

namespace SoundPanel;

/// <summary>Voice commands in the main window: Ctrl+M, the Fifu menu, or saying "Fifu".</summary>
public partial class MainWindow
{
    private VoiceAssistant _voice = null!;
    private int _voiceToastPercent = -10;
    private bool _micLoading;
    private readonly DispatcherTimer _busTimer = new();

    private void ScheduleBusReset(int ms)
    {
        _busTimer.Stop();
        _busTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _busTimer.Start();
    }

    private void InitVoice()
    {
        _voice = new VoiceAssistant(_vm);
        FillMics();

        _busTimer.Tick += (_, _) =>
        {
            _busTimer.Stop();
            FifuBus.SetMood(null);
        };

        _voice.PhaseChanged += phase =>
        {
            switch (phase)
            {
                case VoicePhase.Downloading when _voice.DownloadPercent >= _voiceToastPercent + 10:
                    _voiceToastPercent = _voice.DownloadPercent;
                    ShowToast($"ვტვირთავ ხმის მოდელს… {_voice.DownloadPercent}% (მხოლოდ ერთხელ)");
                    break;
                case VoicePhase.Listening:
                    ShowToast("ვუსმენ… თქვი ბრძანება");
                    break;
                case VoicePhase.Idle:
                    FifuRobot.Level = 0;
                    _voiceToastPercent = -10;
                    break;
            }

            // the mini widget (minimized app) shows the same face
            if (phase == VoicePhase.Idle)
            {
                ScheduleBusReset(2000);
            }
            else
            {
                _busTimer.Stop();
                FifuBus.SetMood(phase == VoicePhase.Listening ? FifuMood.Listening : FifuMood.Working);
            }

            UpdateFifuMood();
        };

        _voice.LevelChanged += level =>
        {
            FifuRobot.Level = level;
            FifuBus.SetLevel(level);
        };

        _voice.Heard += text =>
        {
            ShowToast($"გავიგე: „{text}“");
            FifuBus.SetMood(FifuMood.Working);
        };

        _voice.Finished += outcome =>
        {
            ShowToast(outcome.Message);
            FifuReact(outcome.Ok ? FifuMood.Happy : FifuMood.Error, 1800);
            FifuBus.SetMood(outcome.Ok ? FifuMood.Happy : FifuMood.Error);
            ScheduleBusReset(1800);
        };

        _voice.HandsFreeChanged += on =>
        {
            HandsFreeCheck.IsChecked = on;
            ShowToast(on ? "ვუსმენ სიტყვა „Fifu“-ს" : "ხმით გააქტიურება გამორთულია");
        };

        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = _voice.RunOrCancelAsync();
                e.Handled = true;
            }
        }), true);

        if (VoiceMic.HandsFree)
            Dispatcher.BeginInvoke(new Action(() => _voice.SetHandsFree(true)), DispatcherPriority.Loaded);
    }

    private void FillMics()
    {
        _micLoading = true;
        MicCombo.Items.Clear();
        MicCombo.Items.Add("Windows default");
        foreach (var d in VoiceMic.Devices()) MicCombo.Items.Add(d);
        MicCombo.SelectedIndex = Math.Max(0, MicCombo.Items.IndexOf(VoiceMic.Selected));
        _micLoading = false;
    }

    private void MicCombo_Loaded(object sender, RoutedEventArgs e) => FillMics();

    private void HandsFreeCheck_Click(object sender, RoutedEventArgs e)
        => _voice.SetHandsFree(HandsFreeCheck.IsChecked == true);

    private void MicCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_micLoading || MicCombo.SelectedItem is not string name) return;
        VoiceMic.Selected = MicCombo.SelectedIndex == 0 ? "" : name;
    }

    private void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        _ = _voice.RunOrCancelAsync();
    }
}