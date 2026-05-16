# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README-zh_CN.md)

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
- 支持显式文件条目、递归目录、最后一级路径通配、目标别名，以及 `/etc/myapp/app.conf` 这类根路径别名。
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

先发布应用：

```bash
dotnet publish ./src/MyApp/MyApp.csproj \
  -c Release \
  -f net10.0 \
  -o ./publish
```

生成 Debian 安装包：

```bash
dotnet-pack deb \
  --name:MyCompany.MyApp \
  --title:"MyApp Service" \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:./packages \
  --summary:"MyApp background service" \
  --description:"A .NET service packaged with Zongsoft.Tools.Packager."
```

生成的包文件名遵循以下规则：

```text
<name>@<version>_<runtime>.<extension>
<name>-<edition>@<version>_<runtime>.<extension>
```

示例：

```text
MyCompany.MyApp@1.0.0_linux-x64.deb
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
  --name:MyCompany.MyApp \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:./packages
```

生成 Debian 安装包，并把应用配置安装到 `/etc`：

```bash
dotnet-pack deb \
  --name:MyCompany.MyApp \
  --title:"MyApp Service" \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:./packages \
  --category:utils \
  MyApp.dll \
  appsettings.json \
  appsettings.Production.json:/etc/myapp/appsettings.json
```

生成带依赖元数据的 RPM 安装包：

```bash
dotnet-pack rpm \
  --name:MyCompany.MyApp \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:./packages \
  --license:MIT \
  --dependencies:"dotnet-runtime-10.0 >= 10.0" \
  --provides:"mycompany-myapp = 1.0.0" \
  --conflicts:"mycompany-myapp-legacy"
```

禁用 systemd 生成，只打包文件：

```bash
dotnet-pack tar \
  --name:MyTool \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --daemon:none
```

### 必需选项

| 选项 | 说明 |
| --- | --- |
| `--name:<name>` | 应用/软件包名称，也用于推断默认安装路径和服务名。 |
| `--version:<version>` | 软件包版本，`0.0.0.0` 会被拒绝。 |
| `--platform:<platform>` | 目标平台。支持的枚举值包括 `linux`、`unix`、`osx`、`windows`/`win`、`unknown`；Linux 包通常使用 `linux`。 |
| `--framework:<tfm>` | 目标框架标识，例如 `net8.0`、`net9.0` 或 `net10.0`。 |

### 通用选项

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source:<path>` | 当前目录 | 待打包的源目录。 |
| `--output:<path>` | 源目录 | 生成安装包的输出目录。相对路径基于 `--source` 解析。 |
| `--edition:<name>` | 空 | 可选发行/版本标识。会追加到包名；对 RPM 而言，有值时也作为 release。 |
| `--compilation:<name>` | `Release` | 查找宿主文件时使用的构建配置目录，例如 `bin/<configuration>/<framework>`。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，例如 `x64`、`x86`、`arm64`、`arm`。 |
| `--overwrite` | `false` | 覆盖已存在的包文件。未指定时，输出文件已存在会导致创建失败。 |
| `--install-path:<path>` | `/opt/<vendor>/<name>` 或 `/opt/<name>` | Linux 安装目录。名称包含点号时，第一个片段会作为 vendor 目录。 |
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
  --name:MyApp \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish
```

如果提供了位置参数，则只包含这些文件或目录：

```bash
dotnet-pack deb \
  --name:MyApp \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  MyApp.dll \
  appsettings.json \
  wwwroot
```

每个条目都可以在最后一个冒号后指定目标别名：

```bash
dotnet-pack deb \
  --name:MyApp \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  appsettings.Production.json:appsettings.json \
  ../shared/logo.png:assets/logo.png \
  nginx.conf:/etc/nginx/conf.d/myapp.conf
```

打包项规则：

- 相对路径基于 `--source` 解析。
- 允许指定 `--source` 之外的绝对路径；未指定别名时，只使用文件名。
- 目录会递归包含。
- 通配符支持最后一级路径中的 `*` 和 `?`。
- 重复的目标路径会报告为冲突并跳过。
- 以 `/` 或 `\` 开头的别名是根路径条目。在 `.deb` 和 `.rpm` 中，它们会安装到对应根路径；在 `.tar.gz` 中，它们存放在 `.install/root/` 下，并由 `install.sh` 复制。
- Unix 主机会保留文件权限。Windows 主机会给 `.sh`、`.dll`、`.exe` 和无扩展名文件分配 `0755`，其他文件使用 `0644`。

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
ExecStart=dotnet <install-path>/<host>.dll
```

如果提供 `--daemon-bind:<value>`，生成的服务会把它作为 `--urls` 传给应用。纯数字值会被当作本机 HTTP 端口：

```bash
--daemon-bind:8080
```

生成：

```ini
ExecStart=dotnet <install-path>/<host>.dll --urls http://127.0.0.1:8080
```

使用 `--daemon-environments:<names>` 可将指定命令变量或环境变量写入服务文件：

```bash
dotnet-pack deb \
  --name:MyApp \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --daemon-environments:ASPNETCORE_ENVIRONMENT \
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
dotnet-pack deb \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./bin/%compilation%/%framework%/publish
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

tar 包包含应用文件、`.install/` 下的可选生命周期脚本、`.install/root/` 下的可选根路径条目，以及可执行的 `install.sh`。

安装：

```bash
tar -xzf MyCompany.MyApp@1.0.0_linux-x64.tar.gz
sudo ./install.sh
```

安装到暂存目录：

```bash
DESTDIR=/tmp/stage ./install.sh
```

覆盖安装路径：

```bash
INSTALL_PATH=/srv/myapp sudo ./install.sh
```

卸载：

```bash
sudo ./install.sh uninstall
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
dpkg-deb --info ./packages/MyCompany.MyApp@1.0.0_linux-x64.deb
dpkg-deb --contents ./packages/MyCompany.MyApp@1.0.0_linux-x64.deb
sudo dpkg -i ./packages/MyCompany.MyApp@1.0.0_linux-x64.deb
```

`/etc/` 下的根路径条目也会写入 Debian `conffiles` 元数据。

### `.rpm`

RPM 包包含 RPM lead/signature/header 元数据，以及 gzip 压缩的 `newc` cpio 载荷。

检查并安装：

```bash
rpm -qip ./packages/MyCompany.MyApp@1.0.0_linux-x64.rpm
rpm -qlp ./packages/MyCompany.MyApp@1.0.0_linux-x64.rpm
rpm -qp --scripts ./packages/MyCompany.MyApp@1.0.0_linux-x64.rpm
sudo rpm -Uvh ./packages/MyCompany.MyApp@1.0.0_linux-x64.rpm
```

`/etc/` 下的根路径条目会被标记为 RPM 配置文件。

## 从源码构建

还原并构建：

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```

使用 Cake 构建：

```bash
dotnet cake --target=build --edition=Release
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
