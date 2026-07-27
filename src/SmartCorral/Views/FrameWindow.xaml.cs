using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartCorral.Models;
using SmartCorral.Services;

// Resolve WPF drag/drop types (ambiguous with WinForms, which is enabled for the tray).
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace SmartCorral.Views;

/// <summary>
/// A real DataFrame window: drops create .lnk items; click an item to launch it.
/// Phase 1a — items rendered imperatively (MVVM comes later).
/// </summary>
public partial class FrameWindow
{
    private readonly DataFrame _frame;
    private readonly FrameManager _mgr;

    public FrameWindow(DataFrame frame, FrameManager mgr)
    {
        _frame = frame;
        _mgr = mgr;
        InitializeComponent();
        TitleText.Text = frame.Title;
        RenderItems();
    }

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
        if (sender is Button b && b.Tag is FrameItem item && !string.IsNullOrEmpty(item.Target))
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
            }
            catch
            {
                // ignore launch failures for now
            }
        }
    }

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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
