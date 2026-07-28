using System;
using System.IO;
using SmartCorral.Services.Platform;

namespace SmartCorral.Services;

/// <summary>
/// Owns the "session tidy" of the desktop: hides native icons while Smart Corral is actively
/// organizing (there are items in frames) and restores them on exit. Hiding uses the OS "Show
/// desktop icons" toggle (DesktopIconHider.ToggleDesktopIcons), which keeps the desktop list-view
/// active so rubber-band drag-selection still works.
///
/// Icons are NOT hidden on a fresh/empty run — that way the user can see their desktop, read the
/// tip on the empty frame, and drag files in (or configure AI). Hiding kicks in the moment the
/// first item lands in a frame (Hide()).
///
/// The toggle FLIPS state, so absolute state is tracked with a flag file (content "toggle"): every
/// hide writes it; every restore (clean shutdown or next-launch crash recovery) toggles once and
/// clears it. Icons always come back — even after a hard crash — without double-toggling.
/// </summary>
public static class DesktopShell
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FlagFile = Path.Combine(DataDir, ".icons_hidden");
    private const string ToggleFlag = "toggle";

    /// <summary>Call at startup. If <paramref name="hide"/> is true, hides the icons now (there's
    /// content to organize); otherwise leaves them visible (fresh/empty run). Always undoes any
    /// leftover SW_HIDE and recovers from a prior crash.</summary>
    public static void Startup(bool hide)
    {
        try
        {
            Directory.CreateDirectory(DataDir);

            // Undo any leftover SW_HIDE from the old hiding method so the native toggle keeps drag-select.
            DesktopIconHider.ShowDesktopListView();

            // Crash recovery: only a "toggle" flag means icons are natively hidden now.
            if (File.Exists(FlagFile))
            {
                bool nativelyHidden = File.ReadAllText(FlagFile).Trim() == ToggleFlag;
                File.Delete(FlagFile);
                if (nativelyHidden)
                    DesktopIconHider.ToggleDesktopIcons(); // restore to visible
            }

            if (hide) Hide();
        }
        catch
        {
            // Never let desktop-toggle failure kill startup.
        }
    }

    /// <summary>Hides the icons if not already hidden (idempotent). Called when the first item
    /// lands in a frame — the transition from "empty/welcome" to "organizing".</summary>
    public static void Hide()
    {
        try
        {
            if (File.Exists(FlagFile)) return; // already hidden this session
            if (DesktopIconHider.ToggleDesktopIcons())
                File.WriteAllText(FlagFile, ToggleFlag);
        }
        catch { }
    }

    /// <summary>Restores icons to visible — only if we hid them (flag set). Idempotent.</summary>
    public static void Shutdown()
    {
        try
        {
            if (File.Exists(FlagFile))
            {
                DesktopIconHider.ToggleDesktopIcons(); // undo our hide → visible
                File.Delete(FlagFile);
            }
        }
        catch { }
    }
}
