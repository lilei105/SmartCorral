using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
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
    private DesktopWatcher? _watcher;

    // Show-on-Win+D: a WinEvent hook (EVENT_SYSTEM_FOREGROUND) fires the INSTANT the foreground window
    // changes. When it's the desktop (Progman/WorkerW — Win+D / "Show desktop"), float the frames above
    // the desktop so they stay visible; otherwise honor ForceTopmost (off = behind windows, no blocking).
    // Event-driven (not polling) so the flip is immediate — no flicker, no "click twice to surface".
    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    private WinEventProc? _fgProc;       // kept alive (prevent GC) — assigned in StartForegroundTracker
    private IntPtr _fgHook = IntPtr.Zero;
    private bool? _framesTopmost;
    private System.Windows.Threading.DispatcherTimer? _reassertTimer;
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

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
        _frames.SweepDataHealth(); // self-heal: dead links, dup SourcePaths, orphan wrappers/custody
        _frames.RefreshAll();
        Logger.Info($"Startup ready: {_frames.Data.Frames.Count} frame(s).");

        // 6. AI auto-categorize (fire-and-forget; off-thread LLM, UI-thread apply). No-op if not configured.
        _ = AiOrganizeService.RunAsync(_frames, _settings);

        // 7. Watch the desktop for newly-arrived files → incremental auto-categorize. Started AFTER
        //    RetakeAll (so it doesn't see re-custody moves), only if enabled AND AI is configured.
        StartWatcher();
        StartForegroundTracker(); // smart topmost: float frames above the desktop on Win+D only

        // Best-effort restore on a hard exit; RestoreAll at next launch covers anything this misses.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { Logger.Info("ProcessExit: RestoreAll."); CustodyService.RestoreAll(); };
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        Logger.Info("=== Smart Corral exiting (clean) — persist + RestoreAll ===");
        if (_fgHook != IntPtr.Zero) { UnhookWinEvent(_fgHook); _fgHook = IntPtr.Zero; } // remove foreground hook
        StopWatcher();        // stop watching BEFORE RestoreAll moves files back onto the desktop
        _frames?.Persist();
        int kept = CustodyService.RestoreAll(); // clean exit → desktop restored (locked files retry next launch)
        if (kept > 0 && _tray != null)
        {
            // Some files couldn't be moved back (locked by a running app) — they stay in custody and
            // retry next launch. Tell the user so the missing desktop icons aren't a surprise.
            _tray.Balloon("部分文件未还原", $"{kept} 个文件正在运行、未能还原回桌面（下次启动自动重试）。", warn: true);
            System.Threading.Thread.Sleep(1500); // let the toast register before the tray is disposed
        }
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
            // Match the watcher to the (possibly changed) incremental toggle.
            if (_settings.EnableIncrementalCategorize) StartWatcher(); else StopWatcher();
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
        // Pause the watcher around ClearAll: its RestoreAll puts files back on the desktop = Created
        // events we must NOT auto-file (the RunAsync below re-categorizes them).
        _watcher?.Pause();
        _frames?.ClearAll();   // restore files to desktop, wipe frames/shortcuts
        _watcher?.Resume();
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

        // Pause the watcher: ClearAll's RestoreAll returns files to the desktop — must NOT re-file them
        // (the user explicitly asked for everything back).
        _watcher?.Pause();
        _frames?.ClearAll();   // RestoreAll (files → desktop, manifest cleared) + wipe frames/shortcuts
        _frames?.AddFrame();   // reopen one empty welcome frame (so it's not a bare tray)
        _watcher?.Resume();
    }

    // ---- incremental auto-categorize (desktop watcher) ----

    /// <summary>Starts the desktop watcher if incremental categorize is enabled AND AI is configured.
    /// Idempotent — no-op if already running or preconditions aren't met.</summary>
    private void StartWatcher()
    {
        if (_watcher != null) return;
        if (!_settings.EnableIncrementalCategorize) return;
        if (!AiOrganizeService.IsConfigured(_settings))
        {
            Logger.Info("DesktopWatcher: not starting (AI not configured).");
            return;
        }
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _watcher = new DesktopWatcher(desktop, OnNewDesktopFiles);
            _watcher.Start();
            Logger.Info($"DesktopWatcher: watching '{desktop}'.");
        }
        catch (Exception ex)
        {
            Logger.Error("DesktopWatcher: failed to start", ex);
        }
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;
        try { _watcher.Dispose(); } catch { }
        _watcher = null;
        Logger.Info("DesktopWatcher: stopped.");
    }

    /// <summary>Watcher callback (already on the UI thread): categorize ONLY the newly-arrived paths.</summary>
    private void OnNewDesktopFiles(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        if (_frames == null) return;
        _ = AiOrganizeService.CategorizePathsAsync(_frames, _settings, paths,
            onResult: (msg, err) => _tray?.Balloon("自动归类", msg, warn: err));
    }

    /// <summary>Tells the desktop watcher to ignore a path for a short window — call when an item is
    /// released back to the desktop (drag-out) so it isn't immediately auto-filed again.</summary>
    public static void IgnoreWatcherPath(string? path)
    {
        if (!string.IsNullOrEmpty(path) && Application.Current is App a && a._watcher != null)
            a._watcher.IgnorePath(path);
    }

    private void StartForegroundTracker()
    {
        _fgProc = OnForegroundChanged;
        _fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                   _fgProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // One-shot re-assert: "Show desktop" can re-raise the desktop surface a moment AFTER our initial
        // topmost set (covering a frame or two). Re-applying topmost ~220 ms later beats that late raise.
        _reassertTimer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(220) };
        _reassertTimer.Tick += (_, _) =>
        {
            _reassertTimer.Stop();
            if (_framesTopmost == true)
            {
                _frames?.SetAllTopmost(true);
                Logger.Info("Foreground: re-asserted frames topmost (post Win+D)");
            }
        };
    }

    // WinEvent callback (runs on the UI thread via WINEVENT_OUTOFCONTEXT): hwnd = the new foreground.
    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        string name = cls.ToString();
        bool isDesktop = name == "Progman" || name == "WorkerW";

        // When the desktop is the foreground (Win+D / "Show desktop" / clicking the desktop), float the
        // frames above the desktop so they stay visible; otherwise honor ForceTopmost (off → behind
        // windows, not blocking). Win11's "Show desktop" doesn't actually minimize windows, so Win+D and
        // a desktop click are indistinguishable here — we float on both. Reframe: at the desktop = your
        // frames are visible (the organizer's home).
        bool wantTop = _settings.ForceTopmost || isDesktop;

        if (wantTop != _framesTopmost)
        {
            _framesTopmost = wantTop;
            _frames?.SetAllTopmost(wantTop);
            if (wantTop) _reassertTimer?.Start(); else _reassertTimer?.Stop();
            Logger.Info($"Foreground '{name}' -> frames topmost={wantTop}");
        }
    }
}
