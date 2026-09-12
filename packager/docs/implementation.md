# Zongsoft.Tools.Packager 实现说明

本文档面向维护者，说明 `Zongsoft.Tools.Packager` 的源码结构、命令执行流水线、包模型、文件收集规则、systemd 脚本生成，以及 `.tar.gz`、`.deb`、`.rpm` 三种包格式的当前实现方式。

本文的应用示例引用 hosting 中真实的 [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) 宿主，暂存目录、Bash 工作目录和版本约定见 [README 快速开始](../README.zh-Hans.md#快速开始)。宿主 DLL 为 `Zongsoft.Hosting.Web.dll`，`--daemon:zongsoft.web` 指定包和服务标识；根路径配置示例引用 hosting 的 `.deploy/default/nginx/zongsoft.web.conf`。

## 设计目标

`Zongsoft.Tools.Packager` 的目标是使用纯 .NET 代码生成 Linux 应用安装包，尽量减少对目标系统工具链的打包期依赖。

核心设计取向：

- 对外提供统一入口 `dotnet-pack`，通过 `tar`、`deb`、`rpm` 子命令选择输出格式。
- 在三种格式之间复用应用元数据、变量解析、文件收集、脚本生成和命名规则。
- 默认服务模型面向 systemd，适合 .NET 后台服务和 Web 服务。
- 直接写入包格式原语，而不是调用 `tar`、`dpkg-deb`、`rpmbuild`、`cpio`。
- 支持在 Windows、Linux、macOS 上生成 Linux 包，并对文件权限做平台兼容处理。

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 初始化终端命令树，注册 `TarCommand`、`DebCommand`、`RpmCommand`。 |
| `PackCommand.Version.cs` | 嵌套 `VersionFile` 负责源版本读取、身份校验、Edition 选择和成功后保存。 |
| `PackCommand.cs` | 三种打包命令的模板方法基类，声明通用命令选项并编排执行流程。 |
| `MigrationProfile.cs` / `MigrationLoader.cs` | 使用 Zongsoft.Core Profile 读取 INI、展开变量、查找参数、排序并计算 SQL 校验和，预处理客户端批次分隔符，SQL 语法由目标数据库校验。 |
| `MigrationBundle.cs` | 按目标 RID 收集完整独立运行器目录，写入计划、SQL、执行入口和启动检查标识。 |
| `../.shared/` / `../migrator/src/` | 两端链接编译的纯升迁协议；独立运行器包含嵌套数据库/S3 升迁器、状态与锁及执行入口。 |
| `PackCommand.Tar.cs` | 创建 `Package.Tar`。 |
| `PackCommand.Deb.cs` | 创建 `Package.Deb`。 |
| `PackCommand.Rpm.cs` | 创建 `Package.Rpm`，解析 RPM 专用的 `provides`、`conflicts`。 |
| `Package.cs` | 包模型、安装脚本模型、包条目模型和文件收集逻辑。 |
| `Package.Tar.cs` | `.tar.gz` 包类型：默认安装路径、文件名、入口方法。 |
| `Package.Deb.cs` | `.deb` 包类型：默认安装路径、文件名、入口方法。 |
| `Package.Rpm.cs` | `.rpm` 包类型：默认安装路径、文件名、RPM 专用属性。 |
| `Generator.cs` | 从当前程序集计算生成工具身份，由三种格式的元数据写入共享。 |
| `Generator.Tar.cs` | 写入 gzip PAX tar、`install.sh`、`uninstall.sh`。 |
| `Generator.Deb.cs` | 写入 Debian `ar` 容器、`control.tar.gz`、`data.tar.gz`。 |
| `Generator.Rpm.cs` | 写入 RPM lead、signature/header、metadata header、gzip cpio payload。 |
| `Scriptor.Systemd.cs` | 生成或收集 systemd 单元文件，生成安装/卸载脚本。 |
| `Normalizer.cs` / `TextSource.cs` | 按需展开变量；统一解析源目录文件与直接文本。 |
| `FileMatcher.cs` / `Generator.Entries.cs` | 路径段匹配、目录元数据、受控临时载荷流。 |
| `Variables.cs` | 变量集合和常用变量的强类型访问器。 |
| `Utility.cs` | RID、安装路径、路径规范化、Unix 时间戳、文件权限等辅助逻辑。 |
| `Dumper.cs` | 控制台输出启动画面、错误和警告消息。 |

## 执行流水线

入口命令结构：

```text
dotnet-pack
├── tar
├── deb
└── rpm
```

核心流程由 `PackCommand<TPackage>.OnExecuteAsync()` 编排：

```mermaid
flowchart TD
    A["Program.Main(args)"] --> B["Terminal executor dispatches tar/deb/rpm"]
    B --> C["Resolve source and load ApplicationVersion"]
    C --> D["Select and validate identity, then initialize variables"]
    D --> E["Normalize source/output paths"]
    E --> F["Create Package.Tar/Deb/Rpm"]
    F --> M["Load and validate optional migration INI/env"]
    M --> G["Generate systemd scripts and service entry"]
    G --> H["Load package entries"]
    H --> N["Attach selected migration runtime, plan and SQL"]
    N --> V["Replace installation root .version with memory entry"]
    V --> I["Call package.Pack(output, overwrite)"]
    I --> J["Generator writes all package output"]
    J --> S["ApplicationVersion.Save(source/.version)"]
    S --> OK["Report success"]
```

`PackCommand<TPackage>` 做通用工作，子类只负责创建具体 `Package`：

```csharp
protected override Package.Deb CreatePackage(CommandContext context)
```

`RpmCommand` 额外读取：

- `--provides`
- `--conflicts`

这两个选项按逗号或分号拆分，最终写入 RPM metadata header。

## 源版本与包内版本

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

`Package.Entry` 内部支持字节内容构造，复制输入字节并以实际字节数设置 `Size`。`OpenRead()` 对内存条目返回独立的只读流，对普通文件仍打开 `Source`。tar/deb 载荷、RPM SHA-256 摘要和 cpio 载荷均经此入口读取；重复读取互不影响。版本条目通过 `ApplicationIdentifier.Save(Stream)` 写入内存，不追加或转换任何内容，也不创建临时文件；时间戳采用生成时间。

`EntryCollection.SetVersion` 在载荷和升迁资源收集后写入唯一版本条目，并删除指向同一安装根路径的根别名条目。子目录中的其他 `.version` 不受影响。`VersionFile.Load(source, name, edition, version)` 直接依据值判断是否提供选项，不另传存在性布尔标记：空白名称按未提供处理，`version == null` 时从源文件所选版本补全。`VersionFile` 在内存准备完整的待保存模型，不在加载时写盘。源文件由打包器显式 `File.OpenRead` / `File.Create`，交给 `ApplicationVersion.Load(Stream)` / `Save(Stream)` 解析和序列化：确保只访问直属 `.version`，缺失时创建、目录占位或 I/O 故障时失败，不使用 Core 路径重载的目录识别和缺失路径跳过行为。`Pack` 返回后才调用 `Save`，包括 tar 附属安装入口的生成也必须成功；保存失败抛出包含包路径和源路径的 I/O 异常，命令返回非零且不打印整体成功。

本地验证使用当前 Core 源码制作相同版本号的 NuGet 包，通过隔离缓存和包源映射还原 `Zongsoft.Core`；不增加跨仓库项目引用或修改 Core API。

## 命令选项模型

通用必填及条件必填选项：

| 选项 | 类型 | 说明 |
| --- | --- | --- |
| `--name` | `string` | 源版本文件缺失时必填；否则校验名称或使用源名称。 |
| `--version` | `Version` | 源版本文件缺失时必填；否则覆盖所选版本。最终版本不能为零。 |
| `--platform` | `Platform` | 目标平台。 |
| `--framework` | `string` | 目标框架，如 `net8.0`、`net9.0`、`net10.0`。 |

常用可选项：

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source` | 当前目录 | 输入目录。 |
| `--migration` | 空 | 升迁 INI 路径，多个路径以 `;` 或 `\|` 分隔，相对于 source；参数文件按目录约定查找。 |
| `--output` | `source` | 始终作为输出目录；相对路径基于 `source`，不支持指定文件名。 |
| `--exclude` | 空 | 加载打包项时跳过的文件模式列表，多个模式用逗号或分号分隔。 |
| `--edition` | 空 | 包版本/渠道标识，参与包名；RPM 中也作为 release。 |
| `--compilation` | `Release` | 查找宿主文件时使用的配置名。 |
| `--architecture` | `x64` | 目标架构。 |
| `--overwrite` | `false` | 是否覆盖已存在的输出文件。 |
| `--install-path` | 由包名推导 | 安装目录。 |
| `--title` | 空 | 人类可读标题。 |
| `--summary` | 空 | 短描述；可为文件路径。 |
| `--description` | 空 | 长描述；可为文件路径。 |
| `--url` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license` | 空 | 许可证文本。 |
| `--category` | 格式默认 | Debian `Section` 或 RPM `Group`。 |
| `--maintainer` | `Zongsoft Studio <zongsoft@gmail.com>` | 维护者/供应商。 |
| `--dependencies` | 空 | 依赖列表。 |

systemd 与生命周期脚本选项：

| 选项 | 说明 |
| --- | --- |
| `--listen` | 生成服务时传给 `--urls` 的绑定地址；纯数字会转成 `http://127.0.0.1:<port>`。 |
| `--daemon` | systemd 单元文件名/标识；`none`、`disable`、`disabled` 表示禁用。 |
| `--daemon-environments` | 逗号或分号分隔的变量名，写入生成的服务文件。 |
| `--installing` / `--installed` | 安装前/安装后脚本。 |
| `--uninstalling` / `--uninstalled` | 卸载前/卸载后脚本。 |
| `--preinstalling` / `--postinstalling` | 拼接到 `installing` 前后。 |
| `--preinstalled` / `--postinstalled` | 拼接到 `installed` 前后。 |
| `--preuninstalling` / `--postuninstalling` | 拼接到 `uninstalling` 前后。 |
| `--preuninstalled` / `--postuninstalled` | 拼接到 `uninstalled` 前后。 |

## 变量与规范化

### 变量来源

`PackCommand<TPackage>.GetVariables(context)` 先加载描述符默认值，再加载环境变量，最后覆盖显式命令选项（包括额外选项）。变量名不区分大小写，优先级为显式选项 > 环境变量 > 默认值。

`Normalizer.Initialize` 只保存原始值；访问值时递归展开引用，未使用的未知引用不会阻止制包。未知变量、循环引用及超过 64 层的展开失败，诊断指出变量名。展开不读取文件。

身份仍由源 `.version` 与显式 name/edition/version 选项共同确定，不从同名环境变量隐式替代身份。最终身份及已解析的 source/output 覆盖变量集合。`--migration` 仍须显式启用，`--overwrite` 仍是显式开关。

### 变量语法

`Normalizer` 支持两种变量引用形式：

```text
$(name)
%name%
```

示例：

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --listen:8069 \
  --daemon:zongsoft.web \
  --name:"$APP_NAME" \
  --version:"$APP_VERSION" \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

此示例的身份选项由 Bash 展开；`--version` 在进入 OnExecuteAsync 前按 `System.Version` 解析，名称与 Edition 按传入值校验。打包器变量表达式主要用于后续路径、文本和升迁配置。

源路径、输出、载荷、排除表达式、文本和升迁输入引用未知变量时均失败；未使用的变量不展开。`TryNormalize` 保留返回失败的底层接口，调用方不得把错误值继续作为有效输入。

### 文本与文件

`TextSource.Read(source, value, fileOnly)` 统一处理 summary、description 和生命周期钩子：

- `text:` 后内容原样返回，适用于含 Shell `$(...)`、`%...%` 或路径样式的字面文本。
- `file:` 后内容先展开变量，然后按绝对路径或相对 source 的路径读取；文件不存在报错。
- 无前缀时先展开变量；多行值为文本，已有文件按 source 读取，明显的缺失路径报错，其他单行值为文本。可用前缀消除歧义。
- 读取后的文件内容不再展开变量，也不会再次解释成另一个文件路径。
- pre/post 钩子用 `;` 或 `|` 分隔，每项均为文件路径，允许 `file:`，不接受 `text:`；文件名本身不能包含这些列表分隔符。

## 包模型

`Package` 抽象类持有三种格式共享的元数据：

- `Name`
- `PackageIdentity`
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
- `Migration`

包名规则：

```text
identity
identity-edition
```

其中 `identity` 默认等于 `name`；如果指定了未禁用的 `--daemon`，则取 daemon 标识的文件名部分，并去掉可选 `.service` 后缀。

输出文件名规则：

```text
identity@version_runtime.ext
identity-edition@version_runtime.ext
```

示例：

```text
zongsoft.web@1.0.0_linux-x64.deb
zongsoft.web-enterprise@1.0.0_linux-x64.rpm
```

### Runtime Identifier

`Utility.GetRuntimeIdentifier()` 根据平台和架构生成运行时标识：

```text
linux + x64   => linux-x64
linux + arm64 => linux-arm64
windows + x64 => win-x64
windows       => win
```

`Platform.Windows` 通过 `[Alias("Win")]` 为命令解析声明别名；它不是单独的枚举成员。

### 默认安装路径

`Utility.Unix.GetInstallPath(identity)` 根据包标识推导默认安装路径：

```text
Zongsoft.Hosting.Web => /opt/zongsoft/hosting/web
zongsoft.web         => /opt/zongsoft/web
```

规则：

- 名称为空时返回 `/opt`。
- 名称转为小写。
- 每个点号都替换为 `/`，形成多级目录。
- 无点号时安装到 `/opt/<name>`。
- Web 宿主示例指定 `--daemon:zongsoft.web`，因此使用 `/opt/zongsoft/web`；`--name:Zongsoft.Hosting.Web` 仍用于定位宿主 DLL。

## 打包项加载

打包项由 `Package.EntryCollection` 负责加载。

### 默认加载

如果没有位置参数：

```text
递归收集 source 下所有文件
entryName = 文件相对 source 的路径
```

对 `.deb` 和 `.rpm`，`entryName` 会加上安装路径前缀，例如：

```text
InstallPath = /opt/zongsoft/web
EntryName   = opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

对 `.tar.gz`，`EntryPrefix` 为 `null`，应用文件保留在归档根目录下，安装时由 `install.sh` 复制到目标目录。

### 显式加载

位置参数格式：

```text
path
path:alias
```

解析规则：

- 以最后一个冒号拆分路径和别名。
- Windows 盘符中的 `C:` 不作为别名分隔符。
- 相对路径基于 `source`。
- 绝对路径可以位于 `source` 外部；如果没有别名，最终只使用文件名。
- 目录递归展开，目录自身也是条目，保留空目录与源目录模式（Windows 默认 0755）。目录别名 `:~` 被归一化为空路径，将目录内容直接放到安装根目录，hosting 的载荷参数采用此写法。
- `FileMatcher` 统一支持任一路径段中的 `*`、`?`，独立段 `**` 匹配零层或多层目录。每个参数位置按相对于固定前缀的路径（`/` 分隔）Ordinal 排序，不重排全部输入；Windows 匹配忽略大小写，Unix 区分大小写。
- 重复的目标路径会触发冲突警告并跳过。

### 排除规则

`--exclude` 会在 `Package.EntryCollection.Load()` 收集文件时生效：

- 多个模式用逗号或分号拆分。
- 模式先经过变量展开，再统一为 `/` 路径分隔符。
- 相对模式基于 `source`，同时会匹配源文件相对路径、最终包内路径和文件名。
- 支持 `*`、`?`、`**`；目录模式如 `logs/` 等价于 `logs/**`。
- 命中的文件直接跳过，不产生重复条目冲突警告。
- systemd 生成/加入的服务文件和自动生成的 `.version` 文件不经过该过滤器。

### 根路径别名

如果 alias 以 `/` 或 `\` 开头，则条目标记为 `Rooted`。

示例：

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  ../.deploy/default/nginx/zongsoft.web.conf:/etc/nginx/conf.d/zongsoft.web.conf
```

三种格式的处理方式：

| 格式 | 处理方式 |
| --- | --- |
| `.deb` | payload 路径为 `etc/nginx/conf.d/zongsoft.web.conf`，安装后位于 `/etc/nginx/conf.d/zongsoft.web.conf`；`/etc` 下 root 条目会写入 `conffiles`。 |
| `.rpm` | payload 路径为 `/etc/nginx/conf.d/zongsoft.web.conf`，RPM header 中标记配置文件。 |
| `.tar.gz` | 文件存放到 `.root/etc/nginx/conf.d/zongsoft.web.conf`，由 `install.sh` 复制到 `${DESTDIR}/etc/nginx/conf.d/zongsoft.web.conf`。 |

### 文件权限

Unix-like 主机：

```text
File.GetUnixFileMode(path) & rwx mask
```

Windows 主机或读取不到有效权限时：

- `.sh`、`.dll`、`.exe`、无扩展名文件使用 `0755`。
- 其他文件使用 `0644`。

## systemd 生成器

三种包类型当前都使用 `Scriptor.Systemd`。

如果 `--daemon` 被指定且未被禁用，daemon 标识只覆盖包文件名、系统包名和默认安装路径；`Package.Name` 仍保留 `--name` 值，用于定位 .NET 宿主 DLL。

### 服务文件解析

`--daemon` 为空时，默认使用 `Package.Name` 的小写形式作为服务标识，不附加 Edition。

流程：

1. 若 `--daemon:none`、`--daemon:disable` 或 `--daemon:disabled`，禁用服务生成。
2. 否则在 `source` 下查找 `--daemon` 指定的文件。
3. 如果文件存在，将其加入包条目。
4. 如果文件不存在，尝试生成临时 `.service` 文件并加入包条目。

### 宿主定位

生成 `.service` 文件时需要定位 .NET 宿主。查找顺序：

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
SyslogIdentifier=<package-identity>
DynamicUser=no
PrivateTmp=no
ReadWritePaths=<install-path> <install-path>/logs /tmp

Environment=DOTNET_NOLOGO=true
<custom-environment-lines>

[Install]
WantedBy=multi-user.target
```

监听值由 `Variables.Listen` 提供；多个完整 URL 以分号分隔，作为同一个 `--urls` 值保留。HTTP/HTTPS 可同时指定，HTTPS 默认服务器证书由宿主配置。省略选项时不追加 `--urls`，已有 service 的 ExecStart 不改写。

如果 `--listen` 非空，则改为：

```ini
ExecStart=dotnet <install-path>/<host> --urls <bind>
```

其中纯数字绑定值会被转换成：

```text
http://127.0.0.1:<port>
```

### 生命周期脚本

脚本模型映射：

| `Package.InstallScripts` | Debian 文件 | RPM tag | Tar 路径 |
| --- | --- | --- | --- |
| `Installing` | `preinst` | `1023` | 融合到 `install.sh` |
| `Installed` | `postinst` | `1024` | 融合到 `install.sh` |
| `Uninstalling` | `prerm` | `1025` | 融合到 `uninstall.sh` |
| `Uninstalled` | `postrm` | `1026` | 融合到 `uninstall.sh` |

未提供脚本时，默认行为：

- 安装前停止同名 systemd 服务。
- 安装后创建 `/etc/systemd/system/<service>` 符号链接。
- 安装后执行 `systemctl daemon-reload`、`systemctl enable` 和 `systemctl start`。
- 卸载前禁用并停止服务。
- 卸载后删除服务符号链接、重载 systemd，并删除安装目录。

禁用 systemd 时，默认脚本退化为 no-op，卸载后仍会删除安装目录。

不同包格式会在写入生命周期脚本时应用各自的卸载保护：

- Debian 的 `prerm` 仅在 `remove` 或 `deconfigure` 时执行 `Uninstalling`，`postrm` 仅在 `remove` 或 `purge` 时执行 `Uninstalled`；`upgrade`、`failed-upgrade`、`abort-install`、`abort-upgrade` 和 `disappear` 不执行卸载清理。
- RPM 的 `%preun` 和 `%postun` 仅在 `$1=0`（最后一个已安装实例被删除）时执行卸载脚本；升级时 `$1>0`，不会删除新版本负载。
- Tar 包没有包管理器升级回调，只有显式执行 `uninstall.sh` 才进入卸载生命周期；生成器统一删除解析后的 `TARGET`，默认 `Uninstalled` 脚本不再重复删除硬编码安装路径。

## 打包器版本元数据

每个安装包自动记录当前生成工具的身份，逻辑内容为 `Packager:Zongsoft.Tools.Packager@0.9.0.0`。值采用 `程序集名@版本号`，从打包器自身程序集读取，独立于宿主应用版本；不需要新增命令选项，也不要求启用升迁。

| 格式 | 存放位置 | 查看方式 |
| --- | --- | --- |
| tar.gz | PAX 全局扩展属性 `Packager` | 使用支持 PAX 的归档读取器，例如 Python `tarfile` 的 `pax_headers["Packager"]`。 |
| deb | `control.tar.gz` 内 `control` 的 `Packager` 字段 | `dpkg-deb -f <安装包.deb> Packager` |
| rpm | 主 Header 的 `RPMVERSION` 字符串标签（1064） | `rpm -qp --queryformat '%{RPMVERSION}\n' <安装包.rpm>` |

RPM 用生成工具版本标签保存本工具身份；其 `PACKAGER` 标签（1015）仍保存 `--maintainer` 的维护者信息。元数据位于格式头中，不增加安装目录文件，也不改变 `.version` 或 `migration.json`。

`Generator.GetIdentity` 通过 `Assembly.GetName()` 读取自身程序集的简单名称和 `Version`，保留版本对象的完整文本，例如 `0.9.0.0`。三个生成器调用同一方法读取身份，不取调用进程或宿主程序集版本。原始主 Header 的 RPM 摘要生成流程覆盖新增标签。

tar 通过 `PaxGlobalExtendedAttributesTarEntry` 写入一个全局扩展记录，不将其加入 `Package.Entries`；deb 写入控制字段；RPM 直接写入 1064 标签，不占用已有维护者字段。RPM 原生标签含义参见[官方标签说明](https://rpm-software-management.github.io/rpm/manual/tags.html)，PAX API 参见[官方构造说明](https://learn.microsoft.com/en-us/dotnet/api/system.formats.tar.paxglobalextendedattributestarentry.-ctor)。

## `.tar.gz` 实现

实现文件：`Generator.Tar.cs`

### 格式概要

`.tar.gz` 等价于：

```text
gzip(tar archive)
```

当前实现使用 .NET 的 `System.Formats.Tar`：

```csharp
new TarWriter(gzip, TarEntryFormat.Pax, false)
```

因此归档条目使用 PAX tar 格式，可支持更长路径和扩展元数据。

生成 `.tar.gz` 的同时，会在输出目录生成一个同名 `.sh` 安装脚本，例如：

```text
zongsoft.web@1.0.0_linux-x64.tar.gz
zongsoft.web@1.0.0_linux-x64.sh
```

该脚本定位同目录下的 `.tar.gz`，解压到临时目录，并调用解压后的 `install.sh` 完成安装。

### 归档结构

```text
<application files>
.root/<rooted files>
install.sh
uninstall.sh
```

归档开头包含 `Packager` PAX 全局扩展记录，它不是安装文件。rooted 文件只有存在根路径别名时写入 `.root/`。生命周期脚本不再作为独立文件写入，而是融合到 `install.sh` 和 `uninstall.sh`。

### 文件条目

应用文件写入为：

```text
TarEntryType.RegularFile
name = entry.EntryName
mode = entry.Mode
mtime = entry.ModifiedTime
data = entry.OpenRead()
```

rooted 文件写入为：

```text
name = .root/<entry.EntryName>
```

### install.sh / uninstall.sh

`install.sh` 是自包含安装器，权限 `0755`。`uninstall.sh` 是自包含卸载器，权限 `0755`，安装时会复制到目标安装目录。

支持：

- 默认安装。
- 从安装目录执行 `uninstall.sh` 卸载。
- 普通包允许 `INSTALL_PATH` 覆盖应用安装路径；升迁包实际安装时校验固定路径。
- `DESTDIR` 暂存安装。
- 执行融合后的生命周期脚本。
- 安装/卸载 rooted 文件。

安装流程：

```text
SOURCE_DIR = install.sh 所在目录
INSTALL_PATH = 环境变量或包默认安装路径
DESTDIR = 可选暂存目录
TARGET = DESTDIR + INSTALL_PATH
DESTDIR 为空时执行 Installing 生命周期内容
创建 TARGET
复制普通归档文件到 TARGET
复制 uninstall.sh 到 TARGET
复制 rooted 文件到 DESTDIR + /<root-path>
DESTDIR 为空时执行 Installed 生命周期内容
```

卸载流程：

```text
DESTDIR 为空时执行 Uninstalling 生命周期内容
rm -rf TARGET
删除 rooted 文件
DESTDIR 为空时执行 Uninstalled 生命周期内容
```

## `.deb` 实现

实现文件：`Generator.Deb.cs`

### Debian 二进制包概要

Debian 二进制包是 Unix `ar` 容器，现代格式版本为 `2.0`。

当前写入：

```text
debian-binary
control.tar.gz
data.tar.gz
```

### ar 容器

文件以全局头开始：

```text
!<arch>\n
```

每个成员写入 60 字节 ASCII 头：

```text
name/ timestamp uid gid mode size `\n
```

当前实现：

- `uid = 0`
- `gid = 0`
- `mode = 100644`
- 成员长度为奇数时补一个换行字节，使下个成员按偶数边界开始。

### debian-binary

内容固定：

```text
2.0\n
```

### control.tar.gz

`control.tar.gz` 使用 gzip + Ustar tar，包含：

```text
control
preinst
postinst
prerm
postrm
conffiles
```

说明：

- `control` 权限为 `0644`。
- 维护者脚本只有内容非空时写入，权限为 `0755`。
- 维护者脚本自动加 `#!/bin/sh` 和 `set -e`。
- `conffiles` 只在存在 rooted 且路径位于 `/etc/` 下的文件时写入。

### control 字段

当前生成字段：

```text
Package: <package-name>
Version: <version>
Packager: <assembly-name>@<packager-version>
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
- 空行写为 ` .`。
- `License` 与 `Packager` 作为额外字段写入；`Packager` 与宿主 `Version`、`Maintainer` 分别保存不同信息。

### Debian 架构映射

| .NET `Architecture` | Debian architecture |
| --- | --- |
| `X64` | `amd64` |
| `X86` | `i386` |
| `Arm64` | `arm64` |
| `Arm` | `armhf` |
| 其他 | `all` |

### data.tar.gz

`data.tar.gz` 使用 gzip + Ustar tar，先写目录条目，再经 `OpenRead()` 写入普通文件。显式目录保留模式和时间戳，自动补齐的父目录使用 0755；目录不打开内容流，也不写入 conffiles。

对非 rooted 条目，`.deb` 的 `EntryPrefix` 是去掉开头 `/` 的安装路径：

```text
InstallPath = /opt/zongsoft/web
EntryName   = opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

Debian 解包后文件位于：

```text
/opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

对 rooted 条目，prefix 被跳过，因此：

```text
Alias     = /etc/nginx/conf.d/zongsoft.web.conf
EntryName = etc/nginx/conf.d/zongsoft.web.conf
```

安装后位于：

```text
/etc/nginx/conf.d/zongsoft.web.conf
```

## `.rpm` 实现

实现文件：`Generator.Rpm.cs`

### RPM 文件概要

RPM 文件由以下逻辑部分组成：

```text
Lead
Signature
Header
Payload
```

当前实现写入：

```text
lead
signature header
metadata header
gzip(newc cpio payload)
```

当前不生成 GPG/PGP 签名，只写入基本 signature tag，例如包体大小和 MD5 digest。

### Lead

lead 固定 96 字节：

| 偏移 | 内容 |
| --- | --- |
| `0..3` | RPM magic：`ed ab ee db` |
| `4` | major version：`3` |
| `5` | minor version：`0` |
| `6..7` | package type：binary package |
| `8..9` | architecture number |
| `10..75` | `<package-name>-<version>`，最多 65 字节 ASCII 内容，随后保留 NUL |
| `76..77` | OS number：Linux |
| `78..79` | signature type：header-style signature |

lead 架构号映射：

| .NET `Architecture` | RPM lead architecture number |
| --- | --- |
| `X64` | `1` |
| `X86` | `1` |
| `Arm64` | `12` |
| `Arm` | `12` |
| 其他 | `255` |

### Header 编码

RPM header 结构：

```text
magic/version/reserved
index count
store size
index entries
store bytes
padding to 8 bytes (signature header only)
```

index entry：

```text
tag    int32 big-endian
type   int32 big-endian
offset int32 big-endian
count  int32 big-endian
```

当前支持的 type：

| Type | 含义 |
| --- | --- |
| `3` | int16 array |
| `4` | int32 array |
| `6` | string |
| `7` | binary |
| `8` | string array |
| `9` | international string |

数值使用 big-endian，字符串使用 UTF-8 并以 `NUL` 结尾。

### Signature Header

signature section 使用与 RPM header 相同的索引/存储区结构。

只有 signature header 尾部补齐到 8 字节。主 metadata header 的 store 后立即是压缩 payload，不再填充；否则 rpm 查询可能成功，但 rpm2cpio 会读到 gzip 之前的零字节，无法解包。

当前写入：

| Tag | 内容 |
| --- | --- |
| `62` | Signature 不可变区域，BIN 类型、16 字节 trailer。 |
| `269` | metadata header 的 SHA-1 digest。 |
| `273` | metadata header 的 SHA-256 digest。 |
| `1000` | metadata header + payload 的字节长度。 |
| `1004` | metadata header + payload 的 MD5 digest。 |

signature section 末尾按 8 字节对齐。两个 header 的索引均按 tag 升序写入；主 header 的不可变区域 tag 为 `63`。区域 trailer 位于 store 尾部，其负偏移为区域索引字节数，摘要覆盖完整 metadata header（包含 magic、索引、store 与 trailer）。这是完整性摘要，不是发布者的 OpenPGP 签名。格式依据 [RPM V4 格式](https://rpm-software-management.github.io/rpm/manual/format_v4.html) 和 [Header 结构](https://rpm-software-management.github.io/rpm/manual/format_header.html)。

### Metadata Header

当前写入的主要内容：

- 包名、版本、release。
- 摘要、描述、构建时间、构建主机。
- 包大小、许可证、维护者、分类、URL。
- OS 和架构。
- 安装/卸载脚本。
- 文件大小、模式、mtime、digest、用户名、组名、配置文件标记。
- payload 格式、压缩器和压缩级别。
- Requires、Provides、Conflicts。
- dirname、basename、dirindex 三组文件路径表。

常用 tag：

| Tag | 当前写入内容 |
| --- | --- |
| `1000` | 包名。 |
| `1001` | 版本。 |
| `1002` | release；未指定 edition 时为 `1`，否则为 edition。 |
| `1004` | 摘要。 |
| `1005` | 描述。 |
| `1006` | 构建时间。 |
| `1007` | 构建主机。 |
| `1009` | 安装大小。 |
| `1014` | 许可证。 |
| `1015` | 维护者/打包者。 |
| `1016` | 分组。 |
| `1020` | URL。 |
| `1021` | OS，固定 `linux`。 |
| `1022` | RPM 架构。 |
| `1023..1026` | pre/post install、pre/post uninstall 脚本。 |
| `1028` | 文件大小数组。 |
| `1030` | 文件模式数组。 |
| `1034` | 文件修改时间数组。 |
| `1035` | 文件 SHA-256 digest 数组。 |
| `1037` | 文件 flags；`/etc` rooted 文件标记为配置文件。 |
| `1039` / `1040` | 用户名/组名，固定 `root`。 |
| `1048..1050` | Requires flags/name/version。 |
| `1047`, `1112`, `1113` | Provides name/flags/version。 |
| `1053..1055` | Conflicts flags/name/version。 |
| `1056` | Install prefix。 |
| `1064` | 生成工具身份，`程序集名@版本号`。 |
| `1116..1118` | 文件目录索引、文件基本名、目录名。 |
| `1124` | Payload format，固定 `cpio`。 |
| `1125` | Payload compressor，固定 `gzip`。 |
| `1126` | Payload flags，固定 `9`。 |
| `5011` | 文件摘要算法，`8`（SHA-256）。 |
| `5092` / `5093` | 压缩 payload 的 SHA-256 digest / 算法 `8`。 |

### RPM 架构映射

| .NET `Architecture` | RPM architecture |
| --- | --- |
| `X64` | `x86_64` |
| `X86` | `i386` |
| `Arm64` | `aarch64` |
| `Arm` | `armv7hl` |
| 其他 | `noarch` |

### Requires、Provides、Conflicts

关系表达式支持：

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

关系标志：

| 操作符 | Flags |
| --- | --- |
| `<` | `RPM_SENSE_LESS` |
| `>` | `RPM_SENSE_GREATER` |
| `=` | `RPM_SENSE_EQUAL` |
| `<=` | `LESS \| EQUAL` |
| `>=` | `GREATER \| EQUAL` |

默认 Requires：

```text
rpmlib(CompressedFileNames) <= 3.0.4-1
rpmlib(FileDigests) <= 4.6.0-1
rpmlib(PayloadFilesHavePrefix) <= 4.0-1
```

默认 Provides：

```text
<package-name> = <version>-<release>
```

### Payload

payload 是 gzip 压缩后的 ASCII `cpio` newc 归档。newc header magic：

```text
070701
```

生成流程：

1. 根据文件路径收集目录，至少包含 `/`。
2. 写入目录 cpio 条目，模式 `0040000 | entry.Mode`；源目录保留模式，合成父目录为 0755。Header 与 cpio 使用相同目录元数据。
3. 写入文件 cpio 条目，路径为 `.` + RPM 绝对路径，例如 `./opt/zongsoft/web/Zongsoft.Hosting.Web.dll`。
4. 文件模式为 `0100000 | entry.Mode`。
5. 写入 `TRAILER!!!` 结束条目。
6. 原始 cpio 数据补齐到 512 字节边界。
7. 使用 gzip 压缩。

RPM header 同时保存一份文件元数据，供包管理器查询和校验。

## 三种格式对比

| 特性 | `.tar.gz` | `.deb` | `.rpm` |
| --- | --- | --- | --- |
| 外层容器 | gzip tar | Unix ar | RPM lead/signature/header |
| 文件载荷 | PAX tar | `data.tar.gz` | gzip newc cpio |
| 控制元数据 | PAX 全局属性；`install.sh` 与 `uninstall.sh` | `control.tar.gz` | RPM metadata header |
| 生命周期脚本 | 融合到 `install.sh` / `uninstall.sh` | `preinst/postinst/prerm/postrm` | header script tags |
| 包管理器安装 | 否 | `dpkg`/`apt` | `rpm`/`dnf`/`yum` |
| 默认安装路径 | `install.sh` 复制 | payload 内含路径 | payload/header 内含路径 |
| root alias | `.root/` + installer | 直接安装到根路径 | 直接安装到根路径 |
| 配置文件标记 | 无包管理器标记 | `/etc` rooted 文件写入 `conffiles` | `/etc` rooted 文件标记 config flag |
| 签名 | 无 | 无 | 无 GPG/PGP，仅基础 digest |

## 载荷流与目录条目

`Package.Entry.IsDirectory` 区分目录与文件。目录没有内容流、大小为零；生成器统一补齐父目录，文件与目录目标冲突时失败。符号链接和 Windows reparse point 输入明确拒绝，不递归跟随；目标路径不能包含 `..`、换行或 NUL。tar 根别名目录使用 `install -d` 按模式创建，卸载仅对显式目录执行 `rmdir`，非空目录保留，合成的共享父目录不主动删除。

Debian 的 control/data gzip tar 分别写入受控临时文件，ar 依据实际长度流式复制。RPM 原始 cpio 与 gzip payload 使用临时文件，压缩载荷 SHA-256、主 Header + payload MD5 均通过流计算，最后顺序写入各 Header 与载荷，避免完整包体数组。小版本内容和 Header 元数据仍保留内存处理；元数据内存随条目数增长，载荷不随文件字节数增加托管分配。临时文件使用独占 CreateNew、DeleteOnClose，Unix 模式 0600；正常结束及异常均释放。需要足够临时磁盘空间，RPM 峰值包括原始及压缩载荷；未改变 RPM 既有整数大小上限。

## Debian 关系字段

`DebCommand` 提供 `--provides`、`--replaces`、`--breaks`、`--conflicts`、`--recommends`、`--suggests`，分别写入同名首字母大写的 control 字段；`--dependencies` 写 Depends。`Package.Deb` 独占规则，不复用 RPM 解析器。列表以逗号或分号分隔；关系写为 `name (>= version)` 等括号语法，支持 `<< <= = >= >>`。Depends/Recommends/Suggests 可用 `|` 表达替代项，Provides 的版本关系仅允许 `=`。无值不写字段，拒绝非法包名、关系、换行和 NUL；二进制 control 不接受源包的架构限制及构建 profile 表达式。规则依据 [Debian Policy 关系字段](https://www.debian.org/doc/debian-policy/ch-relationships.html)。

## 当前实现边界

- Debian 固定 gzip control/data tar，RPM 固定 gzip cpio，暂不提供 xz/zstd。
- RPM 直接写格式，不调用 rpmbuild，不提供 spec 或 GPG 签名。
- 文件所有者/组固定为 root，不继承构建机 UID/GID，也不提供自定义所有者选项。
- systemd 是当前唯一脚本生成策略。
- 暂不收录或跟随符号链接/reparse point；需要打包其实际文件。
- 大载荷制包依赖临时磁盘容量，既有容器字段的大小上限仍然适用。

已实施项目及验证证据见 [改进任务清单](improvements.md)。

## 安装升迁实现

实际 Native AOT 发布、第三方警告和隔离运行结果见 [验证记录](migration-verification.md) 与 [警告审查](aot-warning-review.md)。

公开契约及真实 hosting 范例见 [安装升迁](migrations.zh-Hans.md) / [English](migrations.md)。`--migration` 是唯一新增打包选项。打包阶段不连接数据库或 S3；`MigrationLoader` 将 INI/参数转换为确定顺序的 `MigrationPlan`，文件路径、SQL SHA-256 和展开后的参数写入计划。指定 INI 缺失或模式无匹配时警告并跳过；已存在的 INI、参数文件及 SQL 仍严格校验，找不到参数或 SQL、选项不合法等均在生成归档前失败。命令执行器的 Failed 事件及空结果均设置非零进程退出码，避免终端吞掉异常后外部脚本误判成功。

`MigrationLoader` 的公共 Load 组织输入流程；私有嵌套 Database 负责脚本路径、通配符、排序、去重、批次与校验和，AmazonS3 负责 Bucket 文本及重名检查。运行器的 Database 基类只共用 ADO.NET 连接执行、建库竞争和有序脚本辅助，不要求 TDengine 使用 DbConnection。

SQL 批次按规范升迁器名称组织，例如 `.migration/.artifacts/mysql/0001.sql` 和 `.migration/.artifacts/postgres/0001.sql`。每次加载计划时各升迁器从 0001 独立计数，同类任务共享连续编号；PostgreSQL 别名统一归入 postgres。每个非空段落仍是独立任务，保留自己的连接参数及脚本列表；任务 Id 用于日志和状态，不作为目录名。同段落内 SQL 重叠匹配去重，跨段落、跨文件和重复指定 INI 不合并或去重。S3 配置直接保存在计划中，不生成空中间目录。

`MigrationLoader.Load` 的局部计数字典传给 `MigrationLoader.Database`，避免多次加载或失败重试继承编号。解析顺序保留；运行器当前串行，但不承诺跨任务执行顺序，数据库任务内部的脚本顺序继续保证。

INI 直接调用 `Zongsoft.Configuration.Profiles.Profile.Load(path, options)`，与 deployer 保持一致。依赖 Core 7.59.0，`ProfileSection` 原生接受 `[amazon.s3]`，不改写段名或条目。重复段落在加载前检查，重复条目由 Profile 检查；关闭 Profile import 指令，避免导入破坏参数文件的明确查找边界。

采用主项目与独立 `migrator` 两个生产项目。命名空间统一为 `Zongsoft.Tools.Packager.Migration`，`.shared` 的计划模型、`MigrationProvider` 名称及参数规则、`MigrationUtility` 通用参数方法及对应本地化资源分别编译进两端。`MigrationPlan.Step/Script/Bucket` 为嵌套模型，`Script.Source/Content` 只在主项目 partial 扩展中定义，不进入 JSON。主项目处理 INI、变量和 Bucket 选项文本，打包过程中不连接数据库或 S3。migrator 包含抽象 partial `Migrator`、抽象 `Migrator.Database` 及其六种嵌套数据库实现、`Migrator.AmazonS3` 实现、执行上下文和调度，独占数据库驱动及 AWS SDK 依赖；TDengine 使用 BCL `ClientWebSocket`，无需连接器包。

主项目不引用或构建 migrator。运行器为 net10.0 Native AOT 程序，目标 glibc Linux x64/arm64，使用独立 Rocky Linux 9/glibc 2.34 环境发布。`.shared/Migration.props` 链接协议源码和资源。Cake 的显式 `migrator` 任务分别准备 `src/.migrator/linux-x64/`、`src/.migrator/linux-arm64/`，符号与发布载荷分离；完整工具包要求两个 RID 均准备成功。普通 `dotnet build/test` 不启动容器，主项目通过普通 Content 收入预先准备的目录。`MigrationBundle` 只选择 RID、检查 ELF 架构、收集完整目录；不分析驱动依赖或重写 `.deps.json`。入口设置 0755，缺失或架构错误时制包失败。原生运行器无需目标 .NET 运行时；系统库要求见升迁指南。

生成载荷包含 `.migration/migration.json`（0600）、`.migration/.artifacts/<升迁器名称>/<四位序号>.sql` 预处理批次（0644）、原生 migrator 及必要 `.so`、计划指纹文件以及 `.migration/migrate.sh`（0755）。`AddGenerated` 遇冲突抛错；这些保留路径不得由用户载荷覆盖。参数以明文存在包内，日志只记录任务、脚本逻辑路径和异常类型，不输出驱动异常正文。SQL 校验和验证在所有外部资源操作之前。

安装停止服务并使旧 ready 标记失效，部署完成后设置 `PACK_INSTALL_PATH`，建立 systemd drop-in，执行所有升迁，再进入 installed/start 及 postinstalled。重复 postinst configure 同样先清除 ready。Debian 在 configure 分支运行，RPM 在 `%post` 运行；未启用升迁时保留原有服务脚本语义。tar 的 DESTDIR 暂存跳过全部安装/卸载生命周期步骤。升迁包的实际安装路径固定，改变 INSTALL_PATH 时在载荷部署前拒绝，使用 --install-path 重新打包可改变目标目录。

状态在 `/var/lib/<PackageName>/packager`；独占文件锁禁止同包同时 apply。执行器先清 ready，验证计划及 SQL，当前串行运行任务，成功才写 ready；跨任务顺序不作为契约，数据库任务内部仍按脚本列表执行。启动检查通过比较公开的计划指纹文件与 ready，不需要服务用户读取 0600 参数文件。指纹使用无缩进 JSON 的 UTF-8 SHA-256，避免 Windows/Linux 格式换行差异；参数字符串内部的换行会原样参与序列化与指纹计算。网络数据库先建库再执行脚本，文件数据库自动建父目录；TDengine 直连 taosAdapter `/rest/ws`，顺序执行 `conn`、`query` 和 `free_result`，支持响应分片、请求关联、超时与取消；S3 采用 HeadBucket/PutBucket 创建桶，随后通过 PutBucketVersioning、PutBucketEncryption、PutBucketTagging 配置已指定的版本控制、默认加密和桶标签，最后以 PutBucketPolicy 设置公共读取，不使用 ACL。已有桶不改配置；本地 pending 文件只在所有配置完成后清除，支持建桶后任一配置失败重试。每次安装和重试都执行全部 SQL，脚本作者负责存在性/预期状态判断及数据幂等性。没有逐文件成功历史、历史校验冲突或已执行跳过逻辑，SQL 校验和仅验证当前包的完整性；不自动回滚，不在卸载时删除数据库、桶或状态。

`MigrationLoader.Database` 的私有批次实现在打包阶段处理 SQL Server GO、MySQL DELIMITER 和 TDengine 分号。MySQL（启用 AllowUserVariables）、PostgreSQL、DuckDB、SQLite 保持完整驱动批次，避免破坏函数、触发器、事务和会话变量作用域。每个批次以 UTF-8 无 BOM 写入安装根的 `.migration/.artifacts/`，保留批次内换行并计算生成字节的校验和；`Script.Content` 只存在于打包端。计划中的 Script.Path 相对于安装根目录，运行器从计划所在的 `.migration/` 推导安装根，只接受 `.migration/.artifacts/` 内的文件；每个文件直接提交一次，不再包含 SQL 分段代码。用户负责 SQL 语法、事务与幂等性。

生产项目位于 `src/Zongsoft.Tools.Packager.csproj` 和 `migrator/src/Zongsoft.Tools.Packager.Migrator.csproj`，运行器的共享协议导入路径为 `../../.shared/Migration.props`。`test/Zongsoft.Tools.Packager.Tests.csproj` 覆盖 Profile、参数、SQL 批次预处理、计划与指纹、三格式制包和 JSON 交接；`migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj` 只引用运行器，覆盖 SQLite/DuckDB 临时数据库、S3、执行状态及 TDengine WebSocket。运行器测试采用普通类型名；主侧交接测试启动独立 migrator 的 `apply/check` 命令，验证预处理脚本执行顺序、主端指纹、篡改失败及完成标记失效。主测试项目通过 `ReferenceOutputAssembly=false` 只保留运行器构建依赖，不直接引用其类型；两组测试均无程序集或 `using` 别名。两个测试项目均纳入解决方案及 Cake 的 `**/test/*.csproj` 发现规则。测试包不是实际安装验证；真实宿主验证记录见 [升迁验证](migration-verification.md)。

### 升迁程序交接与本地化

`migration.json` 的完整字段、类型、可选桶配置和指纹范围见[字段说明](migrations.zh-Hans.md#migrationjson-字段说明)及[英文说明](migrations.md#migrationjson-fields)。

打包器产生 `.migration/migration.json`，包含 FormatVersion、Package、Version、有序 Tasks、已展开参数及包内 SQL 路径/校验和。独立运行器读取计划和 `.migration/.artifacts/` 下的批次，不读取 INI 或 `.env`。JSON 使用源码生成 `MigrationPlan.Serialization`，配置为私有实现；模型的 Validate 检查结构，MigrationProvider 检查参数。运行器使用显式数据库构造，TDengine 请求、S3 策略和状态使用 Utf8JsonWriter。`.migration/migrate.sh apply` 直接执行原生 `Zongsoft.Tools.Packager.Migrator`，退出码决定是否继续启动宿主。运行器在 `/var/lib/<包名>/packager` 维护锁、status.json 和 ready；Shell check 只比较 ready 与包内 id。Web 宿主路径为 `/opt/zongsoft/web/.migration/migration.json`，完整文件职责见 [升迁指南](migrations.zh-Hans.md#打包器与-migrator-的协作)。

主项目 `Properties/Resources`、共享 `MigrationResources`、运行器 `Properties/Resources` 各自提供默认英文及 zh-Hans 资源，配置 ResXFileCodeGenerator 并生成强类型访问代码；原生发布保留中英文资源和全球化能力。C# 消息按 CurrentUICulture 选择，Shell 提示按 LC_ALL、LC_MESSAGES、LANG 选择。状态值和协议字段不本地化。


### 可选升迁输入缺失

`MigrationLoader` 按参数顺序通过 FileMatcher 展开 INI 路径通配符，对不存在的指定文件或无匹配模式通过本地化警告回调提示并继续。全部输入缺失时返回空计划引用，`PackCommand` 按普通包生成脚本，不调用 `MigrationBundle.Attach`，因此不要求运行器产物，也不生成升迁启动门禁。只保留 `.migration/` 为生成内容保留目录，普通载荷可以使用安装根的 `migration/`。已经找到的 INI 仍进行完整格式和内容校验，`.env` 与 SQL 缺失不属于可跳过输入。有效空 INI 不增加任务；若找到过 INI 而最终任务总数为零，则失败。


### S3 桶初始化选项

`MigrationLoader.AmazonS3` 解析带引号的选项文本，规范化 encryption/versioning 模式，按大小写敏感名称收集 tag.*；选项或标签重复即报错。加密描述采用 `MigrationPlan.Bucket.EncryptionOptions` 嵌套类型，包含 Mode 和可选 Key；Bucket 同时包含可选 Versioning 和 Tags。可选属性为 null 时不写入 JSON。`Encryption` 默认 null；显式加密对象的 Mode 必须为 `sse-s3` 或 `sse-kms`，没有“不加密”枚举或空 Mode 默认值。`Bucket.Validate` 由打包端与运行端计划校验共同调用，检查模式、KMS密钥关联和标签限制；共享协议不依赖 AWS SDK。

运行器 `Migrator.AmazonS3.ConfigureAsync` 只将结构化数据映射到标准 S3 请求，先版本控制、后加密与标签，随后执行公共策略。省略字段不发送配置请求；已有桶沿用跳过规则，新建桶配置失败则保留 pending。未增加修改已有桶的选项、后端选择或 RustFS 专用 API，KMS仅引用已有密钥标识。

## 验证建议

源版本与内存条目的构建、回归及 hosting 制包证据见 [.version 验证记录](version-verification.md)。

打包器版本元数据回归 `Package_Provenance_RecordsGeneratorAndPreservesApplicationMetadata` 覆盖三格式生成工具身份、应用版本和维护者字段；2026-09-12 元数据实现后的打包器 192 项测试通过，net8.0/net9.0/net10.0 构建零警告、零错误。

使用 [README](../README.zh-Hans.md#快速开始) 中生成的真实项目安装包；以下命令从 hosting 仓库根目录执行，只检查包内容。安装与卸载用法见 [README 包格式](../README.zh-Hans.md#包格式)。

### tar.gz

```bash
tar -tzf ./packages/zongsoft.web@1.0.0_linux-x64.tar.gz
tar -xOf ./packages/zongsoft.web@1.0.0_linux-x64.tar.gz install.sh
tar -xOf ./packages/zongsoft.web@1.0.0_linux-x64.tar.gz uninstall.sh
```

### deb

```bash
ar t ./packages/zongsoft.web@1.0.0_linux-x64.deb
dpkg-deb --info ./packages/zongsoft.web@1.0.0_linux-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0_linux-x64.deb
```

### rpm

```bash
rpm -qip ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qpc ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm2cpio ./packages/zongsoft.web@1.0.0_linux-x64.rpm | cpio -t
```

## 参考资料

- Debian Policy Manual: [Binary packages](https://www.debian.org/doc/debian-policy/ch-binary.html)
- Debian Policy Manual: [Binary package format appendix](https://www.debian.org/doc/debian-policy/ap-pkg-binarypkg.html)
- Debian Handbook: [The Packaging System](https://www.debian.org/doc/manuals/debian-handbook/packaging-system.en.html)
- rpm.org: [RPM Package Format](https://rpm.org/docs/4.19.x/manual/format.html)
- Linux Standard Base: [RPM Package File Format](https://refspecs.linuxfoundation.org/LSB_3.1.1/LSB-Core-generic/LSB-Core-generic/pkgformat.html)
- GNU tar manual: [GNU tar](https://www.gnu.org/software/tar/manual/)
