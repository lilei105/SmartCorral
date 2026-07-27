using System;
using System.Drawing;
using System.Windows.Forms;

// WinForms is enabled only for the tray NotifyIcon; alias the WPF Application to avoid ambiguity.
using WpfApp = System.Windows.Application;

namespace SmartCorral.Services;

/// <summary>
/// System-tray presence so the app can run with no main window.
/// Menu: AI Settings… / Auto-arrange / Exit.
/// </summary>
public sealed class TrayShell : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayShell(Action onOpenSettings, Action onAutoArrange)
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "灵栅 / Smart Corral",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("AI Settings…", null, (_, _) => onOpenSettings());
        menu.Items.Add("Auto-arrange", null, (_, _) => onAutoArrange());
        menu.Items.Add("-");
        menu.Items.Add("退出 / Exit", null, (_, _) => WpfApp.Current?.Shutdown());
        _icon.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
