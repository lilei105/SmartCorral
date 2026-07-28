# CLAUDE.md — SmartCorral (灵栅)

## What this is

**Smart Corral (灵栅)** — AI-driven Windows desktop organizer. Hides native desktop icons while running, auto-categorizes desktop files/folders into translucent frames via LLM, restores everything on exit. Files never move — it's a visual/session layer + AI sorting.

A from-scratch rewrite (NOT the old Desktop Frames + / BirdyFences codebase). Tech: WPF + .NET 10 + WinForms (tray). ~4000 LOC, 30 source files.

## Build & run

```bash
# IMPORTANT: use `dotnet build` (NOT VS MSBuild) — this project has NO COM references.
# The .lnk shortcut handling is late-bound COM (dynamic WScript.Shell), not a COM reference.
MSBUILD=dotnet
dotnet build "src/SmartCorral/SmartCorral.csproj" -p:Configuration=Debug -restore -nologo -v:minimal
# Run:
"src/SmartCorral/bin/Debug/net10.0-windows/SmartCorral.exe"
```

- .NET 10 (net10.0-windows), WinExe, WPF + WinForms both enabled.
- `Microsoft.CSharp` is NOT referenced (it's in the .NET 10 shared framework).
- WinForms + System.Drawing removed from implicit usings (they clash with WPF types). TrayShell imports them explicitly.
- Solution file is `SmartCorral.slnx` (.NET 10 new format). Build the csproj directly for speed.

## Architecture

See `DESIGN.md` for the full design doc. Key points:

- **Namespace**: `SmartCorral`
- **All UI is code-behind** (imperative C#, not XAML bindings) — like the old project but cleaner.
- **Static managers** (no DI yet): FrameManager, DesktopShell, PersistenceService, etc.
- **Strong-typed models**: Frame (abstract, STJ polymorphic via `$kind`) → DataFrame (with Items). Portal/Note deferred.
- **Persistence**: `data/frames.json` (frames) + `data/settings.json` (AppSettings) + `data/shortcuts/` (.lnk files). Portable, next to EXE.
- **Late-bound COM** for .lnk (ShortcutService via `dynamic` WScript.Shell — no reference, dotnet-buildable).
- **Shell context menu**: ShellContextMenu.cs uses IContextMenu COM (IShellFolder → GetUIObjectOf → IContextMenu → TrackPopupMenuEx → InvokeCommand). Has a hidden WinForms helper window as menu owner (for dismiss-on-click-away on NOACTIVATE frames).

### Key files

| File | Role |
|---|---|
| `App.xaml.cs` | Startup: single-instance → settings → tray → DesktopShell(hide) → FrameManager(load+show) → AI(fire-and-forget) |
| `Services/FrameManager.cs` | Frame lifecycle, drag-drop, AI add, auto-arrange, persist |
| `Services/FrameArranger.cs` | Right-aligned grid layout |
| `Services/FrameSizer.cs` | Width/height from icons-per-row + item count (calibrated: 84px/item, 64px/row, 90px chrome) |
| `Services/DesktopShell.cs` | Hide/show desktop icons + crash self-heal (always restores to visible) |
| `Services/Platform/DesktopIconHider.cs` | Progman→SHELLDLL_DefView→SysListView32 P/Invoke |
| `Services/Platform/MonitorService.cs` | Multi-monitor work area (WinForms Screen + DPI↔DIP conversion) |
| `Services/Platform/SnapGuide.cs` | Fading blue alignment lines overlay during drag/resize snap |
| `Services/Ai/LlmClient.cs` | OpenAI-compatible HTTP (batched, JSON mode, Bearer) |
| `Services/Ai/AiCategorizer.cs` | Index-based categorization prompt + parser |
| `Services/Ai/AiOrganizeService.cs` | Scan→categorize→apply pipeline (off-thread LLM, UI-thread apply) |
| `Services/Com/ShortcutService.cs` | Import/copy .lnk, resolve target+IconLocation |
| `Services/IconService.cs` | SHGetFileInfo/ExtractIconEx icon extraction (honors IconLocation, conditional arrow) |
| `Services/PersistenceService.cs` | System.Text.Json load/save (frames + settings) |
| `Services/TrayShell.cs` | WinForms NotifyIcon (Settings/Auto-arrange/Re-organize/Exit) |
| `Views/FrameWindow.xaml(.cs)` | The frame: drag+snap, resize+snap, roll-up, lock, shell context menu, items render |
| `Views/SettingsWindow.xaml(.cs)` | AI config + icons-per-row + separate-folders toggle |
| `Interop/NonActivatingWindow.cs` | WS_EX_NOACTIVATE base class |
| `Interop/ShellContextMenu.cs` | Full system right-click menu via IContextMenu COM |
| `Models/` | Frame, DataFrame, FrameItem, AppData, AppSettings |

## Conventions & gotchas

- **WPF coords = DIP; Win32 coords = physical pixels.** PointToScreen returns physical pixels; Left/Top/Width/Height are DIP. Cross over: ÷ DpiScale (physical→DIP) or × DpiScale (DIP→physical). DpiScale captured per-frame at ContentRendered via MonitorService.
- **Dispatcher mandatory** for UI mutation from background threads (FileSystemWatcher, AI apply).
- **WinForms enabled ONLY for tray** (TrayShell). Removed from implicit usings so it doesn't clash with WPF types.
- **ShortcutService.Import**: copies dropped .lnk/.url verbatim (preserves IconLocation/args); creates fresh .lnk for raw files/folders.
- **IconService**: `GetIconForShortcutFile(path, showArrow)` — showArrow true when original was .lnk (shows arrow); false for files/folders (no arrow). Resolves IconLocation via COM for no-arrow path.
- **DesktopShell.Shutdown**: ALWAYS restores to visible (never trust a captured "original state" — it can be polluted by a prior crash).
- **FrameWindow ctor calls RenderItems()** (don't remove it — was lost once and frames loaded blank).
- **AI categorization is incremental + non-destructive**: files already imported (by SourcePath) are skipped. "Re-organize all" (tray) clears + re-runs.

## DPI / multi-monitor

Known issue (see `TODO.md`): DpiScaleX/Y captured once at ContentRendered. Moving a frame to a monitor with different DPI → stale scale → drag/resize speed mismatch. Fix: listen for WM_DPICHANGED.

## What's done (as of last session)

- ✅ AI auto-categorize (files + folders, OpenAI-compatible, index-based, broad categories)
- ✅ Session tidy (hide desktop icons, restore on exit, crash self-heal)
- ✅ Frame chrome (multi-frame, rename, delete, roll-up, lock, click-to-front, context menu)
- ✅ Magnetic drag + resize snap with fading guide lines (multi-monitor aware)
- ✅ Auto-arrange (right-aligned grid, content-sized heights)
- ✅ Settings (AI config, icons-per-row 2-8, separate-folders toggle)
- ✅ Shell context menu (full IContextMenu, system-identical)
- ✅ Icon correctness (folders, custom IconLocation, conditional arrow)
- ✅ DPI-corrected drag/resize/menu position

## What's next (see TODO.md)

1. Multi-monitor DPI (WM_DPICHANGED) — user testing with external monitor today.
2. Phase 2: real Mica/acrylic + Portal frames.
3. Phase 3b: FileSystemWatcher incremental + VLM fallback.
4. Packaging: DPAPI key encryption + app icon + single-file publish.
