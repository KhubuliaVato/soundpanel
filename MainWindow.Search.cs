using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SoundPanel.Models;
using SoundPanel.Services;

namespace SoundPanel;

/// <summary>Sidebar search: filters the components (folders). Opens on click, folds when you click elsewhere.</summary>
public partial class MainWindow
{
    private bool _groupSearchOpen;
    private const double GroupSearchOpenWidth = 214;
    private string _groupFilter = "";

    private void InitSearch()
    {
        // a click anywhere else folds the field (the typed filter stays active)
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) =>
        {
            if (_groupSearchOpen && !GroupSearchHost.IsMouseOver) SetGroupSearchOpen(false);
        }), true);
    }

    private void GroupSearchToggle_Click(object sender, RoutedEventArgs e) => SetGroupSearchOpen(!_groupSearchOpen);

    private void SetGroupSearchOpen(bool open)
    {
        if (_groupSearchOpen == open)
        {
            if (open) FocusGroupSearch();
            return;
        }

        _groupSearchOpen = open;
        GroupSearchHost.Tag = open ? "Open" : null;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        GroupSearchHost.BeginAnimation(WidthProperty,
            new DoubleAnimation(open ? GroupSearchOpenWidth : 36, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        GroupSearchField.BeginAnimation(OpacityProperty,
            new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(open ? 220 : 120))
            {
                BeginTime = TimeSpan.FromMilliseconds(open ? 90 : 0)
            });
        GroupsTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, TimeSpan.FromMilliseconds(160)));
        GroupSearchField.IsHitTestVisible = open;

        if (open) FocusGroupSearch();
        else if (GroupSearchBox.IsKeyboardFocusWithin) Keyboard.ClearFocus();
    }

    private void FocusGroupSearch() =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            GroupSearchBox.Focus();
            GroupSearchBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);

    private void GroupSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _groupFilter = FuzzySearch.Normalize(GroupSearchBox.Text.Trim());
        var has = _groupFilter.Length > 0;

        var view = CollectionViewSource.GetDefaultView(_vm.Groups);
        if (has)
        {
            // the selected component always stays, so the library never goes blank
            view.Filter = o => o is TrackGroup g &&
                               (ReferenceEquals(g, _vm.SelectedGroup) ||
                                FuzzySearch.Normalize(g.Name).Contains(_groupFilter));
        }
        else
        {
            view.Filter = null;
        }

        GroupSearchDot.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        GroupSearchClear.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GroupSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            GroupSearchBox.Text = "";
            SetGroupSearchOpen(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SetGroupSearchOpen(false);
            e.Handled = true;
        }
    }

    private void GroupSearchClear_Click(object sender, RoutedEventArgs e)
    {
        GroupSearchBox.Text = "";
        FocusGroupSearch();
    }
}