using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoundPanel.Models;
using SoundPanel.ViewModels;

namespace SoundPanel.Controls;

public partial class DownloadPanel : UserControl
{
    private static readonly Regex UrlRx = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private DownloadItem? _moveItem;

    public DownloadPanel()
    {
        InitializeComponent();
    }

    public bool IsOpen { get; private set; }

    public event EventHandler? OpenChanged;

    private DownloaderViewModel? Vm => DataContext as DownloaderViewModel;

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

        // a copied link is pasted automatically when the box is empty
        if (Vm != null && string.IsNullOrWhiteSpace(Vm.UrlText) && ClipboardLink() is { } link)
            Vm.UrlText = link;

        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)));
        SurfaceTr.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        Dispatcher.BeginInvoke(() => UrlBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        MovePopup.IsOpen = false;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (!IsOpen) Visibility = Visibility.Collapsed;
        };
        Surface.BeginAnimation(OpacityProperty, fade);
        SurfaceTr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(160)));

        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ form

    private void Add_Click(object sender, RoutedEventArgs e) => Vm?.Add();

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || ClipboardLink() is not { } link) return;
        Vm.UrlText = string.IsNullOrWhiteSpace(Vm.UrlText) ? link : Vm.UrlText.TrimEnd() + "\n" + link;
    }

    private void UrlBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Vm?.Add();
            e.Handled = true;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => Vm?.SelectAllEntries(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => Vm?.SelectAllEntries(false);

    private void ClearFinished_Click(object sender, RoutedEventArgs e) => Vm?.ClearFinished();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.UpdateToolsAsync();
    }

    // ------------------------------------------------------------ queue items

    private static DownloadItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as DownloadItem;

    private void ItemCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Cancel(item);
    }

    private void ItemPause_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Pause(item);
    }

    private void ItemResume_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Resume(item);
    }

    private void ItemRetry_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Retry(item);
    }

    private void ItemRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Remove(item);
    }

    private void ItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.Play(item);
    }

    private void ItemFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.ShowInFolder(item);
    }

    private void ItemMove_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        _moveItem = item;
        MoveList.SelectedIndex = -1;
        MovePopup.IsOpen = true;
    }

    private void MoveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MoveList.SelectedItem is TrackGroup group && _moveItem != null)
        {
            var item = _moveItem;
            _moveItem = null;
            MovePopup.IsOpen = false;
            Vm?.Move(item, group);
        }

        MoveList.SelectedIndex = -1;
    }

    // ------------------------------------------------------------ search results

    private void SearchRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundPanel.Services.SearchResult result)
            Vm?.UseResult(result);
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e) => Vm?.LoadMore();

    private void SearchAdd_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundPanel.Services.SearchResult result)
            Vm?.QuickAdd(result);
    }

    // ------------------------------------------------------------ helpers

    private static string? ClipboardLink()
    {
        try
        {
            if (!Clipboard.ContainsText()) return null;
            var text = Clipboard.GetText().Trim();
            return text.Length is > 0 and < 4000 && UrlRx.IsMatch(text) ? text : null;
        }
        catch
        {
            return null; // clipboard is locked by another app
        }
    }
} 