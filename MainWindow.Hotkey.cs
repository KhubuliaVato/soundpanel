using System.Windows;
using System.Windows.Interop;
using SoundPanel.Services;
using SoundPanel.ViewModels;

namespace SoundPanel;

/// <summary>Global hotkey that opens Fifu's quick search, even while the app is minimized.</summary>
public partial class MainWindow
{
    private const int QuickSearchHotkeyId = 0x1F1F;

    private IntPtr _hwnd;
    private string? _activeHotkey;
    private bool _hotkeyRevert;
    private QuickSearchWindow? _quick;

    private void InitHotkey()
    {
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(HotkeyHook);
            ApplyHotkey(false);
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.QuickSearchHotkey) && !_hotkeyRevert)
                ApplyHotkey(true);
        };

        Closed += (_, _) => HotkeyService.Unregister(_hwnd, QuickSearchHotkeyId);
    }

    private void ApplyHotkey(bool announce)
    {
        var previous = _activeHotkey;
        HotkeyService.Unregister(_hwnd, QuickSearchHotkeyId);
        _activeHotkey = null;

        var wanted = _vm.QuickSearchHotkey;
        if (string.IsNullOrWhiteSpace(wanted))
        {
            if (announce) ShowToast("სწრაფი ძებნა გამორთულია");
            return;
        }

        if (HotkeyService.Register(_hwnd, QuickSearchHotkeyId, wanted))
        {
            _activeHotkey = wanted;
            if (announce) ShowToast($"მზადაა! {wanted} გახსნის Fifu-ს ძებნას");
            return;
        }

        // taken by another program: go back to the previous combination
        ShowToast($"{wanted} სხვა პროგრამას უკავია, სცადე სხვა კომბინაცია");
        _hotkeyRevert = true;
        _vm.QuickSearchHotkey = previous ?? "";
        _hotkeyRevert = false;

        if (!string.IsNullOrEmpty(previous) && HotkeyService.Register(_hwnd, QuickSearchHotkeyId, previous))
            _activeHotkey = previous;
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WmHotkey && wParam.ToInt32() == QuickSearchHotkeyId)
        {
            handled = true;
            OpenQuickSearch();
        }
        return IntPtr.Zero;
    }

    private void OpenQuickSearch()
    {
        if (!_vm.HasRoot) return;

        if (_quick != null)
        {
            _quick.Activate();
            _quick.ToggleVoice();
            return;
        }

        _quick = new QuickSearchWindow(_vm, _voice);
        _quick.Closed += (_, _) => _quick = null;
        _quick.Show();
        _quick.Activate();
    }
}