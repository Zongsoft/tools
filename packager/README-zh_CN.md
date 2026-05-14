# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) |
[简体中文](README-zh_CN.md)

-----

## 概述

[**Z**ongsoft.**T**ools.**P**ackager](https://github.com/Zongsoft/tools/tree/main/packager) 是一个制作安装包的工具，可提供 `.tar.gz`、`.deb`、`.rpm` 等 _**L**inux_ 平台的安装包。

### 基本用法

```shell
dotnet-pack <选项...> [参数...]
```

常用 Linux 安装包元数据选项：

- `--maintainer`：包维护者，例如 `Zongsoft Studio<example@zongsoft.com>`。
- `--license`：包许可，例如 `MIT` 或 `Apache-2.0`。
- `--url`：项目或安装包主页。
- `--category`：Debian 的 `Section` 与 RPM 的 `Group`。
- `--dependencies`：依赖列表，用于 Debian `Depends` 与 RPM `Requires`，可用逗号、分号或换行分隔。
- `--provides`：RPM `Provides` 条目。
- `--conflicts`：RPM `Conflicts` 条目。

## 许可

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可协议。
