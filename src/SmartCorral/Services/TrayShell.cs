using System;
using System.Drawing;
using System.Windows.Forms;

// WinForms is enabled only for the tray NotifyIcon; alias the WPF Application to avoid ambiguity.
using WpfApp = System.Windows.Application;
using SmartCorral;

namespace SmartCorral.Services;

/// <summary>
/// System-tray presence so the app can run with no main window.
/// Menu: AI Settings… / Auto-arrange / Re-organize all / Exit.
/// </summary>
public sealed class TrayShell : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayShell(Action onOpenSettings, Action onAutoArrange, Action onReorganizeAll)
    {
        _icon = new NotifyIcon
        {
            Icon = AppIcon(),
            Text = "灵栅 / Smart Corral",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        var header = menu.Items.Add($"灵栅 / Smart Corral  v{AppInfo.Version}");
        header.Enabled = false;
        menu.Items.Add("-");
        menu.Items.Add("设置 / Settings", null, (_, _) => onOpenSettings());
        menu.Items.Add("Auto-arrange", null, (_, _) => onAutoArrange());
        menu.Items.Add("重新整理全部 / Re-organize all", null, (_, _) => onReorganizeAll());
        menu.Items.Add("-");
        menu.Items.Add("退出 / Exit", null, (_, _) => WpfApp.Current?.Shutdown());
        _icon.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>The app's own icon (embedded via &lt;ApplicationIcon&gt;), with a system fallback.</summary>
    private static Icon AppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }
}
