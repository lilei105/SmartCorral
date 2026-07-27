using System;
using System.IO;
using SmartCorral.Services.Platform;

namespace SmartCorral.Services;

/// <summary>
/// Owns the "session tidy" of the desktop: hides native icons while Smart Corral runs and
/// reliably restores them on exit. Crash self-heal: a flag file remembers the original state,
/// so if the app is killed (no clean exit), the next launch restores it.
/// </summary>
public static class DesktopShell
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FlagFile = Path.Combine(DataDir, ".icons_hidden");

    private static bool _originalVisible = true;

    /// <summary>Call at startup. Performs crash-recovery (if a prior run left icons hidden),
    /// then records the current state, hides icons, and arms the flag file.</summary>
    public static void Startup()
    {
        try
        {
            Directory.CreateDirectory(DataDir);

            // 1. Crash recovery: a leftover flag means the last run never restored — fix that first.
            if (File.Exists(FlagFile))
            {
                if (bool.TryParse(File.ReadAllText(FlagFile), out bool recorded))
                {
                    _originalVisible = recorded;
                }
                DesktopIconHider.SetIconsVisible(_originalVisible);
                File.Delete(FlagFile);
            }

            // 2. Record the real current state, then hide.
            _originalVisible = DesktopIconHider.AreIconsVisible();
            File.WriteAllText(FlagFile, _originalVisible.ToString());
            DesktopIconHider.SetIconsVisible(false);
        }
        catch
        {
            // Never let desktop-toggle failure kill startup.
        }
    }

    /// <summary>Call on shutdown (Exit + ProcessExit). Restores the original state, clears the flag.
    /// Idempotent — safe to call multiple times.</summary>
    public static void Shutdown()
    {
        try
        {
            DesktopIconHider.SetIconsVisible(_originalVisible);
            if (File.Exists(FlagFile)) File.Delete(FlagFile);
        }
        catch
        {
            // best-effort
        }
    }
}
