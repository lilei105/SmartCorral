using System;
using System.Runtime.InteropServices;

namespace SmartCorral.Services.Platform;

/// <summary>
/// Hides/shows the native Windows desktop icons by toggling the desktop SysListView32 window.
/// Salvaged technique (vetted): Progman -> SHELLDLL_DefView -> SysListView32 (with a WorkerW
/// fallback for when a wallpaper engine has reparented the desktop view). ShowWindow hides the
/// whole icon list-view at once — same trick Fences uses. Pure Win32, no dependency.
/// </summary>
public static class DesktopIconHider
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    /// <summary>Returns the HWND of the desktop icon list-view (SysListView32), or Zero if not found.</summary>
    public static IntPtr GetDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        // Fallback: with an active wallpaper engine the desktop view often lives under a WorkerW.
        if (defView == IntPtr.Zero)
        {
            IntPtr workerW = IntPtr.Zero;
            do
            {
                workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            } while (defView == IntPtr.Zero && workerW != IntPtr.Zero);
        }

        return defView != IntPtr.Zero
            ? FindWindowEx(defView, IntPtr.Zero, "SysListView32", null)
            : IntPtr.Zero;
    }

    public static bool AreIconsVisible()
    {
        IntPtr lv = GetDesktopListView();
        return lv != IntPtr.Zero && IsWindowVisible(lv);
    }

    public static void SetIconsVisible(bool visible)
    {
        IntPtr lv = GetDesktopListView();
        if (lv != IntPtr.Zero)
        {
            ShowWindow(lv, visible ? SW_SHOW : SW_HIDE);
        }
    }
}
