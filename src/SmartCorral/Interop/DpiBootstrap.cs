using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SmartCorral.Interop;

/// <summary>
/// Makes the process Per-Monitor v2 DPI aware as early as possible. A module initializer runs
/// before Main, i.e. before WPF creates its first window. WPF then re-rasterizes each window at the
/// correct DPI when it moves between monitors of different scale, instead of letting DWM
/// bitmap-scale it (blurry).
///
/// Done in code rather than the manifest because WinForms is enabled in this project, so the SDK
/// strips any dpiAwareness element from app.manifest (WFO0003); and the WPF-generated Main never
/// calls ApplicationConfiguration.Initialize(), so &lt;ApplicationHighDpiMode&gt; would be inert.
/// </summary>
internal static class DpiBootstrap
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (Win10 1703+).
    private static readonly IntPtr PerMonitorV2Context = new(-4);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Returns false if awareness was already set (e.g. by a prior call); that is harmless.
        try { SetProcessDpiAwarenessContext(PerMonitorV2Context); }
        catch { /* entry point absent on pre-1703 builds: leave the default awareness */ }
    }
}
