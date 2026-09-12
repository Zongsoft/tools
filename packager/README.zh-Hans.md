# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` 是一个 .NET 全局工具，用于把已发布的应用目录打包成适合 Linux 分发和安装的 `.tar.gz`、`.deb`、`.rpm` 安装包。

它主要面向 .NET 服务和命令行应用，打包时不依赖外部的 `tar`、`dpkg-deb`、`rpmbuild` 或 `cpio` 命令。

## 快速导航

- [功能特性](#功能特性)
- [安装](#安装)
- [快速开始](#快速开始)
- [命令](#命令)
- [打包项](#打包项)
- [systemd 服务](#systemd-服务)
- [生命周期脚本](#生命周期脚本)
- [安装升迁](#安装升迁)
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
- 支持显式文件条目、递归目录、最后一级路径通配、目标别名，以及 `/etc/nginx/conf.d/zongsoft.web.conf` 这类根路径别名。
- 直接使用 .NET 写入包格式：
  - `.tar.gz` 使用 gzip 压缩的 PAX tar。
  - `.deb` 使用包含 `control.tar.gz` 和 `data.tar.gz` 的 `ar` 容器。
  - `.rpm` 使用 RPM lead/header 元数据和 gzip 压缩的 `newc` cpio 载荷。

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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
<name>@<version>_<runtime>.<extension>
<name>-<edition>@<version>_<runtime>.<extension>
```

如果指定了未禁用的 `--daemon:<name>`，则优先使用 daemon 标识生成包文件名：

```text
<daemon>@<version>_<runtime>.<extension>
<daemon>-<edition>@<version>_<runtime>.<extension>
```

示例：

```text
zongsoft.web@1.0.0_linux-x64.deb
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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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

包内安装根 `.version` 使用 **`ApplicationIdentifier`**，仅以一行表示本次名称、Edition 和版本。内容直接从内存写入，完全采用 `ApplicationIdentifier.Save(Stream)` 的输出，不追加换行；权限为 `0644`。指向该安装位置的旧载荷会被替换，排除规则不影响自动生成的版本条目。

所有制包步骤成功后才按 Core 格式保存源文件，只更新所选 Edition，保留其他 Edition 的名称、版本和顺序；注释及原始空白布局不保留。解析、校验或制包失败不更新源文件。保存源文件失败时命令返回错误，明确指出安装包已生成，并保留该包。

### 通用选项

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source:<path>` | 当前目录 | 待打包的源目录。 |
| `--migration:<paths>` | 空 | 以 `;` 或 `\|` 分隔的升迁 INI 路径，见 [安装升迁](docs/migrations.zh-Hans.md)。 |
| `--output:<path>` | 源目录 | 生成安装包的输出路径，以目录分隔符(`/`或`\`)结尾表示目录，否则为文件。相对路径基于 `--source` 解析。 |
| `--exclude:<patterns>` | 空 | 加载打包项时跳过的文件模式列表，多个模式用逗号或分号分隔。 |
| `--edition:<name>` | 空 | 可选发行/版本标识。会追加到包名；对 RPM 而言，有值时也作为 release。 |
| `--compilation:<name>` | `Release` | 查找宿主文件时使用的构建配置目录，例如 `bin/<configuration>/<framework>`。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，例如 `x64`、`x86`、`arm64`、`arm`。 |
| `--overwrite` | `false` | 覆盖已存在的包文件。未指定时，输出文件已存在会导致创建失败。 |
| `--install-path:<path>` | `/opt/<将点号替换为 / 的标识>` | Linux 安装目录。标识转为小写，每个点号均替换为目录分隔符，例如 `Zongsoft.Hosting.Web` 对应 `/opt/zongsoft/hosting/web`，指定 `--daemon:zongsoft.web` 后则为 `/opt/zongsoft/web`；如果指定了未禁用的 `--daemon`，默认路径改用 daemon 标识而不是 `--name` 推导。 |
| `--title:<text>` | 空 | 人类可读的软件包标题，也用于生成 systemd 描述。 |
| `--summary:<text-or-file>` | 空 | 简短摘要。如果值是已存在文件路径，则读取文件内容。 |
| `--description:<text-or-file>` | 空 | 详细描述。如果值是已存在文件路径，则读取文件内容。 |
| `--url:<url>` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license:<text>` | 空 | 许可证表达式或许可证名称。 |
| `--category:<text>` | 格式默认值 | Debian `Section` 或 RPM `Group`；Debian 默认 `utils`，RPM 默认 `Applications/System`。 |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | 软件包维护者/厂商文本。 |
| `--dependencies:<list>` | 空 | 以逗号或分号分隔的依赖列表。写入 Debian `Depends` 或 RPM `Requires`。 |

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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
- 通配符支持最后一级路径中的 `*` 和 `?`。
- `--exclude` 会在加载打包项时跳过匹配文件。模式相对 `--source`，统一使用 `/` 作为路径分隔符，支持 `*`、`?`、`**`，多个模式用逗号或分号分隔。
- 重复的目标路径会报告为冲突并跳过。
- 以 `/` 或 `\` 开头的别名是根路径条目。在 `.deb` 和 `.rpm` 中，它们会安装到对应根路径；在 `.tar.gz` 中，它们存放在 `.root/` 下，并由 `install.sh` 复制。
- Unix 主机会保留文件权限。Windows 主机会给 `.sh`、`.dll`、`.exe` 和无扩展名文件分配 `0755`，其他文件使用 `0644`。

排除示例：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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
3. 如果省略 `--daemon`，使用小写包名作为服务标识。

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

如果提供 `--daemon-bind:<value>`，生成的服务会把它作为 `--urls` 传给应用。纯数字值会被当作本机 HTTP 端口：

```bash
--daemon-bind:8069
```

生成：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

Web 宿主的 `pack.cmd` 将 `Environment` 和 `ASPNETCORE_ENVIRONMENT` 一并写入生成的服务文件，例如：

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
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

包管理器升级不会进入卸载生命周期。Debian 的 `prerm`/`postrm` 脚本会根据动作参数进行保护，RPM 的 `%preun`/`%postun` 脚本仅在最后一个已安装实例被删除时运行。这可以防止旧包的卸载脚本在升级或同版本覆盖安装期间删除刚安装的新版本负载。Tar 包保持显式的 `install.sh`/`uninstall.sh` 生命周期，其生成的卸载器只删除解析后的 `TARGET` 路径。

## 安装升迁

从源码制作工具包时，先构建 migrator，再执行 `dotnet pack src/Zongsoft.Tools.Packager.csproj`。主项目不引用升迁器项目，详见[构建说明](docs/migrations.zh-Hans.md#构建与工具包生成)。

中间文件为 `<安装目录>/.migration/migration.json`，预处理 SQL 批次保存在 `.migration/.artifacts/` 中。独立升迁运行器使用 **Native AOT** 发布到 glibc Linux，目标机无需安装 .NET 运行时。TDengine 直接使用 WebSocket 连接 taosAdapter，不依赖 `TDengine.Connector`；升迁提示提供英文和简体中文。两程序的文件交接、执行顺序与启动门禁见[协作说明](docs/migrations.zh-Hans.md#打包器与-migrator-的协作)。

增加 `--migration` 即可在应用启动前创建数据库、表结构及 S3/RustFS 存储桶。一个选项指定多个 `.ini` 路径，以 `;` 或 `|` 分隔；对应 `.env` 连接参数按约定从同目录及父目录查找。

S3 存储桶初始化还支持默认加密（`encryption:sse-s3` / `encryption:sse-kms`）、版本控制（`versioning:enabled` / `versioning:suspended`）和桶标签（`tag.<名称>:<值>`）。已有桶保持不变，语法及重试行为见[升迁指南](docs/migrations.zh-Hans.md)。

对上文准备的 Zongsoft hosting `./publish` 目录：

```text
--migration:../.deploy/$(scheme)/migration/zongsoft.db-$(environment).ini;../.deploy/$(scheme)/migration/zongsoft.fs-$(environment).ini
```

指定的升迁 INI 不存在或文件名通配符无匹配时，输出警告并跳过；全部文件缺失时生成普通包，不附带升迁产物和门禁。已存在但无效的 INI、缺少参数文件或 SQL 脚本仍然报错。

请用 Shell 引号包住整个选项，并按 [hosting 升迁指南](docs/migrations.zh-Hans.md) 创建部署文件。指南使用真实 Zongsoft.Upgrading 建表脚本，说明参数、执行顺序及恢复方法。支持 SQL Server、MySQL、SQLite、DuckDB、PostgreSQL（`postgres`/`postgresql`）、TDengine 和 `amazon.s3`。

主项目负责准备升迁计划，独立 [`migrator/src`](migrator/src/Zongsoft.Tools.Packager.Migrator.csproj) 项目在 Linux 上执行。两端链接 `.shared` 中的纯协议源码，不生成共享 DLL；migrator 是 Native AOT 原生程序，并附带必要的原生库。

升迁失败会阻止服务启动，之后的 systemd 启动也需要完成标记。每次安装和重试均执行全部 SQL，由脚本作者保证可重复执行，包括部分失败后的重试；不维护逐文件成功历史。包内包含展开后的连接凭据（`migration.json` 权限 `0600`），安装包也需要按含凭据产物保护。原生升迁运行器支持 glibc Linux x64/arm64。tar `DESTDIR` 暂存跳过生命周期钩子和升迁；实际升迁安装使用打包时的 `--install-path`。

## 变量

选项值和打包项参数可以使用两种变量形式：

```text
$(name)
%name%
```

变量名不区分大小写，加载来源包括：

1. 环境变量。
2. 已声明命令选项及其默认值。
3. 命令解析器接受的额外命令行选项。

当前实现遇到同名变量时会保留第一次出现的值。因此，除非刻意如此，否则应避免定义与打包选项同名的环境变量。

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
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

## 包格式

### `.tar.gz`

tar 命令会生成 `.tar.gz` 包及同名 `.sh` 安装脚本。tar 包包含应用文件、`.root/` 下的可选根路径条目，以及可执行的 `install.sh` 和 `uninstall.sh`。生命周期脚本会融合进 `install.sh` 和 `uninstall.sh`。

一键安装：

```bash
sudo sh ./packages/zongsoft.web@1.0.0_linux-x64.sh
```

安装：

```bash
tar -xzf ./packages/zongsoft.web@1.0.0_linux-x64.tar.gz
sudo ./install.sh
```

安装到暂存目录：

```bash
DESTDIR=/tmp/stage ./install.sh
```

覆盖安装路径：

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
dpkg-deb --info ./packages/zongsoft.web@1.0.0_linux-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0_linux-x64.deb
sudo dpkg -i ./packages/zongsoft.web@1.0.0_linux-x64.deb
```

`/etc/` 下的根路径条目也会写入 Debian `conffiles` 元数据。

### `.rpm`

RPM 包包含 RPM lead/signature/header 元数据，以及 gzip 压缩的 `newc` cpio 载荷。

检查并安装：

```bash
rpm -qip ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0_linux-x64.rpm
sudo rpm -Uvh ./packages/zongsoft.web@1.0.0_linux-x64.rpm
```

`/etc/` 下的根路径条目会被标记为 RPM 配置文件。

## 从源码构建

以下命令从 tools 仓库的 `packager` 目录执行。

还原并构建：

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```

独立 `migrator` 任务将运行器准备到 `src/.migrator/`，主项目通过普通内容声明复制这些文件，不引用或构建运行器。制作完整工具包可运行下面的 Cake 构建，或先执行 `dotnet cake --target=migrator --edition=Release`，再执行 `dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release`。未准备运行器时普通打包仍可用，`--migration` 会提示缺少运行器。

使用 Cake 构建：

```bash
dotnet cake --target=build --edition=Release
```

[`test`](test/Zongsoft.Tools.Packager.Tests.csproj) 覆盖打包器输入、SQL 批次预处理、制包和调用独立 migrator 进程的 JSON 交接；[`migrator/test`](migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj) 只引用运行器，覆盖数据库、S3 及 TDengine WebSocket。两个测试项目可分别运行：

```powershell
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0
dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0
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

`The version number is invalid.`

版本值缺失、无效，或解析为 `0.0.0.0`。

`The source path '<path>' does not exist.`

某个位置参数没有匹配到存在的文件、目录或通配路径。

包文件已存在。

重新执行时加上 `--overwrite`，或选择另一个 `--output` 目录。

## 更多细节

参见 [docs/implementation.md](docs/implementation.md) 了解内部设计、打包流水线和格式级实现细节。

## 许可证

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可证。
