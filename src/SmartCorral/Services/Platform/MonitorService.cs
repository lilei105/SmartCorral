using System;
using System.Windows;
using WF = System.Windows.Forms;

namespace SmartCorral.Services.Platform;

/// <summary>
/// Multi-monitor work-area lookup, returning WPF DIP rects. DpiScale is captured from the first
/// frame window at load (per-monitor-DPI-v2 with mixed scales is approximated to one scale).
/// </summary>
public static class MonitorService
{
    public static double DpiScaleX { get; set; } = 1.0;
    public static double DpiScaleY { get; set; } = 1.0;

    /// <summary>Work area (DIPs) of the monitor that contains the given DIP point.</summary>
    public static Rect WorkAreaForPoint(double xDip, double yDip)
    {
        try
        {
            int dx = (int)(xDip * DpiScaleX);
            int dy = (int)(yDip * DpiScaleY);
            var r = WF.Screen.FromPoint(new System.Drawing.Point(dx, dy)).WorkingArea;
            return new Rect(r.Left / DpiScaleX, r.Top / DpiScaleY, r.Width / DpiScaleX, r.Height / DpiScaleY);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    /// <summary>Work area (DIPs) of the monitor the mouse cursor is on.</summary>
    public static Rect WorkAreaForMouse()
    {
        try
        {
            var r = WF.Screen.FromPoint(WF.Cursor.Position).WorkingArea;
            return new Rect(r.Left / DpiScaleX, r.Top / DpiScaleY, r.Width / DpiScaleX, r.Height / DpiScaleY);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }
}
