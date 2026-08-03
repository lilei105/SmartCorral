# SmartCorral 桌面窗口层级问题：分析与待核实项

## 1. 产品背景

SmartCorral（灵栅）是一款 Windows 桌面整理工具。它在桌面上显示若干半透明的分类框（Frame），用户可以把桌面上的文件/快捷方式拖进框里，也可以由 AI 自动分类。被收入框的文件从原生桌面消失（物理移动到托管目录），但在框里仍可左键启动、右键显示系统菜单。

## 2. 要解决的问题

产品定位是"常驻桌面的助手"。核心交互需求：

- **正常工作时**：框应该位于普通应用窗口**之下**（不挡屏、不抢焦点），用户能正常使用其他软件。
- **Win+D / 显示桌面时**：框应该**保持可见**——因为文件都已经从桌面移走了（桌面是空的），如果框也消失了，用户回到桌面什么都看不到。
- **不要闪烁、不要延迟**。

这三条合在一起就是经典"桌面小组件"的需求——像 Rainmeter 的 "Stay on Desktop" 模式那样，窗口是桌面的一部分，不受 Win+D 影响，在普通窗口之下。

## 3. 尝试过的方案

### 方案 A：挂入 WorkerW 桌面壁纸层（理想方案，未成功）

#### 背景：什么是 WorkerW 方案

Windows 桌面有一组特殊窗口：

```
Progman（桌面宿主窗口）
├── SHELLDLL_DefView（桌面图标列表）
│   └── SysListView32（图标列表的 ListView 控件）
WorkerW（壁纸渲染窗口，在 SHELLDLL_DefView 背后）
```

`WorkerW` 是渲染桌面壁纸的窗口，它在 z-order 上位于 `SHELLDLL_DefView`（图标层）**背后**。如果把我们的窗口挂到 `WorkerW` 上（通过 `SetParent`），它就会：

- 在壁纸之上、图标层之下 → 可见（桌面图标已被我们的程序移走，所以不会被图标盖住）
- 在普通应用窗口之下 → 不挡屏
- 属于桌面层的一部分 → Win+D 不会隐藏它

这是 Rainmeter、Wallpaper Engine 等桌面定制工具经典使用的方案。

#### 创建 WorkerW 的标准方法

`WorkerW` 不总是存在。标准做法是向 `Progman` 发送一个未公开的消息 `0x052C`，让 `Progman` 创建它。社区中有多种参数组合被报告有效，常见的有：

- `SendMessage(Progman, 0x052C, 0, 0)`
- `SendMessage(Progman, 0x052C, 0xD, 1)`（Rainmeter 使用）
- `PostMessage(Progman, 0x052C, 0xD, 1)`（异步版本）

发送后，再通过 `EnumWindows` 枚举顶层窗口，找到包含 `SHELLDLL_DefView` 的窗口（即 `Progman`），然后取其下方紧邻的 `WorkerW`。

#### 在我们的系统上的实际表现

**测试环境**：Windows 11 家庭中文版，Build 26200，3 个显示器（含 2 个外接），运行有 LittleBigMouse（多显示器鼠标管理工具）和 ASUS PC Assistant（含 OLED 防烧屏功能）。

**诊断结果**：

1. `Progman` 存在且正常（矩形覆盖虚拟屏幕 4482×3548，包含 `SHELLDLL_DefView` 子窗口）。

2. 系统中有 **17 个 `WorkerW` 窗口**，但全部异常：
   - 尺寸都是 **202×56 像素或 0×0**（不是全屏壁纸窗口）
   - 全部 **不可见**（`IsWindowVisible` 返回 false）
   - 没有一个包含 `SHELLDLL_DefView` 子窗口
   - 没有一个有 `WS_EX_LAYERED` 扩展样式
   - **在发送 0x052C 之前就已经存在**（不是我们创建的）

3. 向 `Progman` 发送 `0x052C` 消息，**尝试了三种参数组合**：
   - `SendMessageTimeout(Progman, 0x052C, 0xD, 1, SMTO_NORMAL, 2000)` → WorkerW 数量不变（17→17），无新全屏窗口
   - `SendMessageTimeout(Progman, 0x052C, 0, 0, SMTO_NORMAL, 2000)` → 同上
   - `PostMessage(Progman, 0x052C, 0xD, 1)` + 500ms 等待 → 同上

4. 使用 `SystemParametersInfoW(SPI_SETDESKWALLPAPER, ...)` **刷新壁纸**（重新设置当前壁纸路径，等于一次 no-op 刷新），等待 1000ms → WorkerW 数量不变（17→17），无新全屏窗口。

5. 经典查找算法（`EnumWindows` → 找含 `SHELLDLL_DefView` 的窗口 → `FindWindowEx(NULL, progman, "WorkerW", NULL)` 取其后的 WorkerW）返回 `NULL`——即 `Progman` 的 z-order 下方没有 `WorkerW`。

6. 当前壁纸路径来自 ASUS 软件：`...\ASUSPCAssistant_...\AsusOLEDShifter\Shift-xxx.jpg`

**结论**：无法获取或创建全屏壁纸 WorkerW → 无法将窗口挂入桌面层。

**待核实的关键问题**：0x052C 和 SystemParametersInfo 都没能创建 WorkerW，原因是 (a) Windows 11 Build 26200 改变了 DWM 的桌面合成架构、不再使用 WorkerW 渲染壁纸，还是 (b) ASUS OLED Shifter / LittleBigMouse 等第三方软件干扰了 Progman 的消息处理？如果是 (b)，禁用相关软件后 WorkerW 方案可能仍然可行。

### 方案 B：挂入 Progman（失败）

`Progman` 是桌面宿主窗口，`GetShellWindow()` 总是能可靠获取它（不像 WorkerW 需要查找/创建）。

**尝试**：`SetParent(frameHwnd, progman)` ——调用成功（返回原父窗口），但 frame **完全不可见**。

**原因分析**：`Progman` 的客户区被 `SHELLDLL_DefView`（桌面图标列表）覆盖。我们的 frame 作为 `Progman` 的另一个子窗口，在 z-order 上位于 `SHELLDLL_DefView` **之下** → 被图标层盖住 → 不可见。

**为什么经典方案不直接挂 Progman**：`WorkerW` 在 `SHELLDLL_DefView` **背后**（z-order 更低），挂到 WorkerW 上的窗口在图标层后面、壁纸层前面，所以可见。但我们的系统上没有这个 WorkerW，所以这个方案的前提不成立。

### 方案 C：Topmost 切换（当前方案，不完美）

**思路**：平时 frame 不置顶（位于普通窗口之下，不挡屏）；检测到前台变为桌面（用户按了 Win+D 或点击了桌面）时，临时把 frame 设为 Topmost（浮出在桌面层之上）；前台变回普通窗口时取消 Topmost。

**实现**：使用 Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化。前台是 `Progman`（桌面）时设 Topmost，否则取消。

**实际效果**：
- Win+D 时 8 个 frame 中有 7 个能浮出，**1 个（"垂直行业项目库"）经常浮不上来**。
- 取消 Topmost 时 frame 和刚激活的窗口之间的 z-order 可能错乱（frame 压在窗口上面）。
- 快速交替点击桌面/窗口时 z-order 抖动明显。

**为什么 1 个 frame 浮不上来**：8 个 frame 在循环里依次调 `SetWindowPos(HWND_TOPMOST)`。`SetWindowPos` 返回 `True`（调用成功），但该 frame 仍然不可见。推测原因是 Windows 的桌面合成器（DWM）是异步的——它在我们的循环执行的某个时刻重新断言了桌面层的 Topmost z-order，恰好盖住了循环中位置较低的那个 frame。220ms 后的重断言（再次对全部 frame 调 `SetWindowPos(HWND_TOPMOST)`）大部分时候能补救，但不是 100% 有效。

**为什么无法根治**：frame 是 Topmost、桌面层也是 Topmost——两者在**同一个 Topmost z-order 层**里竞争。`SetWindowPos(HWND_TOPMOST)` 是"举手"操作——谁最后举手谁在最上面。但 Windows 可以在任何时刻重新"举手"（重新断言桌面层 Topmost），我们的 frame 就会被压下去。这是一场双方都能无限重试的竞争，我们无法保证自己是最后一个。

## 4. 待核实的问题总结

请帮忙核实以下推断是否成立：

1. **Windows 11 Build 26200（约 25H2）是否仍然支持 0x052C 创建壁纸 WorkerW？** 有没有微软官方文档或社区报告指出 24H2/25H2 改变了 DWM 的桌面合成方式？

2. **Rainmeter 的 "Position: On Desktop" 模式在 Windows 11 24H2/25H2 上是否仍然有效？** 如果 Rainmeter 也遇到同样的问题，说明是 Windows 版本变化；如果 Rainmeter 仍然正常，说明可能是我们的测试环境（ASUS 软件 / LittleBigMouse）导致。

3. **那 17 个 202×56 的不可见 WorkerW 窗口是否正常？** 还是说某个第三方软件创建它们时干扰了桌面窗口层级？

4. **ASUS OLED Shifter（UWP 后台任务）是否可能拦截 0x052C 或干扰 SystemParametersInfo？** 它以非标准方式设置壁纸（通过 UWP API 而非 SystemParametersInfo），可能导致 Progman 不响应传统的壁纸操作消息。
