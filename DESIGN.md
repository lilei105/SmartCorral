# 灵栅 / Smart Corral — 设计文档

> 现代化、AI 驱动的 Windows 桌面整理器。运行时把桌面藏起来，由 AI 把文件自动分类进毛玻璃"围栏"框里；关闭时可靠还原成普通桌面。文件一个不动。
>
> 本文档是重写的架构契约（基于对 Desktop Frames + / BirdyFences 的审计与取舍）。代码/仓库命名 `SmartCorral`，产品名随语言显示"灵栅"或"Smart Corral"。

---

## 1. 产品定义

一个 Windows 10/11 桌面整理器，只做一件事并把它做好：**让桌面清爽、美观，并由 AI 自动整理文件**。

- **运行时**：藏掉 Windows 原生桌面图标，把你桌面上的文件由 AI 自动分类，装进现代毛玻璃框（"围栏"）里展示。
- **关闭时**：可靠还原成普通 Windows 桌面，所有文件/图标回来。
- **文件永不移动**——纯视觉/会话层 + AI 排序，不是物理归档。
- **精简**：只要数据框 + 文件夹门户 + AI 自动整理 + 现代外观。明确**砍掉**：便签框、标签页、Profiles、进程自动化、SpotSearch、焦点模式、12 种音效、彩蛋、捐赠/公告残留、自造消息框帝国。

非目标：不做完整复刻 Stardock Fences；不做物理移动文件的"永久整理"（用户已明确选会话式）。

---

## 2. 技术栈

| 项 | 选择 | 理由 |
|---|---|---|
| 运行时 | **.NET 8 (LTS)**, `net8.0-windows` | 长期支持、单文件自包含发布（保留"绿色便携 exe"） |
| UI 框架 | **WPF** + **XAML + MVVM** | 命门是"非激活/半透明/置顶在桌面之上的浮窗"，WPF 的 HwndSource 对定制窗口样式最成熟 |
| 现代外观层 | **WPF-UI**（Fluent 控件/主题）+ **DWM** `SetWindowAttribute`（Mica/亚克力/圆角/暗色） | 在 WPF 上做出 Win11 风格的毛玻璃围栏，而非上世纪凸边框 |
| MVVM | **CommunityToolkit.Mvvm** | 轻量、官方、源生成 |
| DI | **Microsoft.Extensions.Hosting**（或轻量手写） | 够用即可，不过度工程 |
| 序列化 | **System.Text.Json** + 源生成 | 编译期契约、快、字段改错编译报错 |
| COM 隔离 | `.lnk`/`.url` 集中在 `ShortcutService` | 不污染构建、不再卡 `dotnet build` |
| 日志 | 轻量文件日志（带分级 + 轮转） | 诊断用，默认关 |

不选 WinUI 3：它的窗口模型对"透明+不抢焦+置顶浮窗"支持弱（恰是本应用 90% 的界面）。不选 Avalonia：跨平台对深度 Windows 依赖是浪费。

---

## 3. 分层架构

```
┌─ Presentation ─────────── WPF + WPF-UI + MVVM（XAML 窗口/控件/主题）
├─ Application / Core ───── FrameManager · DesktopShell · DragDropService · TrayShell · SettingsService
├─ AI ───────────────────── AiCategorizer · LlmClient · VlmCategorizer · FrameNamer · CategoryService
├─ Platform (Win32/P-Invoke) NonActivatingWindow · DesktopIconHider · WallpaperSampler · MouseHook
├─ Persistence ──────────── 强类型模型 + System.Text.Json
└─ COM ──────────────────── ShortcutService（IWshRuntimeLibrary 隔离）
```

依赖方向单向向下：UI → Core → (AI / Platform / Persistence / COM)。Core 不依赖 UI；AI 与 Platform 是可替换的服务。

---

## 4. 项目结构

```
SmartCorral/
├─ DESIGN.md
├─ README.md
├─ .gitignore
└─ src/
   ├─ SmartCorral/                    (WPF, net8.0-windows, WinExe)
   │   ├─ App/              启动引导、DI 容器、启动顺序
   │   ├─ Models/           Frame(抽象)/DataFrame/PortalFrame/FrameItem/AppSettings（强类型）
   │   ├─ Services/
   │   │   ├─ Core/         FrameManager, DesktopShell, DragDropService, TrayShell,
   │   │   │                SettingsService, PersistenceService
   │   │   ├─ Ai/           AiCategorizer(+接口), LlmClient, VlmCategorizer,
   │   │   │                FrameNamer, CategoryService
   │   │   ├─ Platform/     NonActivatingWindow, DesktopIconHider, WallpaperSampler,
   │   │   │                MouseHook, DwmInterop
   │   │   └─ Com/          ShortcutService
   │   ├─ ViewModels/       FrameViewModel, FrameChromeViewModel, SettingsViewModel, TrayViewModel
   │   ├─ Views/            FrameWindow.xaml, PortalFrameWindow.xaml, SettingsWindow.xaml
   │   └─ Themes/           Fluent 样式、亚克力、明暗主题、强调色
   └─ SmartCorral.Tests/    单元测试（分类器、模型、持久化）
```

---

## 5. 核心抽象

- **`Frame`**（抽象基类）→ **`DataFrame`**（装 `.lnk` 快捷方式）、**`PortalFrame`**（镜像真实文件夹）。强类型，替代旧代码的 `List<dynamic>`/JObject。`NoteFrame` 不做。
- **`FrameItem`** — `Filename, IsFolder, IsLink, IsNetwork, DisplayName, Target, DisplayOrder`。
- **`FrameWindow`** — 继承 `NonActivatingWindow` 的 WPF 窗口；按 Frame 类型决定内容。半透明、不抢焦点、可拖动/缩放/卷起。
- **`FrameManager`** — 框的增删改查、渲染、持久化。**目标 ≤ 2500 行**（旧 `FrameManager.cs` 是 9932 行的上帝类；MVVM 分离关注点后大幅瘦身）。不再塞拖拽/右键/图标管线。
- **`DesktopShell`** — "会话整理"总控：`HideNativeDesktopIcons()` / `RestoreNativeDesktopIcons()` + 崩溃自愈标志位（见 §6）。
- **`DragDropService`** — 接收 shell 拖放 → `ShortcutService.CreateShortcut` → `FrameManager.AddItem`。
- **`AiCategorizer`**（接口）→ `LlmCategorizer`（文本）+ `VlmCategorizer`（图标兜底）。输入：桌面文件描述符列表；输出：每个文件 → 类别 + 置信度。
- **`CategoryService`** — 类别 ↔ 框的 create-or-find；AI 命名；用户可改名/合并/删；**持久化 + 与当前桌面对账**（见 §7）。
- **`FrameNamer`** — 用 LLM 给一组文件起一个简短、跟随 UI 语言的框名。
- **`LlmClient`** — OpenAI 兼容 `/chat/completions`，批量、`response_format=json_object`、Bearer、超时 60s。
- **`ShortcutService`**（COM）— `CreateLnk` / `ResolveLnk` / `ExtractUrl`，单独隔离。
- **`SettingsService`** — 强类型 `AppSettings`，JSON，`ApiKey` 用 **DPAPI（CurrentUser）** 加密。

---

## 6. "清爽桌面"生命周期（命门）

```
启动
  1. 单实例 → 载入设置 → 构建 DI
  2. 【自愈】若上次非正常退出且标志位为"我们藏过图标" → 先强制 RestoreNativeDesktopIcons()
  3. DesktopShell.HideNativeDesktopIcons()        → 桌面瞬间清爽
  4. 扫描桌面文件 → AiCategorizer.Categorize()    → 批量一次，{file: category, confidence}
  5. CategoryService 建框（持久化 + 对账）+ ShortcutService 建指向桌面真文件的快捷方式
  6. FileSystemWatcher（去抖 3s）监听新文件 → 增量分类

运行
  框里展示分类好的快捷方式；用户可拖拽/改名/换色；AI 处理新文件

退出（OnExit 与正常关闭路径）
  DesktopShell.RestoreNativeDesktopIcons()        → 桌面完全还原
  清除"我们藏过图标"标志位
```

**关键不变量：文件永不移动。** "关 app 还原"因此天然安全——只动了可见性，没碰文件。

**可靠还原（三重保险）：**
1. 正常退出 → `RestoreNativeDesktopIcons()`。
2. 崩溃自愈 → 标志位（注册表/文件），下次启动检测到"藏过 + 未正常退出"则强制还原。
3. 兜底 → 该隐藏是 Explorer 进程内的 listview 状态，**重启 Explorer 或重启系统必然恢复**。

---

## 7. AI 模块

**配置**（`AiSettings`，UI 可编辑，DPAPI 加密 key）：
```
BaseUrl            // 如 https://api.openai.com/v1 或 https://api.deepseek.com/v1 或 http://localhost:11434/v1
ApiKey             // DPAPI 加密
TextModel          // 如 gpt-4o-mini / deepseek-chat / qwen-...
VlmModel           // 可选，低置信度兜底用
EnableVlmFallback  // bool
ConfidenceThreshold// float，默认 0.6
```

**分类流水线：**
1. 收集桌面文件描述符 `{name, ext, resolvedTarget, size}`。
2. `LlmCategorizer` → JSON `{file: {category, confidence}}`（一次批量）。
3. 置信度 < 阈值 且 `EnableVlmFallback` → `VlmCategorizer` 把这些文件的图标喂进去再判。
4. `CategoryService` 把每个类别映射到一个框（建或找，AI 命名）。

**类别策略：** AI 看桌面**实际内容自创类别**（不固定列表）。类别**持久化**（跨启动记住），启动时与当前桌面对账：
- 已分类文件仍在桌面 → 保留归类。
- 桌面新文件 → 增量分类进已有/新框。
- 桌面已删除文件 → 清出框（快捷方式失效即移除）。

**无 AI 也能用：** 没配置 key → 退化成手动 Fences（自己拖文件进框）。AI 是增强，不是依赖。

---

## 8. 数据 & 持久化

- `AppData { List<Frame> Frames, AppSettings Settings }`，`System.Text.Json` 源生成。
- 单层布局：便携 exe 同级 `data/`（`frames.json`、`settings.json`、`shortcuts/`）。**无 Profiles**。
- 始终通过 `PersistenceService` 读写，绝不散落路径。

---

## 9. 外观与主题

- **WPF-UI** 提供 Fluent 控件 + 明暗主题 + Mica 包装。
- **DWM** `SetWindowAttribute`：`DWMWA_SYSTEMBACKDROP_TYPE`（Mica/亚克力）、`DWMWA_WINDOW_CORNER_PREFERENCE`（圆角）、暗色边框。围栏 = 毛玻璃 + 圆角 + 微阴影。
- 明暗跟随系统；强调色可配。设置/AI 配置窗口用 WPF-UI Fluent，与围栏视觉统一。
- **明确抛弃**旧版那种老式凸边框/灰按钮。

---

## 10. 从旧代码抢救的 Windows 技巧（逐个验证合理性）

| 技巧 | 判断 |
|---|---|
| `NonActivatingWindow`（`WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`） | ✅ 标准做法，照搬 |
| 藏桌面图标（`Progman`→`SHELLDLL_DefView`→`SysListView32`，`WorkerW` 兜底） | ✅ hack 但 Fences 也这么干，无干净官方 API；封装进 `DesktopIconHider` + 加还原自愈 |
| 壁纸取主色（`SPI_GETDESKWALLPAPER` + `TranscodedWallpaper` 监听 + 像素采样） | ✅ 保留，重写时取更稳实现 |
| 桌面双击 / show-desktop 鼠标钩子（`WH_MOUSE_LL`） | ✅ 照搬 |
| `.lnk`/`.url` 解析（`IWshRuntimeLibrary` + 二次抓取兜底） | ✅ 抽成 `ShortcutService` |
| 其它 SPI/Shell 调用 | ⚠️ 有更现代替代就用新的 |

原则：复制**技巧与 P/Invoke 签名**，不复制代码结构。新代码用强类型 + MVVM + DI 重写。

---

## 11. 分阶段路线图（~2 个月，每阶段独立可用）

| 阶段 | 周 | 产出 | 验收 |
|---|---|---|---|
| **0 骨架** | 1 | 新解决方案、DI/XAML 启动、`NonActivatingWindow` + 手写一个毛玻璃浮框、托盘、单实例 | "hello frame"：一个毛玻璃浮框飘在桌面 |
| **1 核心无 AI** | 2–3 | `DataFrame`、拖拽 + `ShortcutService`(COM 隔离)、图标提取、强类型持久化、框 chrome(移动/缩放/卷起/右键)、`DesktopShell` 藏/还原 + 崩溃自愈 | 手动 Fences 平价：拖文件进框、关 app 桌面还原 |
| **2 Portal + 美化** | 4 | `PortalFrame`（文件夹镜像）、现代主题打磨（WPF-UI 明暗/强调色/圆角毛玻璃） | 视觉到位：毛玻璃围栏 + 文件夹门户 |
| **3 AI 文本** | 5–6 | `LlmClient` + `AiCategorizer`(文本) + `CategoryService`(持久化+对账) + `FrameNamer` + 批量流水线，接入启动 + `FileSystemWatcher` 增量；AI 配置 UI | 开 app 自动分类桌面、AI 命名框 |
| **4 VLM + 打磨** | 7 | `VlmCategorizer` 兜底、设置 UI 完善、错误处理、日志 | 低置信度文件由图标兜底分类 |
| **5 加固发布** | 8 | 单文件自包含发布、自测、打包 | 可分发 |

---

## 12. 已确认的取舍（决策记录）

- ✅ 重写，不改造（旧代码 ~40% 核心 / 30% 高级 / 20% 杂物，核心还被埋在 9932 行上帝类里）。
- ✅ 只要核心：`DataFrame` + `PortalFrame`，砍 `NoteFrame`/标签页/Profiles/自动化/SpotSearch/焦点/音效/彩蛋。
- ✅ 会话式整理（模式 B）：藏桌面图标 + 快捷方式框，关 app 还原；不做物理移动。
- ✅ AI：启动自动一次 + `FileSystemWatcher` 增量；文本优先，低置信度 VLM 兜底；OpenAI 兼容 `baseUrl/key/model`；AI 命名框；类别持久化 + 对账。
- ✅ 技术栈：WPF + WPF-UI + .NET 8 + CommunityToolkit.Mvvm + System.Text.Json。
- ✅ 新仓库（独立于旧 fork），单文件便携发布。
- ✅ 名字：灵栅（中文）/ Smart Corral（英文）。

---

## 13. 待各阶段细化（不在初稿展开）

- `Frame`/`FrameItem` 精确字段集、`frames.json` schema 终稿。
- AI 提示词设计（分类 prompt、命名 prompt、JSON schema 约束）。
- 设置窗口与托盘菜单的精确布局。
- `DesktopIconHider` 在多显示器/不同 Windows 版本上的兼容矩阵。
- 错误与降级策略（API 失败、限流、超时、断网）。
- 性能预算（启动到分类完成的延迟上限）。
