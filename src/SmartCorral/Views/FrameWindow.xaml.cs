using System.Windows.Input;

namespace SmartCorral.Views;

/// <summary>
/// Phase 0 "hello frame": a translucent, rounded, non-activating floating panel over the desktop.
/// Demonstrates NonActivatingWindow + modern shell look. Drag by the title bar; close via ✕ or tray.
/// </summary>
public partial class FrameWindow
{
    public FrameWindow()
    {
        InitializeComponent();
        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // DragMove keeps the non-activating behaviour (frame moves without taking focus).
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Closing the frame does not exit the app (ShutdownMode = OnExplicitShutdown);
        // the app keeps running in the tray until "Exit".
        Close();
    }
}
