using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SoundPanel;

/// <summary>Sound-processing page: sidebar button + opening/closing next to the other pages.</summary>
public partial class MainWindow
{
    private void InitFx()
    {
        FxPane.OpenChanged += (_, _) =>
        {
            NavFx.IsChecked = FxPane.IsOpen;

            if (FxPane.IsOpen)
            {
                LibraryPage.Visibility = Visibility.Collapsed;
            }
            else if (!SettingsPane.IsOpen && !DownloadPane.IsOpen)
            {
                LibraryPage.Visibility = Visibility.Visible;
                LibraryPage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            }
        };

        // opening another page closes this one
        SettingsPane.OpenChanged += (_, _) =>
        {
            if (SettingsPane.IsOpen) FxPane.Close();
        };
        DownloadPane.OpenChanged += (_, _) =>
        {
            if (DownloadPane.IsOpen) FxPane.Close();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FxPane.IsOpen)
            {
                FxPane.Close();
                e.Handled = true;
            }
        };
    }

    private void NavFx_Click(object sender, RoutedEventArgs e)
    {
        if (FxPane.IsOpen)
        {
            FxPane.Close();
            return;
        }

        SettingsPane.Close();
        DownloadPane.Close();
        FxPane.Open();
    }
}