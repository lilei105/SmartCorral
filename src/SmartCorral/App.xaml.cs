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

        Logger.TrimIfNeeded();
        Logger.Info("=== Smart Corral starting ===");

        // 2. ONE-TIME legacy recovery: if an older build left the icons globally hidden, restore them.
        //    The custody model clears the desktop per-item now — there is no global hide anymore.
        DesktopShell.RecoverLegacyHide();

        // 3. Crash self-heal: FIRST move every custodied item back to its original desktop path. If the
        //    last run ended cleanly this is a no-op; if it crashed, the desktop is fully restored here.
        CustodyService.RestoreAll();

        // 4. Load settings + tray
        _settings = PersistenceService.LoadSettings();
        Logger.Enabled = _settings.EnableLogging;
        Logger.Info($"Settings loaded (logging={Logger.Enabled}, AI model='{_settings.AiModel}').");
        _tray = new TrayShell(OpenSettings, () => _frames?.ArrangeAll(), ReorganizeAll, RestoreAllFiles);

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
        Logger.Info($"Startup ready: {_frames.Data.Frames.Count} frame(s).");

        // 6. AI auto-categorize (fire-and-forget; off-thread LLM, UI-thread apply). No-op if not configured.
        _ = AiOrganizeService.RunAsync(_frames, _settings);

        // Best-effort restore on a hard exit; RestoreAll at next launch covers anything this misses.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { Logger.Info("ProcessExit: RestoreAll."); CustodyService.RestoreAll(); };
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        Logger.Info("=== Smart Corral exiting (clean) — persist + RestoreAll ===");
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
            Logger.Enabled = _settings.EnableLogging; // pick up the toggle immediately
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
        Logger.Info("Tray: 'AI re-categorize' requested.");
        // Gate the DESTRUCTIVE step (ClearAll) on AI being configured — checked BEFORE ClearAll, not
        // inside RunAsync, so an unconfigured AI never wipes the user's current frames for nothing.
        if (!AiOrganizeService.IsConfigured(_settings))
        {
            Logger.Warn("ReorganizeAll: AI not configured — aborting before ClearAll (frames left intact).");
            _tray?.Balloon("AI 未配置", "请先在「设置」里填好 Base URL / API Key / Model，再点重新归类。", warn: true);
            return;
        }
        // Tell the user it's working right away — the LLM call + filing can take ~10–15 s, which
        // otherwise reads as "nothing happened".
        _tray?.Balloon("AI 分类中", "正在用 AI 重新分类桌面（约 10–15 秒）…");
        // ClearAll restores every custodied item to the desktop first, then wipes frames/shortcuts —
        // so the desktop is repopulated before the AI re-scan (otherwise it'd scan an empty desktop and
        // orphan everything in custody). RunAsync then re-categorizes and re-takes each item.
        _frames?.ClearAll();
        if (_frames != null)
            _ = AiOrganizeService.RunAsync(_frames, _settings, onResult: (msg, err) => _tray?.Balloon("AI 分类", msg, warn: err));
    }

    /// <summary>Tray "Restore all files" panic button: move every custodied item back to its desktop
    /// (ClearAll does the RestoreAll + wipes frames/shortcuts), then reopen one empty welcome frame so
    /// the app looks like a fresh start. A confirm first spells out that frame groupings are cleared.</summary>
    private void RestoreAllFiles()
    {
        Logger.Info("Tray: 'Restore all files' requested.");
        var rc = MessageBox.Show(
            "把所有文件还原回桌面？\n\n框里的归类会被清空（文件不会丢）。之后可重新拖入，或用「AI 重新归类」。",
            "还原所有文件 / Restore all files",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (rc != MessageBoxResult.OK) return;

        _frames?.ClearAll();   // RestoreAll (files → desktop, manifest cleared) + wipe frames/shortcuts
        _frames?.AddFrame();   // reopen one empty welcome frame (so it's not a bare tray)
    }
}
