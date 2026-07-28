# SmartCorral 开发踩坑与已知问题

开发者向。记录本项目中踩过的坑、为什么坑、以及现在的处理方式，避免重复掉进去。用户向内容请看根目录 [README](../README.md)。

---

## 1. 构建

- **用 `dotnet build`，不要用 VS 的 MSBuild。** 项目没有任何 COM 引用（.lnk 处理是 late-bound COM `WScript.Shell`，不是 COM 引用），`dotnet build` 原生可构建。
  ```bash
  dotnet build "src/SmartCorral/SmartCorral.csproj" -p:Configuration=Debug -restore
  ```
- **.NET 10**（`net10.0-windows`），WPF + WinForms 都启用。
- **改完代码构建失败、报 `MSB3021/MSB3027` "文件被占用"** = 上一次构建的 `SmartCorral.exe` 还在运行。先 `taskkill /F /IM SmartCorral.exe` 再构建。
- 解决方案文件是新的 `.slnx` 格式；直接构建 csproj 更快。

---

## 2. DPI / 多显示器（最大的坑）

### 2.1 必须把进程设成 Per-Monitor v2，但常规两条路都失效
窗口跨不同 DPI 显示器移动时不模糊、拖拽跟手，前提是进程是 **Per-Monitor v2**。但本项目里两条"标准"声明方式都**不生效**：

- **manifest 里写 `dpiAwareness`**：项目启用了 WinForms，SDK 会把 manifest 里的高 DPI 设置**剥掉**（警告 `WFO0003`）。
- **`<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`**：这是 WinForms 的 `ApplicationConfiguration.Initialize()` 路径；而本项目的入口是 **WPF 的 `App.xaml`**，生成的 `Main`（见 `obj/.../App.g.cs`）**根本不调用** `ApplicationConfiguration.Initialize()`，所以这个属性完全不被读取。

**现在的做法**：`Interop/DpiBootstrap.cs` 用 `[ModuleInitializer]` 在 `Main` 之前调 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`。模块初始化器是托管代码里最早的注入点，在任何 WPF 窗口创建之前。

**验证**：用 `GetProcessDpiAwareness` 查进程应返回 `2`（per-monitor）。命令：
```powershell
# P/Invoke shcore!GetProcessDpiAwareness(handle, out int)
# 0=unaware, 1=system, 2=per-monitor
```

### 2.2 两套坐标系
- **WPF 坐标 = DIP**（`Left/Top/Width/Height`）；**Win32 坐标 = 物理像素**（`PointToScreen` 返回的是物理像素）。
- 跨越两套：物理→DIP 除以 `DpiScale`，DIP→物理 乘以 `DpiScale`。每帧 `DpiScale` 在 `ContentRendered` 和 `WM_DPICHANGED` 时捕获（见 `MonitorService`、`NonActivatingWindow`）。

### 2.3 拖拽/缩放要用"本帧当前 DPI"，不是全局
`FrameWindow` 拖拽/缩放的距离换算用 `VisualTreeHelper.GetDpi(this)`（本帧自己当前 DPI），**不要**用 `MonitorService.DpiScaleX/Y`（全局，多个框在不同 DPI 屏时会互相污染）。`MonitorService` 的全局值只用于 `WorkAreaForPoint/ForMouse` 的 DIP↔物理换算。

### 2.4 磁吸辅助线必须用物理像素定位
`SnapGuide` 是一个覆盖整个虚拟屏的 overlay 窗口，画布按**主屏** DPI。但 frame 在副屏（不同 DPI）时，它的 `Left/Top` 是按**副屏** DPI 的 DIP——两个 DIP 空间不一致，直接 `减 VirtualScreenLeft` 会让辅助线偏一个 DPI 倍数。

**现在的做法**：`FrameWindow` 把吸附位置的**物理像素**传给 `SnapGuide`（拖拽用 `_dragOriginScreenTL`——拖拽起始时捕获的**物理左上角**，不是鼠标点击点；缩放用 `PointToScreen(0,0)+尺寸*DPI`），`SnapGuide.ToCanvas` 再用 overlay 自己的 `PointToScreen(0,0)` 原点 + DPI 换算成画布坐标。物理像素跨屏一致，所以每个屏都对。

> 踩过的具体错：第一版用鼠标点击点 `_dragOriginScreen` 当原点 → 辅助线落在 frame 中间而不是左上角。

---

## 3. 围栏框置顶（ForceTopmost）

- 默认 `Topmost=true`（框总在最上层）。设置里可关。
- **关掉后点击仍要能提到其它框之上**：不能用 `Topmost` 切换（会把框弹到所有其它程序之上）。改用 `SetWindowPos(HWND_TOP, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)`——提到非置顶窗口 Z 序最上面，不激活、不置顶，切到别的程序时仍能被盖住。
- 见 `FrameWindow.BringToFront`。

---

## 4. 真 Mica/亚克力毛玻璃 —— 在围栏框上做不出来（已搁置）

Phase 2 想把假半透明换成系统真毛玻璃（Mica/Acrylic），**反复尝试后失败并回退**。结论：系统背景在当前围栏框窗口上无法渲染。**不要再重复下面这些配方。**

**症状**：所有 DWM 调用都返回 `S_OK`（dark mode、圆角、`DwmExtendFrameIntoClientArea`、`DWMWA_SYSTEMBACKDROP_TYPE`=Mica(2)/Acrylic(3)），但窗口始终不透明——Mica 显黑、Acrylic 显灰，背景画在窗口背后却透不出来。环境 Win11 24H2（build 26200），API 确实支持。

**试过且都失败的配方**：
- `DwmExtendFrameIntoClientArea` 1px 边距 + `CompositionTarget.BackgroundColor=Transparent`
- Sheet-of-glass（四边 margins 全 -1）+ CompositionTarget 透明
- `WindowChrome GlassFrameThickness="-1"`（默认 WindowStyle，`CaptionHeight=0`）—— 即 Difegue/Mica-WPF-Sample 的配方
- 排除了 `WS_EX_NOACTIVATE` 和 `Topmost`（都临时关掉，依旧黑/灰）

**关键线索**：设了 `WindowChrome` 之后，系统的标题栏关闭按钮还在（跟自定义的 ✕ 重叠成"双叉"）→ **`WindowChrome` 对 `NonActivatingWindow` 派生的窗口根本没生效**，于是从没产生过能让背景透出的玻璃面。

**推断的根因**：这套自定义窗口配置（手动 `SetWindowLong` 注入 `WS_EX_NOACTIVATE`、完全自定义 chrome、进程级 PMv2 模块初始化器）破坏了标准 WPF→DWM 背景管线。这是 WPF 的已知糙边（dotnet/wpf#8545）。

**下次再攻应换思路**：
1. 先用一个**干净的 `Window` 子类**（不做 ex-style 手术）验证 `WindowChrome` 能否生效；能的话再排查 `NonActivatingWindow` 到底哪一步挡了它。
2. 或上 **WinUI 3 / XAML Island**，原生支持毛玻璃。
3. 或接受当前的半透明着色（`#CC1C1C2B` + `AllowsTransparency=True`）—— 注意：`AllowsTransparency=True`（分层窗口）本身就被系统排除在毛玻璃之外，这就是死结所在。

> 相关细节见项目记忆 `mica-backdrop-deadend`。

---

## 5. 跨屏内容发软（未解决）

把 frame 拖到不同 DPI 的副屏，内容（文字/图标）有时略发软。**根因未定位**：曾尝试去掉 `AllowsTransparency`（改成不透明窗口）依然发软，所以不是分层窗口单一原因。当前接受现状。可能与 PMv2 + `WS_EX_NOACTIVATE` 窗口的跨屏重渲染有关，待查。

---

## 6. WinForms 只用于托盘

WinForms 仅 `TrayShell`（`NotifyIcon`）用。已从隐式 usings 移除 `System.Windows.Forms` / `System.Drawing`，避免与 WPF 类型（如 `Application`、`Color`、`Image`）冲突；需要的地方显式 `using` 或类型别名（见 `App.xaml.cs`：`using Application = System.Windows.Application;`）。

---

## 7. 图标高清

`IconService` 用 `SHGetImageList` + `IImageList::GetIcon` 取 **jumbo(256px)**，回退 extralarge(48) → large(32)，保证高分屏下图标不发虚。注意 `IImageList` COM 接口要按 vtable 顺序声明满 19 个方法（`GetIcon` 是最后一个）才能落到正确的槽位。

---

## 8. 其它必记的小坑

- **`FrameWindow` 构造函数里必须调 `RenderItems()`**——曾经被误删，导致加载后框是空白的。
- **`.lnk` 处理是 late-bound COM**（`ShortcutService` 用 `dynamic WScript.Shell`），不是 COM 引用，所以 `dotnet build` 能编。
- **`DesktopShell.Shutdown` 永远把桌面图标还原成"可见"**——不要信任运行时捕获的"原始状态"（可能被上次崩溃污染）。
- **拖拽/缩放/菜单位置**跨 DPI 屏时都要走 `DpiScale` 或 `VisualTreeHelper.GetDpi`，别直接用 DIP。
