using System;
using System.Runtime.InteropServices;

namespace SmartCorral.Services.Platform;

/// <summary>
/// Hides/shows native desktop icons via the OS "Show desktop icons" toggle — the same one the
/// desktop right-click → View menu uses — by sending WM_COMMAND 0x7402 to SHELLDLL_DefView.
/// (0x7402 is the verified Win10/11 command ID; the older 0x7000 does not work on modern builds.)
///
/// This keeps the list-view control active (only the icons disappear), so rubber-band
/// drag-selection on the desktop still works — unlike hiding the SysListView32 window (SW_HIDE),
/// which removed the whole control and killed drag-select.
///
/// The 0x7402 command is a TOGGLE (flips the current state), not a set, so callers must track
/// absolute state themselves (see DesktopShell's flag-file logic).
/// </summary>
internal static class DesktopIconHider
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const uint WM_COMMAND = 0x0111;
    private static readonly IntPtr ToggleDesktopIconsCmd = new(0x7402);
    private const int SW_SHOW = 5;

    /// <summary>The desktop SHELLDLL_DefView (Progman child, or under a WorkerW when a wallpaper
    /// engine has reparented the desktop view). Desktop view commands go to this window.</summary>
    public static IntPtr GetDesktopDefView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            IntPtr workerW = IntPtr.Zero;
            do
            {
                workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            } while (defView == IntPtr.Zero && workerW != IntPtr.Zero);
        }

        return defView;
    }

    /// <summary>The desktop icon list-view (SysListView32), child of SHELLDLL_DefView.</summary>
    public static IntPtr GetDesktopListView()
    {
        IntPtr defView = GetDesktopDefView();
        return defView != IntPtr.Zero
            ? FindWindowEx(defView, IntPtr.Zero, "SysListView32", null)
            : IntPtr.Zero;
    }

    /// <summary>Ensures the list-view window is shown. Undoes any leftover SW_HIDE from the old
    /// hiding method, so the native toggle below keeps drag-selection working. Idempotent.</summary>
    public static void ShowDesktopListView()
    {
        IntPtr lv = GetDesktopListView();
        if (lv != IntPtr.Zero) ShowWindow(lv, SW_SHOW);
    }

    /// <summary>Toggles the OS "Show desktop icons" setting. Returns true if the command was sent.
    /// It FLIPS state — pair with DesktopShell's flag-file logic to track absolute state.</summary>
    public static bool ToggleDesktopIcons()
    {
        IntPtr defView = GetDesktopDefView();
        if (defView == IntPtr.Zero) return false;
        SendMessage(defView, WM_COMMAND, ToggleDesktopIconsCmd, IntPtr.Zero);
        return true;
    }
}
