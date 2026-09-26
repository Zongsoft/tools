## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。`src/` 是 `net10.0-windows` WinForms 正则表达式测试器，使用 `System.Text.RegularExpressions` 展示 Match、Group 和 Capture 结果；`tool/` 是 `net10.0` 的 .NET 工具启动器，工具包中携带 Windows x64 的 GUI 文件。

## 工作边界

- `MainForm.cs` 保存匹配、文件操作、快捷键和结果树行为；`MainForm.Designer.cs` 保存控件布局与事件连接。
- `AboutDialog.cs` 读取程序集元数据；对应 `*.Designer.cs` 与 `.resx` 由设计器维护。
- `Program.cs` 是 Windows 桌面入口。不要为跨平台复用而在本工具中引入与其职责无关的框架抽象。
- `tool/Program.cs` 只定位并启动随包分发的界面程序；开发时仍直接运行 `src/` 产生的 `.exe`，不可在 WinForms 项目中设置 `PackAsTool`。
- `build.cake` 的 `build` 先以工具版本发布 GUI，再打出单个工具包；`pack` 依赖 `build` 并推送 NuGet。工具包必须包含 README、图标、GUI 运行配置和卫星资源。
- 两个项目继承根 `Directory.Packages.props` 的中央包管理；通用分析器由根 `Directory.Build.props` 引用并集中定版，工具自身的 NuGet 包版本留在 `tool/` 项目文件中。

## 高风险契约

- 修改控件名或事件时同步代码隐藏与 Designer；优先通过 WinForms Designer 改布局，避免大范围重写生成代码。
- 保持正则选项、匹配顺序、组/捕获层级以及状态栏索引、长度和值的对应关系。
- 打开和保存只处理用户明确选择的文件；不要在启动或测试时覆盖现有文件。
- UI 文本变化检查资源、本地化、助记键和键盘快捷方式。

## 验证

- 构建：`dotnet build Zongsoft.Tools.Regular.slnx`；本地制包：`dotnet cake --edition Release --target build`；`pack` 将构建并推送到 NuGet.org。
- 通过隔离的 `--tool-path` 安装本地包，确认命令、GUI 文件、运行配置、中文资源与直接运行的 Debug `.exe`；不要用全局安装替代本地验证。
- 在 Windows 手工验证无匹配、多匹配、命名组、重复捕获、零长度捕获、无效表达式和各 RegexOptions 组合。
- 布局变化检查常用缩放比例、窗口缩放、键盘导航以及打开/保存流程；本项目当前没有自动化测试。

代码规范采用 `Zongsoft.CodeAnalysis`，版本由仓库根 `Directory.Packages.props` 管理，检查方法与 SDK 要求见 [仓库规范](../AGENTS.md#代码规范检查)。
