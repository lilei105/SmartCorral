using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SmartCorral.Services;
using SmartCorral.Services.Platform;

namespace SmartCorral.Interop;

/// <summary>
/// A WPF window that never steals focus when clicked/moved/resized — essential for desktop
/// frames that must float over the desktop without disrupting the active window.
/// Salvaged technique: WS_EX_NOACTIVATE on the HWND + MA_NOACTIVATE from WM_MOUSEACTIVATE.
/// Also handles WM_DPICHANGED so the cached MonitorService scale stays current when a frame
/// moves between monitors of different DPI (PerMonitorV2 app — see app.manifest).
/// </summary>
public class NonActivatingWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_DPICHANGED_BEFOREPARENT = 0x02E2;
    private const int WM_DPICHANGED_AFTERPARENT = 0x02E3;

    private const double BaseDpi = 96.0;

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

        // Seed the shared scale from this window's own DPI, so it is correct before the first
        // WM_DPICHANGED fires (and even when the app runs entirely on one monitor).
        var d = VisualTreeHelper.GetDpi(this);
        MonitorService.DpiScaleX = d.DpiScaleX;
        MonitorService.DpiScaleY = d.DpiScaleY;

        // Intercept WM_MOUSEACTIVATE (no activate on click) and WM_DPICHANGED (refresh scale).
        HwndSource? source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return (IntPtr)MA_NOACTIVATE;

            case WM_DPICHANGED:
            case WM_DPICHANGED_BEFOREPARENT:
            case WM_DPICHANGED_AFTERPARENT:
                HandleDpiChanged(wParam);
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Re-capture the new DPI from WM_DPICHANGED's wParam (HIWORD = y, LOWORD = x, in DPI units)
    /// and refresh the shared MonitorService scale. Override to react (e.g. re-render icons).
    /// </summary>
    private void HandleDpiChanged(IntPtr wParam)
    {
        int dword = (int)(wParam.ToInt64() & 0xFFFFFFFF);
        double x = (ushort)(dword & 0xFFFF) / BaseDpi;      // LOWORD
        double y = (ushort)((dword >> 16) & 0xFFFF) / BaseDpi; // HIWORD

        MonitorService.DpiScaleX = x;
        MonitorService.DpiScaleY = y;

        OnDpiChanged(x, y);
    }

    /// <summary>Override to react to a DPI change (e.g. re-render items at a new size).</summary>
    protected virtual void OnDpiChanged(double dpiScaleX, double dpiScaleY) { }
}
