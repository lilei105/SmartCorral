using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartCorral.Interop;

/// <summary>
/// A WPF window that never steals focus when clicked/moved/resized — essential for desktop
/// frames that must float over the desktop without disrupting the active window.
/// Salvaged technique: WS_EX_NOACTIVATE on the HWND + MA_NOACTIVATE from WM_MOUSEACTIVATE.
/// </summary>
public class NonActivatingWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        IntPtr handle = helper.Handle;

        // Add WS_EX_NOACTIVATE so Windows never gives this window the keyboard focus.
        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

        // Intercept WM_MOUSEACTIVATE and tell Windows not to activate on click.
        HwndSource? source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }
}
