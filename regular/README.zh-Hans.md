# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

这是一个面向 `net10.0-windows` 的 Windows Forms 正则表达式测试器，可查看匹配、分组与捕获结果，设置正则选项，以及打开和保存正则表达式文本文件。

## 开发规范检查

应用项目使用 `Zongsoft.CodeAnalysis`，版本由仓库根 `Directory.Packages.props` 管理；使用 .NET SDK 10.0.401 或具有 Roslyn 5.9 及以上版本编译器的工具链。分析器为私有构建依赖，不随工具作为运行时依赖分发。

```powershell
dotnet build src/Zongsoft.Tools.Regular.csproj -p:ZongsoftCodeStyleStrict=true
dotnet format style src/Zongsoft.Tools.Regular.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

构建目标为 `net10.0-windows`。匹配、捕获结果、正则选项和文件操作通过 Windows 界面验证；本工具没有自动化测试项目。详细规范检查及资源生成要求见 [仓库说明](../AGENTS.md#代码规范检查)。
