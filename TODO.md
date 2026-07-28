# SmartCorral — 待办

## 待排查

- **跨屏内容发软**：把 frame 拖到不同 DPI 的副屏后，内容（文字/图标）偶尔略发软。DPI 管线（PMv2、`WM_DPICHANGED`、每帧 DPI、物理像素磁吸）已全部做完；曾去掉 `AllowsTransparency` 也没修好，根因未定位。详见 `docs/development-pitfalls.md` §5。

## 后续阶段（DESIGN.md 路线图）

- **Phase 2**：
  - 真 Mica/亚克力毛玻璃 —— **已尝试、已回退**：在当前自定义 frame 窗口上渲染不出来（详见 `docs/development-pitfalls.md` §4、记忆 `mica-backdrop-deadend`）。下次需换策略（先用干净 `Window` 验证 `WindowChrome` / 或上 WinUI 岛）。
  - Portal 文件夹框 —— 待做。
- **Phase 3b**：FileSystemWatcher 增量（新文件后台自动归类）+ VLM 看图标兜底。
- **打包**：DPAPI 加密 API key + app 图标 + 单文件自包含发布。

## 已完成（归档）

- ✅ 跨不同 DPI 显示器拖框（`WM_DPICHANGED` 刷新 DpiScale + 每帧自身 DPI 拖拽/缩放 + 物理像素磁吸线）。
- ✅ 高 DPI 图标（jumbo 256px，回退 extralarge 48 / large 32）。
- ✅ 图标加大（40 DIP）+ 字号微调。
- ✅ "总置顶"开关 + 关闭后点击仍可提前（`SetWindowPos(HWND_TOP, NOACTIVATE)`，不抢焦点）。
- ✅ 公开 GitHub 仓库 + 用户向 README + 开发踩坑文档。
