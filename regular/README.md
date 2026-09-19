# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

A graphical utility for testing regular expressions and inspecting matches, groups, and captures.

## Development checks

Production and test projects use `Zongsoft.CodeAnalysis` 1.1.0. Use .NET SDK 10.0.401 or a compatible newer compiler. The analyzer is a private build dependency, not a runtime dependency of the tool.

```powershell
dotnet build src/Zongsoft.Tools.Regular.csproj -p:ZongsoftCodeStyleStrict=true
dotnet format style src/Zongsoft.Tools.Regular.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

The build checks every configured target framework. See the [repository instructions](../AGENTS.md#代码规范检查) for editor configuration, resource generation and validation requirements.
