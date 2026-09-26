# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

A Windows Forms utility for testing regular expressions and inspecting matches, groups, and captures. It supports regex options and opening and saving regex pattern files.

## Install and run

The NuGet tool supports Windows x64 and requires the .NET 10 Windows Desktop Runtime. Install it with the .NET SDK:

```powershell
dotnet tool install -g Zongsoft.Tools.Regular
dotnet regular
```

`dotnet-regular` is the tool command and also works directly when the global tools directory is on `PATH`. Update or remove the tool with `dotnet tool update -g Zongsoft.Tools.Regular` or `dotnet tool uninstall -g Zongsoft.Tools.Regular`.

The installed command starts the bundled Windows application and then exits. The application keeps its own window open. The GUI remains a separate `net10.0-windows` executable; a small `net10.0` launcher supplies the .NET tool command because the SDK cannot pack a WinForms project directly as a tool.

## Develop the Windows application

`src/Zongsoft.Tools.Regular.csproj` remains the startup project in Visual Studio. F5, `dotnet run --project src/Zongsoft.Tools.Regular.csproj`, and the built `src/bin/Debug/net10.0-windows/Zongsoft.Tools.Regular.exe` all run the GUI directly. A normal solution build does not create NuGet packages.

Like the other tools in this repository, Cake's `build` target produces local packages. Run this from the `regular` directory on Windows:

```powershell
dotnet cake --edition Release --target build
```

The command publishes the GUI for `win-x64`, packages it with the launcher, and writes `Zongsoft.Tools.Regular.<version>.nupkg` and `Zongsoft.Tools.Regular.win-x64.<version>.nupkg` to `tool/bin/Release`. The version comes from `tool/Zongsoft.Tools.Regular.Tool.csproj` and is applied to the published GUI assembly.

Test the package without changing globally installed tools:

```powershell
$toolVersion = dotnet msbuild tool/Zongsoft.Tools.Regular.Tool.csproj -getProperty:Version -nologo
dotnet tool install Zongsoft.Tools.Regular --tool-path ./tool/bin/local-install --version "$toolVersion" --source ./tool/bin/Release --no-http-cache
./tool/bin/local-install/dotnet-regular.cmd
```

If that version is already installed at the test path, uninstall it from that path before reinstalling. With `NUGET_API_KEY` set, `dotnet cake --edition Release --target pack` builds and pushes both packages to NuGet.org. The NuGet Trusted Publishing policy must allow both `Zongsoft.Tools.Regular` and `Zongsoft.Tools.Regular.win-x64`.

## Development checks

Both projects inherit central package management from the repository root `Directory.Packages.props`. `Directory.Build.props` supplies the private `Zongsoft.CodeAnalysis` reference, whose version is centrally defined; the launcher has no other package dependencies. Its own NuGet package version remains in the tool project, as with deployer and packager. Use .NET SDK 10.0.401 or a toolchain with Roslyn 5.9 or later.

```powershell
dotnet build Zongsoft.Tools.Regular.slnx -p:ZongsoftCodeStyleStrict=true
dotnet format style Zongsoft.Tools.Regular.slnx --no-restore --verify-no-changes --diagnostics IDE0049
```

Validate matching, capture results, regex options, file operations, and startup through both the direct executable and an isolated local tool installation in the Windows UI. This tool has no automated test project. See the [repository instructions](../AGENTS.md#代码规范检查) for editor configuration, resource generation and validation requirements.
