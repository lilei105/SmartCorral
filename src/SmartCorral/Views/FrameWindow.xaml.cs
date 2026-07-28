using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using SmartCorral.Models;
using SmartCorral.Services;
using SmartCorral.Services.Com;
using SmartCorral.Services.Platform;
using SmartCorral.Interop;
using System.Windows.Interop;
using System.Runtime.InteropServices;

// Resolve WPF drag/drop types (WinForms is removed from implicit usings, but keep these explicit).
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace SmartCorral.Views;

/// <summary>
/// A DataFrame window. Drop files to add them; right-click for frame actions;
/// double-click the title to roll up/down; drag for magnetic snapping to edges/frames.
/// </summary>
public partial class FrameWindow
{
    private const double SnapThreshold = 10.0;
    private const double SnapGap = 12.0;

    // SetWindowPos flags for raising a frame without activating or making it topmost.
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private readonly DataFrame _frame;
    private readonly FrameManager _mgr;
    private readonly DispatcherTimer _saveTimer;
    private bool _rolled;
    private double _restoredHeight;

    // custom drag state
    private bool _dragging;
    private Point _dragOriginScreen;
    private Point _dragOriginScreenTL; // physical top-left at drag start (for snap-guide anchoring)
    private double _dragOriginLeft;
    private double _dragOriginTop;

    // custom resize state (bottom-right grip)
    private bool _resizing;
    private Point _resizeStartScreen;
    private double _resizeStartWidth, _resizeStartHeight, _resizeStartLeft, _resizeStartTop;

    public FrameWindow(DataFrame frame, FrameManager mgr)
    {
        _frame = frame;
        _mgr = mgr;
        InitializeComponent();
        TitleText.Text = frame.Title;

        _saveTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _mgr.Persist(); };
        LocationChanged += ScheduleSave;
        SizeChanged += ScheduleSave;

        // capture DPI so MonitorService can convert device px <-> DIPs (multi-monitor).
        ContentRendered += (_, _) =>
        {
            var d = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            MonitorService.DpiScaleX = d.DpiScaleX;
            MonitorService.DpiScaleY = d.DpiScaleY;
        };;

        ApplyLockState();
        if (frame.IsRolled)
        {
            _rolled = false;
            ToggleRoll();
        }

        RenderItems();
    }

    private void ScheduleSave(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---- items ----
    public void RenderItems()
    {
        TitleText.FontSize = FrameSizer.TitleFont;
        IconsPanel.Children.Clear();
        FoldersPanel.Children.Clear();

        var ordered = _frame.Items.OrderBy(i => i.DisplayOrder).ToList();
        EmptyHint.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_mgr.SeparateFolders)
        {
            foreach (var item in ordered.Where(i => !i.IsFolder))
                IconsPanel.Children.Add(BuildItem(item));
            foreach (var item in ordered.Where(i => i.IsFolder))
                FoldersPanel.Children.Add(BuildItem(item));
            FoldersPanel.Visibility = FoldersPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            foreach (var item in ordered)
                IconsPanel.Children.Add(BuildItem(item));
            FoldersPanel.Visibility = Visibility.Collapsed;
        }
    }

    private UIElement BuildItem(FrameItem item)
    {
        // Show the shortcut arrow ONLY if the ORIGINAL desktop item was a .lnk — raw files/folders
        // we wrapped in a .lnk should look like themselves, not like shortcuts.
        bool showArrow = !string.IsNullOrEmpty(item.SourcePath)
                         && item.SourcePath.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase);

        var icon = new Image
        {
            Source = IconService.GetIconForShortcutFile(ShortcutService.AbsolutePath(item.Filename), showArrow),
            Width = FrameSizer.IconSize,
            Height = FrameSizer.IconSize,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = item.DisplayName,
            MaxWidth = FrameSizer.LabelMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White,
            FontSize = FrameSizer.ItemFont,
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var btn = new Button
        {
            Width = FrameSizer.ButtonWidth,
            Margin = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Content = stack,
            Tag = item
        };
        btn.Click += Item_Click;
        btn.PreviewMouseRightButtonUp += Item_RightClick;
        return btn;
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is FrameItem item && !string.IsNullOrEmpty(item.Filename))
        {
            try
            {
                string abs = ShortcutService.AbsolutePath(item.Filename);
                Process.Start(new ProcessStartInfo(abs) { UseShellExecute = true });
            }
            catch
            {
                // ignore launch failures for now
            }
        }
    }

    private void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is FrameItem item && !string.IsNullOrEmpty(item.SourcePath))
        {
            // Show the real Windows shell context menu for the original desktop item.
            Point pt = PointToScreen(e.GetPosition(this));
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int px = (int)pt.X;
            int py = (int)pt.Y;
            ShellContextMenu.Show(item.SourcePath, hwnd, px, py);
            e.Handled = true; // prevent the frame's own context menu from also showing
        }
    }

    // ---- drag/drop of files ----
    private void Frame_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Frame_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            _mgr.AddDroppedFiles(_frame, files);
            RenderItems();
        }
    }

    // ---- custom drag with magnetic snap ----
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) { ToggleRoll(); return; }
        if (_frame.IsLocked) return;

        _dragging = true;
        _dragOriginLeft = Left;
        _dragOriginTop = Top;
        _dragOriginScreen = PointToScreen(e.GetPosition(this));
        _dragOriginScreenTL = PointToScreen(new Point(0, 0));
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            Point cur = PointToScreen(e.GetPosition(this));
            // Use THIS frame's current DPI, not the shared MonitorService scale: the latter can be
            // stale/wrong when several frames sit on monitors of different DPI (PerMonitorV2 app).
            var dpi = VisualTreeHelper.GetDpi(this);
            double dx = (cur.X - _dragOriginScreen.X) / dpi.DpiScaleX;
            double dy = (cur.Y - _dragOriginScreen.Y) / dpi.DpiScaleY;
            double nl = _dragOriginLeft + dx;
            double nt = _dragOriginTop + dy;
            var (sx, sy, snapX, snapY) = Snap(nl, nt);
            Left = sx;
            Top = sy;

            // Guide overlay canvas uses the PRIMARY monitor's DPI, so pass PHYSICAL pixels: frame
            // Left/Top are in THIS monitor's own DIP space, and DIP would misalign on other screens.
            // Anchor on the frame's top-left edge — NOT the click point (_dragOriginScreen).
            double physLeft = _dragOriginScreenTL.X + (sx - _dragOriginLeft) * dpi.DpiScaleX;
            double physTop = _dragOriginScreenTL.Y + (sy - _dragOriginTop) * dpi.DpiScaleY;
            if (snapX) SnapGuide.ShowVertical(physLeft, physTop); else SnapGuide.HideVertical();
            if (snapY) SnapGuide.ShowHorizontal(physLeft, physTop); else SnapGuide.HideHorizontal();
        }
        else if (_resizing)
        {
            Point cur = PointToScreen(e.GetPosition(this));
            var dpi = VisualTreeHelper.GetDpi(this);
            double dx = (cur.X - _resizeStartScreen.X) / dpi.DpiScaleX;
            double dy = (cur.Y - _resizeStartScreen.Y) / dpi.DpiScaleY;
            double rawRight = _resizeStartLeft + _resizeStartWidth + dx;
            double rawBottom = _resizeStartTop + _resizeStartHeight + dy;
            var (sr, sb, snapR, snapB) = SnapResize(rawRight, rawBottom);
            double newW = System.Math.Max(MinWidth, sr - Left);
            double newH = System.Math.Max(MinHeight, sb - Top);
            Width = newW;
            Height = newH;

            // guide lines anchored at the bottom-right corner (physical pixels — see drag note).
            // Left/Top are unchanged during resize, so the frame's physical top-left is reliable here.
            Point physTL = PointToScreen(new Point(0, 0));
            double physRight = physTL.X + newW * dpi.DpiScaleX;
            double physBottom = physTL.Y + newH * dpi.DpiScaleY;
            if (snapR) SnapGuide.ShowVertical(physRight, physBottom); else SnapGuide.HideVertical();
            if (snapB) SnapGuide.ShowHorizontal(physRight, physBottom); else SnapGuide.HideHorizontal();
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging && !_resizing) return;
        _dragging = false;
        _resizing = false;
        SnapGuide.Hide();
        ReleaseMouseCapture();
    }

    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_frame.IsLocked) return;
        _resizing = true;
        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        CaptureMouse();
    }

    private (double right, double bottom, bool snapR, bool snapB) SnapResize(double right, double bottom)
    {
        Rect work = MonitorService.WorkAreaForPoint(Left, Top);
        var xs = new List<double> { work.Right, work.Right - SnapGap };
        var ys = new List<double> { work.Bottom, work.Bottom - SnapGap };
        foreach (var r in _mgr.GetOpenFrameBounds(_frame.Id))
        {
            xs.Add(r.Right); xs.Add(r.Left); xs.Add(r.Left - SnapGap);
            ys.Add(r.Bottom); ys.Add(r.Top); ys.Add(r.Top - SnapGap);
        }

        double bestR = right, bestDR = SnapThreshold;
        foreach (double c in xs) { double d = System.Math.Abs(right - c); if (d < bestDR) { bestDR = d; bestR = c; } }

        double bestB = bottom, bestDB = SnapThreshold;
        foreach (double c in ys) { double d = System.Math.Abs(bottom - c); if (d < bestDB) { bestDB = d; bestB = c; } }

        return (bestR, bestB, bestDR < SnapThreshold, bestDB < SnapThreshold);
    }

    private (double x, double y, bool snapX, bool snapY) Snap(double left, double top)
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        Rect work = MonitorService.WorkAreaForPoint(left, top);

        var xs = new List<double>
        {
            work.Left, work.Left + SnapGap,
            work.Right - w, work.Right - w - SnapGap
        };
        var ys = new List<double>
        {
            work.Top, work.Top + SnapGap,
            work.Bottom - h, work.Bottom - h - SnapGap
        };
        foreach (var r in _mgr.GetOpenFrameBounds(_frame.Id))
        {
            xs.Add(r.Left);
            xs.Add(r.Right);
            xs.Add(r.Right + SnapGap); // sit to the right of it, with a gap
            ys.Add(r.Top);
            ys.Add(r.Bottom);
            ys.Add(r.Bottom + SnapGap); // sit below it, with a gap
        }

        double bestX = left, bestDX = SnapThreshold;
        foreach (double c in xs)
        {
            double d = System.Math.Abs(left - c);
            if (d < bestDX) { bestDX = d; bestX = c; }
        }

        double bestY = top, bestDY = SnapThreshold;
        foreach (double c in ys)
        {
            double d = System.Math.Abs(top - c);
            if (d < bestDY) { bestDY = d; bestY = c; }
        }

        return (bestX, bestY, bestDX < SnapThreshold, bestDY < SnapThreshold);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => BringToFront();

    /// <summary>Re-assert topmost to raise this frame above sibling frames (no focus change).</summary>
    private void BringToFront()
    {
        if (_mgr.ForceTopmost)
        {
            Topmost = false;
            Topmost = true;
            return;
        }

        // Not always-on-top: raise this frame to the top of the non-topmost Z-order so it comes
        // above sibling frames, without activating (preserve NOACTIVATE) or becoming topmost — so
        // other windows can still cover it when you switch away.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ---- context menu actions ----
    private void NewFrame_Click(object sender, RoutedEventArgs e) => _mgr.AddFrame();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        string name = Interaction.InputBox("Frame name:", "Rename Frame", _frame.Title);
        if (!string.IsNullOrWhiteSpace(name) && name != _frame.Title)
        {
            _mgr.RenameFrame(_frame.Id, name);
            TitleText.Text = _frame.Title;
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _frame.IsLocked = !_frame.IsLocked;
        ApplyLockState();
        _mgr.Persist();
    }

    private void Roll_Click(object sender, RoutedEventArgs e) => ToggleRoll();

    private void Delete_Click(object sender, RoutedEventArgs e) => _mgr.DeleteFrame(_frame.Id);

    // ---- chrome helpers ----
    private void ApplyLockState()
    {
        ResizeGrip.Visibility = _frame.IsLocked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleRoll()
    {
        _rolled = !_rolled;
        if (_rolled)
        {
            _restoredHeight = Height;
            ItemsScroll.Visibility = Visibility.Collapsed;
            MinHeight = 40;
            Height = 62;
        }
        else
        {
            ItemsScroll.Visibility = Visibility.Visible;
            MinHeight = 120;
            if (_restoredHeight > 0) Height = _restoredHeight;
        }
        _frame.IsRolled = _rolled;
        _mgr.Persist();
    }
}
