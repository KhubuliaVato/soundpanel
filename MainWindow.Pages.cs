using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SoundPanel;

/// <summary>Library / Downloads / Settings are pages that swap inside the main area.</summary>
public partial class MainWindow
{
    private void InitPages()
    {
        // Fifu's voice / sleep-timer controls live inside Settings > Fifu
        FifuStash.Children.Remove(FifuSettings);
        SettingsPane.HostFifu(FifuSettings);

        SettingsPane.UpdateToolsClicked += async (_, _) =>
        {
            _vm.ShowMessage("yt-dlp ახლდება…");
            await _downloader.UpdateToolsAsync();
            _vm.ShowMessage("yt-dlp-ის განახლება დასრულდა");
        };
        SettingsPane.OpenChanged += (_, _) =>
        {
            if (SettingsPane.IsOpen)
            {
                DownloadPane.Close();
                RefreshSleepUi();
            }
            SyncPages();
        };
        DownloadPane.OpenChanged += (_, _) =>
        {
            if (DownloadPane.IsOpen) SettingsPane.Close();
            SyncPages();
        };

        InitSearch();

        // first run: ask for the music folder right away (afterwards it is remembered)
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_vm.HasRoot) ChooseFolder_Click(this, new RoutedEventArgs());
        }), DispatcherPriority.ApplicationIdle);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) => UpdateDownloadBadge();
        timer.Start();
    }

    private void SyncPages()
    {
        var otherPage = SettingsPane.IsOpen || DownloadPane.IsOpen;

        NavDownloads.IsChecked = DownloadPane.IsOpen;
        NavSettings.IsChecked = SettingsPane.IsOpen;

        if (otherPage)
        {
            LibraryPage.Visibility = Visibility.Collapsed;
        }
        else if (LibraryPage.Visibility != Visibility.Visible)
        {
            LibraryPage.Visibility = Visibility.Visible;
            LibraryPage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
    }

    private void UpdateDownloadBadge()
    {
        var running = _downloader.Items.Cast<SoundPanel.Models.DownloadItem>().Count(i => i.IsRunning);
        DownloadBadge.Text = running.ToString();
        DownloadBadgeBox.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavDownloads_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadPane.IsOpen) DownloadPane.Close();
        else OpenDownloader();
        SyncPages(); // keeps the toggle in line with the real state
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPane.IsOpen) SettingsPane.Close();
        else SettingsPane.Open();
        SyncPages();
    }

    private void GroupsList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // picking a component always shows the library
        SettingsPane.Close();
        DownloadPane.Close();
    }
}