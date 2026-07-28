using System;
using System.IO;
using SmartCorral.Services.Platform;

namespace SmartCorral.Services;

/// <summary>
/// Owns the "session tidy" of the desktop: hides native icons while Smart Corral runs and restores
/// them on exit. Hiding uses the OS "Show desktop icons" toggle (DesktopIconHider.ToggleDesktopIcons),
/// which keeps the desktop list-view active so rubber-band drag-selection still works.
///
/// That toggle FLIPS state rather than setting it, so we track absolute state with a flag file:
/// every hide writes the flag with content "toggle"; every restore (clean shutdown or next-launch
/// crash recovery) toggles once and clears it. Only a "toggle" flag means icons are natively
/// hidden and need a recovery toggle — a stale "hidden" flag is the old SW_HIDE mechanism (handled
/// by ShowDesktopListView), so it's just cleared.
/// </summary>
public static class DesktopShell
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FlagFile = Path.Combine(DataDir, ".icons_hidden");
    private const string ToggleFlag = "toggle";

    public static void Startup()
    {
        try
        {
            Directory.CreateDirectory(DataDir);

            // Make sure the list-view window itself is shown — undoes any leftover SW_HIDE from the
            // old hiding method so the native toggle below keeps drag-selection working.
            DesktopIconHider.ShowDesktopListView();

            // Crash recovery: only a "toggle" flag means icons are natively hidden now. (A stale
            // "hidden" flag is from the old SW_HIDE method; ShowDesktopListView already fixed that.)
            if (File.Exists(FlagFile))
            {
                bool nativelyHidden = File.ReadAllText(FlagFile).Trim() == ToggleFlag;
                File.Delete(FlagFile);
                if (nativelyHidden)
                    DesktopIconHider.ToggleDesktopIcons(); // restore to visible
            }

            // Hide for this session (one toggle). Arm the flag so a crash can self-heal next launch.
            if (DesktopIconHider.ToggleDesktopIcons())
                File.WriteAllText(FlagFile, ToggleFlag);
        }
        catch
        {
            // Never let desktop-toggle failure kill startup.
        }
    }

    /// <summary>Restores icons to visible — only if we hid them (flag set), to avoid toggling
    /// when we never did. Idempotent: safe to call multiple times.</summary>
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
        catch
        {
            // best-effort
        }
    }
}
