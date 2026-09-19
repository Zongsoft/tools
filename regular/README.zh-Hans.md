# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

这是一个正则表达式的 GUI 测试器。

## 开发规范检查

生产和测试项目使用 `Zongsoft.CodeAnalysis` 1.1.0；使用 .NET SDK 10.0.401 或更新的兼容编译器。分析器为私有构建依赖，不随工具作为运行时依赖分发。

```powershell
dotnet build src/Zongsoft.Tools.Regular.csproj -p:ZongsoftCodeStyleStrict=true
dotnet format style src/Zongsoft.Tools.Regular.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

多目标构建覆盖项目全部目标框架；详细规范检查及资源生成要求见 [仓库说明](../AGENTS.md#代码规范检查)。
