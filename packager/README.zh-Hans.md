# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` 是一个 .NET 全局工具，用于把已发布的应用目录打包成适合 Linux 分发和安装的 `.tar.gz`、`.deb`、`.rpm` 安装包。

它主要面向 .NET 服务和命令行应用，打包时不依赖外部的 `tar`、`dpkg-deb`、`rpmbuild` 或 `cpio` 命令。

## 快速导航

- [打包器版本元数据](#打包器版本元数据)
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
- [故障排查](#故障排查)
- [实现说明](docs/implementation.md)

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

每个安装包自动记录当前生成工具的身份，逻辑内容为 `Packager:Zongsoft.Tools.Packager@0.11.0.0`。值采用 `程序集名@版本号`，从打包器自身程序集读取，独立于宿主应用版本；无需指定额外选项或启用升迁。

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

确认构建成功且 `src/bin/Release/Zongsoft.Tools.Packager.0.11.0.nupkg` 已生成后，首次安装执行：

```powershell
dotnet tool install -g Zongsoft.Tools.Packager --version 0.11.0 --source ./src/bin/Release --no-http-cache
```

若已安装该工具，尤其是重新编译了同一版本，先卸载，再执行上面的本地安装命令：

```powershell
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

示例版本 `0.11.0` 对应当前项目版本，请随实际 `.nupkg` 调整。`--source` 限定本次安装只使用本地目录，避免选中 NuGet.org 的同名包；`--no-http-cache` 禁用下载缓存，选项说明见 [.NET 工具安装文档](https://learn.microsoft.com/zh-cn/dotnet/core/tools/dotnet-tool-install)。安装后使用 `dotnet tool list -g` 核对版本。这里的“本地”指包来源，`-g` 仍会替换当前用户的全局工具。只做本地测试不要运行 Cake 的 `pack` 任务，它会推送到 NuGet.org。

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

三个子命令共享通用选项。`rpm` 额外支持 RPM 包关系元数据选项。

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
| `  output:<path>` | 源目录 | 安装包输出目录，无论是否以目录分隔符结尾都作为目录处理；不支持通过此选项指定文件名。相对路径基于 `  source` 解析。 |
| `--exclude:<patterns>` | 空 | 加载打包项时跳过的文件模式列表，多个模式用逗号或分号分隔。 |
| `--edition:<name>` | 空 | 可选发行/版本标识。会追加到包名；对 RPM 而言，有值时也作为 release。 |
| `--compilation:<name>` | `Release` | 查找宿主文件时使用的构建配置目录，例如 `bin/<configuration>/<framework>`。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，例如 `x64`、`x86`、`arm64`、`arm`。 |
| `--overwrite` | `false` | 覆盖已存在的包文件。未指定时，输出文件已存在会导致创建失败。 |
| `--install-path:<path>` | `/opt/<将点号替换为 / 的标识>` | Linux 安装目录。标识转为小写，每个点号均替换为目录分隔符，例如 `Zongsoft.Hosting.Web` 对应 `/opt/zongsoft/hosting/web`，指定 `--daemon:zongsoft.web` 后则为 `/opt/zongsoft/web`；如果指定了未禁用的 `--daemon`，默认路径改用 daemon 标识而不是 `--name` 推导。 |
| `--listen:<port-or-url>` | 空 | 自动生成服务时使用的监听端口或地址；端口默认使用 127.0.0.1，完整地址原样传给宿主 --urls。 |
| `--title:<text>` | 空 | 人类可读的软件包标题，也用于生成 systemd 描述。 |
| `--summary:<text-or-file>` | 空 | 简短摘要。如果值是已存在文件路径，则读取文件内容。 |
| `--description:<text-or-file>` | 空 | 详细描述。如果值是已存在文件路径，则读取文件内容。 |
| `--url:<url>` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license:<text>` | 空 | 许可证表达式或许可证名称。 |
| `--category:<text>` | 格式默认值 | Debian `Section` 或 RPM `Group`；Debian 默认 `utils`，RPM 默认 `Applications/System`。 |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | 软件包维护者/厂商文本。 |
| `--dependencies:<list>` | 空 | 以逗号或分号分隔的依赖列表。Debian 写入 `Depends`，版本关系使用 `name (>= version)`；RPM 写入 `Requires`，可使用 `name >= version`。 |

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
3. 如果省略 `  daemon`，使用最终应用名称（`Package.Name`）的小写形式作为服务标识，不附加 Edition。

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

多个完整地址用分号分隔，并给整个值加引号。例如，Web 宿主需要同时监听 HTTP 与 HTTPS 时，可使用：

```text
--listen:"http://0.0.0.0:8069;https://0.0.0.0:8443"
```

HTTPS 需要在宿主中配置可用的默认服务器证书，打包器不生成或配置证书；具体要求见 [Kestrel 端点说明](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/servers/kestrel/endpoints.md)。这表示可选配置，并非 hosting 当前脚本已启用 HTTPS。

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

生命周期脚本可以是源目录相对文件路径、绝对文件路径，也可以是内联脚本文本。

| 选项 | 执行时机 |
| --- | --- |
| `--installing:<script>` | 安装前。 |
| `--installed:<script>` | 安装后。 |
| `--uninstalling:<script>` | 卸载/移除前。 |
| `--uninstalled:<script>` | 卸载/移除后。 |

每个主钩子都可以追加前置或后置脚本片段：

| 选项 | 执行时机 |
| --- | --- |
| `--preinstalling:<paths>` / `--postinstalling:<paths>` | 围绕 `installing` 执行。 |
| `--preinstalled:<paths>` / `--postinstalled:<paths>` | 围绕 `installed` 执行。 |
| `--preuninstalling:<paths>` / `--postuninstalling:<paths>` | 围绕 `uninstalling` 执行。 |
| `--preuninstalled:<paths>` / `--postuninstalled:<paths>` | 围绕 `uninstalled` 执行。 |

多个 pre/post 脚本路径可用 `;` 或 `|` 分隔。

如果未提供脚本，工具会生成默认脚本。对 systemd 包而言，默认脚本会在安装/移除前停止服务，创建或删除 `/etc/systemd/system/<service>` 符号链接，重载 systemd，安装后启用服务，并在卸载后删除安装目录。

Debian 的 `prerm` 仅在 `remove` 或 `deconfigure` 时进入卸载生命周期，`postrm` 仅在 `remove` 或 `purge` 时执行卸载收尾。RPM 的 `%preun`/`%postun` 脚本仅在最后一个已安装实例被删除（`$1=0`）时运行，仍有已安装实例时保留载荷。Tar 包使用显式的 `install.sh`/`uninstall.sh` 生命周期，其生成的卸载器只删除解析后的 `TARGET` 路径。

## 升迁产物集成

升迁由独立的 [migrator 工具](../migrator/README.zh-Hans.md) 预先制作。packager 不解析 `.migration`/`.env`、SQL 或执行计划，也不携带原生执行器。

`--migrator` 指定制作升迁时的输入名称，可带目录，例如 `--migrator:../../packages/zongsoft`。

先展开变量，再判断是否包含目录分隔符 `/` 或 `\`：不包含时，从最终打包源目录（`--source`）逐级向父目录查找，直到文件系统根目录，不遍历子目录；包含时，相对路径基于源目录，绝对路径直接使用，均不向上查找。`--migrator:zongsoft` 启用向上查找，`--migrator:./zongsoft` 仅限定在源目录；查找起点不是运行命令时的工作目录。

既有 `-migrate`、`-migration`、`.migrate`、`.migration` 后缀忽略大小写识别，未带后缀时追加 `-migrate`。不能填写 Edition、版本、RID、扩展名、通配符或路径列表。

工具使用本次安装包最终确定的 Edition、版本、平台和架构定位产物，包括从源 `.version` 取得的值及默认 x64。无 Edition 时省略对应部分。例如 enterprise、1.0.0、Linux x64 对应：

```text
zongsoft-migrate-enterprise@1.0.0_linux-x64.tar.gz
zongsoft-migrate-enterprise@1.0.0_linux-x64.sh
```

名称可以不同于宿主名称，但 Edition、版本和 RID 必须匹配。每一级目录只有在压缩包和脚本都不存在时才继续向上；只找到其中一份立即报错并指出缺失文件的完整路径。找到完整配套后立即校验归档元数据和 RID，校验失败不再向上查找。两份文件必须来自同一目录，不拼配不同目录、不选择其他版本、Edition 或架构。到根目录仍未找到时，错误列出预期文件名和已检查目录。未指定选项，或选项值为空、空字符串、全空白字符时，均不启用升迁，也不收录升迁产物。

两个文件原样存入安装根 `.migration/`，不展开归档；脚本为 0755，压缩包为 0600。与载荷目标冲突时报错。安装时调用脚本 `apply` 并传入 `/var/lib/<包名>/packager`；失败阻止启动。systemd 的 `ExecStartPre` 调用同一脚本 `check`，只比较完成标记，不解压、不连接服务。无 daemon 时仍执行升迁，DESTDIR 暂存不执行钩子，卸载保留状态与数据库/桶。目标机需要 POSIX sh、tar/gzip、cmp 和运行器所需系统库；详见升迁指南。
## 变量

选项值和打包项参数可以使用两种变量形式：

```text
$(name)
%name%
```

变量名不区分大小写。显式命令选项（含额外选项）覆盖环境变量，环境变量覆盖描述符默认值。变量按使用展开，未使用的无效引用不阻止制包；用到的未知或循环引用会报错。

`name`、`edition`、`version` 仍由源版本文件及显式身份选项决定；同名环境变量不替代身份。最终身份与解析后的 source/output 覆盖变量集合。`--migrator` 必须显式启用，`--overwrite` 仍为显式开关。

下面的名称和版本先由 Bash 展开。`version` 在进入打包流程前就按 `System.Version` 解析，不能直接传入字面量 `%APP_VERSION%` 或 `$(APP_VERSION)`；名称和 Edition 也按传入值参与源版本校验。打包器变量表达式用于路径、脚本文本、升迁配置等后续规范化位置。

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

| 变量 | 含义 |
| --- | --- |
| `name` | 软件包/应用名称。 |
| `version` | 软件包版本。 |
| `edition` | 可选发行/版本标识。 |
| `platform` | 目标平台。 |
| `architecture` | 目标架构。 |
| `framework` | 目标框架。 |
| `compilation` | 构建配置。 |
| `source` | 规范化后的源目录。 |
| `output` | 规范化后的输出目录。 |
| `RuntimeIdentifier` | 根据平台与架构推断的运行时标识。 |

### 文本来源

摘要、描述和四个主生命周期钩子共用解析规则：`file:` 明确指定文件（相对 source），`text:` 后的字面文本原样保留。无前缀时展开打包变量，读取源目录下已有文件；多行内容为文本，明显的缺失文件路径会报错，其余值为文本。文件内容直接使用，不展开变量或作为路径读取，Shell 表达式可放入文件或 `text:` 文本中。pre/post 钩子是以 `;` 或 `|` 分隔的严格文件列表，不接受 `text:`。

Debian/RPM 载荷使用自动清理的临时文件和流式摘要，需预留临时磁盘空间；不在内存中拼接整个包体。实现见[载荷流与目录条目](docs/implementation.md#载荷流与目录条目)。

## 包格式

### `.tar.gz`

tar 命令会生成 `.tar.gz` 包及同名 `.sh` 安装脚本。tar 包包含应用文件、`.root/` 下的可选根路径条目，以及可执行的 `install.sh` 和 `uninstall.sh`。生命周期脚本会融合进 `install.sh` 和 `uninstall.sh`。

一键安装：

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

普通包可覆盖安装路径；启用升迁时路径固定，应以 `  install path` 重新打包：

```bash
sudo env INSTALL_PATH=/srv/zongsoft/web ./install.sh
```

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

检查并安装：

```bash
dpkg-deb --info ./packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0-x64.deb
sudo dpkg -i ./packages/zongsoft.web@1.0.0-x64.deb
```

`/etc/` 下的根路径条目也会写入 Debian `conffiles` 元数据。

### `.rpm`

RPM 包包含 RPM lead/signature/header 元数据，以及 gzip 压缩的 `newc` cpio 载荷。

检查并安装：

```bash
rpm -qip ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0-x64.rpm
sudo rpm -Uvh ./packages/zongsoft.web@1.0.0-x64.rpm
```

`/etc/` 下的根路径条目会被标记为 RPM 配置文件。

## 从源码构建

以下命令从 tools 仓库的 `packager` 目录执行。

还原并构建：

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```


使用 Cake 构建：

```bash
dotnet cake --target=build --edition=Release
```


```powershell
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0
```

通过 Cake 脚本运行测试：

```bash
dotnet cake --target=test --edition=Release
```

## 故障排查

`The source directory '<path>' does not exist.`

`--source` 的值在变量展开和路径规范化后不存在。

`The daemon host location failed.`

没有找到已有服务文件，工具也无法定位宿主 `.dll` 或可用于推断 `.dll` 名称的唯一 `.exe`。可以提供 `--daemon:<service-file>`，或使用 `--daemon:none` 禁用服务生成。

`A valid nonzero --version or selected source version is required. Source: <path>`

没有可用的命令版本或源文件版本，或版本为零；非版本文本会在命令选项解析阶段报错。

`The source path '<path>' does not exist.`

某个位置参数没有匹配到存在的文件、目录或通配路径。

包文件已存在。

重新执行时加上 `--overwrite`，或选择另一个 `--output` 目录。

## 更多细节

参见 [docs/implementation.md](docs/implementation.md) 了解内部设计、打包流水线和格式级实现细节。

## 许可证

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可证。

本地源搜索及链接规则见[实现文档](docs/implementation.md#本地搜索与源链接)。


## 开发规范检查

生产和测试项目使用 `Zongsoft.CodeAnalysis`，版本由仓库根 `Directory.Packages.props` 管理；使用 .NET SDK 10.0.401 或具有 Roslyn 5.9 及以上版本编译器的工具链。分析器为私有构建依赖，不随工具作为运行时依赖分发。

```powershell
dotnet build src/Zongsoft.Tools.Packager.csproj -p:ZongsoftCodeStyleStrict=true
dotnet format style src/Zongsoft.Tools.Packager.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

多目标构建覆盖项目全部目标框架；详细规范检查及资源生成要求见 [仓库说明](../AGENTS.md#代码规范检查)。
