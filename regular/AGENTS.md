## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `net10.0-windows` WinForms 正则表达式测试器，使用 `System.Text.RegularExpressions` 展示 Match、Group 和 Capture 结果。

## 工作边界

- `MainForm.cs` 保存匹配、文件操作、快捷键和结果树行为；`MainForm.Designer.cs` 保存控件布局与事件连接。
- `AboutDialog.cs` 读取程序集元数据；对应 `*.Designer.cs` 与 `.resx` 由设计器维护。
- `Program.cs` 是 Windows 桌面入口。不要为跨平台复用而在本工具中引入与其职责无关的框架抽象。

## 高风险契约

- 修改控件名或事件时同步代码隐藏与 Designer；优先通过 WinForms Designer 改布局，避免大范围重写生成代码。
- 保持正则选项、匹配顺序、组/捕获层级以及状态栏索引、长度和值的对应关系。
- 打开和保存只处理用户明确选择的文件；不要在启动或测试时覆盖现有文件。
- UI 文本变化检查资源、本地化、助记键和键盘快捷方式。

## 验证

- 构建：`dotnet build Zongsoft.Tools.Regular.slnx`。
- 在 Windows 手工验证无匹配、多匹配、命名组、重复捕获、零长度捕获、无效表达式和各 RegexOptions 组合。
- 布局变化检查常用缩放比例、窗口缩放、键盘导航以及打开/保存流程；本项目当前没有自动化测试。
