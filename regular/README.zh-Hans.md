# Zongsoft.Tools.Regular

[English](README.md) | [简体中文](README.zh-Hans.md)

用于测试正则表达式的 Windows 程序。可以查看匹配、分组和捕获结果，设置正则选项，并打开或保存表达式文件。

## 运行要求

Windows x64 和 .NET 10 Windows Desktop Runtime。

## 安装与运行

```powershell
dotnet tool install -g Zongsoft.Tools.Regular
dotnet regular
```

命令会打开 Regular 窗口。如果 .NET 全局工具目录已加入 `PATH`，也可以直接运行 `dotnet-regular`。

## 更新或卸载

```powershell
dotnet tool update -g Zongsoft.Tools.Regular
dotnet tool uninstall -g Zongsoft.Tools.Regular
```
