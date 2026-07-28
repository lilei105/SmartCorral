using System.Windows;
using SmartCorral.Models;
using SmartCorral.Services;
using SmartCorral.Services.Ai;
using SmartCorral.Views;

// Resolve the WPF Application (avoid ambiguity with System.Windows.Forms.Application, since WinForms is enabled for the tray).
using Application = System.Windows.Application;

namespace SmartCorral;

public partial class App : Application
{
    private TrayShell? _tray;
    private FrameManager? _frames;
    private AppSettings _settings = new();

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        // 1. Single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
            Shutdown();
            return;
        }

        // 2. ONE-TIME legacy recovery: if an older build left the icons globally hidden, restore them.
        //    The custody model clears the desktop per-item now — there is no global hide anymore.
        DesktopShell.RecoverLegacyHide();

        // 3. Crash self-heal: FIRST move every custodied item back to its original desktop path. If the
        //    last run ended cleanly this is a no-op; if it crashed, the desktop is fully restored here.
        CustodyService.RestoreAll();

        // 4. Load settings + tray
        _settings = PersistenceService.LoadSettings();
        _tray = new TrayShell(OpenSettings, () => _frames?.ArrangeAll(), ReorganizeAll);

        // 5. Load frames + show windows, then re-custody everything already filed (RestoreAll put the
        //    items back on the desktop; this moves each into a fresh custody path for THIS session and
        //    re-points the raw-file shortcuts so icons/launch stay correct).
        _frames = new FrameManager();
        _frames.IconsPerRow = _settings.IconsPerRow;
        _frames.SeparateFolders = _settings.SeparateFolders;
        _frames.ForceTopmost = _settings.ForceTopmost;
        _frames.UIScale = _settings.UIScale;
        _frames.Initialize();
        _frames.RetakeAllIntoCustody();
        _frames.RefreshAll();

        // 6. AI auto-categorize (fire-and-forget; off-thread LLM, UI-thread apply). No-op if not configured.
        _ = AiOrganizeService.RunAsync(_frames, _settings);

        // Best-effort restore on a hard exit; RestoreAll at next launch covers anything this misses.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CustodyService.RestoreAll();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        _frames?.Persist();
        CustodyService.RestoreAll(); // clean exit → desktop fully restored, manifest cleared
        _tray?.Dispose();
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings) { Owner = null };
        bool? ok = w.ShowDialog();
        if (ok == true && _frames != null)
        {
            _frames.IconsPerRow = _settings.IconsPerRow;
            _frames.SeparateFolders = _settings.SeparateFolders;
            _frames.ForceTopmost = _settings.ForceTopmost;
            _frames.ApplyTopmost();
            _frames.UIScale = _settings.UIScale;
            _frames.SizeFramesToContent();
            _frames.RefreshAll();
            _frames.ArrangeAll();
        }
    }

    private void ReorganizeAll()
    {
        // ClearAll restores every custodied item to the desktop first, then wipes frames/shortcuts —
        // so the desktop is repopulated before the AI re-scan (otherwise it'd scan an empty desktop and
        // orphan everything in custody). RunAsync then re-categorizes and re-takes each item.
        _frames?.ClearAll();
        if (_frames != null) _ = AiOrganizeService.RunAsync(_frames, _settings);
    }
}
