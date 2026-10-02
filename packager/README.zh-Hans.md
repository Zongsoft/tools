# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` 是一个 .NET 全局工具，把应用文件目录（如纯前端 `dist` 或已发布的 .NET 应用）制作成可在 Linux 上分发和安装的 **`.tar.gz`**、**`.deb`**、**`.rpm`** 安装包，并按需生成 systemd 服务、安装/卸载脚本和 Nginx 站点配置。

包格式由 .NET 直接写出，不依赖外部的 `tar`、`dpkg-deb`、`rpmbuild` 或 `cpio` 命令，因此在 Windows 上同样可以制作 Linux 安装包。

> 🚨 注意：`dotnet-pack` 只 **制作** 安装包，从不安装。但安装包在目标主机上安装时会执行生命周期脚本，可能修改系统文件、服务和应用目录。部署到生产主机前，请先检查包内容并在预发布环境验证。

## 目录

- [功能特性](#功能特性)
- [基础概念](#基础概念)
- [安装](#安装)
- [快速开始](#快速开始)
- [命令参考](#命令参考)
- [应用身份与版本文件](#应用身份与版本文件)
- [打包项](#打包项)
- [systemd 服务](#systemd-服务)
- [生命周期脚本](#生命周期脚本)
- [Web 托管配置](#web-托管配置)
- [升迁产物集成](#升迁产物集成)
- [变量](#变量)
- [包格式](#包格式)
- [推荐打包流程](#推荐打包流程)
- [故障排查](#故障排查)
- [从源码构建](#从源码构建)
- [相关文档](#相关文档)

## 功能特性

- **一套命令，三种格式**：`tar`、`deb`、`rpm` 子命令共享同一套元数据、变量、文件条目、服务脚本和输出命名规则。
- **自动生成服务**：未提供 `.service` 文件时自动生成 systemd 服务，可用 `--listen` 设置监听地址。
- **完整的生命周期**：为所有格式生成安装和卸载脚本，支持自定义钩子及前置/后置脚本片段。
- **灵活的载荷选择**：支持显式文件、递归目录、路径段通配（含 `**`）、排除模式、目标别名，以及 `/etc/nginx/conf.d/zongsoft.web.conf` 这类根路径条目。
- **应用版本管理**：读取并回写源目录的 `.edition` 清单（回退 `.version`），支持多个 发行版 _(**E**dition)_。
- **Web 托管配置**：从 `web.profile` 生成 Nginx 站点配置，并在安装时激活。
- **升迁集成**：收录独立 [migrator 工具](../migrator/README.zh-Hans.md)制作的升迁产物，安装时自动执行。
- **变量**：支持 `$(name)` 与 `%name%` 两种引用语法，可从环境变量和 `.env` 文件取值。
- **跨平台制包**：在 Unix 类系统上保留文件权限；在 Windows 上为可执行文件提供保守的默认权限。

## 基础概念

使用本工具前，先了解以下概念；后续各章节都建立在它们之上。

### 制包流程

一次制包依次完成以下步骤：

1. **固定源目录**：解析 `--source`（默认当前目录）。
2. **确定应用身份**：合并源目录的 `.edition` 清单文件或回退读取的 `.version` 版本标识文件中的值与 `--name`、`--edition`、`--version` 选项。
3. **加载变量**：依次读取默认值、环境变量、各级 `.env` 和命令选项。
4. **收集打包项**：按位置参数和 `--exclude` 确定载荷文件。
5. **生成附属内容**：systemd 服务、生命周期脚本、Nginx 配置、升迁产物。
6. **编码并写出**：生成 `.tar.gz`（含配套 `.sh`）、`.deb` 或 `.rpm`。
7. **回写版本**：制包成功后才保存源版本文件；存在 `.edition` 时同时回写 `.edition` 和 `.version`。

### 核心术语

| 术语 | 含义 |
| --- | --- |
| **源目录** | `--source` 指向的已发布或已暂存的应用文件目录。打包项、输出目录、升迁产物等相对路径都以它为基准。 |
| **应用身份** | 名称（name）、可选发行版 _(**E**dition)_ 和 版本号 _(**V**ersion)_ 的组合，决定包名、默认安装目录和包内 `.version`。 |
| **打包项(载荷)** | 最终写入安装包的文件。不提供位置参数时递归包含整个源目录；提供时只包含所列文件和目录。 |
| **安装根** | 应用在目标主机上的安装目录，默认由标识推导，例如 `/opt/zongsoft/web`。普通载荷相对它安装。 |
| **根路径条目** | 别名以 `/` 开头的打包项，安装到系统绝对路径（如 `/etc/...`），而不是安装根之下。 |
| **服务(daemon)** | 默认生成或收录的 systemd 服务，安装后启用。只打包文件时使用 `--daemon:none`。 |
| **生命周期脚本** | 在目标主机安装前后、卸载前后执行的脚本，默认自动生成，也可自定义。 |
| **变量** | 在选项值和打包项中以 `$(name)` 或 `%name%` 引用的值。 |

### 职责边界

| 本工具负责 | 本工具不负责 |
| --- | --- |
| 生成安装包文件及 tar 包配套的 `.sh` 安装脚本 | 安装、升级或卸载安装包：tar 包由 `.sh` 安装，`.deb`/`.rpm` 由系统包管理器安装 |
| 生成 systemd 服务、生命周期脚本和 Nginx 配置 | 在目标机上启停服务、配置证书或运行 Nginx |
| 写入依赖声明（`Depends`、`Requires` 等） | 下载或内嵌依赖的软件包 |
| 按名称匹配并原样携带升迁产物 | 解析或执行升迁计划（由 [migrator](../migrator/README.zh-Hans.md) 负责） |

## 安装

作为 .NET 全局工具安装、更新、检查和卸载：

```bash
# 安装
dotnet tool install -g Zongsoft.Tools.Packager

# 更新
dotnet tool update -g Zongsoft.Tools.Packager

# 检查
dotnet tool list -g
dotnet-pack

# 卸载
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

### 从本地源码安装

无需发布到 [nuget.org](https://nuget.org)，即可从源码生成的 `.nupkg` 安装以便测试。以下命令使用 .NET 10 SDK，在本仓库的 `packager` 目录执行；packager 不需要原生升迁执行器或 AOT 构建环境。

1. 生成本地工具包：

   ```powershell
   dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release
   ```

2. 确认 `src/bin/Release/Zongsoft.Tools.Packager.<version>.nupkg` 已生成后安装（版本号自动从项目文件读取）：

   ```powershell
   $toolVersion = dotnet msbuild src/Zongsoft.Tools.Packager.csproj -getProperty:Version -nologo
   dotnet tool install -g Zongsoft.Tools.Packager --version "$toolVersion" --source ./src/bin/Release --no-http-cache
   ```

3. 若已安装该工具（尤其是重新编译了同一版本），先执行 `dotnet tool uninstall -g Zongsoft.Tools.Packager` 再重复第 2 步，最后用 `dotnet tool list -g` 核对版本。

说明：

- `--source` 限定本次安装只使用本地目录，避免选中 NuGet.org 的同名包；`--no-http-cache` 禁用下载缓存。选项详见 [.NET 工具安装文档](https://learn.microsoft.com/zh-cn/dotnet/core/tools/dotnet-tool-install)。
- 这里的“本地”指包来源，`-g` 仍会替换当前用户的全局工具。

> 🚨 注意：只做本地测试时不要运行 Cake 的 `pack` 任务，它会把包推送到 NuGet.org。

## 快速开始

本节以 hosting 仓库中真实的 [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) 宿主为例，完整演示“准备应用 → 制包 → 检查”的过程。其他应用只需替换名称、版本和文件列表。

### 第 1 步：准备应用

按宿主的[部署流程](https://github.com/Zongsoft/hosting/blob/main/web/default/deploy.cmd)准备应用及其插件。在 hosting 根目录 `.env` 的根层定义 `framework=net10.0`，并在当前 Windows 控制台设置同值的 `framework` 环境变量供 Cake 构建使用。脚本已不再询问目标框架；将远程调试设为 `off`（Release），然后选择 Linux、x64。如果只准备宿主，在打包提示处输入 `exit`（当前脚本此分支返回 `1`，不代表前面的部署失败）：

```cmd
set "framework=net10.0"
set "Environment=production"
cd /d D:\Zongsoft\hosting\web\default
deploy.cmd
```

### 第 2 步：生成安装包

直接在宿主目录中制包，未指定 `--source` 时源目录就是当前目录，位置参数从中挑选载荷。也可运行宿主 [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd)：独立脚本默认 tar、Release、x64，只打包已有文件。当前 Web 脚本的环境提示未将输入值赋回 `environment`，因此运行前设置 `Environment=production`，选择 `deb`、版本 `1.0.0`，升迁提示留空。下面是对应的核心制包命令（省略空的 Edition 和 migrator 选项）：

```cmd
dotnet-pack deb ^
	--name:Zongsoft.Hosting.Web ^
	--title:Zongsoft.Web ^
	--version:1.0.0 ^
	--compilation:Release ^
	--platform:linux ^
	--architecture:x64 ^
	--Environment:production ^
	--DOTNET_ENVIRONMENT:production ^
	--ASPNETCORE_ENVIRONMENT:production ^
	--listen:8069 ^
	--daemon:zongsoft.web ^
	--web:nginx ^
	--daemon-environments:Environment,DOTNET_ENVIRONMENT,ASPNETCORE_ENVIRONMENT ^
	--exclude:**/logs/;bin/$(compilation)/$(framework)/*.staticwebassets.* ^
	--output:.packages ^
	../../mime ^
	appsettings.json ^
	web*.config ^
	web*.option ^
	wwwroot ^
	plugins ^
	bin/$(compilation)/$(framework):~
```

| 选项或参数 | 作用 |
| --- | --- |
| `--name:Zongsoft.Hosting.Web` | 应用名称，入口程序集为 `Zongsoft.Hosting.Web.dll`。 |
| `--title:Zongsoft.Web` | 人类可读标题，也作为服务描述。 |
| `--version:1.0.0` | 示例发行版本号，请按实际版本号设置。 |
| `--compilation`、`framework` | 编译配置由选项传入，框架从合并 Variables（本例为 hosting `.env`）读取；供 `--exclude` 和载荷参数中的 `$(compilation)`、`$(framework)` 引用。 |
| `--Environment`、`--DOTNET_ENVIRONMENT`、`--ASPNETCORE_ENVIRONMENT` | 自定义变量，经 `--daemon-environments` 写入生成服务的环境变量。 |
| `--listen:8069` | 生成的服务监听 `http://127.0.0.1:8069`。 |
| `--daemon:zongsoft.web` | 软件包和服务标识为 `zongsoft.web`，安装目录为 `/opt/zongsoft/web`。 |
| `--web:nginx` | 依据宿主的 `web.profile` 生成 Nginx 站点配置。 |
| `--exclude` | 跳过日志目录和构建产生的静态 Web 资产清单。 |
| `--output:.packages` | 安装包输出到宿主目录下的 `.packages`。 |
| `../../mime` 至 `plugins` | 选择载荷：hosting 仓库根目录的 MIME 定义、宿主配置、静态文件和已部署的插件。 |
| `bin/$(compilation)/$(framework):~` | 目录别名 `~` 把编译输出的内容直接放到安装根目录。 |

> 💡 提示：把 `deb` 换成 `tar` 或 `rpm` 即可生成其他格式。重复生成同一格式时，请更换输出目录或添加 `--overwrite`。

daemon 的脚本生成 `zongsoft.daemon.service`，传入 `Environment` 和 `DOTNET_ENVIRONMENT`；terminal 的脚本传入相同变量但使用 `--daemon:disabled`，不会为交互式终端设置进程环境。三个宿主的打包脚本均可选择收录已有升迁产物，不会自动制作它们；先在 hosting 根目录运行 `migrate.cmd`，再在宿主的升迁提示中填写 `zongsoft`。完整脚本参数见 [hosting README](https://github.com/Zongsoft/hosting/blob/main/README.zh-Hans.md#安装包与升迁包)，工具行为以本 README 为准。

### 第 3 步：检查产物

上例在宿主目录生成 `.packages/zongsoft.web@1.0.0-x64.deb`。在 Linux 或 WSL 的宿主目录中，用以下只读命令查看元数据和文件清单，不会执行生命周期脚本：

```bash
dpkg-deb --info ./.packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./.packages/zongsoft.web@1.0.0-x64.deb
```

其他格式的检查与安装方法见[包格式](#包格式)。

### 输出文件命名

包文件名由标识、发行版 _(**E**dition)_（可选）、版本号 _(**V**ersion)_ 及架构组成：

```text
<name>@<version>-<architecture>.<extension>
<name>-<edition>@<version>-<architecture>.<extension>
```

- 指定了未禁用的 `--daemon:<name>` 时，优先用 daemon 标识代替 `<name>`，例如 `zongsoft.web@1.0.0-x64.deb`。
- `<architecture>` 为小写架构名，例如 `x64`、`arm64`；`<extension>` 为 `tar.gz`、`deb` 或 `rpm`。
- tar 包配套的 `.sh` 安装入口使用相同的文件主名。

## 命令参考

> 本节及后续章节的 .NET 示例沿用快速开始的约定：在 hosting 仓库的 `web/default` 宿主目录中执行，并已按 deploy.cmd 完成 Release、net10.0 编译与插件部署。

### 语法

```bash
dotnet-pack tar <选项...> [打包项...]
dotnet-pack deb <选项...> [打包项...]
dotnet-pack rpm <选项...> [打包项...]
```

- 必须指定且只指定 `tar`、`deb`、`rpm` 之一。三个子命令共享通用选项，`deb` 和 `rpm` 另有各自的包关系选项。
- 选项写作 `--key:value` 或 `--key=value`；含空格的值按终端语法加引号。
- 位置参数是[打包项](#打包项)，省略时打包整个源目录。

退出码：

| 退出码 | 含义 |
| --- | --- |
| `0` | 制包成功 |
| `1` | 参数、输入或制包失败 |
| `2` | 未指定命令 |

以下选项表中，标记 **必需** 的选项必须提供；标记 _条件必需_ 的选项仅在源目录既无 `.edition` 也无 `.version` 时必需。

### 身份与目标平台

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--name:<name>` | _条件必需_ | 应用/软件包名称，也用于定位生成服务时的 .NET 宿主程序集。 |
| `--version:<version>` | _条件必需_ | 发行版本号；存在源目录的 `.edition` 清单文件或 `.version` 版本标识文件时，覆盖其中所选版本的版本号。零版本号 _(`0.0.0.0`)_ 会被拒绝。 |
| `--edition:<name>` | 由 `.edition` 定义 | 可选发行版名，追加到包名。 |
| `--platform:<platform>` | **必需** | 目标平台：`linux`、`unix`、`osx`、`windows`/`win`、`unknown`；Linux 包通常用 `linux`。 |
| `--framework:<tfm>` | `framework` 变量或空 | 可选 .NET 目标框架，例如 `net10.0`；用于查找 `bin/<compilation>/<framework>` 中的宿主。选项未指定或为空时使用合并后的变量；最终值为空时跳过该构建目录，仍可定位源目录中的宿主。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，例如 `x64`、`x86`、`arm64`、`arm`。 |
| `--compilation:<name>` | `Release` | 可选 .NET 构建配置，用于查找 `bin/<compilation>/<framework>` 中的宿主；也可通过 `$(compilation)` 引用。 |

普通文件打包无需提供 `--framework` 或 `--compilation`，这两个选项不会执行编译。源目录已有应用 DLL，或使用现成的 `.service` 文件时，也可省略两项；从 .NET 构建目录查找宿主时通过 `--framework`、环境变量或祖先 `.env` 提供框架，`--compilation` 默认使用 `Release`。

### 输入与输出

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source:<path>` | 当前目录 | 待打包的源目录。 |
| `--output:<path>` | 源目录 | 安装包输出目录。无论是否以分隔符结尾都视为目录，不能指定文件名；相对路径基于 `--source`。 |
| `--exclude:<patterns>` | 空 | 加载打包项时跳过的文件模式，多个模式用逗号或分号分隔。 |
| `--overwrite[:布尔值]` | `false` | 覆盖已存在的产物；tar 归档和安装脚本作为一组提交。 |

### 安装与服务

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--install-path:<path>` | `/opt/<标识路径>` | Linux 安装目录。标识转为小写并把每个点号替换为 `/`：`Zongsoft.Hosting.Web` 对应 `/opt/zongsoft/hosting/web`；指定未禁用的 `--daemon:zongsoft.web` 时改用 daemon 标识，得到 `/opt/zongsoft/web`。 |
| `--daemon:<名称或文件>` | 自动查找/生成 | systemd 服务标识或现有 `.service` 文件；`none`、`disable`、`disabled` 禁用服务。 |
| `--daemon-environments:<名称列表>` | 空 | 生成服务时从变量视图读取这些名称的值，写入 `Environment=`；用 `,` 或 `;` 分隔。 |
| `--listen:<port-or-url>` | 空 | 生成服务的监听端口或地址；纯端口绑定 `127.0.0.1`，完整地址原样传给宿主 `--urls`。 |

### 包描述

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--title:<text>` | 空 | 人类可读的软件包标题，也用作生成的 systemd 描述。 |
| `--summary:<text-or-file>` | 空 | 简短摘要，取值规则见[文本来源](#文本来源)。 |
| `--description:<text-or-file>` | 空 | 详细描述，取值规则见[文本来源](#文本来源)。 |
| `--homepage:<url>` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license:<text>` | 空 | 许可证表达式或名称。 |
| `--category:<text>` | 格式默认值 | Debian `Section`（默认 `utils`）或 RPM `Group`（默认 `Applications/System`）。 |
| `--maintainer:<text>` | `Zongsoft` | 软件包维护者。 |
| `--manufacturer:<text>` | `Zongsoft` | 软件生产厂家；未指定、null 或空字符串使用默认值，纯空白不触发默认值。 |

### 扩展功能

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--web:<hoster[:filepath]>` | 空 | 生成 Web 托管器配置；当前支持 `nginx`，默认读取 `source/web.profile`。见 [Web 托管配置](#web-托管配置)。 |
| `--migrator:<name>` | 空 | 收录升迁产物的输入名称，可带目录。见[升迁产物集成](#升迁产物集成)。 |
| `--installing` 等 | 自动生成 | 生命周期钩子及前置/后置片段，见[生命周期脚本](#生命周期脚本)。 |

### 包依赖与关系

`--dependencies:<list>` 在 `deb` 和 `rpm` 中统一使用 `name[:range]` 语法。范围采用 [NuGet 区间表示法](https://learn.microsoft.com/zh-cn/nuget/concepts/package-versioning#version-ranges)，另外支持将 `[10.0,)` 简写为 `[10.0)`。

| 输入 | 要求的版本 |
| --- | --- |
| `runtime` 或 `runtime:(,)` | 不限版本 |
| `runtime:10.0`、`runtime:[10.0,)` 或 `runtime:[10.0)` | 大于等于 10.0 |
| `runtime:[10.0]` | 等于 10.0 |
| `runtime:(10.0,)` | 大于 10.0 |
| `runtime:(,11.0]` | 小于等于 11.0 |
| `runtime:(,11.0)` | 小于 11.0 |
| `runtime:[10.0,11.0)` | 大于等于 10.0、小于 11.0 |
| `runtime:(10.0,11.0]` | 大于 10.0、小于等于 11.0 |
| `runtime:[10.0,11.0]` / `runtime:(10.0,11.0)` | 两端均包含 / 均不包含 |

**区间外**的逗号或分号分隔各组必须满足的依赖；组内以 `|` 表示满足任意一个即可。整个值应加引号：

```text
--dependencies:"aspnetcore-runtime-10.0:[10.0,11.0);openssl:[3.0) | libressl:[4.0)"
```

Debian 写入 `Depends`，RPM 写入 `Requires`。对于 `runtime:[10.0,11.0) | alternative:[9.0)`，Debian 输出 `runtime (>= 10.0) | alternative (>= 9.0), runtime (<< 11.0) | alternative (>= 9.0)`；RPM 输出 `((runtime >= 10.0 with runtime < 11.0) or alternative >= 9.0)`。RPM 的替代依赖需要 RPM 4.13+，使用 `with` 的双边区间需要 RPM 4.14+。单组依赖若在 Debian 中展开超过 1024 个关系组，会明确报错。

统一的是区间表示法。版本端点保留原文，按目标包管理器的原生规则比较；打包器不会按 NuGet 版本规则归一化、重排或比较端点。Debian 的虚拟包上下界可能由不同提供者分别满足；RPM 的 `with` 要求同一个包同时满足上下界。不同发行版的包名不会自动映射。Debian 的 `libc6:any`、RPM 的 `pkgconfig(openssl)` 等原生名称仍受各自格式约束；`:[`、`:(` 或冒号后以数字开头的裸版本引出范围。字母开头的原生版本请使用括号形式。

`10.*` 等浮动版本、错误区间、空替代项均会使制包失败。重复约束按原样保留；未指定或空依赖列表不写入应用依赖。打包器只写声明，不下载或内嵌依赖，安装环境需有可用的软件源。以下其他关系选项继续使用原生语法。

#### Debian 关系选项

| 选项 | control 字段 |
| --- | --- |
| `--provides:<list>` | Provides |
| `--replaces:<list>` | Replaces |
| `--breaks:<list>` | Breaks |
| `--conflicts:<list>` | Conflicts |
| `--recommends:<list>` | Recommends |
| `--suggests:<list>` | Suggests |

- 版本关系必须放在括号内，例如 `zongsoft.daemon (>= 1.0.0)`；支持的运算符为 `<<`、`<=`、`=`、`>=`、`>>`。
- Recommends、Suggests 支持以 `|` 表示替代项（满足其一即可）；Provides 的版本关系只接受 `=`。Depends 由上面的统一区间语法生成。
- 多个条目用逗号或分号分隔，整个选项应加引号。非法关系及换行会使制包失败。

#### RPM 关系选项

| 选项 | 说明 |
| --- | --- |
| `--provides:<list>` | RPM `Provides` 条目。 |
| `--conflicts:<list>` | RPM `Conflicts` 条目。 |

RPM 的 Provides、Conflicts 条目支持 `name`、`name = version`、`name >= version`、`name <= version`、`name > version`、`name < version` 或 `name(>= version)`。Requires 由上面的统一区间语法生成。

### 选项值约定

- **布尔值**：`--overwrite` 等同于 `--overwrite:true`。可显式指定 `true/false`、`1/0`、`yes/no`、`on/off`、`enable/disable` 或 `enabled/disabled`。
- **枚举值**：沿用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 的[转换规则](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Common/Convert.cs)，不额外检查枚举成员是否已定义，请提供有效枚举项。
- **输出冲突**：在制包前和提交前各检查一次；生成失败时旧产物保留。

### 常见示例

打包纯前端 `dist` 目录，在前端项目根目录执行：

```bash
dotnet-pack tar \
  --name:example.frontend \
  --version:1.0.0 \
  --platform:linux \
  --source:./dist \
  --output:../packages \
  --daemon:none
```

省略位置参数会包含整个 `dist` 目录；`--daemon:none` 关闭服务生成。可将 `tar` 替换为 `deb` 或 `rpm`。

生成便携式 tarball：

```bash
dotnet-pack tar \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

生成带依赖元数据的 RPM 安装包：

```bash
dotnet-pack rpm \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  --license:MIT \
  --dependencies:"aspnetcore-runtime-10.0:[10.0)" \
  --provides:"zongsoft.web = 1.0.0" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

生成带 Nginx 配置的 Debian 安装包（详见 [Web 托管配置](#web-托管配置)）：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  --category:utils \
  --web:nginx \
  --exclude:*.profile \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

禁用 systemd 服务，只打包文件：

```bash
dotnet-pack tar \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

## 应用身份与版本文件

打包器只检查 `--source` 直属文件：优先用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 的 [`ApplicationManifest`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationManifest.cs) 读取 `.edition`，仅不存在时才用 [`ApplicationIdentifier`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationIdentifier.cs) 读取 `.version`。所选文件损坏、不可读或路径被目录占位时直接失败，不回退，也不搜索父子目录。

### 源清单格式

单版本 `.edition` 可写为 `Zongsoft.Hosting.Web@1.0.0`。具名 Edition 清单可以在首行指定当前发行版：

```ini
Zongsoft.Hosting.Web=Enterprise

[Community]
1.0.0

[Enterprise]
2.0.0
```

单版本与具名 Edition 格式不能混用。省略 `=Enterprise` 表示未设置 [`Editions.Current`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationManifest.cs)。旧多 Edition `.version` 必须改名为 `.edition`；标识读取器只读取首个非空行，不解析后续 Edition 段落。回退标识可写为 `Zongsoft.Hosting.Web-Community@1.0.0`。

### 身份合并规则

| 身份 | 规则 |
| --- | --- |
| 名称 | `--name` 未指定或为空白时使用源名称；显式名称须忽略大小写一致，保留源文件拼写。 |
| 清单中的 Edition | 显式非空 `--edition` 须在清单中存在，忽略大小写匹配并保留文件拼写；否则依次使用 Current、唯一 Edition。多个 Edition 且无 Current 时要求明确选择；无具名 Edition 时使用顶层版本。 |
| 标识中的 Edition | 显式非空 `--edition` 可以替换或补充标识中的 Edition；否则使用文件自带 Edition。 |
| 版本号 | `--version` 覆盖所选版本，最终版本必须非零。 |

两种文件均不存在时必须提供有效 `--name`、`--version`。身份只来自源文件和显式身份选项；选项可引用环境或 `.env` 变量。先固定 source 再读取身份，最终身份用于输出、载荷、脚本和升迁产物匹配。

### 包内版本文件

安装根 `.version` 始终由最终名称、Edition 和版本通过 [`ApplicationIdentifier.Save(Stream)`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationIdentifier.cs) 生成：UTF-8 无 BOM、权限 `0644`。生成项替换所有同安装目标的旧条目（包括根别名），不受排除规则影响。

源直属 `.edition` 不作为载荷入包，即使显式选择或通过别名改名也会排除；指向安装根 `.edition` 的其他条目同样排除。其他子目录文件沿用普通载荷规则。

### 源目录的清单文件或版本标识文件回写

| 制包前的文件状态 | 成功后的保存行为 |
| --- | --- |
| 存在 `.edition`，无论是否有 `.version` | `.edition` 和 `.version` 都被改写；缺失的 `.version` 会创建。 |
| 只有 `.version` | 只更新单行标识 `.version`。 |
| 两者均无 | 成组创建 `.edition`、`.version`。 |

清单只更新所选 Edition 的版本号，并将最终具名 Edition 设为 Current；其他 Edition 的名称、版本和顺序保留，注释及空行遵循 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 保存规则。清单使用 UTF-8 无 BOM、CRLF；标识采用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 序列化。末尾可以有或没有 LF、CRLF 换行，不影响标识解析。

所有产物成功后才保存源文件。单文件原子保存，双文件创建或回写成组提交并在失败时回滚；两者均无时不覆盖并发创建的文件。解析、校验或制包失败不更新源文件；保存失败保留已生成包，报告源文件路径和包路径，命令返回错误。

## 打包项

打包项是最终写入包载荷的文件，通过命令的位置参数指定。

### 选择载荷

**全部打包**：不提供位置参数时，递归包含 `--source` 下的所有文件：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:bin/Release/net10.0 \
  --output:../../../.packages
```

**显式选择**：提供位置参数时，只包含所列文件或目录。以下示例与宿主 [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) 相同，选择 MIME 定义、应用设置、宿主配置、静态文件、插件，并用目录别名 `~` 把编译输出放到安装根目录：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --exclude:"**/logs/;bin/Release/net10.0/*.staticwebassets.*" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### 目标别名

每个条目都可在最后一个冒号后指定目标别名，即它在包内的相对位置。以下示例使用 hosting 仓库中已有的文件演示别名，并禁用服务生成：

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  ../README.md:docs/hosting-web.md \
  ../../zongsoft-logo.png:assets/logo.png \
  web.profile:docs/web.profile
```

以 `/` 或 `\` 开头的别名是 **根路径条目**，会安装到系统绝对路径：

| 格式 | 根路径条目的处理 |
| --- | --- |
| `.deb` | 安装到对应根路径；`/etc/` 下的条目写入 `conffiles`。 |
| `.rpm` | 安装到对应根路径；`/etc/` 下的条目标记为配置文件。 |
| `.tar.gz` | 存放在包内 `.root/` 下，由 `install.sh` 复制到根路径。 |

### 排除文件

`--exclude` 在加载打包项时跳过匹配的文件。模式相对 `--source`，统一使用 `/` 作为分隔符，支持 `*`、`?`、`**`，多个模式用逗号或分号分隔：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --exclude:"**/logs/;bin/Release/net10.0/*.staticwebassets.*" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### 路径与匹配规则

- **相对路径** 基于 `--source` 解析；也允许 `--source` 之外的绝对路径，未指定别名时只使用文件名。
- **目录** 会递归包含。目录自身也入包，保留空目录与源权限（Windows 默认为 `0755`）；合成的父目录为 `0755`。
- **通配**：任一路径段支持 `*`、`?`；独立段 `**` 匹配零层或多层目录。每个参数的匹配结果按相对固定前缀的路径 Ordinal 排序，参数之间保持书写顺序。Windows 匹配忽略大小写，Unix 区分大小写。
- **冲突**：拒绝文件与目录冲突；重复的目标路径报告为冲突并跳过。
- **符号链接**：源链接保留逻辑名称并读取目标内容；目录载荷中的嵌套目录链接会被跳过。本地搜索及链接规则详见[实现说明](docs/implementation.zh-Hans.md#本地搜索与源链接)。

### 文件权限

| 制包主机 | 权限来源 |
| --- | --- |
| Unix 类系统 | 保留源文件权限。 |
| Windows | `.sh`、`.dll`、`.exe` 和无扩展名文件为 `0755`，其他文件为 `0644`。 |

## systemd 服务

服务启用且能定位宿主或现成服务文件时，所有包格式都会携带一个 systemd 服务，并由生命周期脚本负责注册、启用和移除。

### 服务来源

1. 如果 `--source` 下存在 `--daemon:<name>` 指定的文件，则直接使用该文件。
2. 否则自动生成 `<daemon>.service`。
3. 省略 `--daemon` 时，以最终应用名称（`Package.Name`）的小写形式作为服务标识，不附加 Edition。

使用 `--daemon:none`、`--daemon:disable` 或 `--daemon:disabled` 可禁用服务，只打包文件。

### 定位 .NET 宿主

自动生成服务时，按以下顺序查找宿主程序集：

1. `<source>/<name>.dll`
2. `<source>` 下唯一的 `.exe`，转换为同名 `.dll`
3. `<source>/bin/<compilation>/<framework>/<name>.dll`
4. `<source>/bin/<compilation>/<framework>` 下唯一的 `.exe`，转换为同名 `.dll`

未提供有效的 `framework` 或 `compilation` 时跳过第 3、4 步。找不到宿主时提示定位失败并继续打包，不生成服务；带升迁产物的包须能定位宿主或指定 `--daemon:none`。

生成的服务运行：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

### 监听地址

`--listen:<value>` 会作为 `--urls` 传给应用。省略时不追加 `--urls`；使用已有 `.service` 文件时不改写其 `ExecStart`。

| 取值 | 生成的 `--urls` |
| --- | --- |
| `--listen:8069`（纯端口） | `http://127.0.0.1:8069` |
| `--listen:http://0.0.0.0:8069`（完整地址） | 原样使用 |
| `--listen:"http://0.0.0.0:8069;https://0.0.0.0:8443"`（多个地址） | 以分号分隔，整个值加引号 |

例如 `--listen:8069` 生成：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

> 💡 提示：纯端口默认绑定 `127.0.0.1`，适合由本机反向代理转发。需要应用直接接受网络连接时，使用 `http://0.0.0.0:8069` 这样的完整地址。

HTTPS 需要在宿主中配置可用的默认服务器证书，打包器不生成或配置证书，要求见 [Kestrel 端点说明](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/servers/kestrel/endpoints.md)。上表中的 HTTPS 为可选配置，hosting 当前脚本并未启用。

> 🚨 注意：宿主没有可用的默认服务器证书时，HTTPS 端点将无法启动。请在应用运行环境中配置并妥善保护证书。

### 服务环境变量

`--daemon-environments` 列出的变量值会写入生成服务的 `Environment=`。Web 宿主的 [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) 用它写入 `Environment`、`DOTNET_ENVIRONMENT` 和 `ASPNETCORE_ENVIRONMENT`。下面的 Bash 示例显式指定框架；宿主脚本则从 Variables 读取框架：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --daemon-environments:Environment,DOTNET_ENVIRONMENT,ASPNETCORE_ENVIRONMENT \
  --Environment:Production \
  --DOTNET_ENVIRONMENT:Production \
  --ASPNETCORE_ENVIRONMENT:Production \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

变量值可来自额外的命令选项（如上例）、环境变量或 `.env` 文件，见[变量](#变量)。

## 生命周期脚本

生命周期脚本在目标主机安装和卸载时执行。

### 主钩子

| 阶段 | 之前 | 之后 |
| --- | --- | --- |
| 安装 | `--installing:<文本或文件>` | `--installed:<文本或文件>` |
| 卸载/移除 | `--uninstalling:<文本或文件>` | `--uninstalled:<文本或文件>` |

主钩子可以是源目录相对文件路径、绝对文件路径或内联脚本文本，取值规则见[文本来源](#文本来源)。

### 前置与后置片段

每个主钩子都可追加基于文件的前置和后置脚本片段，默认均为空：

| 主钩子 | 前置文件选项 | 后置文件选项 |
| --- | --- | --- |
| `--installing` | `--preinstalling:<路径列表>` | `--postinstalling:<路径列表>` |
| `--installed` | `--preinstalled:<路径列表>` | `--postinstalled:<路径列表>` |
| `--uninstalling` | `--preuninstalling:<路径列表>` | `--postuninstalling:<路径列表>` |
| `--uninstalled` | `--preuninstalled:<路径列表>` | `--postuninstalled:<路径列表>` |

多个路径以 `;` 或 `|` 分隔。前置/后置选项只接受文件列表，不接受内联文本或 `text:`。

### 默认脚本

未提供脚本时，工具会生成默认脚本。对带 systemd 服务的包，默认脚本会：

- 在安装和移除前停止服务；
- 创建或删除 `/etc/systemd/system/<service>` 符号链接，并重载 systemd；
- 安装后启用服务；
- 卸载后删除安装目录。

### 各格式的阶段差异

| 格式 | 卸载生命周期何时执行 |
| --- | --- |
| Debian | `prerm` 仅在 `remove` 或 `deconfigure` 时进入卸载生命周期；`postrm` 仅在 `remove` 或 `purge` 时执行卸载收尾。 |
| RPM | `%preun`/`%postun` 仅在最后一个已安装实例被删除（`$1=0`）时运行；仍有已安装实例时保留载荷。 |
| tar | 使用显式的 `install.sh`/`uninstall.sh`；生成的卸载器只删除解析后的 `TARGET` 路径。 |

> 🚨 注意：默认生命周期脚本可能停止或启用服务，并在卸载时删除应用安装目录。请先检查生成的脚本和目标路径，再在主机上执行。

### 文本来源

摘要、描述和四个主钩子共用同一套取值规则：

| 写法 | 解释 |
| --- | --- |
| `file:<路径>` | 明确读取文件（相对 source）。 |
| `text:<文本>` | 冒号后的字面文本原样保留。 |
| 无前缀 | 先展开打包变量：若是源目录下已有的文件则读取；多行内容视为文本；明显的缺失文件路径报错；其余视为文本。 |

文件内容直接使用，不展开变量，也不再作为路径解释。需要 Shell 表达式时，请放入文件或 `text:` 文本中。

## Web 托管配置

`--web:nginx[:filepath]` 读取 INI 格式的 `web.profile`（默认位于源目录），通过 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 的 [Profile](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Configuration/Profiles/Profile.cs) 导入，生成安装根下的 `.web/nginx/<PackageName>.conf`。一个最小的 `web.profile` 示例：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://app:8069
```

在 Web 宿主中，`server=~` 表示使用生成服务的监听端口（如 `8069`）；站点设置应按实际环境调整。

### 载荷与转换相互独立

`--web` 只负责转换，不决定输入 `*.profile` 文件是否入包，这仍由位置参数和 `--exclude` 控制。例如 `--exclude:*.profile` 排除输入文件，但不影响生成 Nginx 配置。

| `--web` 取值 | 行为 |
| --- | --- |
| 省略、空值或 `none` | 不生成 Web 配置 |
| `nginx` | 生成 Nginx 配置 |
| `iis` | 保留名称，尚未实现 |

### 安装时激活

| 场景 | 行为 |
| --- | --- |
| 裸机安装（默认） | 创建系统加载链接并校验配置；Nginx 运行中则重载，已停止则不启动。 |
| 容器构建 | 向安装进程传入 `HOSTER_WEB_ACTIVATION=0` 关闭激活，再从已知安装根收集 `.web/nginx/*.conf`。 |
| 普通卸载 | 删除 `.web`。 |

`HOSTER_WEB_ACTIVATION` 接受 `0`/`1` 及不区分大小写的 `false`/`true`，显式空值非法。关闭激活时仍会交付配置文件。

> 💡 提示：字段作用域、默认值与继承规则，路径匹配、证书、负载均衡、健康检查、原始设置、变量、容器构建等完整范例，以及主动检查和 Cookie 保持所需的模块与最低版本，见 [Web 配置指南](docs/web.zh-Hans.md)。

## 升迁产物集成

升迁（数据库、存储等的结构与数据变更）由独立的 [migrator 工具](../migrator/README.zh-Hans.md)预先制作。本打包工具只负责把已制作好的 **升迁归档** 和配套 **启动脚本** 收录进安装包：它不解析 `.migration`/`.ini`、SQL 或执行计划，也不携带原生执行器。

使用 `--migrator:<名称或路径>` 启用。例如源目录为 `hosting/web/default/`、产物位于 `hosting/.migration/` 时，只需指定 `--migrator:zongsoft`。不指定该选项或提供空值时，不启用升迁集成。

### 产物命名

打包器按本次安装包 **最终确定** 的 发行版 _(**E**dition)_、版本号 _(**V**ersion)_、平台和架构定位产物（包括从源目录的 `.edition` 清单文件或 `.version` 版本标识文件取得的值和默认的 x64）；无 发行版 _(**E**dition)_ 时省略对应部分。例如 enterprise、1.0.0、Linux x64 对应：

```text
zongsoft-enterprise(migrate)@1.0.0_linux-x64.tar.gz
zongsoft-enterprise(migrate)@1.0.0_linux-x64.sh
```

- 文件前缀为 `<name>[-<edition>](migrate)@<version>_<RID>`，名称原样使用，无 Edition 时省略 `-<edition>`；选项填写制作升迁时的 `--name`，不包含自动生成的 `(migrate)` 标记。
- 选项值中不要包含 发行版 _(**E**dition)_、版本号 _(**V**ersion)_、RID、扩展名、通配符或路径列表。
- 升迁名称可以不同于宿主名称，但 发行版 _(**E**dition)_、版本号 _(**V**ersion)_ 和 RID 必须匹配。

### 查找位置

查找从最终的 `--source` 目录开始，而不是运行命令时的工作目录：

| 写法 | 查找范围 |
| --- | --- |
| 只写名称，如 `zongsoft` | 从源目录向父目录逐级查找直到文件系统根。每层先查目录本身，再查其直属 `.migration/`，然后才向上；不遍历其他子目录。 |
| 含 `/` 或 `\`，如 `./zongsoft` | 只查显式目录。相对路径基于 `--source`，绝对路径直接使用；`./zongsoft` 只查源目录。 |

匹配规则：

- 只有归档和脚本 **都不存在** 时才继续查找下一位置。
- 只找到其中一份时立即报错，并指出缺失配套文件的完整路径。
- 找到完整配套后立即校验归档元数据和 RID，校验失败不再继续查找。
- 两份文件必须来自同一目录，不会跨目录拼配，也不会替换成其他 发行版 _(**E**dition)_、版本号 _(**V**ersion)_ 或架构。
- 一直查到根目录仍未找到时，错误会列出预期文件名和已检查的目录（含各级 `.migration/`）。

### 打包与安装行为

- 两个文件原样放入安装根的 `.migration/`，打包时不展开归档。运行时升迁启动脚本解压到独立临时目录，计划及执行器直接位于临时目录根部，不在安装目录中再展开一层 `.migration/`；脚本权限为 `0755`，归档为 `0600`。载荷与目标冲突时制包失败。
- 安装时，生成的钩子以 `/var/lib/<包名>/packager` 为状态目录运行 `apply`；升迁失败会阻止服务启动。
- systemd 的 `ExecStartPre` 运行 `check`，比较包的计划指纹与本地 `ready` 成功标记；它不读取数据库或桶的当前状态。
- 无 daemon 时仍会执行升迁；DESTDIR 暂存安装不执行生命周期钩子；卸载保留升迁状态、数据库和桶。
- 目标机需要 POSIX sh、tar/gzip、cmp 以及执行器所需的系统库，详见 migrator 的升迁指南。

> 🚨 注意：安装包含升迁产物的软件包时会运行 `apply`，可能创建或修改数据库、用户、权限和 Amazon S3 桶。生产安装前请备份数据并在预发布环境验证。

## 变量

选项值和打包项参数都可以引用变量，支持两种等价语法：

```text
$(name)
%name%
```

变量名不区分大小写，可包含点号、连字符和索引。

### 加载顺序

变量按以下顺序加载，后加载的覆盖先加载的（空值同样参与覆盖）：

1. 描述符默认值
2. 系统环境变量
3. 从文件系统根目录到源目录，各级目录直属的 `.env` 文件（由远到近）
4. 显式命令选项，包括额外选项（如 `--Environment:Production`）

`--framework` 未指定或为空时，使用合并后变量集中的非空 `framework`；非空选项优先。没有非空变量可用时沿用原有处理流程，纯空白选项值保持原有行为。

`.env` 的读取规则：

- 先用环境变量和选项解析并固定 `--source`，再加载 `.env`；`.env` 不能确定或重新指定 source，也不会额外加载工作目录的祖先链或子目录。
- 使用 [`Profile.Load`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Configuration/Profiles/Profile.cs) 读取，支持 INI 与 `#@import`。根级条目保留原名；段落各级名称与条目名以 `_` 拼接。例如 `[io rustfs]` 下的 `access_key=example` 生成 `io_rustfs_access_key`，根级 `environment=Development` 生成 `environment`。
- 缺失的文件跳过；读取或解析失败则终止制包。
- 每次调用的变量相互独立，不修改进程环境变量。

### 展开规则

- 变量按使用时递归展开：未使用的无效引用不阻止制包；用到的未知、循环或超过 64 层的引用会报错。
- 命令先保留原始选项文本。定位 `source` 后，显式提供的 `name`、`edition`、`version` 先展开，再用于源目录的 `.edition` 清单文件或 `.version` 版本标识文件校验及版本转换；`platform`、`architecture` 和 `overwrite` 也先展开再转换。
- `name`、`edition`、`version` 只由源目录的 `.edition` 清单文件或 `.version` 版本标识文件和显式身份选项决定，同名的环境变量或 `.env` 变量不能替代身份；但显式选项可以引用 `.env` 中的变量。尚未从源目录的 `.edition` 清单文件或 `.version` 版本标识文件获得的身份值不能用于定位该源目录。
- 最终身份以及解析后的 `source`、`output` 会覆盖变量集合中的同名值。
- `--migrator` 必须显式启用；`--overwrite` 可由环境变量或 `.env` 提供，再由命令行覆盖。

可以传入字面量 `$(APP_VERSION)` 或 `%APP_VERSION%` 由打包器展开（在 Bash 中须加单引号，避免 Shell 抢先解释），也可以直接让 Shell 展开：

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --listen:8069 \
  --daemon:zongsoft.web \
  --name:"$APP_NAME" \
  --version:"$APP_VERSION" \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### 常用变量

| 变量 | 含义 |
| --- | --- |
| `name`、`version`、`edition` | 软件包身份及可选发行版名 |
| `platform`、`architecture`、`RuntimeIdentifier` | 目标操作系统、CPU 架构及组合后的运行时标识 |
| `framework`、`compilation` | 目标 .NET 框架和构建配置 |
| `source`、`output` | 规范化后的源目录和安装包输出目录 |

## 包格式

三种格式共享同一套元数据和载荷，差异只在容器结构和安装方式：

| 格式 | 容器结构 | 安装方式 |
| --- | --- | --- |
| `.tar.gz` | gzip 压缩的 PAX tar，附带同名 `.sh` 安装入口 | 运行 `.sh` 或解压后运行 `install.sh` |
| `.deb` | `ar` 容器，含 `debian-binary`、`control.tar.gz`、`data.tar.gz` | 系统包管理器（如 `dpkg`） |
| `.rpm` | RPM lead/signature/header 元数据 + gzip 压缩的 `newc` cpio 载荷 | 系统包管理器（如 `rpm`） |

Debian/RPM 载荷使用自动清理的临时文件和流式摘要，不在内存中拼接整个包体，制包时需预留临时磁盘空间，实现见[载荷流与目录条目](docs/implementation.zh-Hans.md#载荷流与目录条目)。

安装前请先查看包内容。下文的文件清单和元数据查看命令都是只读操作，不会运行生命周期脚本。

### `.tar.gz`

tar 命令生成 `.tar.gz` 包及同名 `.sh` 安装脚本。tar 包包含应用文件、`.root/` 下的可选根路径条目，以及融合了生命周期脚本的可执行 `install.sh` 和 `uninstall.sh`。

PAX 全局属性 `Architecture` 记录既有 `--architecture` 选项确定的目标 CPU 架构（默认为 `x64`），采用与文件名一致的小写值，如 `x64`、`arm64`、`x86` 或 `arm`。

检查内容：

```bash
tar -tzf ./.packages/zongsoft.web@1.0.0-x64.tar.gz
```

> 🚨 注意：以下安装命令使用 `sudo` 执行生成的安装脚本，可能写入系统目录并管理服务。请先使用 `DESTDIR` 暂存安装，或在预发布主机验证。

| 操作 | 命令 |
| --- | --- |
| 一键安装 | `sudo sh ./.packages/zongsoft.web@1.0.0-x64.sh` |
| 解压后安装 | `tar -xzf ./.packages/zongsoft.web@1.0.0-x64.tar.gz && sudo ./install.sh` |
| 安装到暂存目录 | `DESTDIR=/tmp/stage ./install.sh` |
| 覆盖安装路径 | `sudo env INSTALL_PATH=/srv/zongsoft/web ./install.sh` |
| 卸载 | `cd /opt/zongsoft/web && sudo ./uninstall.sh` |

覆盖安装路径仅适用于未集成升迁产物的包；集成升迁产物时安装路径在制包时固定，如需调整请在打包时指定 `--install-path`。

### `.deb`

检查元数据和载荷：

```bash
dpkg-deb --info ./.packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./.packages/zongsoft.web@1.0.0-x64.deb
```

> 🚨 注意：`dpkg` 安装会以特权运行软件包生命周期脚本。请先检查脚本并在预发布主机验证。

```bash
sudo dpkg -i ./.packages/zongsoft.web@1.0.0-x64.deb
```

`/etc/` 下的根路径条目会写入 Debian `conffiles` 元数据。

### `.rpm`

检查元数据、载荷和生命周期脚本：

```bash
rpm -qip ./.packages/zongsoft.web@1.0.0-x64.rpm
rpm -qlp ./.packages/zongsoft.web@1.0.0-x64.rpm
rpm -qp --scripts ./.packages/zongsoft.web@1.0.0-x64.rpm
```

> 🚨 注意：`rpm` 安装会以特权运行软件包生命周期脚本。请先检查脚本并在预发布主机验证。

```bash
sudo rpm -Uvh ./.packages/zongsoft.web@1.0.0-x64.rpm
```

`/etc/` 下的根路径条目会被标记为 RPM 配置文件。

### 应用元数据

`--homepage` 表示应用主页，`--manufacturer` 表示软件生产厂家，`--maintainer` 表示软件包维护者。变量 `homepage`、`manufacturer`、`maintainer` 按既有的默认值 → 环境变量 → 祖先 `.env` → 显式选项顺序加载。厂家值解析后为 null 或空字符串时使用 `Zongsoft`，纯空白不使用默认值。

| 格式 | 主页 | 生产厂家 | 维护者 |
| --- | --- | --- | --- |
| tar.gz | — | PAX 全局扩展属性 `Manufacturer` | — |
| deb | `Homepage` | 自定义 control 字段 `Manufacturer` | `Maintainer` |
| rpm | `URL`（1020） | `VENDOR`（1011） | `PACKAGER`（1015） |

Debian 文本字段遵循既有规范化规则：去除外围空白，纯空白的厂家字段不写入；tar 与 RPM 保留厂家值。这些字段属于格式元数据，不增加安装目录文件。

### 打包器版本元数据

每个安装包都自动记录生成它的打包器身份，逻辑内容为 `Packager:Zongsoft.Tools.Packager@<assembly-version>`。该值从打包器自身程序集读取，独立于应用版本，无需额外选项。

| 格式 | 存放位置 | 查看方式 |
| --- | --- | --- |
| tar.gz | PAX 全局扩展属性 `Packager` | 支持 PAX 的归档读取器，例如 Python `tarfile` 的 `pax_headers["Packager"]` |
| deb | `control.tar.gz` 内 `control` 的 `Packager` 字段 | `dpkg-deb -f <安装包.deb> Packager` |
| rpm | 主 Header 的 `RPMVERSION` 字符串标签（1064） | `rpm -qp --queryformat '%{RPMVERSION}\n' <安装包.rpm>` |

RPM 的 `PACKAGER` 标签（1015）保存的是 `--maintainer` 维护者信息。上述元数据都位于格式头中，不增加安装目录文件，也不改变 `.version` 或 `migration.json`。

## 推荐打包流程

1. **准备应用。** 编译应用并部署插件，直接以应用目录作为 `--source`，用位置参数选择所需的程序、配置、插件和静态文件，无需另建暂存目录。递归打包全部文件时，请把输出目录放在源目录之外，避免旧安装包意外进入载荷。
2. **选择载荷范围。** 不提供位置参数时会包含 `--source` 下的全部文件；需要更小、更易审查的包时，明确列出文件和目录，并用 `--exclude` 排除不应入包的文件。
3. **确认软件包身份。** 检查 `--name`、`--version`、可选的 `--edition`、平台和架构；确认生成的服务标识、`--install-path` 与 `--listen` 地址符合目标环境。
4. **检查生成物。** 使用 `tar -tzf`、`dpkg-deb --info`/`--contents` 或 `rpm -qip`/`-qlp`/`-qp --scripts` 检查文件和元数据；包会管理服务时，同时复核生命周期脚本和 systemd 单元。
5. **在预发布环境安装验证。** 测试安装、服务启动、配置路径和卸载行为；可对 tar 包使用 `DESTDIR`，或使用一次性主机。记录环境相关配置，不依赖只有生产机才具备的隐式条件。
6. **谨慎发布到生产。** 生产安装前备份应用数据并确认恢复步骤。启用 `--migrator` 后，安装会执行升迁，可能修改数据库和 Amazon S3 桶；不同版本的软件包之间要保留同一个升迁状态目录。

> 💡 提示：`--overwrite` 只会替换输出目录中已有的安装包文件，不会覆盖或更新已经安装的应用。

> 🚨 注意：制包成功只表示安装包文件已生成，不能证明目标环境可以安装该包或应用能够正常启动。

## 故障排查

| 错误信息 | 原因与处理 |
| --- | --- |
| `指定的 '<路径>' 目录不存在。` | 变量展开和路径规范化后，`--source` 指向的位置不存在。 |
| `指定的 '<路径>' 源路径不存在。` | 位置参数没有匹配到现有文件、目录或通配路径。 |
| `必须通过 --version 或所选源版本提供有效的非零版本号。源文件：<路径>` | 命令选项及源目录的 `.edition` 清单文件或 `.version` 版本标识文件都未提供有效版本号，或版本号为零；非版本号文本会在变量展开后、从这些文件选择版本之前报错。 |
| `后台宿主程序定位失败。` | 找不到已有服务文件或可用的宿主 `.dll`，也无法从唯一的 `.exe` 推断名称。可指定 `--daemon:<service-file>`，或用 `--daemon:none` 禁用服务。 |
| `输出文件“<路径>”已存在。若要替换它，请添加 --overwrite；也可指定其他 --output 目录。` | 指出冲突的安装包或 tar 安装脚本。需要替换时添加 `--overwrite`，或换用其他 `--output` 目录；未启用覆盖时已有产物保持不变。 |

## 从源码构建

以下命令在 tools 仓库的 `packager` 目录执行。

| 任务 | 命令 |
| --- | --- |
| 还原并构建 | `dotnet restore Zongsoft.Tools.Packager.slnx`<br>`dotnet build Zongsoft.Tools.Packager.slnx -c Release` |
| 运行回归测试 | `dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0` |
| Cake 构建（含本地制包） | `dotnet cake --target=build --edition=Release` |
| Cake 测试（默认目标） | `dotnet cake --target=test --edition=Release` |

- Cake 会将同一 `--edition` 配置传给依赖还原、编译和测试。
- Debug 引用本地 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 编译产物；Release 使用项目声明的 [NuGet 包](https://www.nuget.org/packages/Zongsoft.Core)，测试项目跟随主项目使用该依赖。
- 代码规范检查及资源生成要求见[仓库说明](../AGENTS.md#代码规范检查)。

## 相关文档

- [Web 托管配置指南](docs/web.zh-Hans.md)：`web.profile` 完整语法、Nginx 映射与安装行为。
- [实现说明](docs/implementation.zh-Hans.md)：内部设计、打包流水线和格式级实现细节。
- [migrator 工具](../migrator/README.zh-Hans.md)：制作升迁归档和启动脚本。

## 许可证

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可证。
