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

        // 2. Load settings + tray
        _settings = PersistenceService.LoadSettings();
        _tray = new TrayShell(OpenSettings, () => _frames?.ArrangeAll());

        // 3. Take over the desktop: hide native icons (restore on exit; crash-recover on next launch).
        DesktopShell.Startup();

        // 4. Load frames + show windows
        _frames = new FrameManager();
        _frames.Initialize();

        // 5. AI auto-categorize (fire-and-forget; off-thread LLM, UI-thread apply). No-op if not configured.
        _ = AiOrganizeService.RunAsync(_frames, _settings);

        // Best-effort restore on a hard exit; the flag file covers anything this misses next launch.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DesktopShell.Shutdown();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        _frames?.Persist();
        DesktopShell.Shutdown();
        _tray?.Dispose();
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings) { Owner = null };
        w.ShowDialog();
    }
}
