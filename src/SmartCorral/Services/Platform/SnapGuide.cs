using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartCorral.Services.Platform;

/// <summary>
/// Click-through, non-activating overlay that draws short, fading blue alignment lines while a
/// frame snaps. Each line is anchored at the frame's relevant corner and extends only ~200px,
/// fading to transparent at both ends (instead of a crude full-screen rule).
/// </summary>
public static class SnapGuide
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const double Extent = 100.0; // line half-length in px (~200 total)
    private static readonly Color LineColor = Color.FromRgb(0x5B, 0x6C, 0xFF);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static Window? _win;
    private static Line? _vline;
    private static Line? _hline;

    private static void Ensure()
    {
        if (_win != null) return;

        _win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            IsHitTestVisible = false,
            ResizeMode = ResizeMode.NoResize,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };

        _win.SourceInitialized += (s, e) =>
        {
            IntPtr h = new WindowInteropHelper(_win).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        };

        var canvas = new Canvas();
        _vline = MakeLine(vertical: true);
        _hline = MakeLine(vertical: false);
        canvas.Children.Add(_vline);
        canvas.Children.Add(_hline);
        _win.Content = canvas;
        _win.Show();
    }

    private static Line MakeLine(bool vertical)
    {
        // transparent -> opaque -> transparent along the line's direction
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = vertical ? new Point(0, 1) : new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, LineColor.R, LineColor.G, LineColor.B), 0.0));
        brush.GradientStops.Add(new GradientStop(LineColor, 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, LineColor.R, LineColor.G, LineColor.B), 1.0));

        return new Line
        {
            Stroke = brush,
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed
        };
    }

    /// <summary>Short vertical line at screenX, centered on screenAnchorY.</summary>
    public static void ShowVertical(double screenX, double screenAnchorY)
    {
        Ensure();
        if (_vline == null) return;
        double x = screenX - SystemParameters.VirtualScreenLeft;
        double cy = screenAnchorY - SystemParameters.VirtualScreenTop;
        _vline.X1 = x; _vline.X2 = x;
        _vline.Y1 = cy - Extent; _vline.Y2 = cy + Extent;
        _vline.Visibility = Visibility.Visible;
    }

    /// <summary>Short horizontal line at screenY, centered on screenAnchorX.</summary>
    public static void ShowHorizontal(double screenAnchorX, double screenY)
    {
        Ensure();
        if (_hline == null) return;
        double cx = screenAnchorX - SystemParameters.VirtualScreenLeft;
        double y = screenY - SystemParameters.VirtualScreenTop;
        _hline.Y1 = y; _hline.Y2 = y;
        _hline.X1 = cx - Extent; _hline.X2 = cx + Extent;
        _hline.Visibility = Visibility.Visible;
    }

    public static void HideVertical() { if (_vline != null) _vline.Visibility = Visibility.Collapsed; }
    public static void HideHorizontal() { if (_hline != null) _hline.Visibility = Visibility.Collapsed; }

    public static void Hide()
    {
        HideVertical();
        HideHorizontal();
    }
}
