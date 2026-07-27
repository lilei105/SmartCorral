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

// Resolve WPF drag/drop types (WinForms is removed from implicit usings, but keep these explicit).
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace SmartCorral.Views;

/// <summary>
/// A DataFrame window. Drop files to add them; right-click for frame actions;
/// double-click the title bar to roll up/down; lock to freeze position.
/// </summary>
public partial class FrameWindow
{
    private readonly DataFrame _frame;
    private readonly FrameManager _mgr;
    private readonly DispatcherTimer _saveTimer;
    private bool _rolled;
    private double _restoredHeight;

    public FrameWindow(DataFrame frame, FrameManager mgr)
    {
        _frame = frame;
        _mgr = mgr;
        InitializeComponent();
        TitleText.Text = frame.Title;

        // debounced live-save of position/size
        _saveTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _mgr.Persist(); };
        LocationChanged += ScheduleSave;
        SizeChanged += ScheduleSave;

        ApplyLockState();
        if (frame.IsRolled)
        {
            _rolled = false; // ToggleRoll flips it to true
            ToggleRoll();
        }
    }

    private void ScheduleSave(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---- items ----
    private void RenderItems()
    {
        IconsPanel.Children.Clear();
        foreach (var item in _frame.Items.OrderBy(i => i.DisplayOrder))
        {
            IconsPanel.Children.Add(BuildItem(item));
        }
    }

    private UIElement BuildItem(FrameItem item)
    {
        var icon = new Image
        {
            Source = IconService.GetIcon(item.Target),
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = item.DisplayName,
            MaxWidth = 72,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var btn = new Button
        {
            Width = 76,
            Margin = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Content = stack,
            Tag = item
        };
        btn.Click += Item_Click;
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

    // ---- drag/drop ----
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

    // ---- title bar ----
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) { ToggleRoll(); return; }
        if (_frame.IsLocked) return;
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

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
        ResizeMode = _frame.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }

    private void ToggleRoll()
    {
        _rolled = !_rolled;
        if (_rolled)
        {
            _restoredHeight = Height;
            ItemsScroll.Visibility = Visibility.Collapsed;
            Height = 62;
        }
        else
        {
            ItemsScroll.Visibility = Visibility.Visible;
            if (_restoredHeight > 0) Height = _restoredHeight;
        }
        _frame.IsRolled = _rolled;
        _mgr.Persist();
    }
}
