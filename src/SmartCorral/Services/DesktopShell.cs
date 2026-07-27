using System;
using System.IO;
using SmartCorral.Services.Platform;

namespace SmartCorral.Services;

/// <summary>
/// Owns the "session tidy" of the desktop: hides native icons while Smart Corral runs and
/// ALWAYS restores them (visible) on exit. A flag file arms crash self-heal: if the app is
/// killed without a clean exit, the next launch sees the flag and force-restores.
///
/// NOTE: we intentionally do NOT trust a captured "original state". A prior hard crash can leave
/// the desktop hidden, which would make us capture the wrong state and then "restore" to hidden.
/// Restoring to visible on exit is the safe, correct behavior for the session-tidy model.
/// </summary>
public static class DesktopShell
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FlagFile = Path.Combine(DataDir, ".icons_hidden");

    public static void Startup()
    {
        try
        {
            Directory.CreateDirectory(DataDir);

            // Crash recovery: a leftover flag means the last run never restored — force visible now.
            if (File.Exists(FlagFile))
            {
                DesktopIconHider.SetIconsVisible(true);
                File.Delete(FlagFile);
            }

            // Arm the flag, then hide.
            File.WriteAllText(FlagFile, "hidden");
            DesktopIconHider.SetIconsVisible(false);
        }
        catch
        {
            // Never let desktop-toggle failure kill startup.
        }
    }

    /// <summary>Always restores icons to visible (idempotent; safe to call multiple times).</summary>
    public static void Shutdown()
    {
        try
        {
            DesktopIconHider.SetIconsVisible(true);
            if (File.Exists(FlagFile)) File.Delete(FlagFile);
        }
        catch
        {
            // best-effort
        }
    }
}
