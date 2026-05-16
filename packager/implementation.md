# Zongsoft.Tools.Packager Implementation

本文档说明 `Zongsoft.Tools.Packager` 的设计、核心实现、打包流水线，以及 `.deb`、`.tar.gz`、`.rpm` 三种安装包格式的相关规范和当前实现方式。

## 设计目标

`Zongsoft.Tools.Packager` 的目标是用纯 .NET 代码生成可在 Linux 系统上安装的应用包，减少对 `dpkg-deb`、`rpmbuild`、`tar`、`cpio` 等外部命令的运行时依赖。

核心目标包括：

- 提供统一命令入口 `dotnet-pack`，通过 `tar`、`deb`、`rpm` 子命令选择输出格式。
- 复用同一套应用元数据、变量解析、文件收集、服务脚本生成逻辑。
- 为 systemd 服务型 .NET 应用提供默认安装/卸载脚本。
- 对 `.deb`、`.tar.gz`、`.rpm` 使用格式原语直接写入文件。
- 保持跨平台开发可用：在 Windows 上也可生成 Linux 包，并为文件模式提供保守默认值。

## 模块结构

主要源文件如下：

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 初始化命令树，注册 `TarCommand`、`DebCommand`、`RpmCommand`。 |
| `PackCommand.cs` | 三种打包命令的公共执行模板，声明通用命令选项。 |
| `PackCommand.Tar.cs` | 创建 `Package.Tar`。 |
| `PackCommand.Deb.cs` | 创建 `Package.Deb`。 |
| `PackCommand.Rpm.cs` | 创建 `Package.Rpm`，解析 `provides`、`conflicts`。 |
| `Package.cs` | 定义包模型、包条目模型、文件收集和别名处理。 |
| `Package.Tar.cs` | 定义 `.tar.gz` 包的默认安装路径、文件名和打包入口。 |
| `Package.Deb.cs` | 定义 `.deb` 包的默认安装路径、文件名和打包入口。 |
| `Package.Rpm.cs` | 定义 `.rpm` 包的默认安装路径、文件名、RPM 专用属性和打包入口。 |
| `Generator.Tar.cs` | 写入 gzip tar 包和 `install.sh`。 |
| `Generator.Deb.cs` | 写入 Debian `ar` 包、control tarball 和 data tarball。 |
| `Generator.Rpm.cs` | 写入 RPM lead、signature、header 和 gzip cpio payload。 |
| `Scriptor.Systemd.cs` | 生成或收集 systemd 单元文件与生命周期脚本。 |
| `Normalizer.cs` | 变量解析、文本规范化、文件内容读取。 |
| `Variables.cs` | 命令变量集合和常用变量访问器。 |
| `Utility.cs` | Runtime Identifier、路径、Unix 时间戳、文件模式等工具方法。 |
| `Dumper.cs` | 统一输出错误和警告消息。 |

## 命令模型

入口命令由 `Program.Main()` 初始化：

```text
dotnet-pack
├── tar
├── deb
└── rpm
```

`PackCommand<TPackage>` 是模板方法基类。它负责：

1. 校验版本号。
2. 初始化变量集合。
3. 规范化 `source` 和 `output` 路径。
4. 创建目标 `Package` 实例。
5. 生成安装脚本和 systemd 单元文件。
6. 加载打包项。
7. 调用对应格式的 `Pack()` 方法写入输出文件。

子类只负责创建具体包对象：

```csharp
protected override Package.Deb CreatePackage(CommandContext context)
```

`RpmCommand` 额外解析：

- `--provides`
- `--conflicts`

这两个值以逗号或分号分隔，最终写入 RPM metadata header。

## 变量与规范化

### 变量来源

变量由 `PackCommand<TPackage>.GetVariables()` 提供，来源顺序为：

1. 当前进程环境变量。
2. 命令描述符中声明的选项及默认值。
3. 命令行中出现但未在描述符中声明的额外选项。
4. 代码显式追加的变量。

变量名不区分大小写。后写入的同名变量会覆盖先写入的值。

### 变量语法

`Normalizer` 支持两种变量引用语法：

```text
$(name)
%name%
```

例如：

```bash
dotnet-pack deb \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
  --source:./bin/%compilation%/%framework%/publish
```

### 文本与文件

`summary`、`description` 和脚本相关变量会经过 `NormalizeFile()` 处理：

- 如果值为空，结果为空。
- 先进行变量展开。
- 如果展开后的值是一个存在的文件路径，则读取文件内容。
- 否则保留展开后的文本。

当前 `Scriptor.Systemd` 还会把脚本变量当作源目录相对路径再次读取，因此脚本选项推荐传入源目录下的相对脚本路径。

## 包模型

### Package

`Package` 抽象类持有所有格式共享的元数据：

- `Name`
- `PackageName`
- `Edition`
- `Version`
- `Platform`
- `Architecture`
- `Runtime`
- `Framework`
- `Title`
- `Summary`
- `Description`
- `Maintainer`
- `License`
- `Url`
- `Category`
- `InstallPath`
- `Dependencies`
- `Entries`
- `Scripts`

`PackageName` 的规则：

```text
name
name-edition
```

输出文件名的规则：

```text
name@version_runtime.ext
name-edition@version_runtime.ext
```

例如：

```text
Zongsoft.Example@1.0.0_linux-x64.deb
Zongsoft.Example-enterprise@1.0.0_linux-x64.rpm
```

### Runtime Identifier

`Utility.GetRuntimeIdentifier()` 根据平台和架构生成运行时标识：

```text
linux + x64   => linux-x64
linux + arm64 => linux-arm64
windows + x64 => win-x64
windows       => win
```

`Platform.Windows` 还声明了 `Win` 别名。

### 默认安装路径

`Utility.Unix.GetInstallPath(name)` 生成默认安装路径：

```text
Zongsoft.Example => /opt/zongsoft/zongsoft.example
MyApp            => /opt/myapp
```

规则是：

- 名称为空时返回 `/opt`。
- 名称统一转为小写。
- 如果名称包含点号，则点号前的部分作为 vendor 目录。
- 否则直接安装到 `/opt/<name>`。

## 打包项加载

打包项由 `Package.EntryCollection` 负责加载。

### 默认加载

如果没有传入位置参数：

```text
递归收集 source 下所有文件
entryName = 文件相对 source 的路径
```

### 显式加载

如果传入位置参数，每个参数格式为：

```text
path
path:alias
```

解析规则：

- 以最后一个冒号分隔路径和别名。
- Windows 盘符路径中的 `C:` 不作为别名分隔符。
- `path` 可为相对路径、绝对路径、文件、目录或含通配符路径。
- 相对路径以 `source` 为基准。
- 目录会递归展开。
- 通配符支持 `*` 和 `?`，当前实现只在最后一级名称上调用 `Directory.GetFiles(working, pattern)` 与 `Directory.GetDirectories(working, pattern)`。
- 如果路径位于 `source` 外部且没有指定别名，则别名为空，最终只保留文件名。

### 目标路径

最终 `entryName` 会经过：

```text
entryName = NormalizePath(prefix + alias)
```

其中：

- `.deb` 和 `.rpm` 的 `prefix` 是安装路径去掉开头 `/` 后的相对路径，例如 `opt/zongsoft/zongsoft.example`。
- `.tar.gz` 的 `prefix` 为 `null`，文件保持在归档根目录下的相对路径。

### 文件模式

Unix 主机：

```text
File.GetUnixFileMode(path) & rwx mask
```

Windows 主机：

- `.sh`、`.dll`、`.exe`、无扩展名文件使用 `0755`。
- 其他文件使用 `0644`。

## systemd 脚本生成

所有包类型当前都使用 `Scriptor.Systemd`。

### 服务文件选择

`daemon` 变量为空时，默认使用小写包名。生成服务文件时会补齐 `.service` 后缀。

流程：

1. 在 `source` 下查找同名服务文件。
2. 如果存在，将该文件加入打包项。
3. 如果不存在，则尝试生成临时服务文件。

### 宿主定位

生成服务文件时需要定位 .NET 宿主。查找顺序为：

1. `<source>/<name>.dll`
2. `<source>/bin/<compilation>/<framework>/<name>.dll`
3. `<source>` 下唯一 `.exe`，并推断同名 `.dll`
4. `<source>/bin/<compilation>/<framework>` 下唯一 `.exe`，并推断同名 `.dll`

找不到宿主时输出：

```text
The daemon host location failed.
```

### 生成的服务内容

普通服务：

```ini
[Unit]
Description=<title-or-name>

[Service]
Type=simple
WorkingDirectory=<install-path>
ExecStartPre=mkdir -p <install-path>/logs
ExecStart=dotnet <install-path>/<host>
Restart=on-failure
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=<name>
DynamicUser=no
PrivateTmp=no
ReadWritePaths=<install-path> <install-path>/logs /tmp

Environment=DOTNET_NOLOGO=true
<custom-environment-lines>

[Install]
WantedBy=multi-user.target
```

Web 服务：

```ini
ExecStart=dotnet <install-path>/<host> --urls <daemon-bind>
```

### 生命周期脚本

脚本模型：

| Package field | Debian file | RPM tag | Tar path |
| --- | --- | --- | --- |
| `Installing` | `preinst` | pre-install script | `.install/installing.sh` |
| `Installed` | `postinst` | post-install script | `.install/installed.sh` |
| `Uninstalling` | `prerm` | pre-uninstall script | `.install/uninstalling.sh` |
| `Uninstalled` | `postrm` | post-uninstall script | `.install/uninstalled.sh` |

未指定脚本时，默认脚本会：

- 安装前停止同名 systemd 服务。
- 安装后创建 `/etc/systemd/system/<service>` 符号链接。
- 安装后执行 `systemctl daemon-reload` 和 `systemctl enable`。
- 卸载前停止服务。
- 卸载后删除服务符号链接、重载 systemd，并删除安装目录。

## `.tar.gz` 实现

实现文件：`Generator.Tar.cs`

### 格式规范概要

tar 是顺序归档格式，每个成员包含头部、文件名、权限、时间戳和数据块。gzip 是压缩层，`.tar.gz` 等价于：

```text
gzip(tar archive)
```

当前实现使用 .NET `System.Formats.Tar`：

```csharp
new TarWriter(gzip, TarEntryFormat.Pax, false)
```

也就是说，归档条目使用 PAX tar 格式。PAX 是 POSIX tar 扩展格式，可表达比传统 ustar 更长的路径和更丰富的元数据。

### 当前包结构

```text
<application files>
.install/installing.sh
.install/installed.sh
.install/uninstalling.sh
.install/uninstalled.sh
install.sh
```

其中 `.install/*` 只有在对应脚本不为空时才写入。

### 文件条目

每个应用文件写入为：

```text
TarEntryType.RegularFile
name = entry.EntryName
mode = entry.Mode
mtime = entry.ModifiedTime
data = File.OpenRead(entry.Source)
```

### install.sh

`install.sh` 是 tar 包的自包含安装器，权限为 `0755`。

它支持：

- 默认安装。
- `install.sh uninstall` 卸载。
- `INSTALL_PATH` 覆盖安装路径。
- `DESTDIR` 暂存安装。
- 调用 `.install/` 下的生命周期脚本。

安装流程：

```text
SOURCE_DIR = install.sh 所在目录
INSTALL_PATH = 环境变量或包默认安装路径
DESTDIR = 可选暂存目录
TARGET = DESTDIR + INSTALL_PATH
执行 installing.sh
创建 TARGET
复制归档中除 install.sh 与 .install 之外的文件到 TARGET
执行 installed.sh
```

卸载流程：

```text
执行 uninstalling.sh
rm -rf TARGET
执行 uninstalled.sh
```

### 适用场景

`.tar.gz` 适合：

- 没有系统包管理器的部署环境。
- 容器镜像构建过程中的文件交付。
- 需要 `DESTDIR` 暂存安装的场景。
- 需要人工检查、解压、复制的轻量分发场景。

## `.deb` 实现

实现文件：`Generator.Deb.cs`

### Debian 二进制包规范概要

Debian 二进制包是一个 Unix `ar` 归档，现代格式版本为 `2.0`。其成员通常包括：

```text
debian-binary
control.tar.*
data.tar.*
```

其中：

- `debian-binary` 是文本文件，内容为格式版本。
- `control.tar.*` 包含控制文件和维护者脚本。
- `data.tar.*` 包含安装到文件系统的实际文件。

当前实现固定使用 gzip 压缩：

```text
debian-binary
control.tar.gz
data.tar.gz
```

### ar 容器

`.deb` 输出以全局头开始：

```text
!<arch>\n
```

每个 ar 成员头为 60 字节 ASCII 字段，当前写入：

```text
name/ timestamp uid gid mode size `\n
```

当前实现：

- `uid = 0`
- `gid = 0`
- `mode = 100644`
- 奇数字节长度的成员后补一个换行字节，使下一个成员按偶数边界开始。

### debian-binary

当前写入：

```text
2.0\n
```

### control.tar.gz

`control.tar.gz` 使用 PAX tar + gzip，包含：

```text
control
preinst
postinst
prerm
postrm
```

脚本文件只有在内容不为空时才写入，权限为 `0755`，且会自动加：

```sh
#!/bin/sh
set -e
```

`control` 权限为 `0644`。

### control 字段

当前生成的 control 字段：

```text
Package: <package-name>
Version: <version>
Section: <category-or-utils>
Priority: optional
Architecture: <debian-architecture>
Installed-Size: <payload-size-in-KiB>
Maintainer: <maintainer>
Homepage: <url>
License: <license>
Depends: <dependencies>
Description: <summary-or-title-or-name>
 <long-description-line>
 .
 <long-description-line>
```

说明：

- `Depends` 仅在依赖非空时写入。
- `Description` 第一行是短描述。
- 长描述每行前置一个空格。
- 空行写为 ` .`，符合 Debian control 多行字段的常见写法。
- `License` 不是 Debian control 的标准必需字段，当前实现作为额外字段写入。

### Debian 架构映射

| .NET Architecture | Debian Architecture |
| --- | --- |
| `X64` | `amd64` |
| `X86` | `i386` |
| `Arm64` | `arm64` |
| `Arm` | `armhf` |
| 其他 | `all` |

### data.tar.gz

`data.tar.gz` 使用 PAX tar + gzip，写入 `Package.Entries` 中的文件。

对 `.deb` 而言，`EntryPrefix` 是去掉开头 `/` 的安装路径。因此默认情况下：

```text
InstallPath = /opt/zongsoft/zongsoft.example
EntryName   = opt/zongsoft/zongsoft.example/MyApp.dll
```

Debian 安装器解包时会把该路径安装到系统根目录下：

```text
/opt/zongsoft/zongsoft.example/MyApp.dll
```

## `.rpm` 实现

实现文件：`Generator.Rpm.cs`

### RPM 包规范概要

RPM 包文件由四个逻辑部分组成：

```text
Lead
Signature
Header
Payload
```

传统 RPM v3 文件仍包含 96 字节 lead。现代 RPM 主要依赖 signature 和 header。payload 通常是压缩后的 `cpio` 归档。

当前实现写入：

```text
lead
signature header
metadata header
gzip(newc cpio payload)
```

当前实现不生成 GPG/PGP 签名，仅写入包体大小和 MD5 digest 相关 signature tag，用于基本完整性元数据。

### Lead

lead 固定 96 字节，当前写入：

| 偏移 | 内容 |
| --- | --- |
| `0..3` | RPM magic：`ed ab ee db` |
| `4` | major version：`3` |
| `5` | minor version：`0` |
| `6..7` | package type：binary package |
| `8..9` | architecture number |
| `10..75` | `<package-name>-<version>`，最长 66 字节 |
| `76..77` | OS number：Linux |
| `78..79` | signature type：header style signature |

架构号当前映射：

| .NET Architecture | RPM lead architecture number |
| --- | --- |
| `X64` | `1` |
| `X86` | `1` |
| `Arm64` | `12` |
| `Arm` | `12` |
| 其他 | `255` |

### Signature

signature section 使用 RPM header 相同的索引/存储区结构。当前包含：

| Tag | 说明 |
| --- | --- |
| `257` | Header + payload 的字节长度。 |
| `261` | Header + payload 的 MD5 digest。 |

signature section 末尾按 8 字节对齐。

### Header 基本结构

RPM header 结构：

```text
magic/version/reserved
index count
store size
index entries
store bytes
padding to 8 bytes
```

每个 index entry 为：

```text
tag    int32 big-endian
type   int32 big-endian
offset int32 big-endian
count  int32 big-endian
```

当前实现支持的写入类型：

| Type | 含义 |
| --- | --- |
| `3` | int16 array |
| `4` | int32 array |
| `6` | string |
| `7` | binary |
| `8` | string array |
| `9` | international string |

数值按 big-endian 写入，字符串以 UTF-8 加 `NUL` 结尾。

### Metadata Header

当前 metadata header 写入内容包括：

- 包名、版本、release。
- 摘要、描述、构建时间、构建主机。
- 包大小、许可证、维护者、分类、URL。
- 操作系统和架构。
- pre/post install、pre/post uninstall 脚本。
- 文件大小、模式、mtime、摘要、用户名、组名。
- payload 格式、压缩器和压缩级别。
- dependency/provide/conflict 元数据。
- dirname/basename/dirindex 三组文件路径表。

当前常用 tag 包括：

| Tag | 当前写入内容 |
| --- | --- |
| `1000` | 包名。 |
| `1001` | 版本。 |
| `1002` | release。未指定 edition 时为 `1`，否则为 edition。 |
| `1004` | 摘要。 |
| `1005` | 描述。 |
| `1006` | 构建时间。 |
| `1007` | 构建主机。 |
| `1009` | 安装大小。 |
| `1014` | 许可证。 |
| `1015` | 维护者/打包者。 |
| `1016` | 分组。 |
| `1020` | URL。 |
| `1021` | OS，固定为 `linux`。 |
| `1022` | RPM 架构。 |
| `1023..1026` | 安装/卸载脚本。 |
| `1048..1050` | Requires flags/name/version。 |
| `1047`, `1112`, `1113` | Provides name/flags/version。 |
| `1053..1055` | Conflicts flags/name/version。 |
| `1116..1118` | 文件目录索引、文件基本名、目录名。 |
| `1124` | Payload format，固定 `cpio`。 |
| `1125` | Payload compressor，固定 `gzip`。 |
| `1126` | Payload flags，固定 `9`。 |

### RPM 架构映射

| .NET Architecture | RPM Architecture |
| --- | --- |
| `X64` | `x86_64` |
| `X86` | `i386` |
| `Arm64` | `aarch64` |
| `Arm` | `armv7hl` |
| 其他 | `noarch` |

### Dependencies、Provides、Conflicts

依赖关系语法支持两类形式：

```text
name
name = version
name >= version
name <= version
name > version
name < version
name(= version)
name(>= version)
```

关系标志映射：

| 操作符 | Flags |
| --- | --- |
| `<` | `RPM_SENSE_LESS` |
| `>` | `RPM_SENSE_GREATER` |
| `=` | `RPM_SENSE_EQUAL` |
| `<=` | `LESS | EQUAL` |
| `>=` | `GREATER | EQUAL` |

默认 Requires 会自动加入 RPM 自身能力约束：

```text
rpmlib(CompressedFileNames) <= 3.0.4-1
rpmlib(PayloadFilesHavePrefix) <= 4.0-1
rpmlib(PayloadIsGzip) <= 5.4.0-1
```

默认 Provides 会加入：

```text
<package-name> = <version>-<release>
```

### Payload

payload 是 gzip 压缩后的 ASCII `cpio` newc 归档。newc header magic 为：

```text
070701
```

生成流程：

1. 收集文件所需目录，至少包含 `/`。
2. 为目录写入 cpio 条目，模式为 `0040755`。
3. 为文件写入 cpio 条目，路径为 `.` + rpm 绝对路径，例如 `./opt/myapp/app.dll`。
4. 文件模式为 `0100000 | entry.Mode`。
5. 写入 `TRAILER!!!` 结束条目。
6. 原始 cpio 数据补齐到 512 字节边界。
7. 使用 gzip 压缩。

当前 payload 中的 cpio header 包含文件名、模式、mtime、大小和文件内容；RPM header 同时写入一份用于包管理器查询和校验的文件元数据。

## 格式对比

| 特性 | `.tar.gz` | `.deb` | `.rpm` |
| --- | --- | --- | --- |
| 外层容器 | gzip tar | Unix ar | RPM lead/signature/header |
| 文件载荷 | PAX tar | `data.tar.gz` | gzip newc cpio |
| 控制元数据 | `install.sh` 与脚本文件 | `control.tar.gz` | RPM metadata header |
| 生命周期脚本 | `.install/*.sh` | `preinst/postinst/prerm/postrm` | header script tags |
| 包管理器安装 | 否 | `dpkg`/`apt` | `rpm`/`dnf`/`yum` |
| 默认安装路径处理 | `install.sh` 复制到目标目录 | payload 内含安装路径 | payload 与 header 内含安装路径 |
| 签名 | 无 | 无 | 无 GPG/PGP，仅基础 digest 元数据 |

## 当前实现边界

以下内容是当前实现的边界，后续可以按需要增强：

- `.deb` 固定使用 `control.tar.gz` 和 `data.tar.gz`，未提供 xz/zstd 压缩选项。
- `.deb` control 字段只覆盖最常用字段，未实现 `Conffiles`、`Recommends`、`Suggests`、`Breaks` 等扩展字段。
- `.rpm` 由代码直接写入，未调用 `rpmbuild`，因此未生成 spec 文件，也未集成 GPG 签名。
- `.rpm` payload 固定为 gzip cpio，未提供 xz/zstd/zstd payload 选项。
- 文件所有者和组当前固定为 `root/root` 或归档默认值，没有命令选项可配置。
- 目录模式固定策略较简单，RPM 目录为 `0755`。
- glob 只处理最后一级路径模式，不实现 `**` 多级通配。
- systemd 是当前唯一脚本生成策略，尚未抽象其他 init 系统。
- 生命周期脚本推荐使用源目录相对路径；绝对路径脚本和文本脚本场景需要进一步整理 `NormalizeFile()` 与 `Scriptor.Systemd.ReadFile()` 的职责。

## 验证建议

生成包后建议在目标发行版或容器中执行以下检查。

### tar.gz

```bash
tar -tzf package.tar.gz
tar -xzf package.tar.gz -C /tmp/package-test
DESTDIR=/tmp/stage /tmp/package-test/install.sh
find /tmp/stage -maxdepth 5 -type f | sort
```

### deb

```bash
ar t package.deb
dpkg-deb --info package.deb
dpkg-deb --contents package.deb
dpkg-deb --control package.deb /tmp/control
```

可进一步在 Debian/Ubuntu 容器中安装：

```bash
sudo dpkg -i package.deb
systemctl status <service>
sudo dpkg -r <package-name>
```

### rpm

```bash
rpm -qip package.rpm
rpm -qlp package.rpm
rpm -qp --scripts package.rpm
rpm2cpio package.rpm | cpio -t
```

可进一步在 Fedora/RHEL/openSUSE 容器或虚拟机中安装：

```bash
sudo rpm -Uvh package.rpm
systemctl status <service>
sudo rpm -e <package-name>
```

## 参考资料

- Debian Policy Manual: [Binary packages](https://www.debian.org/doc/debian-policy/ch-binary.html)
- Debian Policy Manual: [Binary package format appendix](https://www.debian.org/doc/debian-policy/ap-pkg-binarypkg.html)
- Debian Handbook: [Packaging System](https://www.debian.org/doc/manuals/debian-handbook/packaging-system.en.html)
- rpm.org: [RPM Package format](https://rpm.org/docs/4.19.x/manual/format.html)
- Linux Standard Base: [RPM Package File Format](https://refspecs.linuxfoundation.org/LSB_3.1.1/LSB-Core-generic/LSB-Core-generic/pkgformat.html)
- GNU tar manual: [GNU tar](https://www.gnu.org/software/tar/manual/)
