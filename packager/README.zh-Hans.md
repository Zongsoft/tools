# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` 是一个 .NET 全局工具，用于把已发布的应用目录打包成适合 Linux 分发和安装的 `.tar.gz`、`.deb`、`.rpm` 安装包。

它主要面向 .NET 服务和命令行应用，打包时不依赖外部的 `tar`、`dpkg-deb`、`rpmbuild` 或 `cpio` 命令。

## 基础概念

- **源目录：** `--source` 指向已发布或已暂存的应用文件。打包器只读取该目录直属的 `.version` 文件。
- **打包项：** 不提供位置参数时，递归包含 `--source` 下的全部文件；提供位置参数时，只包含指定文件和目录。
- **打包与安装：** `dotnet-pack` 只生成安装包，不会安装。tar 包附带 `.sh` 安装脚本；`.deb` 和 `.rpm` 由系统包管理器安装。
- **服务与生命周期：** 默认生成 systemd 服务和安装/卸载脚本。只打包文件、不管理服务时，使用 `--daemon:none`。
- **升迁集成：** `--migrator` 可选地收录由独立工具制作的升迁归档和启动脚本。打包器只匹配并携带这两个文件，制作安装包时不解析或执行升迁计划。

> 💡 提示：可先按[快速开始](#快速开始)走通流程，再查看[打包项](#打包项)和[命令](#命令)调整载荷与元数据。

> 🚨 注意：安装包会执行生命周期脚本，可能修改系统文件、服务和应用目录。安装到生产主机前，先检查包内容并在预发布环境验证。

## 快速导航

- [基础概念](#基础概念)
- [功能特性](#功能特性)
- [安装](#安装)
- [快速开始](#快速开始)
- [命令](#命令)
- [打包项](#打包项)
- [systemd 服务](#systemd-服务)
- [生命周期脚本](#生命周期脚本)
- [升迁产物集成](#升迁产物集成)
- [变量](#变量)
- [包格式](#包格式)
- [推荐打包流程](#推荐打包流程)
- [打包器版本元数据](#打包器版本元数据)
- [故障排查](#故障排查)
- [实现说明（简体中文）](docs/implementation.zh-Hans.md)

## 功能特性

- 使用统一命令生成 `.tar.gz`、`.deb`、`.rpm` 三种安装包。
- 三种格式共享同一套包元数据、变量、文件条目、服务脚本和输出命名规则。
- 未提供服务文件时，可自动生成 systemd 服务文件。
- 为所有支持的包格式生成安装和卸载生命周期脚本。
- 在 Unix 类系统上打包时保留文件权限。
- 在 Windows 上打包时为可执行文件提供保守的权限默认值。
- 支持 `$(name)` 和 `%name%` 两种变量引用语法。
- 支持显式文件条目、递归目录、路径段通配（含 `**`）、目标别名，以及 `/etc/nginx/conf.d/zongsoft.web.conf` 这类根路径别名。
- 直接使用 .NET 写入包格式：
  - `.tar.gz` 使用 gzip 压缩的 PAX tar。
  - `.deb` 使用包含 `control.tar.gz` 和 `data.tar.gz` 的 `ar` 容器。
  - `.rpm` 使用 RPM lead/header 元数据和 gzip 压缩的 `newc` cpio 载荷。

## 打包器版本元数据

每个安装包自动记录当前生成工具的身份，逻辑内容为 `Packager:Zongsoft.Tools.Packager@<assembly-version>`。值采用 `程序集名@版本号`，从打包器自身程序集读取，独立于宿主应用版本；无需指定额外选项或启用升迁。

| 格式 | 存放位置 | 查看方式 |
| --- | --- | --- |
| tar.gz | PAX 全局扩展属性 `Packager` | 使用支持 PAX 的归档读取器，例如 Python `tarfile` 的 `pax_headers["Packager"]`。 |
| deb | `control.tar.gz` 内 `control` 的 `Packager` 字段 | `dpkg-deb -f <安装包.deb> Packager` |
| rpm | 主 Header 的 `RPMVERSION` 字符串标签（1064） | `rpm -qp --queryformat '%{RPMVERSION}\n' <安装包.rpm>` |

RPM 用生成工具版本标签保存本工具身份；其 `PACKAGER` 标签（1015）保存 `--maintainer` 的维护者信息。元数据位于格式头中，不增加安装目录文件，也不改变 `.version` 或 `migration.json`。

## 安装

作为 .NET 全局工具安装：

```bash
dotnet tool install -g Zongsoft.Tools.Packager
```

更新已安装版本：

```bash
dotnet tool update -g Zongsoft.Tools.Packager
```

检查已安装工具：

```bash
dotnet tool list -g
dotnet-pack
```

卸载：

```bash
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

### 从本地源码安装（用于测试）

从源码运行回归测试可使用 `dotnet cake --edition Release`（默认目标为 `test`）。Cake 会将同一配置传给依赖还原、编译和测试。Debug 引用本地 framework 的 Core 编译产物；Release 使用项目声明的 Core NuGet 包，测试项目跟随主项目使用该依赖。

源码编译后无需发布到 NuGet.org，即可从生成的 `.nupkg` 安装。以下命令使用 .NET 10 SDK，在 `D:/Zongsoft/tools/packager` 目录执行；其他检出位置使用对应目录。

直接生成本地工具包；packager 不需要原生升迁运行器或 AOT 构建环境：

```powershell
dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release
```

确认构建成功且 `src/bin/Release/Zongsoft.Tools.Packager.<version>.nupkg` 已生成后，首次安装执行：

```powershell
$toolVersion = dotnet msbuild src/Zongsoft.Tools.Packager.csproj -getProperty:Version -nologo
dotnet tool install -g Zongsoft.Tools.Packager --version "$toolVersion" --source ./src/bin/Release --no-http-cache
```

若已安装该工具，尤其是重新编译了同一版本，先卸载，再执行上面的本地安装命令：

```powershell
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

安装命令从项目文件自动读取版本号；包文件名中的 `<version>` 表示该值。`--source` 限定本次安装只使用本地目录，避免选中 NuGet.org 的同名包；`--no-http-cache` 禁用下载缓存，选项说明见 [.NET 工具安装文档](https://learn.microsoft.com/zh-cn/dotnet/core/tools/dotnet-tool-install)。安装后使用 `dotnet tool list -g` 核对版本。这里的“本地”指包来源，`-g` 仍会替换当前用户的全局工具。只做本地测试不要运行 Cake 的 `pack` 任务，它会推送到 NuGet.org。

## 快速开始

以下示例使用 `D:/Zongsoft/hosting` 中真实的 [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) 宿主。先按宿主的[部署流程](https://github.com/Zongsoft/hosting/blob/main/web/default/deploy.cmd)准备应用及其插件。在 Windows 控制台运行脚本，将远程调试设为 `off`（Release），然后选择 Linux、x64、net10.0；如果只准备宿主，在打包提示处输入 `exit`：

```cmd
cd /d D:\Zongsoft\hosting\web\default
deploy.cmd
```

以下 Bash 命令从 hosting 仓库根目录执行（WSL 中使用对应挂载路径），将准备好的宿主内容收集到新建的 `./publish` 暂存目录。项目将 `plugins/` 排除在构建内容之外，因此必须显式包含已部署的插件和宿主配置：

```bash
mkdir -p ./publish
cp -a ./web/default/bin/Release/net10.0/. ./publish/
cp -a ./web/default/plugins ./publish/
cp -a ./web/default/wwwroot ./publish/
cp ./web/default/appsettings.json ./web/default/web.config ./web/default/web*.option ./publish/
cp ./mime ./publish/
```

打包示例使用 `1.0.0` 作为发行版本，请按实际发布版本设置。保持宿主 [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) 中的 `--name:Zongsoft.Hosting.Web`、`--title:Zongsoft.Web`、`--daemon:zongsoft.web` 组合：入口 DLL 为 `Zongsoft.Hosting.Web.dll`，软件包和服务标识为 `zongsoft.web`，安装目录为 `/opt/zongsoft/web`。

`--output:../packages/` 相对于 `./publish` 解析，因此安装包输出到 hosting 仓库下的 `./packages`。下文示例为不同用法；重复生成同一格式时，请更换输出目录或使用 `--overwrite`。

生成 Debian 安装包：

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
  --source:./publish \
  --output:../packages/ \
  --summary:"Zongsoft plugin-based Web host" \
  --description:"Hosts ASP.NET applications built with Zongsoft plugins."
```

生成的包文件名遵循以下规则：

```text
<name>@<version>-<architecture>.<extension>
<name>-<edition>@<version>-<architecture>.<extension>
```

`<architecture>` 使用小写架构名，例如 `x64`、`arm64`，文件名由名称、Edition（可选）、版本及架构组成；`<extension>` 为 `tar.gz`、`deb` 或 `rpm`。tar 包配套的 `.sh` 入口使用相同文件主名。

如果指定了未禁用的 `--daemon:<name>`，则优先使用 daemon 标识生成包文件名：

```text
<daemon>@<version>-<architecture>.<extension>
<daemon>-<edition>@<version>-<architecture>.<extension>
```

示例：

```text
zongsoft.web@1.0.0-x64.deb
```

## 命令

```bash
dotnet-pack tar <选项...> [打包项...]
dotnet-pack deb <选项...> [打包项...]
dotnet-pack rpm <选项...> [打包项...]
```

必须指定且只指定 `tar`、`deb`、`rpm` 之一。三个子命令共享通用选项；`deb` 和 `rpm` 另有各自的包关系选项。选项使用 `--key:value` 或 `--key=value`，含空格的值按终端语法加引号。位置参数的载荷映射见[打包项](#打包项)。
制作成功返回 `0`，无命令返回 `2`，参数、输入或制包失败返回 `1`。

### 示例

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
  --source:./publish \
  --output:../packages/
```

生成 Debian 安装包，将宿主真实的 [Nginx 配置](https://github.com/Zongsoft/hosting/blob/main/.deploy/default/nginx/zongsoft.web.conf)放到 `/etc/nginx/conf.d`。该配置将请求转发到生成服务的 `8069` 端口；站点设置应按实际环境调整：

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
  --source:./publish \
  --output:../packages/ \
  --category:utils \
  . \
  ../.deploy/default/nginx/zongsoft.web.conf:/etc/nginx/conf.d/zongsoft.web.conf
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
  --source:./publish \
  --output:../packages/ \
  --license:MIT \
  --dependencies:"aspnetcore-runtime-10.0 >= 10.0" \
  --provides:"zongsoft.web = 1.0.0"
```

禁用 systemd 生成，只打包文件：

```bash
dotnet-pack tar \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

### 必需与条件必需选项

| 选项 | 说明 |
| --- | --- |
| `--name:<name>` | 源 `.version` 不存在时必需。应用/软件包名称，也用于定位生成服务时的 .NET 宿主程序集。 |
| `--version:<version>` | 源 `.version` 不存在时必需，否则覆盖其所选版本。零版本会被拒绝。 |
| `--platform:<platform>` | 目标平台。支持的枚举值包括 `linux`、`unix`、`osx`、`windows`/`win`、`unknown`；Linux 包通常使用 `linux`。 |
| `--framework:<tfm>` | 目标框架标识，例如 `net8.0`、`net9.0` 或 `net10.0`。 |

### 应用版本文件

打包器只读取 `--source` 直属的 `.version`，不递归也不查找父目录。源文件使用 `ApplicationVersion.Load/Save` 管理应用名称与各 Edition 的版本。hosting 的 daemon 不区分 Edition 时可使用：

```text
zongsoft.daemon@1.0.0
```

hosting 的 `web/default` 宿主不区分 Edition 时可写为 `Zongsoft.Hosting.Web@1.0.0`；需要管理 Edition 时，首行只写应用名称，随后在各 `[edition]` 段落下写对应的裸版本号。两种源格式不能混用。

- `--name` 未指定或为空白时使用文件名称；非空时忽略大小写比较，必须一致，最终保留文件中的拼写。
- 未指定 `--edition` 或传入空值时，没有具名 Edition 则使用顶层版本，只有一个则自动选择，多个则要求明确指定。非空 Edition 必须在文件中存在，忽略大小写查找并保留文件拼写。单版本文件不允许指定具名 Edition。
- 指定 `--version` 时覆盖所选版本，否则使用文件中的对应版本；最终版本必须非零。源文件不存在时必须指定有效的 `--name`、`--version`；可选的 Edition 决定创建单版本还是具名版本文件。

确定身份后才初始化完整变量，使输出、载荷、安装脚本和升迁路径中的 `$(name)`、`$(edition)`、`$(version)` 使用最终值。源目录路径若依赖尚未确定的身份变量，则报变量错误，不循环推导。源文件存在但损坏或无法读取时退出打包。

包内安装根 `.version` 使用 **`ApplicationIdentifier`**，仅以一行表示本次名称、Edition 和版本。内容直接从内存写入，完全采用 `ApplicationIdentifier.Save(Stream)` 的输出，不追加换行；权限为 `0644`。指向该安装位置的载荷会被生成的版本条目替换，排除规则不影响自动生成的版本条目。

所有制包步骤成功后才按 Core 格式保存源文件，只更新所选 Edition，保留其他 Edition 的名称、版本和顺序；注释及原始空白布局不保留。解析、校验或制包失败不更新源文件。保存源文件失败时命令返回错误，明确指出安装包已生成，并保留该包。

### 通用选项

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source:<path>` | 当前目录 | 待打包的源目录。 |
| `--migrator:<name>` | 空 | 升迁制作时的输入名称，可带目录；裸名称从源目录向父目录查找，按最终 Edition、版本和 RID 匹配。 |
| `--output:<path>` | 源目录 | 安装包输出目录，无论是否以目录分隔符结尾都作为目录处理；不支持通过此选项指定文件名。相对路径基于 `--source` 解析。 |
| `--exclude:<patterns>` | 空 | 加载打包项时跳过的文件模式列表，多个模式用逗号或分号分隔。 |
| `--edition:<name>` | 空 | 可选发行/版本标识。会追加到包名；对 RPM 而言，有值时也作为 release。 |
| `--compilation:<name>` | `Release` | 查找宿主文件时使用的构建配置目录，例如 `bin/<configuration>/<framework>`。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，例如 `x64`、`x86`、`arm64`、`arm`。 |
| `--overwrite[:布尔值]` | `false` | 覆盖已存在的产物；tar 归档和安装脚本作为一组提交。 |
| `--install-path:<path>` | `/opt/<将点号替换为 / 的标识>` | Linux 安装目录。标识转为小写，每个点号均替换为目录分隔符，例如 `Zongsoft.Hosting.Web` 对应 `/opt/zongsoft/hosting/web`，指定 `--daemon:zongsoft.web` 后则为 `/opt/zongsoft/web`；如果指定了未禁用的 `--daemon`，默认路径改用 daemon 标识而不是 `--name` 推导。 |
| `--daemon:<名称或文件>` | 自动查找/生成 | 指定 systemd 服务标识或现有 `.service` 文件；`none`、`disable`、`disabled` 禁用服务。 |
| `--daemon-environments:<名称列表>` | 空 | 生成服务时从变量视图读取这些名称的值，写入 `Environment=`；用 `,` 或 `;` 分隔。 |
| `--listen:<port-or-url>` | 空 | 自动生成服务时使用的监听端口或地址；端口默认使用 127.0.0.1，完整地址原样传给宿主 --urls。 |
| `--title:<text>` | 空 | 人类可读的软件包标题，也用于生成 systemd 描述。 |
| `--summary:<text-or-file>` | 空 | 简短摘要。如果值是已存在文件路径，则读取文件内容。 |
| `--description:<text-or-file>` | 空 | 详细描述。如果值是已存在文件路径，则读取文件内容。 |
| `--url:<url>` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license:<text>` | 空 | 许可证表达式或许可证名称。 |
| `--category:<text>` | 格式默认值 | Debian `Section` 或 RPM `Group`；Debian 默认 `utils`，RPM 默认 `Applications/System`。 |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | 软件包维护者/厂商文本。 |
| `--dependencies:<list>` | 空 | 以逗号或分号分隔的依赖列表。Debian 写入 `Depends`，版本关系使用 `name (>= version)`；RPM 写入 `Requires`，可使用 `name >= version`。 |

`--overwrite` 可裸写，也可显式写 `true/false`、`1/0`、`yes/no`、`on/off` 或 `enable(d)/disable(d)`；其他值按 Core `Switch` 约定视为 false。枚举选项沿用 Core 的转换规则，不额外检查枚举成员是否已定义；调用方应提供有效枚举项。输出冲突在制包前和提交前检查；生成失败时旧产物保留。包生成成功后，源 `.version` 才原子保存；保存失败时包保留而命令报错。

### Debian 选项

Debian 同样支持带版本约束的 `--dependencies`。例如为 hosting 的 `web/default` 包声明 ASP.NET Core 10 运行时要求：

```bash
--dependencies:"aspnetcore-runtime-10.0 (>= 10.0)"
```

生成的 `control` 文件包含 `Depends: aspnetcore-runtime-10.0 (>= 10.0)`。Debian 的版本关系必须放在括号内，不能直接照抄 RPM 的 `aspnetcore-runtime-10.0 >= 10.0`；支持的运算符为 `<<`、`<=`、`=`、`>=`、`>>`。多个依赖用逗号或分号分隔，`|` 表示满足其中一个即可，整个选项应加引号。打包器只写入依赖声明，不会下载或内嵌这些软件包，安装环境需有可用的软件源。

| 选项 | control 字段 |
| --- | --- |
| `--provides:<list>` | Provides |
| `--replaces:<list>` | Replaces |
| `--breaks:<list>` | Breaks |
| `--conflicts:<list>` | Conflicts |
| `--recommends:<list>` | Recommends |
| `--suggests:<list>` | Suggests |

列表以逗号或分号分隔，版本关系使用括号，例如 `zongsoft.daemon (>= 1.0.0)`。`--dependencies` 写入 Depends；Depends、Recommends、Suggests 支持 `|` 替代项，Provides 的版本关系只接受 `=`。非法关系及换行会使制包失败。此处是关系语法示例，不表示 hosting 实际声明了该依赖。

### RPM 选项

| 选项 | 说明 |
| --- | --- |
| `--provides:<list>` | 以逗号或分号分隔的 RPM `Provides` 条目。 |
| `--conflicts:<list>` | 以逗号或分号分隔的 RPM `Conflicts` 条目。 |

RPM 关系条目支持 `name`、`name = version`、`name >= version`、`name <= version`、`name > version`、`name < version` 或 `name(>= version)` 形式。

## 打包项

打包项是最终写入包载荷的文件。

如果没有提供位置参数，工具会递归包含 `--source` 下的所有文件：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

如果提供了位置参数，则只包含这些文件或目录。以下示例选择宿主程序集、运行时配置、应用设置、插件和 MIME 定义：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  "*.dll" \
  Zongsoft.Hosting.Web.deps.json \
  Zongsoft.Hosting.Web.runtimeconfig.json \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  plugins \
  wwwroot \
  mime
```

每个条目都可以在最后一个冒号后指定目标别名。以下局部文件包使用 hosting 仓库中已有的文件演示别名，此示例禁用服务生成：

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  ../web/README.md:docs/hosting-web.md \
  ../zongsoft-logo.png:assets/logo.png \
  ../.deploy/default/nginx/zongsoft.web.conf:/etc/nginx/conf.d/zongsoft.web.conf
```

打包项规则：

- 相对路径基于 `--source` 解析。
- 允许指定 `--source` 之外的绝对路径；未指定别名时，只使用文件名。
- 目录会递归包含。
- 任一路径段支持 `*`、`?`；独立段 `**` 匹配零层或多层目录。每个参数在当前位置按相对固定前缀的路径 Ordinal 排序，保留全部参数顺序。Windows 匹配忽略大小写，Unix 区分大小写。
- 目录自身入包，保留空目录与源模式（Windows 默认为 0755）；合成父目录为 0755。拒绝文件/目录冲突；源链接保留逻辑名称并读取目标，目录载荷中的嵌套目录链接跳过。
- `--exclude` 会在加载打包项时跳过匹配文件。模式相对 `--source`，统一使用 `/` 作为路径分隔符，支持 `*`、`?`、`**`，多个模式用逗号或分号分隔。
- 重复的目标路径会报告为冲突并跳过。
- 以 `/` 或 `\` 开头的别名是根路径条目。在 `.deb` 和 `.rpm` 中，它们会安装到对应根路径；在 `.tar.gz` 中，它们存放在 `.root/` 下，并由 `install.sh` 复制。
- Unix 主机会保留文件权限。Windows 主机会给 `.sh`、`.dll`、`.exe` 和无扩展名文件分配 `0755`，其他文件使用 `0644`。

排除示例：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --exclude:"*.pdb;*.xml;logs/**"
```

## systemd 服务

默认情况下，所有包格式都使用 systemd 脚本生成器。

服务解析顺序：

1. 如果 `--source` 下存在 `--daemon:<name>` 指定的文件，则使用该文件。
2. 否则生成 `<daemon>.service`。
3. 如果省略 `--daemon`，使用最终应用名称（`Package.Name`）的小写形式作为服务标识，不附加 Edition。

使用以下任一值禁用服务生成：

```bash
--daemon:none
--daemon:disable
--daemon:disabled
```

需要生成服务文件时，工具按以下顺序定位 .NET 宿主：

1. `<source>/<name>.dll`
2. `<source>/bin/<compilation>/<framework>/<name>.dll`
3. `<source>` 下唯一的 `.exe`，并转换成同名 `.dll`。
4. `<source>/bin/<compilation>/<framework>` 下唯一的 `.exe`，并转换成同名 `.dll`。

生成的普通服务会运行：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

如果提供 `--listen:<value>`，生成的服务会把它作为 `--urls` 传给应用。纯数字值会被当作本机 HTTP 端口：

```bash
--listen:8069
```

生成：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

完整地址也可显式指定，例如 `--listen:http://0.0.0.0:8069`。省略该选项时不追加 `--urls`；使用已有 service 文件时不改写其中的 ExecStart。

> 💡 提示：纯数字 `--listen` 默认绑定 `127.0.0.1`，适合由本机反向代理转发。需要应用直接接受网络连接时，使用 `http://0.0.0.0:8069` 这样的完整地址。

多个完整地址用分号分隔，并给整个值加引号。例如，Web 宿主需要同时监听 HTTP 与 HTTPS 时，可使用：

```text
--listen:"http://0.0.0.0:8069;https://0.0.0.0:8443"
```

HTTPS 需要在宿主中配置可用的默认服务器证书，打包器不生成或配置证书；具体要求见 [Kestrel 端点说明](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/servers/kestrel/endpoints.md)。这表示可选配置，并非 hosting 当前脚本已启用 HTTPS。

> 🚨 注意：宿主没有可用的默认服务器证书时，HTTPS 端点将无法启动。请在应用运行环境中配置并妥善保护证书。

Web 宿主的 `pack.cmd` 将 `Environment` 和 `ASPNETCORE_ENVIRONMENT` 一并写入生成的服务文件，例如：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --daemon-environments:Environment,ASPNETCORE_ENVIRONMENT \
  --Environment:Production \
  --ASPNETCORE_ENVIRONMENT:Production
```

## 生命周期脚本

生命周期脚本可以是源目录相对文件路径、绝对文件路径，也可以是内联脚本文本。四个主钩子的执行时机如下：

- **安装：** `--installing:<script>` 在安装前执行，`--installed:<script>` 在安装后执行。
- **卸载或移除：** `--uninstalling:<script>` 在移除前执行，`--uninstalled:<script>` 在移除后执行。

每个主钩子都可以追加基于文件的前置和后置脚本片段，默认均为空：

| 主钩子 | 前置文件选项 | 后置文件选项 |
| --- | --- | --- |
| `--installing:<文本或文件>` | `--preinstalling:<路径列表>` | `--postinstalling:<路径列表>` |
| `--installed:<文本或文件>` | `--preinstalled:<路径列表>` | `--postinstalled:<路径列表>` |
| `--uninstalling:<文本或文件>` | `--preuninstalling:<路径列表>` | `--postuninstalling:<路径列表>` |
| `--uninstalled:<文本或文件>` | `--preuninstalled:<路径列表>` | `--postuninstalled:<路径列表>` |

多个 pre/post 脚本路径使用 `;` 或 `|` 分隔。pre/post 选项只接受文件列表，不接受内联文本。

如果未提供脚本，工具会生成默认脚本。对 systemd 包而言，默认脚本会在安装/移除前停止服务，创建或删除 `/etc/systemd/system/<service>` 符号链接，重载 systemd，安装后启用服务，并在卸载后删除安装目录。

Debian 的 `prerm` 仅在 `remove` 或 `deconfigure` 时进入卸载生命周期，`postrm` 仅在 `remove` 或 `purge` 时执行卸载收尾。RPM 的 `%preun`/`%postun` 脚本仅在最后一个已安装实例被删除（`$1=0`）时运行，仍有已安装实例时保留载荷。Tar 包使用显式的 `install.sh`/`uninstall.sh` 生命周期，其生成的卸载器只删除解析后的 `TARGET` 路径。

> 🚨 注意：默认生命周期脚本可能停止或启用服务，并在卸载时删除应用安装目录。请先检查生成的脚本和目标路径，再在主机上执行。

## 升迁产物集成

升迁由独立的 [migrator 工具](../migrator/README.zh-Hans.md) 预先制作。packager 不解析 `.migration`/`.ini`、SQL 或执行计划，也不携带原生执行器。设置 `--migrator:<名称或路径>` 可收录已制作的升迁归档和配套启动脚本，例如 `--migrator:../../packages/zongsoft`。

查找从最终的 `--source` 目录开始，而不是从运行命令时的工作目录开始：

- 只写名称（如 `zongsoft`）时，从源目录向父目录逐级查找，直到文件系统根目录；不会查找子目录。
- 值中包含 `/` 或 `\` 时按显式路径处理。相对路径基于 `--source`，绝对路径直接使用。例如 `--migrator:./zongsoft` 只查源目录。
- 已有的 `-migrate`、`-migration`、`.migrate` 或 `.migration` 后缀不区分大小写；未带后缀时追加 `-migrate`。不要在值中包含 Edition、版本、RID、扩展名、通配符或路径列表。

工具使用本次安装包最终确定的 Edition、版本、平台和架构定位产物，包括从源 `.version` 取得的值及默认 x64。无 Edition 时省略对应部分。例如 enterprise、1.0.0、Linux x64 对应：

```text
zongsoft-migrate-enterprise@1.0.0_linux-x64.tar.gz
zongsoft-migrate-enterprise@1.0.0_linux-x64.sh
```

升迁名称可以不同于宿主名称，但 Edition、版本和 RID 必须匹配。只有压缩包和脚本都不存在时才继续向父目录查找；只找到其中一份就立即报错，并指出缺失配套文件的完整路径。找到完整配套后立即校验归档元数据和 RID，校验失败不会继续向上查找。两份文件必须来自同一目录，不会拼配不同目录，也不会替换成其他版本、Edition 或架构。一直查到根目录仍未找到时，错误会列出预期文件名和已检查目录。不指定选项或提供空值时，不启用升迁集成。

两个文件原样放入安装根目录的 `.migration/`，打包时不展开归档；启动脚本权限为 0755，压缩包为 0600。载荷与目标冲突时制包失败。安装时生成的钩子会以 `/var/lib/<包名>/packager` 作为状态目录运行 `apply`。systemd 的 `ExecStartPre` 会运行 `check`，比较包的计划指纹与本地 `ready` 成功标记；它不会读取数据库或桶的当前状态。无 daemon 时仍会执行升迁；DESTDIR 暂存不会执行生命周期钩子；卸载会保留升迁状态、数据库和桶。目标机需要 POSIX sh、tar/gzip、cmp 和执行器所需系统库，详见升迁指南。

> 🚨 注意：安装包含升迁产物的软件包时会运行 `apply`，可能创建或修改数据库、用户、权限和 Amazon S3 桶。生产安装前请备份数据并在预发布环境验证；升迁失败会阻止服务启动。

## 变量

选项值和打包项参数可以使用两种变量形式：

```text
$(name)
%name%
```

变量名支持点号、连字符和索引，不区分大小写。依次加载描述符默认值、系统环境变量、从远到近的祖先链 `.env`、显式命令选项（含额外选项）；后加载覆盖先加载，空值也参与覆盖。变量按使用递归展开，未使用的无效引用不阻止制包；用到的未知、循环或超过 64 层的引用会报错。

命令先用既有环境变量和选项解析并固定 `--source`，默认当前工作目录，再加载文件系统根目录到该源目录的各级直属 `.env`；不搜索子目录，也不额外加载另一条工作目录祖先链。`.env` 不能用于确定或重新指定 source。使用 Core `Profile.Load` 读取 INI 及 `#@import`；根条目保留原名，各级段落名与条目名以 `_` 拼接。例如 `[io rustfs]` 下的 `access_key=example` 生成 `io_rustfs_access_key=example`，根级 `environment=Development` 生成 `environment`。缺失文件跳过，读取或解析失败终止制包。每次调用的变量独立，不修改进程环境变量。

`name`、`edition`、`version` 仍由源版本文件及显式身份选项决定；同名环境变量及 `.env` 变量不替代身份，显式选项可引用 `.env` 中的变量。最终身份与解析后的 source/output 覆盖变量集合。`--migrator` 必须显式启用；`--overwrite` 可由环境变量或 `.env` 提供，再由命令行覆盖。

命令先保留原始选项文本。定位 `source` 后，显式提供的 `name`、`edition`、`version` 先展开再参与源版本校验和版本类型转换；`platform`、`architecture` 和 `overwrite` 也先展开再转换。可传入字面量 `$(APP_VERSION)` 或 `%APP_VERSION%`，在 Bash 中须加引号避免 Shell 抢先解释。尚未从源 `.version` 获得的身份值不能用来定位该源目录。

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
  --source:./publish \
  --output:../packages/
```

常用变量：

- `name`、`version` 和 `edition`：软件包身份及可选发行版标识。
- `platform`、`architecture` 和 `RuntimeIdentifier`：目标操作系统、CPU 架构及组合后的运行时标识。
- `framework` 和 `compilation`：目标 .NET 框架和构建配置。
- `source` 和 `output`：规范化后的源目录和安装包输出目录。

### 文本来源

摘要、描述和四个主生命周期钩子共用解析规则：`file:` 明确指定文件（相对 source），`text:` 后的字面文本原样保留。无前缀时展开打包变量，读取源目录下已有文件；多行内容为文本，明显的缺失文件路径会报错，其余值为文本。文件内容直接使用，不展开变量或作为路径读取，Shell 表达式可放入文件或 `text:` 文本中。pre/post 钩子是以 `;` 或 `|` 分隔的严格文件列表，不接受 `text:`。

Debian/RPM 载荷使用自动清理的临时文件和流式摘要，需预留临时磁盘空间；不在内存中拼接整个包体。实现见[载荷流与目录条目](docs/implementation.zh-Hans.md#载荷流与目录条目)。

## 包格式

安装前先查看包内容。下面的文件清单和元数据查看命令是只读操作，不会运行生命周期脚本。

### `.tar.gz`

tar 命令会生成 `.tar.gz` 包及同名 `.sh` 安装脚本。tar 包包含应用文件、`.root/` 下的可选根路径条目，以及可执行的 `install.sh` 和 `uninstall.sh`。生命周期脚本会融合进 `install.sh` 和 `uninstall.sh`。

一键安装：

> 🚨 注意：下面的命令使用 `sudo` 执行生成的安装脚本，可能写入系统目录并管理服务。请先使用 `DESTDIR` 暂存安装，或在预发布主机验证。

```bash
sudo sh ./packages/zongsoft.web@1.0.0-x64.sh
```

安装：

```bash
tar -xzf ./packages/zongsoft.web@1.0.0-x64.tar.gz
sudo ./install.sh
```

安装到暂存目录：

```bash
DESTDIR=/tmp/stage ./install.sh
```

未集成升迁产物的软件包可在安装时覆盖目标路径：

```bash
sudo env INSTALL_PATH=/srv/zongsoft/web ./install.sh
```

集成升迁产物时，安装路径在软件包制作时固定。如需调整，请在打包时指定 `--install-path`。

卸载：

```bash
cd /opt/zongsoft/web
sudo ./uninstall.sh
```

### `.deb`

Debian 包包含：

```text
debian-binary
control.tar.gz
data.tar.gz
```

先检查元数据和载荷：

```bash
dpkg-deb --info ./packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0-x64.deb
```

> 🚨 注意：`dpkg` 安装会以特权运行软件包生命周期脚本。请先检查脚本并在预发布主机验证。

```bash
sudo dpkg -i ./packages/zongsoft.web@1.0.0-x64.deb
```

`/etc/` 下的根路径条目也会写入 Debian `conffiles` 元数据。

### `.rpm`

RPM 包包含 RPM lead/signature/header 元数据，以及 gzip 压缩的 `newc` cpio 载荷。

先检查元数据、载荷和生命周期脚本：

```bash
rpm -qip ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0-x64.rpm
```

> 🚨 注意：`rpm` 安装会以特权运行软件包生命周期脚本。请先检查脚本并在预发布主机验证。

```bash
sudo rpm -Uvh ./packages/zongsoft.web@1.0.0-x64.rpm
```

`/etc/` 下的根路径条目会被标记为 RPM 配置文件。

## 推荐打包流程

1. **准备干净的源目录。** 将应用发布或暂存到 `--source`，再有选择地复制必需的配置、插件、静态文件和其他资源。如果递归打包全部文件，请把输出目录放在源目录之外，避免旧安装包意外进入载荷。
2. **选择载荷范围。** 不提供位置参数时会递归包含 `--source` 下的全部文件；需要更小或更容易审查的软件包时，明确列出文件和目录，并用 `--exclude` 排除不应入包的文件。
3. **确认软件包身份。** 检查 `--name`、`--version`、可选 `--edition`、平台和架构。确认生成的服务标识、`--install-path` 与 `--listen` 地址符合目标环境。纯数字监听端口默认绑定本机地址。
4. **检查生成物。** 使用 `tar -tzf`、`dpkg-deb --info` / `--contents` 或 `rpm -qip` / `-qlp` / `-qp --scripts` 检查文件和元数据。软件包会管理服务时，也要复核生命周期脚本和 systemd 单元。
5. **在预发布环境安装验证。** 测试安装、服务启动、配置路径和卸载行为；可在 tar 包上使用 `DESTDIR`，或使用一次性主机。记录环境相关配置，不要依赖生产机才有的隐式条件。
6. **谨慎发布到生产。** 生产安装前备份应用数据并确认恢复步骤。启用 `--migrator` 后，安装会执行升迁并可能修改数据库和 Amazon S3 桶；不同软件包版本之间要保留同一个升迁状态目录。

> 💡 提示：`--overwrite` 只会替换输出目录中已有的安装包文件，不会覆盖或更新已经安装的应用。

> 🚨 注意：制包成功只表示安装包文件已生成，不能证明目标环境可以安装该包或应用能够正常启动。

## 从源码构建

以下命令从 tools 仓库的 `packager` 目录执行。

还原并构建：

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```


Cake 构建包含打包器配置的构建步骤：

```bash
dotnet cake --target=build --edition=Release
```

直接运行回归测试：

```bash
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0
```

也可使用 Cake：

```bash
dotnet cake --target=test --edition=Release
```

## 故障排查

- **`The source directory '<path>' does not exist.`** 变量展开和路径规范化后，`--source` 指向的位置不存在。
- **`The daemon host location failed.`** 没有找到已有服务文件或可用宿主 `.dll`，也无法从唯一的 `.exe` 推断名称。可指定 `--daemon:<service-file>`，或使用 `--daemon:none` 禁用服务生成。
- **`A valid nonzero --version or selected source version is required. Source: <path>`** 没有有效的命令版本或源文件版本，或者版本为零；非版本文本会在变量展开后、选择源版本前报错。
- **`The source path '<path>' does not exist.`** 位置参数没有匹配到现有文件、目录或通配路径。
- **`Package file already exists.`** 重新执行时使用 `--overwrite`，或选择其他 `--output` 目录。

## 更多细节

参见 [docs/implementation.zh-Hans.md](docs/implementation.zh-Hans.md) 了解内部设计、打包流水线和格式级实现细节。

## 许可证

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可证。

本地源搜索及链接规则见[实现文档](docs/implementation.zh-Hans.md#本地搜索与源链接)。
详细规范检查及资源生成要求见 [仓库说明](../AGENTS.md#代码规范检查)。
