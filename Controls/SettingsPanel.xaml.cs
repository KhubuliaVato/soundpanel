using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoundPanel.ViewModels;

namespace SoundPanel.Controls;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    public bool IsOpen { get; private set; }

    public event EventHandler? DownloaderRequested;
    public event EventHandler? OpenChanged;

    /// <summary>"yt-dlp update" was pressed; MainWindow runs it through the downloader.</summary>
    public event EventHandler? UpdateToolsClicked;

    private void UpdateTools_Click(object sender, RoutedEventArgs e) => UpdateToolsClicked?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------ page open / close

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

        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ Fifu section

    /// <summary>MainWindow builds Fifu's voice / sleep-timer controls and places them here.</summary>
    public void HostFifu(UIElement content) => FifuHost.Content = content;

    public void ShowFifu() => CatFifu.IsChecked = true;

    // ------------------------------------------------------------ categories

    private void Cat_Checked(object sender, RoutedEventArgs e)
    {
        if (SecAbout == null || sender is not RadioButton { Tag: string tag }) return; // still loading

        SecGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        SecLook.Visibility = tag == "Look" ? Visibility.Visible : Visibility.Collapsed;
        SecFifu.Visibility = tag == "Fifu" ? Visibility.Visible : Visibility.Collapsed;
        SecAudio.Visibility = tag == "Audio" ? Visibility.Visible : Visibility.Collapsed;
        SecKeys.Visibility = tag == "Keys" ? Visibility.Visible : Visibility.Collapsed;
        SecAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

        SectionScroll.ScrollToTop();
        Sections.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    // ------------------------------------------------------------ quick-search hotkey capture

    private bool _capturing;

    private MainViewModel? MainVm => DataContext as MainViewModel;

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        HotkeyHint.Text = "დააჭირე კომბინაციას… (Esc — გაუქმება)";
        HotkeyHint.Visibility = Visibility.Visible;
        HotkeyButton.Focus();
    }

    private void HotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Escape)
        {
            StopCapture();
            return;
        }

        if (SoundPanel.Services.HotkeyService.IsModifierKey(key)) return; // still holding Ctrl/Alt/Shift

        var text = SoundPanel.Services.HotkeyService.Format(key, System.Windows.Input.Keyboard.Modifiers);
        if (text == null)
        {
            HotkeyHint.Text = "საჭიროა Ctrl, Alt ან Shift + ღილაკი";
            return;
        }

        if (MainVm != null) MainVm.QuickSearchHotkey = text;
        StopCapture();
    }

    private void HotkeyButton_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => StopCapture();

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm != null) MainVm.QuickSearchHotkey = "";
        StopCapture();
    }

    private void StopCapture()
    {
        _capturing = false;
        HotkeyHint.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ library folder + reset

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm == null) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "აირჩიე საქაღალდე მუსიკით" };
        if (MainVm.HasRoot) dialog.InitialDirectory = MainVm.RootFolder;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            MainVm.SetRootFolder(dialog.FolderName);
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => MainVm?.ResetSettings();
}