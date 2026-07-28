# 去掉 wrapper.lnk 层：frame item 改为「内存 stub + 代理点击」

**状态：已设计，未开工。** 这是一次架构简化重构，建议作为独立、专注的工作来做（配假文件测试），不要在长会话尾巴上 rushed 改。

## 动机
现在的 custody 模型：归类时把**真文件**从桌面移进 custody，但 frame 里显示的不是真文件，而是一个**包装用 `.lnk`**（`data/shortcuts/` 下），它指向 custody 副本。这层 wrapper 是**旧架构的遗物**——旧架构文件留在桌面、用 .lnk 指过去"不移动就能启动"。现在 custody 已经把真文件移走了，custody 副本本身就是真文件，再套一层 wrapper 纯属多余中转。

**目标**：frame item 变成纯内存 stub（图标 + 文字 + custody 路径），左键/右键/图标**直接代理给 custody 真文件**，砍掉整层 wrapper 机制。

## 现状 vs 提议

| | 现状（wrapper） | 提议（stub） |
|---|---|---|
| 归类 | Take（移走）+ `Import`建 wrapper.lnk + `Retarget` | 只 Take（移走） |
| frame 显示 | wrapper.lnk（真实文件） | 内存 stub（图标+文字） |
| 左键启动 | `Process.Start(wrapper.lnk)` | `Process.Start(custody 真文件)` |
| 右键菜单 | 系统菜单 on `LivePath`（custody） | 同（已经是） |
| 图标 | 从 wrapper.lnk 解析 | 直接从 custody 真文件抽 |
| 箭头 | `SourcePath` 是 .lnk 才显示 | custody 路径是 .lnk 才显示（更简单） |

## 改动清单（按文件）

### `Models/FrameItem.cs`
- **移除** `Filename`（wrapper 路径）和 `Target`（缓存的目标）—— stub 不需要。
- 保留：`DisplayName`、`IsFolder`、`SourcePath`（还原用）、`LivePath`（[JsonIgnore]，运行时 custody 路径）、`DisplayOrder`。
- 旧 `frames.json` 里的 `Filename`/`Target` 字段：STJ 反序列化时自动忽略多余字段，**无需迁移**；custody 路径由启动时 `RetakeAll` 从 `SourcePath` 重新推导。

### `Services/FrameManager.cs`
- `AddDesktopFile` → 返回 `bool`（已改）：只 `Take`；成功就加 `FrameItem{DisplayName, IsFolder, SourcePath, LivePath}`，**不再 Import/Retarget/建 wrapper**。
- `RetakeAllIntoCustody`：只 `Take` + 设 `LivePath`，**去掉 Retarget**；末尾仍 `IconService.ClearCache()`。

### `Views/FrameWindow.xaml.cs`
- `BuildItem` 图标：
  ```csharp
  string custody = item.LivePath!;
  ImageSource icon = custody.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
      ? IconService.GetIconForShortcutFile(custody, showArrow: true)
      : IconService.GetIconForPath(custody);
  ```
- `Item_Click`：`Process.Start(new ProcessStartInfo(item.LivePath!) { UseShellExecute = true })`。
- `Item_RightClick` / 拖拽载荷：**已经用 `LivePath`**，不变。
- 移除对 `ShortcutService.AbsolutePath(item.Filename)` 的引用。

### `Services/IconService.cs`
- 新增/暴露 **public `GetIconForPath(string path)`**（现在只有 private `Load`）——给 raw（非 .lnk）custody 文件抽图标。
- `GetIconForShortcutFile` 保留（.lnk custody 文件用）。

### `Services/Com/ShortcutService.cs`
- **删除** `Import` / `Retarget` / `ResolveTarget` / `AbsolutePath`（全是 wrapper 机制）。
- **保留** `ResolveIconLocation`（`IconService` 抽 .lnk 图标时仍用）。
- （或整个文件只剩这一个方法。）

### `Services/FrameManager.cs`（sweep）
- `SweepDataHealth` 去掉「孤儿 wrapper .lnk」那步（没 wrapper 了）。死链接 / SourcePath 去重 / 孤儿 custody 还原 保留。

### `data/shortcuts/` 目录
- 不再使用。可删（或留着不管）。`SweepDataHealth` 不再扫它。

## 保持不变（不要动）
- **custody 清单 + 还原安全网**：退出 `RestoreAll`、崩溃下次启动自愈——**必须保留**（否则 app 一挂桌面就空了）。
- AI 分类（`AiOrganizeService` → frames.json）。
- 拖拽语义：框间拖 = 改 frames.json（`MoveItem`，custody 不动）；拖出桌面 = 释放（还原 + 移除 item）。
- 启动只「重新托管已归档项」（`RetakeAll`），**不**每次启动全吸桌面（避免重跑 AI + 挪运行中文件）。
- locked-file 处理（Take 失败不加 item、退出未还原弹气泡）。

## 决策（已定）
- **启动行为**：只把 frames.json 里已归档的重新移进 custody；未分类的留桌面。**不**做"启动全吸"。
- **崩溃安全**：保留退出还原 + 启动自愈。

## 小坑 / 注意
- **图标按类型分**：.lnk custody 文件用 `GetIconForShortcutFile`（带箭头、解析 IconLocation）；raw 用 `GetIconForPath`。
- **"打开文件位置"**：系统菜单 on custody 路径 → 会打开 `%LOCALAPPDATA%\SmartCorral\custody` 目录（暴露内部目录）。两种模型都这样，可接受。
- **启动 raw 文件**：`Process.Start(custody\report.pdf)` → 应用从 custody 打开；用户保存即存回 custody（=真文件）。与现状一致。
- **旧 frames.json**：`Filename`/`Target` 字段被忽略，无需迁移脚本。

## 验证（务必假文件）
1. `dotnet build` 干净。
2. 拖几个假文件/文件夹/.lnk 进框 → 进框 + 桌面消失；图标正确（.lnk 带箭头、raw 不带）。
3. 左键 → 正常启动（.lnk 走 WorkingDir/参数；raw 用默认程序）。
4. 右键 → 系统菜单正常（打开/属性/删除）；删除后 item 从框移除。
5. 拖到另一个框 → 重新归类（custody 不动）；拖到桌面 → 释放。
6. 退出 → 全还原桌面；崩溃 → 下次启动自愈。
7. locked 文件（运行中的 exe）→ 不进框、留桌面、退出弹气泡。
8. 确认 `data/shortcuts/` 不再产生新文件。

## 收益
砍掉：`ShortcutService` 的 Import/Retarget/ResolveTarget/AbsolutePath、`data/shortcuts/` 目录、RetakeAll 的 Retarget、sweep 的 wrapper 清理、`targetOverride` 接线。箭头判断从"原 SourcePath 是否 .lnk"简化为"custody 路径是否 .lnk"。frame item 字段更少（无 Filename/Target）。整体少一层中转、架构更直白。
