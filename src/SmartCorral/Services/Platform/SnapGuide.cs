using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartCorral.Services.Platform;

/// <summary>
/// A click-through, non-activating full-virtual-screen overlay that shows blue alignment lines
/// while a frame is being magnetically snapped.
/// </summary>
public static class SnapGuide
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

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
        _vline = MakeLine();
        _hline = MakeLine();
        canvas.Children.Add(_vline);
        canvas.Children.Add(_hline);
        _win.Content = canvas;
        _win.Show();
    }

    private static Line MakeLine() => new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x5B, 0x6C, 0xFF)),
        StrokeThickness = 1,
        Visibility = Visibility.Collapsed
    };

    public static void ShowVertical(double screenX)
    {
        Ensure();
        if (_vline == null) return;
        double x = screenX - SystemParameters.VirtualScreenLeft;
        _vline.X1 = x; _vline.X2 = x;
        _vline.Y1 = 0; _vline.Y2 = SystemParameters.VirtualScreenHeight;
        _vline.Visibility = Visibility.Visible;
    }

    public static void ShowHorizontal(double screenY)
    {
        Ensure();
        if (_hline == null) return;
        double y = screenY - SystemParameters.VirtualScreenTop;
        _hline.Y1 = y; _hline.Y2 = y;
        _hline.X1 = 0; _hline.X2 = SystemParameters.VirtualScreenWidth;
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
