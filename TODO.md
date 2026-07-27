# SmartCorral — 待办

## DPI / 多显示器（明天测试）

1. **跨不同 DPI 显示器拖框**：`MonitorService.DpiScaleX/Y` 在 `ContentRendered` 时只捕获一次。
   把框拖到缩放比例不同的副屏 → DpiScale 还是旧的 → 拖拽/缩放速度不对。
   修法：监听 `WM_DPICHANGED` 消息，重新捕获 DpiScale 并更新 MonitorService。

2. **图标在高 DPI 下偏软**：`SHGetFileInfo` 取的是 32px 大图标，在 150% 缩放下渲染成 48px 轻微模糊。
   修法：按 DPI 取 jumbo(256px) 或 extralarge(48px) 图标。

## 后续阶段（DESIGN.md 路线图）

- **Phase 2**：真 Mica/亚克力毛玻璃美化 + Portal 文件夹框。
- **Phase 3b**：FileSystemWatcher 增量（新文件后台自动归类）+ VLM 看图标兜底。
- **打包**：DPAPI 加密 API key + app 图标 + 单文件自包含发布。
