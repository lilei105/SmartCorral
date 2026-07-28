using System.IO;
using SmartCorral.Services.Platform;

namespace SmartCorral.Services;

/// <summary>
/// Legacy one-shot recovery only. Smart Corral no longer hides desktop icons globally — the custody
/// model (CustodyService) clears the desktop per-item by moving files off it. This remains to undo a
/// leftover global hide from an OLDER build that crashed mid-session (its ".icons_hidden" flag): on the
/// first launch of the custody build it toggles the icons back to visible once and clears the flag.
/// After that this class does nothing.
/// </summary>
public static class DesktopShell
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FlagFile = Path.Combine(DataDir, ".icons_hidden");
    private const string ToggleFlag = "toggle";

    /// <summary>Call once at startup. If an older build left the icons globally hidden (flag present),
    /// restore them to visible and clear the flag. No-op afterwards (and never hides).</summary>
    public static void RecoverLegacyHide()
    {
        try
        {
            // Undo any leftover SW_HIDE from the very old hiding method, so the native toggle keeps drag-select.
            DesktopIconHider.ShowDesktopListView();

            if (File.Exists(FlagFile))
            {
                bool nativelyHidden = File.ReadAllText(FlagFile).Trim() == ToggleFlag;
                File.Delete(FlagFile);
                if (nativelyHidden)
                    DesktopIconHider.ToggleDesktopIcons(); // restore to visible
            }
        }
        catch
        {
            // Never let desktop-toggle failure kill startup.
        }
    }
}
