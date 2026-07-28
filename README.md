# 灵栅 / Smart Corral

> AI 驱动的 Windows 桌面整理器——把乱糟糟的桌面，自动收进一个个半透明"围栏"框里。

运行时藏掉原生桌面图标，由 AI 把桌面上的文件和文件夹自动分类进一个个可拖拽的半透明**围栏框（frame）**；关闭时把桌面原样还原。**文件本身一个都不动**——它只是盖在桌面上的一层视觉/会话层 + AI 排序，不移动、不复制、不删除你的任何文件。

---

## ✨ 功能特性

- **一键清爽桌面**：运行时自动隐藏原生桌面图标，退出时全部还原。即使程序崩溃，下次启动也会自愈还原（不会把你的图标弄丢）。
- **AI 自动分类**：把桌面上的文件/文件夹按含义自动归入不同围栏框（"开发"、"文档"、"图片"、"游戏"……）。支持任何 OpenAI 兼容的服务：OpenAI、DeepSeek、Qwen，或本地 Ollama。
- **文件一个不动**：围栏框里显示的是快捷方式，原始文件始终待在原地。删掉围栏框或退出程序，桌面立刻恢复原状。
- **多围栏框**：随便建多少个框，每个都能：
  - 拖拽移动、拖到屏幕边缘或别的框**磁吸对齐**（带淡蓝色对齐辅助线）；
  - 右下角拖拽**缩放**；
  - 双击标题栏**卷起/展开**；
  - **锁定**（防误拖）、**重命名**、**删除**；
  - 点一下**置顶到其它框之上**。
- **系统级右键菜单**：框里的每个文件/文件夹都支持完整的 Windows 右键菜单（和资源管理器里一模一样）。
- **自动排列**：托盘里一键把所有框按右对齐网格排整齐，按内容自动调整大小。
- **重新整理**：不满意分类？托盘里一键"Re-organize all"，清空重来。
- **多显示器 & 高分屏**：原生支持多显示器；按显示器 DPI 正确缩放，拖拽/缩放/吸附在不同缩放比例的屏幕之间都跟手；图标取高清大图，高分屏下不发虚。
- **图标正确**：正确区分文件夹、自定义图标、快捷方式箭头，跟你资源管理器里看到的一致。
- **可选置顶**：默认框总在最上层；如果你嫌挡事，可以在设置里关掉"总置顶"——关掉后框不抢焦点，但点击仍能把它提到其它框前面。

---

## 🖥️ 环境要求

- **Windows 10 / 11**（64 位）。
- 从源码构建需要 **.NET 10 SDK**。

---

## 🚀 获取与运行

### 从源码构建（当前推荐）

```bash
git clone https://github.com/lilei105/SmartCorral.git
cd SmartCorral
dotnet build "src/SmartCorral/SmartCorral.csproj" -p:Configuration=Debug -restore
```

运行：

```bash
"src/SmartCorral/bin/Debug/net10.0-windows/SmartCorral.exe"
```

### 生成免安装单文件版（自包含）

```bash
dotnet publish "src/SmartCorral/SmartCorral.csproj" -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

产出在 `src/SmartCorral/bin/Release/net10.0-windows/win-x64/publish/SmartCorral.exe`——单个约 72MB 的 exe，**无需安装、无需另装 .NET 运行时**，拷到任意目录双击即可运行；围栏框/配置数据存在它旁边的 `data/`，便携。

---

## 📖 快速上手

1. **启动**：打开 `SmartCorral.exe`。桌面原生图标被隐藏，桌面上出现围栏框。任务栏通知区会出现一个托盘图标。
2. **配 AI（可选）**：右键托盘 → **Settings**，填入你的 AI 服务：
   - **Base URL**：如 `https://api.openai.com/v1`、`https://api.deepseek.com/v1`，本地 Ollama 用 `http://localhost:11434/v1`。
   - **API Key**：本地 Ollama 等免鉴权的可留空。
   - **Model**：如 `gpt-4o-mini`、`deepseek-chat`。
   - 不配置也能用，只是没有自动分类（手动拖文件进框即可）。
3. **自动整理**：配好 AI 后启动会自动跑一次分类。之后想重来，托盘 → **Re-organize all**。
4. **手动操作**：
   - **加文件**：把桌面上的文件/文件夹**拖进**任意围栏框。
   - **移动框**：拖标题栏；靠近屏幕边缘或别的框会自动吸附。
   - **缩放**：拖框右下角的小把手。
   - **卷起**：双击标题栏。
   - **打开**：点框里的图标即用默认程序打开；**右键**出系统菜单。
   - **更多**：在框上右键（新建/重命名/锁定/卷起/删除）。
5. **一键排齐**：托盘 → **Auto-arrange**。
6. **退出**：托盘 → **Exit**。桌面图标立刻全部还原。

---

## ⚙️ 设置项说明

右键托盘 → **Settings**：

| 设置 | 作用 |
|---|---|
| Base URL / API Key / Model | AI 服务配置（OpenAI 兼容）。留空 Key 可用于本地模型。 |
| Icons per frame row | 每行放几个图标（2–8），决定框的宽度。 |
| Separate folders onto their own row | 文件夹单独排一行。 |
| Keep frames always on top | 框是否总在最上层（关掉后其它窗口能盖住框，点击仍可置顶）。 |

---

## 🔒 隐私与安全

- **文件不移动**：程序只读你的桌面文件名来分类，并创建快捷方式；从不移动、复制或上传你的文件内容。
- **数据本地化**：围栏框布局、设置、快捷方式都存在程序目录下的 `data/`，便携、不进注册表。
- **API Key 本地存储**：用 DPAPI（绑定当前 Windows 用户）加密后存在 `data/settings.json`，换用户或换机器无法解密。用本地 Ollama 则无需任何 Key。

---

## ⚠️ 已知限制

- 仅支持 **Windows**。
- 运行期间会隐藏原生桌面图标（退出即还原）。
- 围栏框为**半透明着色**（非系统级磨砂毛玻璃）；真毛玻璃材质在当前窗口架构下未实现，详见 [开发文档](./docs/development-pitfalls.md)。
- 跨不同 DPI 显示器拖动时，框内容可能略有发软（已知问题，排查中）。

---

## 🛠️ 更多文档

- 架构与设计：[DESIGN.md](./DESIGN.md)
- 开发踩坑与已知问题：[docs/development-pitfalls.md](./docs/development-pitfalls.md)

## 背景

重写自开源项目 Desktop Frames + / BirdyFences（MIT），仅取其 Windows 编程技巧，代码全新。技术栈：WPF + WinForms（托盘）+ .NET 10，纯 code-behind。
