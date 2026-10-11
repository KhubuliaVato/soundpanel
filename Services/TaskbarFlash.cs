using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundPanel.Services;

/// <summary>Makes the taskbar button blink (until the window is focused) when a download finishes in the background.</summary>
public static class TaskbarFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    public static void Flash(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle,
                dwFlags = 0x0E, // FLASHW_TRAY | FLASHW_TIMERNOFG
                uCount = uint.MaxValue,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch { /* cosmetic only */ }
    }
}