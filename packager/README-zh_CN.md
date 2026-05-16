# Zongsoft 打包工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

README: [English](README.md) | [简体中文](README-zh_CN.md)

-----

## 概述

[**Z**ongsoft.**T**ools.**P**ackager](https://github.com/Zongsoft/tools/tree/main/packager) 是一个用于生成 Linux 应用安装包的 .NET 全局工具。

它可以将应用目录打包为以下格式：

- `.tar.gz`：带有内置 `install.sh` 安装脚本的 gzip 压缩 tar 包。
- `.deb`：包含 `control.tar.gz` 与 `data.tar.gz` 的 Debian 二进制安装包。
- `.rpm`：包含 RPM 元数据头与 gzip 压缩 `cpio` 载荷的 RPM 安装包。

本工具主要面向已发布的 .NET 服务和命令行应用。它可以自动包含 systemd 单元文件，生成安装/卸载生命周期脚本，保留文件权限模式，并支持从命令选项或环境变量解析变量引用。

## 安装

- 查看已安装工具：

```bash
dotnet tool list
dotnet tool list -g
```

- 首次安装：

```bash
dotnet tool install -g Zongsoft.Tools.Packager
```

- 升级更新：

```bash
dotnet tool update -g Zongsoft.Tools.Packager
```

- 卸载：

```bash
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

安装后命令名为：

```bash
dotnet-pack
```

## 基本用法

```bash
dotnet-pack tar <选项...> [打包项...]
dotnet-pack deb <选项...> [打包项...]
dotnet-pack rpm <选项...> [打包项...]
```

三个子命令共享同一组核心选项。`rpm` 命令额外支持 RPM 专用的软件包关系元数据选项。

### 最小示例

将当前目录打包为 `.tar.gz`：

```bash
dotnet-pack tar \
  --name:Zongsoft.Example \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0
```

将发布目录打包为 Debian 安装包：

```bash
dotnet-pack deb \
  --name:Zongsoft.Example \
  --title:"Zongsoft Example Service" \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./bin/Release/net10.0/publish \
  --output:./packages \
  --category:utils \
  --summary:"Example service" \
  --description:"A sample service packaged by Zongsoft.Tools.Packager."
```

生成带软件包关系的 RPM 安装包：

```bash
dotnet-pack rpm \
  --name:Zongsoft.Example \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./bin/Release/net10.0/publish \
  --output:./packages \
  --license:MIT \
  --dependencies:"dotnet-runtime-10.0 >= 10.0" \
  --provides:"zongsoft-example = 1.0.0" \
  --conflicts:"zongsoft-example-legacy"
```

## 打包项

打包项是最终写入安装包载荷的文件。

如果没有指定位置参数，工具会递归包含源目录下的所有文件：

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 --source:publish
```

如果指定了一个或多个打包项，则只包含这些路径：

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 \
  --source:publish \
  MyApp.dll appsettings.json wwwroot
```

每个打包项都可以在最后一个冒号后指定目标别名：

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 \
  --source:publish \
  appsettings.Production.json:appsettings.json \
  ../shared/logo.png:assets/logo.png
```

打包项规则：

- 相对路径以 `--source` 为基准解析。
- 允许指定 `--source` 之外的绝对路径；若未指定别名，则使用该文件名。
- 若别名以 `/` 开头，则该条目安装到 Linux 文件系统根路径；例如 `nginx.conf:/etc/nginx/conf.d/app.conf`。
- 目录会被递归包含。
- 文件通配符支持在最后一级路径中使用 `*` 与 `?`。
- 重复的目标路径会被报告为冲突并跳过。
- 在 Unix 类系统上会保留文件权限模式。在 Windows 上，`.sh`、`.dll`、`.exe` 以及无扩展名文件默认使用 `0755`，其他文件默认使用 `0644`。

对 `.deb` 和 `.rpm` 而言，打包项会写入安装路径之下。对 `.tar.gz` 而言，打包项写入归档根目录，安装时由 `install.sh` 复制到安装路径。

## 命令选项

### 必需选项

| 选项 | 说明 |
| --- | --- |
| `--name:<name>` | 应用/软件包名称，同时用于推断默认安装路径与服务名称。 |
| `--version:<version>` | 软件包版本号，`0.0.0.0` 会被拒绝。 |
| `--platform:<platform>` | 目标平台，支持 `linux`、`unix`、`osx`、`windows`/`win`、`unknown` 等；Linux 安装包应使用 `linux`。 |
| `--framework:<tfm>` | .NET 目标框架标识，例如 `net8.0`、`net9.0` 或 `net10.0`。 |

### 通用选项

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source:<path>` | 当前目录 | 待打包的源目录。 |
| `--output:<path>` | 源目录 | 生成安装包的输出目录。相对路径会以 `--source` 为基准解析。 |
| `--edition:<name>` | 空 | 可选版本/发行标识。它会追加到软件包名中；对 RPM 而言，有值时还会作为 `Release`。 |
| `--compilation:<name>` | `Release` | 在 `bin/<configuration>/<framework>` 中定位守护进程宿主文件时使用的编译配置。 |
| `--architecture:<arch>` | `x64` | 目标 CPU 架构，常用值为 `x64`、`x86`、`arm64`、`arm`。 |
| `--overwrite` | `false` | 覆盖已经存在的输出安装包。不指定该开关时，若输出文件已存在则创建失败。 |
| `--install-path:<path>` | `/opt/<vendor>/<package>` 或 `/opt/<package>` | 安装目录。若名称包含点号，则第一个点号前的文本会作为厂商目录。 |
| `--title:<text>` | 空 | 人类可读的软件包标题，也会用于生成 systemd 描述。 |
| `--summary:<text-or-file>` | 空 | 简短摘要。若该值指向已存在文件，则使用文件内容。 |
| `--description:<text-or-file>` | 空 | 详细描述。若该值指向已存在文件，则使用文件内容。 |
| `--url:<url>` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license:<text>` | 空 | 许可证表达式或许可证名称。 |
| `--category:<text>` | 依格式而定 | Debian 的 `Section` 或 RPM 的 `Group`。Debian 默认 `utils`，RPM 默认 `Applications/System`。 |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | 软件包维护者/厂商文本。 |
| `--dependencies:<list>` | 空 | 以逗号或分号分隔的依赖列表。会写入 Debian `Depends` 或 RPM `Requires`。 |

### systemd 与生命周期脚本选项

当前打包器对所有格式均使用面向 systemd 的脚本生成器。

| 选项 | 说明 |
| --- | --- |
| `--daemon:<name>` | systemd 单元文件名或标识。若省略，则使用小写的软件包名。缺失 `.service` 后缀时会自动补上。 |
| `--daemon-type:<type>` | 使用 `web` 时会生成带 `--urls <bind>` 的 `ExecStart`；其他值生成普通服务。 |
| `--daemon-bind:<urls>` | 传给生成的 Web 服务的 URL 绑定，例如 `http://0.0.0.0:8080`。 |
| `--environments:<names>` | 以逗号或分号分隔的环境变量名列表，这些变量会写入生成的服务文件。 |
| `--script-installing:<path>` | 安装前执行的源目录相对 shell 脚本文件。 |
| `--script-installed:<path>` | 安装后执行的源目录相对 shell 脚本文件。 |
| `--script-uninstalling:<path>` | 卸载前执行的源目录相对 shell 脚本文件。 |
| `--script-uninstalled:<path>` | 卸载后执行的源目录相对 shell 脚本文件。 |
| `--preinstalling:<paths>` / `--postinstalling:<paths>` | 在安装前主体脚本之前/之后追加执行的脚本文件列表。 |
| `--preinstalled:<paths>` / `--postinstalled:<paths>` | 在安装后主体脚本之前/之后追加执行的脚本文件列表。 |
| `--preuninstalling:<paths>` / `--postuninstalling:<paths>` | 在卸载前主体脚本之前/之后追加执行的脚本文件列表。 |
| `--preuninstalled:<paths>` / `--postuninstalled:<paths>` | 在卸载后主体脚本之前/之后追加执行的脚本文件列表。 |

如果未提供生命周期脚本，工具会生成默认脚本：安装/卸载前停止服务，创建或更新 `/etc/systemd/system/<service>`，重载 systemd，并启用服务。
追加脚本选项不会替换默认脚本，多个脚本路径可用分号 `;` 或管道符 `|` 分隔。

### RPM 专用选项

| 选项 | 说明 |
| --- | --- |
| `--provides:<list>` | 以逗号或分号分隔的 RPM `Provides` 条目。条目可使用 `name`、`name = version`、`name >= version` 或 `name(>= version)` 形式。 |
| `--conflicts:<list>` | 以逗号或分号分隔的 RPM `Conflicts` 条目，关系语法同上。 |

## 变量

选项值与打包项参数可以使用以下两种变量引用形式：

```text
$(name)
%name%
```

变量名不区分大小写，加载来源依次为：

1. 环境变量。
2. 已声明命令选项及其默认值。
3. 命令行解析器接受的额外选项。

同名变量以后加载的值为准，因此可以编写可复用的打包命令：

```bash
dotnet-pack deb \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
  --platform:linux \
  --framework:net10.0 \
  --source:./bin/%compilation%/%framework%/publish
```

常用变量如下：

| 变量 | 含义 |
| --- | --- |
| `name` | 软件包/应用名称。 |
| `version` | 软件包版本。 |
| `edition` | 可选版本/发行标识。 |
| `platform` | 目标平台。 |
| `architecture` | 目标架构。 |
| `framework` | .NET 目标框架。 |
| `compilation` | 编译配置。 |
| `source` | 规范化后的源目录。 |
| `output` | 规范化后的输出目录。 |
| `RuntimeIdentifier` | 根据平台与架构推断的运行时标识。 |

## systemd 服务生成

创建安装包时，工具会确保有一个 systemd 单元文件进入打包项。

服务文件解析顺序：

1. 如果 `--source` 下存在 `--daemon` 指定的文件，则直接使用该文件。
2. 否则生成临时 `.service` 文件。

生成服务时，工具按以下顺序定位 .NET 宿主文件：

1. `<source>/<name>.dll`
2. `<source>/bin/<compilation>/<framework>/<name>.dll`
3. `<source>` 下唯一的 `.exe` 文件；服务会启动同名 `.dll`。
4. `<source>/bin/<compilation>/<framework>` 下唯一的 `.exe` 文件；服务会启动同名 `.dll`。

普通服务会生成：

```ini
ExecStart=dotnet <install-path>/<host>.dll
```

Web 服务会生成：

```ini
ExecStart=dotnet <install-path>/<host>.dll --urls <daemon-bind>
```

## 输出命名

生成的安装包文件名规则如下：

```text
<name>@<version>_<runtime>.<extension>
<name>-<edition>@<version>_<runtime>.<extension>
```

示例：

```text
Zongsoft.Example@1.0.0_linux-x64.tar.gz
Zongsoft.Example@1.0.0_linux-x64.deb
Zongsoft.Example-enterprise@1.0.0_linux-x64.rpm
```

软件包元数据名称为 `<name>` 或 `<name>-<edition>`。

## 格式说明

### `.tar.gz`

tar 安装包包含：

- 应用文件。
- `.install/` 下的可选生命周期脚本。
- 支持安装与卸载流程的 `install.sh`。

安装：

```bash
tar -xzf Zongsoft.Example@1.0.0_linux-x64.tar.gz
sudo ./install.sh
```

安装到暂存目录：

```bash
DESTDIR=/tmp/stage ./install.sh
```

卸载：

```bash
sudo ./install.sh uninstall
```

### `.deb`

Debian 安装包由工具直接写入，包含：

```text
debian-binary
control.tar.gz
data.tar.gz
```

控制归档中包含 `control` 以及可选维护脚本：

```text
preinst
postinst
prerm
postrm
```

安装和检查：

```bash
sudo dpkg -i Zongsoft.Example@1.0.0_linux-x64.deb
dpkg-deb --info Zongsoft.Example@1.0.0_linux-x64.deb
dpkg-deb --contents Zongsoft.Example@1.0.0_linux-x64.deb
```

### `.rpm`

RPM 安装包由工具直接写入。它的载荷是 gzip 压缩的 `cpio` 归档，元数据头中包含软件包名、版本、发行号、脚本、依赖关系、文件名、文件模式、文件摘要与安装前缀。

安装和检查：

```bash
sudo rpm -Uvh Zongsoft.Example@1.0.0_linux-x64.rpm
rpm -qip Zongsoft.Example@1.0.0_linux-x64.rpm
rpm -qlp Zongsoft.Example@1.0.0_linux-x64.rpm
```

## 推荐打包流程

1. 发布应用：

```bash
dotnet publish ./src/MyApp/MyApp.csproj -c Release -f net10.0 -o ./publish
```

2. 生成安装包：

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
  --daemon:myapp.service \
  --daemon-type:web \
  --daemon-bind:http://0.0.0.0:5000 \
  --environments:ASPNETCORE_ENVIRONMENT \
  --ASPNETCORE_ENVIRONMENT:Production
```

3. 分发前检查安装包：

```bash
dpkg-deb --info ./publish/packages/MyCompany.MyApp@1.0.0_linux-x64.deb
dpkg-deb --contents ./publish/packages/MyCompany.MyApp@1.0.0_linux-x64.deb
```

## 从源码构建

还原并构建：

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```

使用 Cake：

```bash
dotnet cake --target=build --edition=Release
```

## 故障排查

- `The source directory '<path>' does not exist.`
  `--source` 经过变量展开和路径规范化后不存在。

- `The daemon host location failed.`
  未找到已有服务文件，并且工具无法定位宿主 `.dll`，也无法通过唯一 `.exe` 推断宿主名。

- `The version number is invalid.`
  版本值为空、非法，或解析为 `0.0.0.0`。

- `The source path '<path>' does not exist.`
  某个打包项参数没有匹配到文件、目录或通配符。

- 安装包已经存在。
  重新执行时指定 `--overwrite`，或选择其他 `--output` 目录。

## 更多细节

内部设计、生成流程和安装包格式细节请参考 [implementation.md](implementation.md)。

## 许可

本项目采用 [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) 许可协议。
