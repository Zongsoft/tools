# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

这是一个 Windows Forms 正则表达式测试器，可查看匹配、分组与捕获结果，设置正则选项，以及打开和保存正则表达式文本文件。

## 安装与运行

NuGet 工具支持 Windows x64，需要安装 .NET 10 Windows Desktop Runtime。使用 .NET SDK 安装：

```powershell
dotnet tool install -g Zongsoft.Tools.Regular
dotnet regular
```

工具命令名为 `dotnet-regular`；全局工具目录在 `PATH` 中时也可直接运行该命令。更新或卸载分别使用 `dotnet tool update -g Zongsoft.Tools.Regular`、`dotnet tool uninstall -g Zongsoft.Tools.Regular`。

命令启动随包分发的 Windows 界面后退出，程序窗口继续运行。界面仍是独立的 `net10.0-windows` 可执行程序；由于 SDK 不支持直接把 WinForms 项目打成 .NET 工具，另用一个 `net10.0` 小型启动器提供工具命令。

## 开发 Windows 程序

在 Visual Studio 中继续将 `src/Zongsoft.Tools.Regular.csproj` 作为启动项目。F5、`dotnet run --project src/Zongsoft.Tools.Regular.csproj`，以及直接运行构建后的 `src/bin/Debug/net10.0-windows/Zongsoft.Tools.Regular.exe`，都直接打开原来的界面。普通解决方案构建不会生成 NuGet 包。

与仓库中的其他工具一样，Cake 的 `build` 任务生成本地包。在 Windows 上从 `regular` 目录执行：

```powershell
dotnet cake --edition Release --target build
```

该任务为 `win-x64` 发布界面程序，并与启动器一起生成 `tool/bin/Release` 中的 `Zongsoft.Tools.Regular.<version>.nupkg` 和 `Zongsoft.Tools.Regular.win-x64.<version>.nupkg`。版本取自 `tool/Zongsoft.Tools.Regular.Tool.csproj`，同时应用到发布的界面程序集。

可以安装到隔离目录验证，不替换已安装的全局工具：

```powershell
$toolVersion = dotnet msbuild tool/Zongsoft.Tools.Regular.Tool.csproj -getProperty:Version -nologo
dotnet tool install Zongsoft.Tools.Regular --tool-path ./tool/bin/local-install --version "$toolVersion" --source ./tool/bin/Release --no-http-cache
./tool/bin/local-install/dotnet-regular.cmd
```

如果同一版本已安装在测试目录，重新安装前先从该目录卸载。设置 `NUGET_API_KEY` 后，`dotnet cake --edition Release --target pack` 会完成构建并将两个包推送到 NuGet.org。NuGet Trusted Publishing 策略需覆盖 `Zongsoft.Tools.Regular` 和 `Zongsoft.Tools.Regular.win-x64` 两个包名。

## 开发规范检查

两个项目均继承仓库根 `Directory.Packages.props` 的中央包管理。`Directory.Build.props` 统一引用私有的 `Zongsoft.CodeAnalysis` 分析器，其版本集中定义；启动器没有其他 NuGet 包依赖。与 deployer、packager 一样，工具自身的 NuGet 包版本保留在所属项目文件中。使用 .NET SDK 10.0.401 或具有 Roslyn 5.9 及以上版本编译器的工具链。

```powershell
dotnet build Zongsoft.Tools.Regular.slnx -p:ZongsoftCodeStyleStrict=true
dotnet format style Zongsoft.Tools.Regular.slnx --no-restore --verify-no-changes --diagnostics IDE0049
```

在 Windows 界面中检查匹配、捕获结果、正则选项和文件操作，并分别验证直接运行 `.exe` 与隔离安装后的工具命令。本工具没有自动化测试项目。编辑器配置、资源生成和验证要求见[仓库说明](../AGENTS.md#代码规范检查)。
