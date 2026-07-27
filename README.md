# 灵栅 / Smart Corral

现代化、AI 驱动的 Windows 桌面整理器。

- **运行时**：藏掉原生桌面图标，由 AI 把桌面文件自动分类进毛玻璃"围栏"框。
- **关闭时**：可靠还原成普通桌面。**文件一个不动**——纯视觉/会话层 + AI 排序。
- **精简**：只要数据框 + 文件夹门户 + AI 自动整理 + 现代外观。

> 状态：设计阶段。完整架构见 [DESIGN.md](./DESIGN.md)。

## 技术栈

WPF + WPF-UI + .NET 8 + CommunityToolkit.Mvvm + System.Text.Json。单文件自包含发布。

## 背景

重写自开源项目 Desktop Frames + / BirdyFences（MIT）。只取其 Windows 编程技巧，代码全新。
