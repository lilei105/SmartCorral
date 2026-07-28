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
| `App.xaml.cs` | Startup: single-instance → legacy-hide recovery → `CustodyService.RestoreAll()` (crash self-heal) → settings → tray → FrameManager(load+show) → `RetakeAllIntoCustody` → AI(fire-and-forget); exit/ProcessExit → `RestoreAll` |
| `Services/CustodyService.cs` | Custody model: `Take`/`Restore`/`RestoreAll` — moves desktop items into `%LOCALAPPDATA%\SmartCorral\custody\` (atomic same-volume) + `data/custody.json` manifest. Bulletproof safety net (pending→done entries, conflict-rename, retry-failed). Replaces the old global icon hide. |
| `Services/FrameManager.cs` | Frame lifecycle, drag-drop, AI add, auto-arrange, persist; custody wiring (AddDesktopFile→Take, RemoveItem/DeleteFrame/ClearAll→Restore) |
| `Services/FrameArranger.cs` | Right-aligned grid layout |
| `Services/FrameSizer.cs` | Width/height from icons-per-row + item count (calibrated: 84px/item, 64px/row, 90px chrome) |
| `Services/DesktopShell.cs` | RETIRED to `RecoverLegacyHide()` — one-shot restore of a leftover `.icons_hidden` flag from an OLD build on first launch of the custody build. No more global hide. |
| `Services/Platform/DesktopIconHider.cs` | Progman→SHELLDLL_DefView→SysListView32 P/Invoke (legacy utility, used only by RecoverLegacyHide) |
| `Services/Platform/MonitorService.cs` | Multi-monitor work area (WinForms Screen + DPI↔DIP conversion) |
| `Services/Platform/SnapGuide.cs` | Fading blue alignment lines overlay during drag/resize snap |
| `Services/Ai/LlmClient.cs` | OpenAI-compatible HTTP (batched, JSON mode, Bearer) |
| `Services/Ai/AiCategorizer.cs` | Index-based categorization prompt + parser |
| `Services/Ai/AiOrganizeService.cs` | Scan→categorize→apply pipeline (off-thread LLM, UI-thread apply) |
| `Services/Com/ShortcutService.cs` | Import/copy .lnk (raw-file wrapper can target the custody path), resolve target+IconLocation, Retarget .lnk at fresh custody path on re-custody |
| `Services/IconService.cs` | SHGetFileInfo/ExtractIconEx icon extraction (honors IconLocation, conditional arrow) |
| `Services/PersistenceService.cs` | System.Text.Json load/save (frames + settings) |
| `Services/TrayShell.cs` | WinForms NotifyIcon (Settings/Auto-arrange/Re-organize/Exit) |
| `Views/FrameWindow.xaml(.cs)` | The frame: drag+snap, resize+snap, roll-up, lock, shell context menu, items render |
| `Views/SettingsWindow.xaml(.cs)` | AI config + icons-per-row + separate-folders toggle |
| `Interop/NonActivatingWindow.cs` | WS_EX_NOACTIVATE base class + WM_DPICHANGED hook |
| `Interop/DpiBootstrap.cs` | Module initializer → SetProcessDpiAwarenessContext (PerMonitorV2) |
| `Interop/ShellContextMenu.cs` | Full system right-click menu via IContextMenu COM |
| `Models/` | Frame, DataFrame, FrameItem, AppData, AppSettings |

## Conventions & gotchas

- **WPF coords = DIP; Win32 coords = physical pixels.** PointToScreen returns physical pixels; Left/Top/Width/Height are DIP. Cross over: ÷ DpiScale (physical→DIP) or × DpiScale (DIP→physical). Process is PerMonitorV2 via `DpiBootstrap` (module initializer — manifest `dpiAwareness` and `<ApplicationHighDpiMode>` are BOTH inert for this WinForms-enabled WPF app). Drag/resize read the frame's own DPI via `VisualTreeHelper.GetDpi(this)`; the shared `MonitorService` scale is refreshed on `WM_DPICHANGED`. **Snap-guide coords must be physical pixels** (the overlay canvas is primary-DPI; a frame's Left/Top on another-DPI monitor are in that monitor's DIP space). Full detail: `docs/development-pitfalls.md`.
- **Dispatcher mandatory** for UI mutation from background threads (FileSystemWatcher, AI apply).
- **WinForms enabled ONLY for tray** (TrayShell). Removed from implicit usings so it doesn't clash with WPF types.
- **ShortcutService.Import**: copies dropped .lnk/.url verbatim (preserves IconLocation/args); creates fresh .lnk for raw files/folders, pointed at the custody path (targetOverride) so it survives the move.
- **IconService**: `GetIconForShortcutFile(path, showArrow)` — showArrow true when original was .lnk (shows arrow); false for files/folders (no arrow). Resolves IconLocation via COM for no-arrow path.
- **Custody model (per-item desktop clear)** — replaces the retired global icon hide. Custody dir = `%LOCALAPPDATA%\SmartCorral\custody` (C:, same volume as desktop → atomic `File.Move`); manifest = portable `data/custody.json`. `RestoreAll` runs FIRST every launch (crash self-heal) + on exit. **NEVER throws** — `Take` degrades to the original path on any failure (icon just stays on desktop). Restore never overwrites (conflict → "(restored)" name). `FrameItem.LivePath` ([JsonIgnore], session-only) = on-disk path of the real item now = its custody path; the per-item shell menu and `RetakeAllIntoCustody` rely on it. Manual drag / AI / future incremental ALL go through `FrameManager.AddDesktopFile → Take` (this is what makes Phase 3b clean: filed files physically leave the desktop, so a watcher only fires for genuinely-new files).
- **Shell context menu must use `item.LivePath`** (custody path), NOT `SourcePath` (empty once custodied) — else Open/Delete/Properties hit a gone path. See FrameWindow `ShowItemMenu`.
- **FrameWindow ctor calls RenderItems()** (don't remove it — was lost once and frames loaded blank).
- **AI categorization is incremental + non-destructive**: files already imported (by SourcePath) are skipped. "Re-organize all" (tray) = `ClearAll` (which `RestoreAll`s items back to the desktop first) → re-runs AI; without the restore-first the desktop is empty, the scanner finds nothing, and files orphan in custody.

## DPI / multi-monitor

Done: process is Per-Monitor v2 (`DpiBootstrap`), `WM_DPICHANGED` refreshes the shared scale, drag/resize use per-frame DPI, snap guides use physical pixels. Open issue: frame **content can look slightly soft** on a different-DPI monitor after dragging — root cause not found (removing `AllowsTransparency` didn't fix it). Details: `docs/development-pitfalls.md` §2/§5.

## What's done (as of last session)

- ✅ AI auto-categorize (files + folders, OpenAI-compatible, index-based, broad categories)
- ✅ Session tidy → **custody model** (per-item: dragging/AI-filing a desktop item moves it into `%LOCALAPPDATA%\SmartCorral\custody\` so it leaves the desktop one-by-one; restore-all on exit + on next launch after a crash). Logic self-tested 30/30 with dummy files; interactive UI QA pending. Replaces the old global icon hide.
- ✅ Frame chrome (multi-frame, rename, delete, roll-up, lock, click-to-front, context menu)
- ✅ Magnetic drag + resize snap with fading guide lines (multi-monitor aware)
- ✅ Auto-arrange (right-aligned grid, content-sized heights)
- ✅ Settings (AI config, icons-per-row 2-8, separate-folders toggle)
- ✅ Shell context menu (full IContextMenu, system-identical)
- ✅ Icon correctness (folders, custom IconLocation, conditional arrow) + jumbo(256) high-res icons
- ✅ Per-Monitor v2 DPI (DpiBootstrap + WM_DPICHANGED + per-frame drag/resize + physical-pixel snap guides)
- ✅ "Always on top" setting + click-to-front when off (SetWindowPos HWND_TOP, no-activate)
- ✅ Larger icons (40 DIP) / font tuning

## What's next (see TODO.md)

0. **Custody model — finish interactive QA with dummy files** (drag→clear, tray-exit→restore, `taskkill /F`→relaunch self-heal, delete-frame/Remove→restore, AI re-categorize, multi-round stress) BEFORE trusting it on real files. Logic is unit-tested; this is the hands-on sign-off.
1. Cross-monitor content softness — root-cause it (DPI plumbing is done; this remains).
2. Phase 2: real Mica/acrylic — **attempted & reverted** (doesn't render on the custom frame window; see `docs/development-pitfalls.md` §4 + memory `mica-backdrop-deadend`). Needs a different strategy (vanilla-Window test / WinUI island). Portal frames still TODO.
3. Phase 3b: FileSystemWatcher incremental + VLM fallback.
4. Packaging: DPAPI key encryption + app icon + single-file publish.
