using System.Windows;
using SmartCorral.Services;
using SmartCorral.Views;

// Resolve the WPF Application (avoid ambiguity with System.Windows.Forms.Application, since WinForms is enabled for the tray).
using Application = System.Windows.Application;

namespace SmartCorral;

public partial class App : Application
{
    private TrayShell? _tray;
    private FrameWindow? _frame;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        // 1. Single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
            Shutdown();
            return;
        }

        // 2. Tray (so the app has a presence without a main window)
        _tray = new TrayShell();

        // 3. Show the first frame ("hello frame")
        _frame = new FrameWindow();
        _frame.Show();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
    }
}
