# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

A Windows Forms utility targeting `net10.0-windows` for testing regular expressions and inspecting matches, groups, and captures. It supports regex options and opening and saving regex pattern files.

## Development checks

The application uses `Zongsoft.CodeAnalysis` with its version defined in the repository root `Directory.Packages.props`. Use .NET SDK 10.0.401 or a toolchain with Roslyn 5.9 or later. The analyzer is a private build dependency, not a runtime dependency of the tool.

```powershell
dotnet build src/Zongsoft.Tools.Regular.csproj -p:ZongsoftCodeStyleStrict=true
dotnet format style src/Zongsoft.Tools.Regular.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

The build targets `net10.0-windows`. Validate matching, capture results, regex options, and file operations in the Windows UI; this tool has no automated test project. See the [repository instructions](../AGENTS.md#代码规范检查) for editor configuration, resource generation and validation requirements.
