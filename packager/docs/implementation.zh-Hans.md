# Zongsoft.Tools.Packager 实现说明

[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

本文档面向维护者，说明 `Zongsoft.Tools.Packager` 的源码结构、命令执行流水线、包模型、文件收集规则、systemd 脚本生成，以及 `.tar.gz`、`.deb`、`.rpm` 三种包格式的当前实现方式。

面向使用者的安装、配置和发布操作说明见[打包器 README](../README.zh-Hans.md)。

本文的应用示例引用 hosting 中真实的 [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) 宿主，暂存目录、Bash 工作目录和版本约定见 [README 快速开始](../README.zh-Hans.md#快速开始)。宿主 DLL 为 `Zongsoft.Hosting.Web.dll`，`--daemon:zongsoft.web` 指定包和服务标识。自动 Web 配置使用宿主的 web.profile；根路径别名一节另以用户自备的 manual.conf 演示普通载荷。

hosting 根脚本通过 source 选择宿主，并接受框架覆盖值；留空时由工具从变量来源读取框架，编译配置由 `--compilation` 指定。生成服务时，daemon 声明 `Environment,DOTNET_ENVIRONMENT`，Web 另声明 `ASPNETCORE_ENVIRONMENT`；terminal 禁用 daemon，声明变量列表不代表设置交互进程环境。脚本中的编译、部署和升迁制作均发生在本工具调用之外，独立 `pack.cmd` 只收集已有载荷。脚本设置及实际命令见 [快速开始](../README.zh-Hans.md#快速开始)。

## 设计目标

`Zongsoft.Tools.Packager` 的目标是使用纯 .NET 代码生成 Linux 应用安装包，尽量减少对目标系统工具链的打包期依赖。

核心设计取向：

- 对外提供统一入口 `dotnet-pack`，通过 `tar`、`deb`、`rpm` 子命令选择输出格式。
- 在三种格式之间复用应用元数据、变量解析、文件收集、脚本生成和命名规则。
- 默认服务模型面向 systemd，适合 .NET 后台服务和 Web 服务。
- 直接写入包格式原语，而不是调用 `tar`、`dpkg-deb`、`rpmbuild`、`cpio`。
- 支持在 Windows、Linux、macOS 上生成 Linux 包；Unix 主机保留文件权限，Windows 主机使用默认权限规则。

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 初始化终端命令树，注册 `TarCommand`、`DebCommand`、`RpmCommand`。 |
| `PackCommand.Version.cs` | 嵌套 `VersionFile` 负责源版本读取、身份校验、Edition 选择和成功后保存。 |
| `PackCommand.cs` | 三种打包命令的模板方法基类，声明通用命令选项并编排执行流程。 |
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
| `Migration.cs` | 按最终应用身份定位外部升迁产物，验证 PAX，原样收录并提供安装协调脚本。 |
| `ApplicationHost.cs` | 一次解析应用宿主及已有或待生成服务。 |
| `Scriptor.Systemd.cs` | 生成或收集 systemd 单元文件，组合应用、升迁和 Web 生命周期。 |
| `Web/Definition*.cs` | 用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) Profile 收集来源声明，处理整体后端覆盖、继承、变量及字段校验。 |
| `Web/Configurator*.cs` | 配置器契约、Nginx 指令树与校验、稳定序列化、可重定位内容片段。 |
| `Web/Installation*.cs` | 生成载荷冲突校验、交付重定位、激活与卸载脚本。 |
| `Utility.cs` / `TextSource.cs` | 按需展开变量；统一解析源目录文件与直接文本。 |
| `Utility.Search` / `Generator.Entries.cs` | 路径段匹配、目录元数据、受控临时载荷流。 |
| `PackageOptions.cs` | 制作选项的类型化访问、模板求值及工具默认值；变量来源由 Core 评估器管理。 |
| `Dependency.cs` | 解析统一依赖区间和替代分组；由 Debian/RPM 编码器按原生关系语法输出。 |
| `Utility.cs` | RID、安装路径、路径规范化、Unix 时间戳、文件权限等辅助逻辑。 |
| `Dumper.cs` | 控制台输出启动画面、错误和警告消息。 |
| `tools/.shared` 链接源码 | `Utility.cs` 与本项目的 `partial Utility` 合并编译，共用递归变量与命令/文本方法；`ArtifactPublisher` 统一管理暂存与发布，单文件原子替换，多文件成组提交并在失败时恢复。布尔开关使用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) `Switch`，枚举沿用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 转换且不检查成员定义，不生成共享 DLL。 |

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
    B --> C["Resolve source and load manifest or identifier"]
    C --> D["Select and validate identity, then create invocation variables"]
    D --> E["Normalize source/output paths"]
    E --> F["Create Package.Tar/Deb/Rpm"]
    F --> M["Locate and validate optional migration artifacts"]
    M --> G["Resolve application host"]
    G --> H["Load ordinary package entries"]
    H --> N["Attach unchanged migration archive and launcher"]
    N --> W["Load Web Profile, generate and attach hoster configuration"]
    W --> L["Generate service and lifecycle scripts; validate targets"]
    L --> V["Replace installation root .version with memory entry"]
    V --> I["Call package.Pack(output, overwrite)"]
    I --> J["Generator stages and commits package output"]
    J --> S["Save both source files if .edition exists; otherwise save .version or create both"]
    S --> OK["Report success"]
```

`PackCommand<TPackage>` 做通用工作，子类只负责创建具体 `Package`：

```csharp
protected override Package.Deb CreatePackage(CommandContext context, PackageOptions options)
```

`RpmCommand` 额外读取：

- `--provides`
- `--conflicts`

这两个选项按逗号或分号拆分，最终写入 RPM metadata header。

## 源版本与包内版本

`VersionFile` 使用 `File.OpenRead` 和 `ApplicationManifest.Load(Stream)` 读取源目录直属 `.edition`。仅 `FileNotFoundException` 才允许回退 `.version` 并调用 `ApplicationIdentifier.Load(Stream)`；空标识、损坏文件、目录占位和其他 I/O 错误立即失败。不搜索其他目录。

清单依次选择显式非空 Edition、Current、唯一 Edition；多个且无选择时报错，无具名 Edition 时用顶层版本。显式选择须存在并保留清单拼写。标识回退允许显式 Edition 替换或补充文件中的 Edition。显式名称须与源名称忽略大小写一致；显式版本覆盖所选版本，最终版本非零。身份只取显式选项及源文件，环境和 `.env` 可通过显式变量引用使用。

载入的清单在内存中原位替换选中 Edition，再将 Current 设为该项，保留其他条目、顺序和 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 的注释及空行布局。制包前准备待保存内容；`Save(TextWriter)` 使用 UTF-8 无 BOM、CRLF 写入器。标识字节采用 `ApplicationIdentifier.Save(Stream)`；读取使用 `ApplicationIdentifier.Load`，末尾有无 LF、CRLF 换行均可，验证只检查解析后的身份，不限制末尾换行。

所有包产物提交成功后才保存源文件：已有清单通过 `ArtifactPublisher` 成组回写 `.edition` 和 `.version`，覆盖已有标识或创建缺失标识，标识记录最终选定的名称、Edition 和版本；仅有标识时只原子更新 `.version`；两者均无时要求显式有效名称和版本，成组暂存及提交两文件，不覆盖并发创建的文件。双文件保存失败回滚。源保存失败保留包并报告受影响路径；解析、校验及制包失败不保存源文件。

`EntryCollection.Load` 排除源直属 `.edition`（含显式别名），指向安装根 `.edition` 的文件项也排除；其他子目录文件沿用普通选择规则。`SetVersion` 写入唯一的安装根 `.version` 内存项，权限 `0644`，替换同目标普通及根别名项，不受排除规则影响。全部编码器及 RPM 摘要通过 `Entry.OpenRead()` 读取；子目录中的 `.version` 保持普通载荷行为。

Debug 引用本地 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 构建，Release 引用集中配置的 NuGet 包，两者须提供 `ApplicationManifest`、`Editions.Current` 和 `ProfileOptions.Directives`；Web 加载通过 `ProfileDirectiveBehavior.Strict` 要求导入文件存在。

## 命令选项模型

通用必填及条件必填选项：

| 选项 | 类型 | 说明 |
| --- | --- | --- |
| `--name` | `string` | 源版本文件缺失时必填；否则校验名称或使用源名称。 |
| `--version` | `string` | 展开后转换为 `System.Version`；源版本文件缺失时必填，否则覆盖所选版本。最终版本不能为零。 |
| `--platform` | `string` | 展开后转换为 `Platform`，指定目标平台。 |

常用可选项：

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--source` | 当前目录 | 输入目录。 |
| `--migration` | 空 | 升迁制作时的输入名称，可带目录；裸名称从源目录向父目录查找，每层还查直属 `.migration/`，按最终 Edition、Version、Runtime 匹配。 |
| `--output` | `source` | 始终作为输出目录；相对路径基于 `source`，不支持指定文件名。 |
| `--exclude` | 空 | 加载打包项时跳过的文件模式列表，多个模式用逗号或分号分隔。 |
| `--edition` | Current 或唯一 Edition | 产品 Edition，参与包名。 |
| `--framework` | `framework` 变量或空 | 可选 .NET 目标框架，用于查找 `bin/<compilation>/<framework>` 中的宿主；省略选项时按变量来源查找；显式空值终止回退。 |
| `--compilation` | `Release` | 可选 .NET 构建配置，用于上述宿主目录查找，也可作为变量引用。 |
| `--architecture` | `x64` | 目标架构。 |
| `--overwrite` | `false` | 是否覆盖已存在的输出文件。 |
| `--install-path` | 由包名推导 | 安装目录。 |
| `--title` | 空 | 人类可读标题。 |
| `--summary` | 空 | 短描述；可为文件路径。 |
| `--description` | 空 | 长描述；可为文件路径。 |
| `--homepage` | `https://github.com/Zongsoft` | 项目主页。 |
| `--license` | 空 | 许可证文本。 |
| `--category` | 格式默认 | Debian `Section` 或 RPM `Group`。 |
| `--maintainer` | `Zongsoft` | 软件包维护者。 |
| `--manufacturer` | `Zongsoft` | 软件生产厂家；未指定、null、空字符串使用默认值，纯空白不触发默认值。 |
| `--dependencies` | 空 | 统一的 `name[:range]` 依赖列表；区间语法支持 `[v)` 简写及 `|` 替代项。 |

systemd 与生命周期脚本选项：

| 选项 | 说明 |
| --- | --- |
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

`PackCommand<TPackage>.CreateEvaluator(context, directory)` 组合命令选项、指定目录直属 `.env`（含显式导入） 和系统环境，工具启用 Fallback，默认值由 Core 在全部普通变量缺失后查询。省略 directory 时跳过 `.env`，供第一次解析 source 使用。源目录存在并绝对化后重新加载变量并固定 source；`.env` 不参与 source 的反向推导。配置及命令选项名不区分大小写，环境变量按平台规则查询；全局变量优先级为显式选项 > source 直属 `.env`（含显式导入） > 环境变量 > 默认值。

共享 `Utility.LoadEnvironmentProfiles` 只加载 source 直属 `.env`，不搜索父目录或子目录；其他文件通过 `#@import` 显式导入。使用 `Profile.Load` 保留 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 的空值和导入语义，章节层级以点号连接为命名空间，条目名中的点号和连字符改为下划线；读取或解析异常终止制包，仅缺失文件跳过。不写入进程环境变量。

变量契约使用 `Zongsoft.Common.IVariables`。命令选项直接使用 `context.Options`，工具计算值保存在 Core `Variables` 中，`.env` 保留 `Profile.ToVariables()` 视图，系统环境直接使用 `Variables.Environments()` 实时读取，不复制、不改写名称或值。环境来源只提供默认命名空间，Windows 忽略大小写，Unix/Linux 区分大小写；参与模板的路径值使用 `/`，其余转义遵循 Core。`TemplateEvaluatorOptions.Fallback` 显式设为 true；同一命名空间按 Providers 顺序查询，全部未找到才逐级进入父命名空间及全局，最后允许来源自身已声明的默认值。首个命中即生效，包括 null。指令参数评估沿用父评估器的 Fallback、Recursive、Culture 和 MaximumDepth；不带命名空间的模板无需额外语法。默认空间用 `${name}`，具名空间用 `${io.rustfs:access_key}`；章节层级以点号连接，条目名及命令选项名中的点号和连字符转换为下划线，非法配置名称不提供变量，配置名称冲突仅在查询时失败。

命令选项集合 context.Options 只注册一次，放在配置及系统环境之前。Core VariablesExtension.TryGetValue 按每一级命名空间查询全部来源，普通查询均传入 false；全局也全部未找到后，才以全局命名空间传入 true 查询已声明的默认值。因此全局优先级为显式选项、source 直属 .env（含显式导入）、系统环境、命令默认值。HasDefaultValue 区别未声明与显式 null，缺省不合成类型零值。工具直接读取、递归模板和指令参数评估共享 Fallback 设置；计算出的覆盖值放在最前面的 Variables 中。

模板直接通过 Core `TemplateEvaluator.Evaluate` / `TryEvaluate` 求值，启用 `Recursive`，默认上限为 64（根模板和递归字符串均计层）。所有输入遵循 Core 转义；`\${name}` 输出字面引用，`\\` 输出字面反斜杠。路径优先使用相对路径，绝对路径使用 `/`，例如 `../.shared/${product}.env` 或 `D:/deploy/${scheme}`。未知变量、循环和深度超限按 Core 错误契约处理。

`.env` 的指令参数通过 `Directives.Processing` 求值，查找顺序为显式选项、当前文件已读内容（含已完成导入）、环境/默认值。不读取后文，不隐式查找导入父文件的局部变量。制作清单的回调先查本次命令的实际值，再查当前 Profile，全部未命中后才查描述符默认值。每条 import 使用完整参数导入一个文件，原始声明和条目值不被模板结果改写。Profile 只负责读取和导入，模板评估由工具显式发起。


所有变量（包括 `framework`）统一遵循首个命中生效：null、空字符串、false 和 0 都不会触发下层回退。只有未提供该值时才继续查找；需要有效值的业务操作负责校验并在无法继续时报告错误。

`PackCommand` 为每次调用建立独立的 `PackageOptions` 选项对象，并传给包、脚本和文本来源；不保留进程级变量状态。访问值时递归展开引用，未使用的未知引用不会阻止制包。未知变量、循环引用及超过 64 层的展开失败，诊断指出变量名。展开不读取文件。

[Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 命令描述符将可能含变量的选项保留为字符串；`source` 先由完整原始变量集展开。显式 `name`、`edition`、`version` 随后展开，`version` 再转为 `System.Version`，用于从源目录的 `.edition` 清单文件或 `.version` 版本标识文件选择版本。确定最终身份后，`platform`、`architecture` 与 `overwrite` 在使用时展开并转换。裸 `--overwrite` 仍为 true，未指定时为 false。

身份仍由源目录的 `.edition` 清单文件或 `.version` 版本标识文件与显式 name/edition/version 选项共同确定，不从同名环境变量或 `.env` 变量隐式替代身份；显式选项可引用 `.env` 中的其他变量。最终身份及已解析的 source/output 覆盖变量集合。`--migration` 仍须显式启用，`--overwrite` 可从环境变量或 `.env` 提供并由命令行覆盖。

### 变量语法

Core `TemplateEvaluator` 支持两种变量引用形式：

```text
${name}
${namespace:name}
```

示例：

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --daemon:zongsoft.web \
  --name:"$APP_NAME" \
  --version:"$APP_VERSION" \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

此示例的身份选项由 Bash 展开；`--version` 作为字符串进入 `OnExecuteAsync`，展开后才按 `System.Version` 解析，名称与 Edition 按传入值校验。打包器变量表达式也可用于后续路径、文本和升迁配置。

源路径、输出、载荷、排除表达式、文本和升迁输入引用未知变量时均失败；未使用的变量不展开。`TryEvaluate` 返回 Core 异常，调用方不得把失败结果继续作为有效输入。

### 文本与文件

`TextSource.Read(source, value, options, fileOnly)` 统一处理 summary、description 和生命周期钩子：

- `text:` 后内容原样返回，适用于含 Shell `${___}`、`${___}` 或路径样式的字面文本。
- `file:` 后内容先展开变量，然后按绝对路径或相对 source 的路径读取；文件不存在报错。
- 无前缀时先展开变量；多行值为文本，已有文件按 source 读取，明显的缺失路径报错，其他单行值为文本。可用前缀消除歧义。
- 读取后的文件内容不展开变量，也不会再次解释成另一个文件路径。
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
- `Manufacturer`
- `License`
- `Homepage`
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

输出文件名由 `Package.GetFileName` 统一生成，三种格式使用以下规则；这里的 `name` 为包标识 `identity`，仍遵循上述 daemon 优先规则：

```text
<name>@<version>-<architecture>.<extension>
<name>-<edition>@<version>-<architecture>.<extension>
```

示例：

```text
zongsoft.web@1.0.0-x64.deb
zongsoft.web-enterprise@1.0.0-x64.rpm
```

文件名中的架构取 `Architecture.ToString().ToLowerInvariant()`，例如 `x64`、`arm64`；扩展名为 `tar.gz`、`deb`、`rpm`，文件名由名称、Edition（可选）、版本及架构组成。tar 的 `.sh` 入口采用同一文件主名。Runtime Identifier 用于匹配升迁产物，不参与安装包文件名。

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
- [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) `Searcher` 统一支持任一路径段中的 `*`、`?`，独立段 `**` 匹配零层或多层目录。每个参数位置按相对于固定前缀的路径（`/` 分隔）Ordinal 排序，不重排全部输入；Windows 匹配忽略大小写，Unix 区分大小写。
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

以下示例假设 `publish/manual.conf` 已由用户准备，仅演示自备配置的根路径别名，不启用 `--web`：

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  manual.conf:/etc/nginx/conf.d/zongsoft.web.conf
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
4. 如果文件不存在，在内存中生成 `.service` 文件并加入包条目，不使用固定临时路径。服务文件名先校验，再加入条目。

### 宿主定位

生成 `.service` 文件时需要定位 .NET 宿主。查找顺序：

1. `<source>/<name>.dll`
2. `<source>` 下唯一 `.exe`，并推断同名 `.dll`
3. `<source>/bin/<compilation>/<framework>/<name>.dll`
4. `<source>/bin/<compilation>/<framework>` 下唯一 `.exe`，并推断同名 `.dll`

`framework` 或 `compilation` 为空或全空白时跳过第 3、4 步。两个选项都不触发编译，普通文件打包、源目录中的 .NET 宿主和现成服务文件均不要求提供两项。纯文件包使用 `--daemon:none` 可直接跳过宿主解析。

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

监听来自应用运行配置。服务生成器不追加 --urls，已有服务的 ExecStart 不改写。

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

禁用 systemd 时，应用服务操作退化为 no-op；升迁以及 Web 交付、激活、卸载步骤保持独立，卸载后仍会删除安装目录。

不同包格式会在写入生命周期脚本时应用各自的卸载保护：

- Debian 的 `prerm` 仅在 `remove` 或 `deconfigure` 时执行 `Uninstalling`，`postrm` 仅在 `remove` 或 `purge` 时执行 `Uninstalled`；`upgrade`、`failed-upgrade`、`abort-install`、`abort-upgrade` 和 `disappear` 不执行卸载清理。
- RPM 的 `%preun` 和 `%postun` 仅在 `$1=0`（最后一个已安装实例被删除）时执行卸载脚本；当 `$1>0` 时保留安装载荷。
- Tar 包只有显式执行 `uninstall.sh` 才进入卸载生命周期；生成器统一删除解析后的 `TARGET`，默认 `Uninstalled` 脚本无额外目录删除操作。

## Web 配置与安装集成

使用语法及部署要求见 [Web 指南](web.zh-Hans.md)。Web 类型位于同一项目的 Web 子命名空间，采用 Definition 输入/有效模型、Configurator.Context/Result 和嵌套 Configurator.Nginx，不添加动态插件加载。

Definition.cs 提供 Load/Resolve 入口；Definition.Loader.cs 收集声明、组织段落并校验结构；Definition.Resolver.cs 集中合并声明、求值和生成有效模型，按绑定与资源、后端策略、健康检查、请求头、原始指令及基础值解析分区。字段值转换属于 Resolver，不单独拆分 Values 文件；Definition.Model.cs 保存模型类型。

加载通过 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) Profile.Load（Directives = { ProfileDirectiveOptions.Import(ProfileDirectiveBehavior.Strict) }），用 Loading 收集合并前的引用者声明，Loaded 收集各完成来源（包括根配置），保留 Profile 实例身份。同一层级的后端池按输入实例整组替换，不能直接枚举最终合并条目。公共字段及层级先校验，随后确定所选托管器覆盖关系，最后仅展开实际消费的值。指令参数及被消费的配置值直接使用 Core TemplateEvaluator，遵循相同的模板转义规则。

Resolver 生成不可变站点、路径及策略记录；Routing 按请求类别及字面基路径组合应用声明、选择可实现的上游，并区分实际监听与连接目标；Nginx 生成器建立指令树，校验原始叶指令上下文/基数、静态监听冲突和结构化绑定关系，再序列化为 UTF-8、Tab、CRLF。安装根是有类型的 ContentPart，不是可被用户文本碰撞的占位符。公共字面值不被当作 Nginx 运行时表达式；不能安全表示的值明确报错。

ApplicationHost 在普通载荷加载前解析一次宿主和服务。Web 生成条目经 Entry.OpenRead 编码；Installation.Validate/ValidateEntry 在最终目标空间检查普通条目、别名、祖先/子路径冲突，检查与加入顺序无关。输出失败不保存源版本，Profile 输入从不写回。

Package.InstallScripts 的 Delivered 独立于四个生命周期。Tar 复制载荷后先执行 Delivered，再按 DESTDIR 门禁执行 Installed；Debian postinst 仅在 configure 中执行 Delivered/Installed；RPM 在 %post 中执行。生成 .conf 是普通可覆盖载荷，不具有 DEB conffile 或 RPM config 标志。

阶段组合如下：

- 交付：清理弃用生成文件及目标归属匹配的链接，Tar 按 INSTALL_PATH 重建含安装根引用的配置。DESTDIR 只改变写入位置，不改变配置内路径，不操作系统链接。
- 安装完成：preinstalled → 升迁准备/apply（若指定）→ installed → Web 激活 → postinstalled；前项失败阻止后项。自定义主钩子及 daemon:none 不取消 Web 步骤。
- 卸载前：preuninstalling → Web 解除加载/按开关校验与重载 → uninstalling → postuninstalling。
- 卸载后：载荷删除 → preuninstalled → uninstalled → .web 清理 → postuninstalled；升级旧版本卸载由现有格式门禁跳过。

激活使用 HOSTER_WEB_ACTIVATION，读取安装时值。固定默认 nginx.conf/nginx.service；nginx -T 确认所生成链接确实被加载。停止状态只校验，运行状态才重载。安装严格失败；普通卸载中的 Nginx 失败警告继续，文件操作失败仍报错。开关关闭不阻止真实文件交付、弃用清理或最终卸载清理。

无新 Web 结果也生成旧产物清理片段，避免取消 --web 后遗留站点。只有当前或旧产物实际涉及 Web 时才调用 Nginx。默认 .web 布局本身就是容器化发现契约，不生成 .hoster 或额外模板。

测试分为声明/有效模型、原生输出、三格式实际解包及隔离 Shell 替身。Shell 夹具把所有系统路径替换为临时目录，并使用假的 nginx/systemctl；不安装包、不操作真实服务。Windows 上依次通过 `PACKAGER_TEST_GIT`、PATH、Git 安装信息及常见安装目录，选择同一套安装中的 Bash 与 cygpath；设置 `MSYS=winsymlinks:nativestrict`，并通过 `cygpath -u` 转换 Windows 路径，使 `/tmp` 等挂载别名与解析后的链接目标一致。运行 Shell 测试前，在临时目录中检查所需工具及符号链接能力；环境不支持时注明原因并跳过，不阻断构建或发布。环境检查通过后，脚本执行失败和断言失败仍正常报错。回归测试覆盖带空格的挂载路径、便携安装、不完整工具组合及工具不可用。这些测试不等于真实安装验证；目标 Linux、Nginx 模块、证书加载和重载应在具备实际依赖的隔离环境中验证。配置契约、模块要求和部署行为见 [Web 配置指南](web.zh-Hans.md)。


## 应用元数据

主页的选项名和变量名为 `homepage`；`Package.Homepage` 提供 tar PAX `Homepage`、Debian `Homepage` 与 RPM `URL`（1020）的值。tar PAX `Maintainer`、Debian `Maintainer` 与 RPM `PACKAGER`（1015）保存包维护者，与生成工具身份独立。`manufacturer` 提供 `Package.Manufacturer`，未定义、null 或展开后为空字符串时使用共享默认值 `Zongsoft`。访问器在通用规范化把纯空白归为空字符串之前保留该原始值。`maintainer` 选项的默认值也为 `Zongsoft`，两者独立保存。

tar 以 PAX 全局属性 `Manufacturer` 保存厂家，Debian 写入自定义 control 字段 `Manufacturer`，RPM 写入标准 `VENDOR` 字符串标签（1011）。Debian 沿用既有文本规范化，包括不写入纯空白的厂家字段；这些元数据不增加载荷条目。参见 [Debian 自定义字段](https://www.debian.org/doc/debian-policy/ch-controlfields.html#user-defined-fields)与 [RPM 标签说明](https://rpm.org/docs/latest/manual/tags.html)。

## 打包器版本元数据

完整字段映射见 [README 应用元数据](../README.zh-Hans.md#应用元数据)。`Generator.GetSummary` 按 Summary、Title、应用 Name 顺序选择首个非空白值；`GetDescription` 使用非空白 Description，否则回退到有效摘要。三格式共用这两个方法。Debian 组合规范化的单行摘要及独立长描述续行，保留内部缩进，空行写为 ` .`；两个文本相同时仅写摘要。Tar 对两个文本字段转义，RPM 保留原始文本。三格式均省略空白 License 与 Homepage；Debian 必填 Maintainer 的 `Unknown` 回退，以及原生分类默认值 `utils` / `Applications/System`，保留格式差异。

Debian 在原生 KiB 单位的 `Installed-Size` 之外，增加按不变区域格式输出的字节数 `PackageSize`，并增加 `InstallPath`。RPM 将非空白安装路径写入应用字符串标签 `1000003`，同时保留已弃用的 `1056`，兼容既有读取器。应用标签仅作声明；使用 `PREFIXES` 会声明生命周期脚本未提供的重定位能力。现代 RPM 查询格式不暴露未注册的应用标签或已弃用的 `DEFAULTPREFIX` 名称，读取器须按编号检查 Header 条目。

共享 BindingDocument 模型生成安装根 JSON .bindings；三个编码器在发布前通过 EntryCollection.SetBindings 写入普通文件，权限 0644、epoch 时间，计入大小与文件清单。Containerizer 按安装根读取并校验。逻辑 UTF-8 上限 1 MiB、深度 32，禁止重复条目和链接。完整定义及实施边界见 [文件规范](#binding-document)。PackCommand 调用 BindingExtractor 提取应用绑定；Web/Routing 按应用声明组合站点，Nginx 生成器将最终前端绑定、请求类别路由、目标与策略合入同一文档。

### 应用绑定提取

BindingExtractor 在输入选择、排除、目标映射及生成条目完成后、发布产物和回写源版本前运行。PackageFiles 将 tar 相对条目映射到 InstallPath，将 deb/rpm 和 rooted 条目映射到最终绝对安装路径；三格式使用同一虚拟根。它只提供最终载荷条目，PhysicalPath 为空，读取流由 BufferedStream 包装，防止 Core 将 FileStream 转为物理文件来源。PackageFiles 仅负责虚拟文件访问。

[BindingExtractor.Configuration.cs](../src/BindingExtractor.Configuration.cs) 中的私有 ProcessInstruction 回调将 zongsoft.tools.binding 指令标记为已接管，并将 Import 的 / 路径转换成等价相对路径。它与 PackageFiles 共用私有 NormalizePath 方法；Core 继续处理作用域、覆盖、可选缺失、循环及深度。绑定值在加载完成后求值，不在回调中提取，也不注册 Core 全局指令处理器。

每个最终 .option 独立加载，导入完成后遍历提供程序的 Instructions。精确 path 选中单项，末尾 /* 只枚举直接子项；filter 先筛选，再求值 template/protocols。ConfigurationView 通过 Core TemplateEvaluator 的 Resolving 回调和 MemberExpression 提供显式 this 导航；this 是当前候选配置项的只读视图，也是唯一允许的根名称，忽略大小写。字符串路径相对当前候选项，前导 / 从本次配置根读取。对已选字段的显式字符串索引才解析 Settings，动态键同样以 this 为根，例如 `${this.Settings[this.Key]}`；TryGetValue 区分缺失与空值。求值器不注册环境/命令来源，关闭递归与回退。

共享 FilterExpression 负责条件拆分、否定、候选值匹配及从左到右组合；deployer 保留变量查询和 TFM 专用比较。地址模板按 separators 切分，协议模板按逗号切分，由 Core 网络值类型及 BindingDocument 规范化。每个候选项按基路径及协议集合分组，端点相交的组传递合并；端点不相交、基路径或能力不同的结果独立保存。required 在全局去重前逐声明检查，非法地址及所选字段错误不降级为空集合。来源仅用于诊断，不进入 JSON 或身份。

例如 /opt/demo/web.option 导入 ./shared.option，读取最终 /opt/demo/shared.option；写 /opt/demo/shared.option 也选择同一条目，写 /shared.option 则选择虚拟根中的另一位置。shared.option 如果也含 binding，仍独立读取；两次结果相同才合并，不按 PI 文本或文件名跳过。完整输入、结果来源与失败示例见 [提取说明](#binding-document)。

每个安装包自动记录当前生成工具的身份，逻辑内容为 `Packager:Zongsoft.Tools.Packager@<assembly-version>`。值采用 `程序集名@版本号`，从打包器自身程序集读取，独立于宿主应用版本；无需指定额外选项或启用升迁。

| 格式 | 存放位置 | 查看方式 |
| --- | --- | --- |
| tar.gz | PAX 全局扩展属性 `Packager` | 使用支持 PAX 的归档读取器，例如 Python `tarfile` 的 `pax_headers["Packager"]`。 |
| deb | `control.tar.gz` 内 `control` 的 `Packager` 字段 | `dpkg-deb -f <安装包.deb> Packager` |
| rpm | 主 Header 的 `RPMVERSION` 字符串标签（1064） | `rpm -qp --queryformat '%{RPMVERSION}\n' <安装包.rpm>` |

RPM 用生成工具版本标签保存本工具身份；其 `PACKAGER` 标签（1015）保存 `--maintainer` 的维护者信息。元数据位于格式头中，不增加安装目录文件，也不改变 `.version` 或 `migration.json`。

`Generator.GetIdentity` 通过 `Assembly.GetName()` 读取自身程序集的简单名称和 `Version`，保留版本对象的完整文本，不写死工具版本号。三个生成器调用同一方法读取身份，不取调用进程或宿主程序集版本。主 Header 的 RPM 摘要覆盖该标签。

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
zongsoft.web@1.0.0-x64.tar.gz
zongsoft.web@1.0.0-x64.sh
```

该脚本定位同目录下的 `.tar.gz`，解压到临时目录，并调用解压后的 `install.sh` 完成安装。

### 归档结构

```text
<application files>
.root/<rooted files>
install.sh
uninstall.sh
```

归档开头包含 PAX 全局扩展记录，它不是安装文件。`GetTarMetadata` 写入 `Packager`、`PackageName`、`Version`、`Manufacturer`、`Architecture`、`Summary`、`Description`、`PackageSize`；还写入非空白的 `License`、`Homepage`、`Maintainer`、`InstallPath`、`Dependencies`、`Category`。不写入 `BuildTime` 属性。

`PackageName` 使用 `package.PackageName`，保留 daemon 身份和 Edition 确定的最终系统包名，不使用应用身份或归档文件名。`Version` 保留 `package.Version.ToString()` 的完整文本，与生成的 `.version` 一致。`Architecture` 使用 `package.Architecture.ToString().ToLowerInvariant()`，与文件名的架构值一致（如 `x64`、`arm64`、`x86` 或 `arm`）。全部属性在归档改名后仍可读取，不增加选项或载荷条目。

`Summary` 按 Summary、Title、应用 Name 顺序选择首个非空白值；Description 为空白时回退到有效摘要。由于 .NET PAX 写入器不接受属性值中的实际换行，这两个字段将反斜杠、CR、LF 分别转义为 `\\`、`\r`、`\n`，读取时解码还原原文，Unicode 保持原样。可选空白值省略，既有 Manufacturer 的纯空白行为保持不变。`Category` 只使用包模型值，不设置格式默认分类；`InstallPath` 描述默认路径，不改变安装时覆盖规则。

`Dependencies` 使用 `Dependency.Split` 与 `Dependency.Parse` 校验统一输入语法，再以 `; ` 连接各组，保留区间、原生版本端点及替代项。该字段仅作声明，tar 安装器不检查或安装依赖；非法声明在产物发布前失败。`PackageSize` 使用 `Package.GetPackageSize()` 按不变区域格式输出字节数，包含生成条目和 rooted 文件，不含容器头及 tar 安装/卸载脚本。目录贡献为零，生成的空 .bindings 文档计入 38 字节，不表示文件系统实际占用。

rooted 文件只有存在根路径别名时写入 `.root/`。生命周期脚本内容写入 `install.sh` 和 `uninstall.sh`。

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
执行 Delivered 内容，包括 Web 弃用清理和路径重定位
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
PackageSize: <payload-size-in-bytes>
Maintainer: <maintainer>
Manufacturer: <manufacturer>
Homepage: <homepage>
License: <license>
InstallPath: <default-installation-path>
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
- `License`、`Manufacturer`、`Packager`、`PackageSize`、`InstallPath` 作为额外字段写入。可选空白字段省略，`PackageSize` 始终记录精确载荷字节数，包含生成的 .bindings 文件；`Installed-Size` 保留向上取整的 KiB 估计值，最小为 1。`Packager` 与宿主 `Version`、`Maintainer` 分别保存不同信息。

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

只有 signature header 尾部补齐到 8 字节。主 metadata header 的 store 后紧跟压缩 payload；否则 rpm 查询可能成功，但 rpm2cpio 会读到 gzip 之前的零字节，无法解包。

当前写入：

| Tag | 内容 |
| --- | --- |
| `62` | Signature 不可变区域，BIN 类型、16 字节 trailer。 |
| `269` | metadata header 的 SHA-1 digest。 |
| `273` | metadata header 的 SHA-256 digest。 |
| `1000` / `270` | metadata header + payload 的字节长度，无符号 32 位 / 64 位。 |
| `1004` | metadata header + payload 的 MD5 digest。 |

signature section 末尾按 8 字节对齐。两个 header 的索引均按 tag 升序写入；主 header 的不可变区域 tag 为 `63`。区域 trailer 位于 store 尾部，其负偏移为区域索引字节数，摘要覆盖完整 metadata header（包含 magic、索引、store 与 trailer）。这是完整性摘要，不是发布者的 OpenPGP 签名。格式依据 [RPM V4 格式](https://rpm-software-management.github.io/rpm/manual/format_v4.html) 和 [Header 结构](https://rpm-software-management.github.io/rpm/manual/format_header.html)。

### Metadata Header

当前写入的主要内容：

- 包名、版本、release。
- 摘要、描述、构建时间、构建主机。
- 包大小、许可证、生产厂家、维护者、分类、主页地址。
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
| `1009` / `5009` | 载荷文件字节数，无符号 32 位 / 64 位。 |
| `1011` | 生产厂家/供应商。 |
| `1014` | 许可证。 |
| `1015` | 维护者/打包者。 |
| `1016` | 分组。 |
| `1020` | URL。 |
| `1021` | OS，固定 `linux`。 |
| `1022` | RPM 架构。 |
| `1023..1026` | pre/post install、pre/post uninstall 脚本。 |
| `1028` | 无符号 32 位文件大小数组。 |
| `1030` | 文件模式数组。 |
| `1034` | 文件修改时间数组。 |
| `1035` | 文件 SHA-256 digest 数组。 |
| `1037` | 文件 flags；`/etc` rooted 文件标记为配置文件。 |
| `1039` / `1040` | 用户名/组名，固定 `root`。 |
| `1048..1050` | Requires flags/name/version。 |
| `1047`, `1112`, `1113` | Provides name/flags/version。 |
| `1053..1055` | Conflicts flags/name/version。 |
| `1056` | 已弃用的默认安装路径，兼容既有读取器。 |
| `1064` | 生成工具身份，`程序集名@版本号`。 |
| `1046` / `271` | 未压缩 cpio 归档字节数，无符号 32 位 / 64 位。 |
| 1000003 | 应用字符串标签：默认安装路径。绑定 JSON 保存在 payload 普通文件中。 |
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

Requires 使用下文的统一依赖模型。Provides、Conflicts 保留以下原生关系表达式：

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

布尔 Requires 还会添加 `rpmlib(RichDependencies) <= 4.12.0-1`。完整的括号表达式存入依赖名称，flags 为 `0`、version 为空。能力依赖使用 `RPMLIB | LESS | EQUAL`；端点含 `~` 或 `^` 时，还分别声明 `TildeInVersions <= 4.10.0-1` 或 `CaretInVersions <= 4.15.0-1`。这些是能力标识版本，不是 RPM 产品版本。

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

## 本地搜索与源链接

本地模式由 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) Searcher 处理。搜索结果保留逻辑名称，读取实际目标。选中目录链接作为载荷根时允许展开，内部目录链接跳过，文件链接按原名称读取目标内容。递归模式不穿过目录链接匹配后续段。选中链接悬空或循环会在输出写入前失败；目标路径校验继续执行。

载荷相对路径以 source 为基准。单模式结果按逻辑相对路径执行 Ordinal 排序，多参数顺序不变。参见 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) [本地搜索](../../../framework/Zongsoft.Core/docs/searcher.zh-Hans.md)。

`Searcher.Search` 通过 `Searcher.Target` 选择文件、目录或两者（默认 Both）；`Match.Origin` 提供逻辑固定目录前缀，用于计算相对输出路径。

## 载荷流与目录条目

`Package.Entry.IsDirectory` 区分目录与文件。目录没有内容流、大小为零；生成器统一补齐父目录，文件与目录目标冲突时失败。链接源使用逻辑名称及目标内容，选中的目录链接可作为载荷根，内部目录链接跳过；目标路径不能包含 `..`、换行或 NUL。tar 根别名目录使用 `install -d` 按模式创建，卸载仅对显式目录执行 `rmdir`，非空目录保留，合成的共享父目录不主动删除。

Debian 的 control/data gzip tar 分别写入受控临时文件，ar 依据实际长度流式复制。RPM 原始 cpio 与 gzip payload 使用临时文件，压缩载荷 SHA-256、主 Header + payload MD5 均通过流计算，最后顺序写入各 Header 与载荷，避免完整包体数组。小版本内容和 Header 元数据仍保留内存处理；元数据内存随条目数增长，载荷不随文件字节数增加托管分配。临时文件使用独占 CreateNew、DeleteOnClose，Unix 模式 0600；正常结束及异常均释放。需要足够临时磁盘空间，RPM 峰值包括原始及压缩载荷；RPM 字段的整数大小上限仍然适用。

## 统一依赖

`PackageOptions.Dependencies` 先展开变量，再由 `Dependency.Split` 分割顶层逗号/分号组；区间及 RPM capability 括号内的标点保持完整。`Package.Dependencies` 仍为字符串数组，两种编码器均调用 `Dependency.Parse` 得到 AND 组及组内 OR 替代项，每项包含名称、原始上下界字符串及是否包含边界的标志。

语法为 `name[:range]`：单独包名表示不限版本，以数字开头的裸版本表示包含下界。支持标准 NuGet 风格区间，并将 `[v)` 作为 `[v,)` 的别名；`[v]` 表示精确版本，`(v,)` 表示不含下界，`(,v]` / `(,v)` 表示上界，`(,)` 表示不限版本。缺省端点必须使用开边界。`libc6:any` 等包名限定仍属于名称；字母开头的原生版本请使用括号。保留原生 epoch、修订号及版本比较语义，不进行 NuGet 归一化、排序、浮动版本解析或矛盾区间合并。旧比较表达式、错误括号、浮动 `*`、空替代项及控制字符使用本地化诊断报错；空列表项及重复约束保留既有列表行为。

Debian 将区间转换为一或两个原生比较，严格边界映射为 `>>` / `<<`；对替代项的比较应用分配律：`foo:[1,2) | bar:[3)` 转换为 `foo (>= 1) | bar (>= 3), foo (<< 2) | bar (>= 3)`。每个输入组最多展开为 1024 个关系组，超限在发布产物前报错。最终名称及版本通过既有 Debian 关系校验。

RPM 的单比较使用普通 flags/name/version 条目；有限区间转换为 `(foo >= 1 with foo < 2)`，替代项转换为 `((foo >= 1 with foo < 2) or bar >= 3)`。`with` 要求同一个包满足两个端点；替代依赖需要 RPM 4.13+，`with` 需要 RPM 4.14+。Debian 没有对应的单提供者运算符，其虚拟依赖的两个端点可能由不同提供者分别满足。两种格式均保留自身的版本比较及提供者语义，统一的是输入表示法。参见 [Debian 关系字段](https://www.debian.org/doc/debian-policy/ch-relationships.html)和 [RPM 布尔依赖](https://rpm.org/docs/latest/manual/boolean_dependencies)。

## Debian 关系字段

`DebCommand` 提供 `--provides`、`--replaces`、`--breaks`、`--conflicts`、`--recommends`、`--suggests`，分别写入同名首字母大写的 control 字段。这些选项保留原生语法：列表以逗号或分号分隔，关系写为 `name (>= version)` 等括号形式，支持 `<< <= = >= >>`；Recommends/Suggests 可用 `|` 表达替代项，Provides 的版本关系仅允许 `=`。`--dependencies` 则使用统一解析器：`--dependencies:"aspnetcore-runtime-10.0:[10.0)"` 写出 `Depends: aspnetcore-runtime-10.0 (>= 10.0)`。`Package.Deb` 格式化解析模型并独立校验输出字段，不复用 RPM 解析器。无值不写字段，拒绝非法包名、关系、换行和 NUL；二进制 control 不接受源包的架构限制及构建 profile 表达式。规则依据 [Debian Policy 关系字段](https://www.debian.org/doc/debian-policy/ch-relationships.html)。

## 当前实现边界

- Debian 固定 gzip control/data tar，RPM 固定 gzip cpio，暂不提供 xz/zstd。
- RPM 直接写格式，不调用 rpmbuild，不提供 spec 或 GPG 签名。
- 文件所有者/组固定为 root，不继承构建机 UID/GID，也不提供自定义所有者选项。
- systemd 是当前唯一脚本生成策略。
- 文件链接读取目标内容，选中的目录链接可展开；载荷内部目录链接跳过，不保留符号链接本身。
- 大载荷制包依赖临时磁盘容量，容器字段的大小上限仍然适用。RPM `newc` 文件大小为八位十六进制值，单文件超过 `uint.MaxValue` 时在产物发布前拒绝；包/归档/签名的总大小在 `uint.MaxValue` 内使用无符号 32 位，超限使用标准 64 位标签（`5009`、`271`、`270`），不再按 `int.MaxValue` 截断。安装大小使用 `LONGSIZE` 查询，它也能读取 32 位 `SIZE`。此改动未实现 RPM 的另一种大文件载荷编码。

## 升迁产物集成

升迁由独立的 [migrator 工具](../../migrator/README.zh-Hans.md) 预先制作。packager 不解析 `.migration`/`.ini`、SQL 或执行计划，也不携带原生执行器。

`--migration` 指定制作升迁时的输入名称，可带目录，例如 `--migration:../../packages/zongsoft`。

先展开变量，再判断是否包含目录分隔符 `/` 或 `\`：不包含时，从最终打包源目录（`--source`）逐级向父目录查找，直到文件系统根目录；每层先查目录本身，再查直属 `.migration/`，不遍历其他子目录。包含分隔符时只定位显式目录：相对路径基于源目录，绝对路径直接使用，既不向上查找，也不隐式检查 `.migration/`。源目录为 `hosting/web/default/`、产物位于 `hosting/.migration/` 时，`--migration:zongsoft` 即可找到；`--migration:./zongsoft` 只查源目录。查找起点不是运行命令时的工作目录。

文件前缀为 `<name>[-<edition>](migrate)@<version>_<RID>`，名称原样使用，无 Edition 时省略对应部分。选项填写制作升迁时的 `--name`，不包含自动生成的 `(migrate)` 标记。不能填写 Edition、版本、RID、扩展名、通配符或路径列表。

工具使用本次安装包最终确定的 Edition、版本、平台和架构定位产物，包括从源目录的 `.edition` 清单文件或 `.version` 版本标识文件取得的值及默认 x64。无 Edition 时省略对应部分。例如 enterprise、1.0.0、Linux x64 对应：

```text
zongsoft-enterprise(migrate)@1.0.0_linux-x64.tar.gz
zongsoft-enterprise(migrate)@1.0.0_linux-x64.sh
```

名称可以不同于宿主名称，但 Edition、版本和 RID 必须匹配。每个查找位置只有在压缩包和脚本都不存在时才继续下一位置；只找到其中一份立即报错并指出缺失文件的完整路径。找到完整配套后立即校验归档元数据和 RID，校验失败不再继续查找。两份文件必须来自同一目录，不拼配不同目录、不选择其他版本、Edition 或架构。到根目录仍未找到时，错误将预期文件名与已检查目录分行显示，各级目录及其 `.migration/` 按查找顺序逐行缩进列出。共享 `Utility.Indent` 使用平台换行并保留嵌套详情的缩进。未指定选项，或选项值为空、空字符串、全空白字符时，均不启用升迁，也不收录升迁产物。

两个文件原样存入安装根 `.migration/`，不展开归档；脚本为 0755，压缩包为 0600。升迁启动脚本执行时另建临时目录，归档内容直接展开到该目录根部，结束后清理，不在安装目录中增加一层 `.migration/`。与载荷目标冲突时报错。安装时调用脚本 `apply` 并传入 `/var/lib/<包名>/packager`；失败阻止启动。systemd 的 `ExecStartPre` 调用同一脚本 `check`，只比较完成标记，不解压、不连接服务。无 daemon 时仍执行升迁，DESTDIR 暂存不执行钩子，卸载保留状态与数据库/桶。目标机需要 POSIX sh、tar/gzip、cmp 和运行器所需系统库；详见升迁指南。

定位与归档校验在 `Migration.Load` 中分离：私有 `Locate` 方法沿 `DirectoryInfo.Parent` 遍历目录，包含根目录，按目录本身、直属 `.migration/` 的顺序记录检查位置；选中同目录配套后由 `Validate` 检查 PAX 元数据。显式目录只检查一次，不隐式查找子目录。所有定位与校验均先于产物收录和制包。

## 验证建议

Cake 的 `--edition` 同时用于依赖还原、编译、测试和制包；`restore` 显式传递 MSBuild 的 `Configuration`，避免按 Debug 还原后以 Release 配合 `--no-restore` 编译时遗漏条件依赖。主项目 Debug 引用本地 framework 的 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) DLL，Release 引用声明的 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) NuGet 包；主测试项目仅在 Debug 添加本地 DLL 引用，Release 通过主项目获得传递包依赖。`dotnet cake --edition Release` 默认执行打包器回归测试，不调用 AOT 构建或 NuGet 推送。

源版本与内存条目的回归由 VersionFileTest、PackageVersionTest 和 PackageArtifactTest 覆盖。

打包器版本元数据回归 `Package_Provenance_RecordsGeneratorAndPreservesApplicationMetadata` 覆盖三格式生成工具身份、应用版本、厂家值和默认值，以及独立的主页和维护者字段。

`PackageMetadataTest` 在 x64、x86、arm64、arm 的实际三格式包中逐项读取字段，核查全部共有应用元数据、生成/版本/根路径载荷字节总数、依赖编码、摘要与描述独立选择、可选空白字段、格式默认值及 RPM 新旧安装路径标签。Header 边界测试覆盖 2 GiB、`uint.MaxValue`、4 GiB 和更大的 64 位值，不分配大载荷。

`Tar_Metadata_IsReadableFromRenamedArchive` 在归档改名后从 PAX 全局头读取包含 daemon 身份和 Edition 的最终包名以及 x64、arm64、x86、arm 架构值，同时覆盖新增元数据、Unicode/多行文本、依赖区间和替代项、不写入 BuildTime 及载荷条目不变。其他 tar 元数据测试覆盖载荷字节总数与版本身份、空可选值省略、摘要/描述回退及反斜杠、CR、LF 的独立转义；非法依赖测试加入 tar，验证失败保留既有产物。来源元数据测试同时覆盖没有 daemon 身份和 Edition 的包名。

使用 [README](../README.zh-Hans.md#快速开始) 中生成的真实项目安装包；以下命令从 hosting 仓库根目录执行，只检查包内容。安装与卸载用法见 [README 包格式](../README.zh-Hans.md#包格式)。

### tar.gz

```bash
tar -tzf ./packages/zongsoft.web@1.0.0-x64.tar.gz
tar -xOf ./packages/zongsoft.web@1.0.0-x64.tar.gz install.sh
tar -xOf ./packages/zongsoft.web@1.0.0-x64.tar.gz uninstall.sh
```

### deb

```bash
ar t ./packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --info ./packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0-x64.deb
```

### rpm

```bash
rpm -qip ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0-x64.rpm
rpm -qpc ./packages/zongsoft.web@1.0.0-x64.rpm
rpm2cpio ./packages/zongsoft.web@1.0.0-x64.rpm | cpio -t
```

## 参考资料

- Debian Policy Manual: [Binary packages](https://www.debian.org/doc/debian-policy/ch-binary.html)
- Debian Policy Manual: [Binary package format appendix](https://www.debian.org/doc/debian-policy/ap-pkg-binarypkg.html)
- Debian Handbook: [The Packaging System](https://www.debian.org/doc/manuals/debian-handbook/packaging-system.en.html)
- rpm.org: [RPM Package Format](https://rpm.org/docs/4.19.x/manual/format.html)
- Linux Standard Base: [RPM Package File Format](https://refspecs.linuxfoundation.org/LSB_3.1.1/LSB-Core-generic/LSB-Core-generic/pkgformat.html)
- GNU tar manual: [GNU tar](https://www.gnu.org/software/tar/manual/)

Web 打包从最终指令树生成 .conf.template，并把完整关系写入安装根 .bindings；无法表达的配置在发布前失败。host/bind 支持逗号与分号。containerizer make 渲染模板，分别记录前端发布及应用绑定，详见 [Web 托管](web.zh-Hans.md).

终端警告使用暗黄色正文和粗体青色参数；错误使用暗红色正文和粗体紫色参数；成功提示使用绿色正文。异常类型以紫色标示，异常消息保留原文。四个工具共用仓库 `.shared/Utility.Output.cs`：`FormatOutput` 和 `FormatError` 构造 Core `CommandOutletContent`；`WriteOutput` 和 `WriteError` 统一终端与文本写入器的输出；使用命令执行器的工具通过 `HandleFailure` 输出异常并标记已处理，退出码由各工具决定。颜色由 Terminals 渲染。重定向的输出以及调用方提供的错误写入器保留纯文本和原通道。

帮助入口在解析业务命令之前匹配独立的 `--help`/`-h`，通过共享 `FormatHelp` 和 `HighlightSyntax` 组织显示；不会加载变量文件或进入文件处理流程。总耗时通过共享 `MeasureOutput` 统计，由工具提供本地化 `Output.Elapsed` 资源及输出回调，首次释放时输出一次；退出码和成功/失败判断仍由业务流程决定。 帮助还接受 `tar`/`deb`/`rpm` 后的单个帮助选项；三种格式在 `PackCommand` 中共用计时。

<a id="binding-document"></a>

## 绑定文档契约

以下以虚构的 demo 应用说明最终文件结构。每个示例说明输入来源、规范值、引用和消费方式；局部对象仅用于解释字段，完整非空文档见第 4.2 节。

### 1. 文档职责

`.bindings` 同时保存监听绑定和代理站点关系，作为 **Packager 根据本次实际交付内容生成的网络绑定快照**。职责如下：

- `bindings` 描述应用与代理的实际监听地址、协议能力及别名。
- `sites` 描述生成的代理站点如何引用这些绑定，以及请求转发到哪个目标。
- Containerizer 从快照读取监听事实和代理关系，再根据部署配置决定宿主端口发布。
- 不保存未求值模板、完整插件配置、私钥、口令、宿主端口映射或健康探测猜测。
- 空集合表示没有提取到声明，不能证明应用运行时没有其他动态监听。

保留 `sites` 的理由：消费端需要知道哪些应用绑定已经经过代理开放，以及跨容器部署时需要调整哪些连接。只有监听端口清单不能表达这些关系。代理关系统一随 `.bindings` 发布，由同一文档维护这些关系。

应用声明的处理指令目标为 `zongsoft.tools.binding`，按完整名称忽略大小写匹配，示例统一小写。`.bindings` 文件名及 JSON 中的 `bindings`、`binding` 字段继续表达绑定文档和引用。

假定本次最终入包视图包含下面这些来源。这里的文件名用于说明来源追踪，来源文件名本身不参与绑定身份：

| 来源 | 本例输入 | 来历和用途 |
| --- | --- | --- |
| `web.option` | `http://*:8069`，`Http1`，别名 `web` | 参考 Web 主端点案例；8069 是应用配置的监听端口，不是 Packager 默认值 |
| `telemetry.option` | `http://*:4317/opentelemetry`，`grpc+http2`，别名 `telemetry` | 参考 OTLP 接收端；4317 和基路径都来自服务端配置，不从客户端连接字符串推断 |
| `garnet.option` | `tcp://0.0.0.0:6379`，`tcp`，别名 `garnet` | 参考 Garnet 服务；本例显式选通配监听，区别于既有案例的回环地址，以便随后说明跨容器访问 |
| `web.profile` 的 `api` 站点 | `https://*:8443`、`https://[::]:8443`，域名 `api.example.test` | 两个地址属于同一入口；8443 由交付配置选择，与应用的 8069 无对应推导关系 |
| `web.profile` 的 `orders` 路径 | `/orders` 转发到 `https://192.0.2.20:9443/orders` | 一个显式远程服务；本包不启动它，也不发布它的 9443 端口 |
| `web.profile` 的 TLS 与 Header 设置 | 验证名和 Host 为 `orders.example.test`；信任根 `file:/etc/demo/upstream-ca.pem` | 供 Nginx 访问远程服务使用；信任根路径是目标运行环境中的资源引用 |

Web 输入的完整最小示意如下。另两个应用声明按同样的过程读取，具体值见后面的来源表：

```xml
<?xml version="1.0" encoding="utf-8" ?>
<?zongsoft.tools.binding name="web"
	path="Kestrel/Endpoints/http"
	template="${this.Url}"
	protocols="${this.Protocols}"
	required="true"?>
<options>
	<option path="kestrel">
		<endpoints>
			<http url="http://*:8069" protocols="Http1" />
		</endpoints>
	</option>
</options>
```

| 数据 | 提取过程 | 去向 |
| --- | --- | --- |
| `path="Kestrel/Endpoints/http"` | 在最终配置视图中选中 http 项 | 形成默认别名 `kestrel/endpoints/http`；不是 HTTP 请求路径 |
| `${this.Url}` | 对选中项求值得到 `http://*:8069` | 拆成 scheme、host、port、path；源 URL 没有路径，所以规范路径为 `/` |
| `${this.Protocols}` | 得到 `Http1`，规范成 `http1` | 写入 `protocols`，供代理选择普通 HTTP 承载 |
| `name="web"` | 加入显式短别名 | 与默认配置路径别名一起进入 aliases |
| `required="true"` | 此声明提取不到有效结果时失败 | 只控制制作期诊断，不写成运行时开关 |

这些值的角色不同：应用根据 option 建立监听；Packager 根据 binding 声明提取交付快照；代理配置器根据快照和 profile 组合入口；Containerizer 再决定网络发布。修改 profile 的入口端口不会修改 option。

#### 1.2 同一端口数据在不同阶段的去向

本例在同机部署时，代理可连接 `127.0.0.1:8069`；在允许跨容器访问的拓扑中，可连接本例应用容器的 DNS 名 `demo-app:8069`。这两个连接地址都不是原始监听 `0.0.0.0:8069` 的替代声明：

| 阶段 | 数据 | 来源及消费方式 |
| --- | --- | --- |
| `.bindings` 的监听 | `0.0.0.0:8069` | 来自 option；供代理和端口发布规划读取 |
| 包内同机代理连接 | `127.0.0.1:8069` | 从 IPv4 通配监听按同机连接规则推导，端口不变 |
| 容器代理连接 | `demo-app:8069` | `demo-app` 来自本例交付规划的服务名；选定绑定和端点后调整连接，不改写监听事实 |
| 宿主入口映射 | `127.0.0.1:18443 → nginx:8443/tcp` | 18443 和宿主回环地址来自部署覆盖；8443 来自 Nginx 入口；不写回包级快照 |
| Garnet 直接发布 | `127.0.0.1:16379 → demo-app:6379/tcp` | 16379 来自部署覆盖，6379 来自 Garnet 绑定；它没有 HTTP 路由 |

如果源 Garnet 改为 `127.0.0.1:6379`，跨容器规划不能把它伪装成 `demo-app:6379` 可达；应报告拓扑无法满足。若只把入口发布端口 18443 改成 28443，包内 binding 身份和 sites 都不应变化。

#### 1.3 不进入文档的数据示例

假定 Garnet 的完整 Settings 还包含 `password=[REDACTED]`、`checkpointdir=~/.garnet` 和 `aof=true`。本例 PI 只读取地址，并明确声明 `tcp`；这三个运行设置不参与绑定或身份计算。入口证书可以由 profile 的资源引用指向，但证书私钥字节不复制到 JSON。

假定应用另外通过代码动态打开 9000 端口而未提供 binding 声明：文档不会凭进程、依赖库或端口扫描补出 9000。消费端只能据已声明内容规划，不能据此认定“9000 不存在”。

### 2. 顶层结构

使用 UTF-8、无 BOM 的 JSON；字段名区分大小写。输出采用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，直接保留 `+`，例如 `"grpc+http2"`，并保留可直接输出的 Unicode 字符；双引号、反斜杠和控制字符仍按 JSON 规则转义。该文件独立作为 UTF-8 JSON 读取，不直接嵌入 HTML 或脚本。端点和绑定的身份仍按第 3.3 节的专用编码计算，不依赖文件的等价转义形式。下面是有效的空文档：

```json
{
	"version": 1,
	"bindings": [],
	"sites": []
}
```

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| `version` | 整数 | 必需，本格式为 1；未知版本报错 |
| `bindings` | 数组 | 必需，可为空，不允许 null 元素 |
| `sites` | 数组 | 必需，可为空，不允许 null 元素 |

缺少文档、缺少必需字段、重复 JSON 属性和未知字段均报错。可选字段缺省表示未指定；显式 null 一律报错。必需字段即使取默认值也必须出现。

#### 2.1 空结果、无代理和完整交付分别是什么

| 输入场景 | 顶层结果 | 数据来龙去脉 |
| --- | --- | --- |
| 已扫描全部交付输入，没有 binding，也没有代理站点 | 上面的空文档 | `version=1` 来自文档格式；两个空数组来自成功完成的提取，不是读取失败的替代值 |
| 只交付 Garnet，未配置 Web 入口 | `bindings` 含第 4.2 节的 Garnet 对象，`sites=[]` | TCP 监听可以直接发布，不因为缺少 HTTP 站点而丢弃 |
| 本文 demo 场景 | 4 个 binding、5 个 endpoint、1 个 site、3 个 route | 三个应用 binding 加一个双地址 Nginx binding；远程 orders 只产生 route，不增加本包监听 |
| 查不到包内 `.bindings` 文件 | 没有合法文档 | 无法区分未生产、损坏与被删除，报错，不返回两个空数组 |

`version=1` 是文件格式号，不是 demo 的版本号、.NET 版本号、包格式版本或绑定数量。第 4.2 节的完整文档按本例规则排列；为阅读而缩进，真正写入包时才压缩成无多余空白的 JSON。

#### 2.2 不合法顶层的对照例

下面每行是独立输入：

```json
{"bindings":[],"sites":[]}
{"version":2,"bindings":[],"sites":[]}
{"version":1,"bindings":null,"sites":[]}
{"version":1,"bindings":[],"sites":[],"listen":"http://*:8069"}
{"version":1,"version":1,"bindings":[],"sites":[]}
```

依次失败的理由是：缺少格式号、格式号不受支持、集合不允许 null、存在未知字段、属性名重复。最后一项即使两个值相等也失败，不能让不同 JSON 读取器各自选择首项或末项。

#### 2.3 可选字段的具体含义

本例普通 HTTP 路由没有 Host 覆盖，也没有上游 TLS；因此第 4.2 节省略 `host`、`tls`。消费端据约定保留请求 Host，并使用明文连接。远程 orders 显式配置这两项，所以必须输出有效值。

`"host":null` 和 `"tls":null` 都报错；空字符串也不能表示“继承”。profile 的继承、清除、默认值在制作时先解析完，不能把尚未决定的输入状态交给消费端再次求值。TLS 验证与信任来源的缺省处理见第 4.4 节。

### 3. 绑定与端点

#### 3.1 绑定

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| `id` | 字符串 | 必需，基于规范内容生成的完整 SHA-256 小写十六进制身份 |
| `owner` | 字符串 | 必需，`application` 或 `nginx`；远程服务不能伪装成本包监听 |
| `aliases` | 字符串数组 | 必需，可为空；合并配置路径别名和显式名称，规范化后排序去重 |
| `endpoints` | 对象数组 | 必需，至少一个；描述同一服务的替代监听地址，不表示负载均衡池 |

身份只由 owner 和规范化端点内容决定。别名、来源路径及时间戳不参与。重排输入不得改变身份；多个独立绑定共用别名时，按别名选择必须报告歧义，不能取第一项。

##### 3.1.1 Web 绑定怎样形成

下面是完整文档中的一个 binding 对象，按本规范表达；两个摘要的计算见第 3.3 节：

```json
{
	"id": "025ae7cd729f6f9206ff7ba5504831755e33cd273e805563a1a71267f31d15bc",
	"owner": "application",
	"aliases": ["kestrel/endpoints/http", "web"],
	"endpoints": [
		{
			"id": "432783ea6b6e659516771ecfcb22caefaa4fc7a2d833b291299805eb5d28c8ab",
			"scheme": "http",
			"host": "0.0.0.0",
			"port": 8069,
			"path": "/",
			"protocols": ["http1"]
		}
	]
}
```

| 数据项 | 来历 | 用途或变化结果 |
| --- | --- | --- |
| `owner=application` | 提取来源是应用 option，而不是最终 Nginx 配置 | Containerizer 将它归属应用容器，不归属代理容器 |
| 默认别名 `kestrel/endpoints/http` | PI 选中项的完整配置路径，无前导斜杠并转为规范小写 | 不填写 name 也能按该路径选择绑定 |
| 短别名 `web` | PI 的 name | 用于人的显式选择；改名不改变 binding ID |
| 一个 endpoint | 此配置项的模板只展开一个地址 | 不因它同时接受许多客户端而复制多份端点 |
| binding ID | owner 加规范端点集合的摘要 | sites 和交付规划以它建立引用；8069 改成 8070 时摘要随之改变 |
| endpoint ID | 规范端点自身的摘要，包含路径和协议 | 在含多个替代地址的绑定内明确指出选用了哪一个 |

##### 3.1.2 多地址合并与别名歧义

参考 Prometheus 案例，假定 exporter 的 urls 是 `http://127.0.0.1:9464/metrics,http://localhost:9464/metrics`，声明能力为 `http1`。记两个规范端点为 A、B，它们来自同一配置项且路径、协议一致，因此形成一个 `{A,B}` 绑定。

| 后续输入 | 结果 | 原因 |
| --- | --- | --- |
| 第二条声明只选中 B，别名为 `metrics` | 合并成 `{A,B}`，合并别名 | 有共同端点且语义一致；并非新增进程 |
| 第二条声明为 `{B,C}`，C 是同一服务的 IPv6 替代地址 | 合并成 `{A,B,C}` | 替代关系经共同端点 B 连接，身份按最终集合计算 |
| 第二条独立声明只给出另一端口 9465，没有任何共同端点 | 保留独立绑定 | 不能仅因 path 和 protocols 相同就猜成同一服务 |
| 两个独立绑定都使用 `metrics` 别名 | 保留两个绑定；按 `metrics` 选择时报歧义 | 不能悄悄取先扫描到的一项 |
| 同一绑定同时出现 `Metrics` 和 `metrics` | 最终只保留 `metrics` | 别名按忽略大小写去重并规范输出 |

对于 `{A,B}`，代理选择一个明确可连接端点，不能自动创建两个 upstream 副本。若用户确实需要负载均衡，应提供第 4.5 节那样的显式远程池。

#### 3.2 端点

端点序列化下列六个字段。HostType、Transports 是模型计算属性，不列为 JSON 字段：

| 字段 | 类型 | 语义 |
| --- | --- | --- |
| `id` | 字符串 | 必需，由该端点的规范内容生成；监听与本包路由通过 binding + endpoint 引用；编码规则见第 3.3 节 |
| `scheme` | 字符串 | 规范地址方案，如 `http`、`https`、`tcp`、`udp`、`opc.tcp` |
| `host` | 字符串 | 规范 IP 或 DNS 名；IPv6 不包含 URL 的方括号 |
| `port` | 整数 | 已解析的端口，1–65535 |
| `path` | 字符串 | 完整监听基路径；不能丢失 gRPC 挂载路径或 OPC 路径 |
| `protocols` | 字符串数组 | 保留组合语义，如 `grpc+http2`，不能拆为两个并列能力 |

 C# 模型提供只读的 `UriHostNameType HostType`，根据 Host 计算，不接受构造参数或独立设置，不输出 JSON，也不作为独立字段参与身份计算。该值对应 [NetworkAddress.HostType](../../../framework/Zongsoft.Core/src/Communication/NetworkAddress.cs)，与其另一个属性 `AddressFamily` 含义不同：

| 规范 host 示例 | 计算得到的 HostType | JSON 中保存什么 | NetworkAddress.AddressFamily |
| --- | --- | --- | --- |
| `0.0.0.0`、`127.0.0.1` | `UriHostNameType.IPv4` | 只保存 host，不另存 hostType | `AddressFamily.InterNetwork` |
| `::`、`::1` | `UriHostNameType.IPv6` | 只保存 host，不另存 hostType | `AddressFamily.InterNetworkV6` |
| `localhost`、`api.example.test` | `UriHostNameType.Dns` | 只保存 host，不另存 hostType | `AddressFamily.Unspecified` |

例如 `localhost` 的主机种类是 Dns；制作时不解析 DNS，也不据此指定它最终使用 IPv4 还是 IPv6，所以其 AddressFamily 为 Unspecified。模型通过 `Uri.CheckHostName(Host)` 得到枚举值；读取文件后同样从 host 计算。未知主机种类在文档校验时失败，不默认当作 DNS。

 C# 模型提供只读的 `string[] Transports`，由 scheme 与 protocols 计算，不接受构造参数或独立设置，不输出 JSON，也不作为独立字段参与身份计算。共用模型复用 Core 的 NetworkProtocol 解析组合、识别承载，再按绑定策略合并能够确定的传输需求；按 `tcp`、`udp` 的顺序去重返回。外部消费者同样根据原始字段计算，不能要求包额外保存一份派生值。

| scheme；protocols | 计算得到的 Transports | 数据来历和边界 |
| --- | --- | --- |
| `tcp` 或 `opc.tcp`；`[]` | `["tcp"]` | 地址方案已经明确 TCP；不要求再填写相同协议 |
| `udp`；`[]` | `["udp"]` | 地址方案已经明确 UDP，不按端口数值猜测协议 |
| `https`；`["grpc+http2"]` | `["tcp"]` | 来自组合中的 HTTP/2 承载，不是来自 grpc 名称或 https 默认值 |
| `https`；`["grpc+http3"]` | `["udp"]` | 来自 HTTP/3 承载；不补入 TCP |
| `https`；`["grpc+http2","grpc+http3"]` | `["tcp","udp"]` | 分别计算两项再合并；两项使用相同端口也保留两种传输 |
| `tcp`；`["protobuf+tcp"]` | `["tcp"]` | 地址方案与明确 TCP 承载一致，不据 Protobuf 编码推导 HTTP 或 gRPC |
| `http` 或 `https`；`[]`、`["http"]` 或 `["protobuf+http"]` | `[]` | 没有声明具体 HTTP 版本，不能据此补入 TCP、UDP 或全部 HTTP 版本 |

空集合表示尚无可确定的传输，不表示无需网络。同样，`https` 配 `["http","grpc+http3"]` 时仅能确定 gRPC 部分需要 UDP；普通 HTTP 部分仍缺版本信息，不能将 `["udp"]` 当成完整发布计划。消费端需要完整传输计划时必须检查原始能力是否足够，信息不足应报告错误，不静默漏掉该能力。裸上层名称没有默认规则时也不猜测传输。

绑定策略先将独立的 `grpc` 解释为 `grpc+http2`，将 `protobuf` 解释为 `protobuf+http`；已有加号组合不再补默认值。已知冲突直接失败，例如 `http` 配 `grpc+http3`、`tcp` 配 `http1`。本格式只接受 http、https、tcp、udp、opc.tcp 地址方案；未知 scheme 不回退到 TCP。完整的输入语法见 [应用网络绑定](../README.zh-Hans.md#application-bindings)。

规范化分别处理 IP、DNS、IPv6 zone、路径和协议组合，不以全字符串转小写代替规范化。协议集合排序去重也必须进入实际哈希输入和输出；仅对排序键做排序不够。

端点独立内容身份及显式引用见第 3.2.3 节。第 3.3 节固定参与字段、属性顺序、字符串转义、整数编码、端点排序及集合去重规则；实现使用专用身份 JSON 编码，不依赖匿名对象的默认序列化。IPv6 zone 保存在 host 的 `%` 后，例如 `fe80::1%eth0`；地址部分规范化、zone 大小写保留，HostType 从去掉 zone 的地址计算。重建 URL 时使用 `[fe80::1%25eth0]`，zone 因属于 host 内容而参与摘要。

##### 3.2.1 地址分解与协议推导

以下各行是独立的规范化演算；除前三行外，不额外加入 demo 完整文档：

| 源 URL；源 protocols | 规范 scheme / host / port / path / protocols | 派生值与理由 |
| --- | --- | --- |
| `http://*:8069`；`Http1` | `http` / `0.0.0.0` / `8069` / `/` / `["http1"]` | ipv4、tcp；星号只代表 IPv4 通配，不补 IPv6 |
| `http://*:4317/opentelemetry`；`http2+grpc` | `http` / `0.0.0.0` / `4317` / `/opentelemetry` / `["grpc+http2"]` | ipv4、tcp；组合项保留 gRPC 语义，不增加普通 HTTP/2 能力 |
| `https://[::]:8443/`；`HTTP2,http1,grpc` | `https` / `::` / `8443` / `/` / `["grpc+http2","http1","http2"]` | ipv6、tcp；URL 方括号不存入 host，数组排序；本例 Nginx 的能力来源另见第 4.1 节 |
| `http://Example.TEST./Metrics/`；`http1` | `http` / `example.test` / `80` / `/metrics` / `["http1"]` | dns、tcp；去 DNS 根点，HTTP 缺省端口为 80，路径按约定规范化 |
| `https://*:9443`；`grpc+http3` | `https` / `0.0.0.0` / `9443` / `/` / `["grpc+http3"]` | ipv4、udp；不因 gRPC 常见于 HTTP/2 而补 tcp，不因此增加额外代理入口 |
| `tcp://0.0.0.0:6379`；`tcp` | `tcp` / `0.0.0.0` / `6379` / `/` / `["tcp"]` | ipv4、tcp；这是本例 Garnet 端点 |
| `opc.tcp://127.0.0.1:4840/FactoryA`；省略 | `opc.tcp` / `127.0.0.1` / `4840` / `/FactoryA` / `[]` | ipv4、tcp；保留 OPC 路径大小写，不套用 HTTP 路径规则 |
| `http://localhost:8080`；省略 | `http` / `localhost` / `8080` / `/` / `[]` | dns；Transports 为 []，版本信息不足，不能补入 tcp 或全部 HTTP 版本 |

在本规范中，前三行各有根据规范内容生成的 endpoint ID；HostType、Transports 按规范规则在模型中计算，不写入 JSON。例如把 https 端点的 `protocols` 从 `["http1"]` 改成 `["http3"]`，Transports 随之从 `["tcp"]` 变成 `["udp"]`，无需同时修改另一字段。原始协议发生变化，规范端点内容及其内容身份也应变化。

路径需要区分两个值：规范 `/metrics` 用于身份及匹配范围；收到的 `/METRICS/child?name=x` 在匹配该范围后仍按既定转发规则保留原路径与查询。不会因为文档保存了小写 path 就重写客户端请求。

##### 3.2.2 无效地址与能力示例

| 输入 | 结果 | 原因 |
| --- | --- | --- |
| `tcp://0.0.0.0` | 提取失败 | TCP 没有本契约定义的缺省端口 |
| `http://*:0` | 提取失败 | 动态端口无法作为固定交付监听快照 |
| `http://user:secret@example.test:80/` | 提取失败，诊断脱敏 | 不允许 userinfo，也不能把凭据带入 JSON |
| `http://*:8069/?mode=a` | 提取失败 | 监听声明不接受 query，不能混淆为客户端请求 URL |
| `tcp://*:6379/cache` | 提取失败 | 本契约的 TCP/UDP 监听只接受根路径 |
| `tcp://*:6379` 配 `http1` | 提取失败 | 地址方案与声明协议存在已知冲突，不能靠丢弃一方修复 |

`required=false` 只允许没有选中结果，不能使上述错误输入变成“忽略这条声明”。

##### 3.2.3 独立端点身份及引用

每个 endpoint 的 `id` 描述该端点的规范监听内容；binding 的 `id` 描述所属组件及整个替代端点集合。端点 ID 不由数组位置、别名或父 binding ID 派生。为绑定增加另一个替代地址时，已有端点的内容不变，其 ID 也不变；binding ID 随集合内容变化。

监听及本包路由同时携带 `binding` 和 `endpoint`。读取时先找到指定 binding，再在它的 endpoints 内按 id 找到端点，并检查引用所在关系要求的 owner。即使一个端点 ID 在文档的其他位置存在，也不能使错误的 binding/endpoint 配对成立。同内容端点可以出现在不同 owner 的绑定内；端点身份本身不承担组件归属判断。

以下是 api 站点一个 listener 的**局部对象**。两项身份按第 3.3 节计算：

```json
{
	"binding": "35a5a0a9b3b5d7866e1da297abf88087c56e08c87b8ea4fa9dad6dcb1ab4e61f",
	"endpoint": "cdcccc23c6055cfcd5039aa9771da9de87894dc30420fc8d0ec583283c40eade",
	"default": false
}
```

| 数据或操作 | 来历和处理 | 结果 |
| --- | --- | --- |
| `binding=B_NGINX` | api/public 的两个最终监听地址组成 nginx 绑定 | 确定监听所属组件和替代地址集合 |
| `endpoint=E_NGINX4` | 在该集合内选中 host=0.0.0.0、port=8443 的端点 | 明确引用 IPv4 地址，不依赖它位于数组第几项 |
| `default=false` | 最终代理监听没有显式 default_server | 属于站点监听关系，不参与 endpoint 身份 |
| 仅把 endpoint 改为 E_NGINX6 | 同一绑定中的 host=::、port=8443 | 引用改为 IPv6，binding 仍相同 |
| 把 endpoint 改为 E_OTLP | E_OTLP 存在，但属于 application 的 B_OTLP | 配对校验失败，不跨绑定寻找替代结果 |
| 重排 B_NGINX 的 endpoints | 两个端点的规范内容和集合成员均未改变 | 身份和引用保持不变 |
| 删除 E_NGINX4 但保留原 listener | 被引用端点不再属于本次绑定集合 | 报告悬空引用；重新制作时须同步生成绑定和引用 |

本包 gRPC 路由以同样方式引用 B_OTLP/E_OTLP：B_OTLP 来自 telemetry 声明，E_OTLP 来自其中的 0.0.0.0:4317/opentelemetry。代理同机连接可为 127.0.0.1:4317，跨容器连接可为 demo-app:4317；连接地址变化不改写原始监听端点的身份。完整路由及 target 结构见第 4.2 节。

共享模型统一完成身份生成、读回重算及配对校验；数组位置和内部排序键不作为引用。

#### 3.3 身份编码与算例

**采用固定规则的紧凑 JSON 编码规范内容，再取完整 SHA-256，作为 endpoint 与 binding 的内容身份。** 规则如下：

1. 先规范化字段及集合，去重，使用 Ordinal 顺序；端点集合按各端点身份输入的 UTF-8 字节顺序排序。
2. endpoint 的身份输入固定为 `scheme,host,port,path,protocols` 五个属性；不包含 id、别名及派生字段。
3. binding 的身份输入固定为 `owner,endpoints` 两个属性；endpoints 使用第 2 项的对象，不包含 endpoint ID。
4. 身份 JSON 使用 UTF-8、无 BOM、无首尾空白和末尾换行。端口使用十进制整数，不带正号、前导零、小数或指数。摘要取 SHA-256 的完整 64 个小写十六进制字符（256 位）。
5. 字符串使用第 3.3.1 节的固定转义规则；字段规范化完成后不再由身份编码器改变大小写或做 Unicode 归一化。协议数组按规范字符串去重、Ordinal 排序；端点数组按规范内容去重、身份输入 UTF-8 字节序排序。
6. 读取时从声明内容重新计算身份并核对；内容变化却保留旧 ID、同一 ID 对应不同内容或引用不属于指定 binding，均失败。所有引用随本次文档一起生成和校验。

Web endpoint 的完整输入为下面一行，**不含代码块显示时的换行**，共 79 字节：

```json
{"scheme":"http","host":"0.0.0.0","port":8069,"path":"/","protocols":["http1"]}
```

结果为 `432783ea6b6e659516771ecfcb22caefaa4fc7a2d833b291299805eb5d28c8ab`。

Web binding 的输入为下面一行，共 117 字节：

```json
{"owner":"application","endpoints":[{"scheme":"http","host":"0.0.0.0","port":8069,"path":"/","protocols":["http1"]}]}
```

结果为 `025ae7cd729f6f9206ff7ba5504831755e33cd273e805563a1a71267f31d15bc`。下面列出完整示例使用的全部身份，后文用简称解释引用，JSON 中仍写真实摘要：

| 说明简称 | 摘要 | 内容来源 |
| --- | --- | --- |
| E_WEB | `432783ea6b6e659516771ecfcb22caefaa4fc7a2d833b291299805eb5d28c8ab` | Web 五字段端点，79 字节 |
| E_OTLP | `918c6bae9bc0e06e61cafe03a2e37a2cb2a569f00834d177e3af7e74c45e83f4` | telemetry 五字段端点，97 字节 |
| E_GARNET | `dc2b1a1b180b183e8b4e9b7a8147a9909691fe8fcb96209b1133f851b81c7020` | Garnet 五字段端点，76 字节 |
| E_NGINX4 | `cdcccc23c6055cfcd5039aa9771da9de87894dc30420fc8d0ec583283c40eade` | Nginx IPv4 五字段端点，101 字节 |
| E_NGINX6 | `0a492aba7787bd961d4e94123c39f709e94ea9be1d38163d061c28da41c98370` | Nginx IPv6 五字段端点，96 字节 |
| B_WEB | `025ae7cd729f6f9206ff7ba5504831755e33cd273e805563a1a71267f31d15bc` | application 加 E_WEB 的内容对象，117 字节 |
| B_OTLP | `a3bf9ca44465c933a625a621f10c1a9a4c632e9b0a7cb27e378c9134f6ca7920` | application 加 E_OTLP 的内容对象，135 字节 |
| B_GARNET | `c39af79ca4a8c092d137a696e477c3f4a0bcc5d45a10abede880c296b2ff742a` | application 加 E_GARNET 的内容对象，114 字节 |
| B_NGINX | `35a5a0a9b3b5d7866e1da297abf88087c56e08c87b8ea4fa9dad6dcb1ab4e61f` | nginx 加两个有序端点内容对象，230 字节 |

这里的端点身份可在所属 binding 内被引用；引用同时携带 binding ID，不能只凭端点摘要绕过所属组件检查。Nginx 的双地址集合里，`0.0.0.0` 排在 `::` 前，顺序来自规范字节，不来自 profile 扫描顺序。

| 输入变化 | 身份是否变化 | 理由 |
| --- | --- | --- |
| `web` 改名 `api`，或移动 option 文件 | 不变 | 别名和来源不参与摘要 |
| 协议从 `HTTP2,http1,http1` 改写为 `http1,http2` | 不变 | 规范化后同一集合 |
| 8069 改成 8070 | endpoint 和 binding 都变化 | 监听内容发生变化 |
| 新增 IPv6 替代端点 | 原 IPv4 endpoint 不变；binding 变化 | 端点集合改变 |
| 仅改变远程路由 Host 或外部域名 | 应用 binding 不变 | 代理策略属于 site，而非应用监听内容 |

共享模型按以上规则计算身份；普通文档 JSON 的属性排版或等价字符串转义不参与摘要。制作入口 CreateBinding 先规范化端点和别名，合并同内容端点，再生成身份；读回文档则拒绝重复身份与不匹配的摘要，不通过覆盖字段修复篡改内容。

##### 3.3.1 身份字符串的完整编码

该规则只定义身份计算字节；接收的普通 JSON 可以使用等价转义或排版，读取后应按同一身份编码重新计算。字符的合法性及地址规范化先由字段规则检查，编码器不会因为能编码某个字符就认可它出现在 host 或 path 中。

| 已规范字符串中的字符 | 身份 JSON 编码 | 示例及数据来历 |
| --- | --- | --- |
| 双引号、反斜杠 | 分别为 `\"`、`\\` | 字符串值内的字符使用 JSON 转义，不能成为结构分隔符 |
| U+0008、U+0009、U+000A、U+000C、U+000D | 分别为 `\b`、`\t`、`\n`、`\f`、`\r` | 统一使用短转义，不在身份输入中写成另一种等价转义 |
| 其余 U+0000～U+001F | `\u00xx`，十六进制字母小写 | 例如 U+001B 编码为 `\u001b`；仍须先通过所属字段的合法性检查 |
| `/`、`+`、`<`、`>`、`&` 等其余 ASCII 字符 | 原字符 | `grpc+http2` 的加号和路径斜杠不转义，不受 HTML 输出选项影响 |
| 所有其他合法 Unicode 标量值 | 直接编码为 UTF-8 | `/工厂A` 直接保存汉字；不写成 `\u5de5\u5382`，U+2028、U+2029 也按原标量编码 |
| 孤立 UTF-16 代理码元或无效 UTF-8 | 拒绝 | 不替换成 U+FFFD 后继续计算，否则不同无效输入会被掩盖 |

例如规范化后的 OPC 端点五字段为 `opc.tcp`、`127.0.0.1`、`4840`、`/工厂A`、`[]`。这组值是独立的身份编码演算输入，不加入 demo 主文档，也不作为 OPC 服务运行验收证据。身份 JSON 为下面一行，无末尾换行，共 84 个 UTF-8 字节：

```json
{"scheme":"opc.tcp","host":"127.0.0.1","port":4840,"path":"/工厂A","protocols":[]}
```

摘要为 `4b11342c4a6df3922352c315683f4fc545fd0b5257d4775cac9f82f7755a9d07`。其中 `/工厂A` 占 8 个 UTF-8 字节，来自斜杠 1 字节、两个汉字各 3 字节和字母 A 1 字节。若输入 JSON 将同一个 path 写成 `"/\u5de5\u5382A"`，解析得到相同字符串，按本规则重编码后仍得到这 84 字节及同一摘要。

固定字段顺序意味着身份编码使用专用写入过程，不依赖 C# 属性声明顺序、字典枚举顺序或 JSON 库默认转义策略。协议能力先完成简写展开、大小写及集合规范化；例如 `HTTP2,http1,http1` 与 `http1,http2` 都编码为 `["http1","http2"]`，因此只调整输入顺序不会改变身份。摘要仅用于标识内容；所属组件和引用关系仍由文档结构校验。

### 4. 站点与路由

每个 `site` 的字段为 `id`、`name`、`hosts`、`listeners`、`routes`，均必需。id 为 `site:` 加小写 name，在本包唯一；name 非空，不包含空白或斜杠。hosts 允许为空，listeners 至少一个，routes 可以为空。站点身份按名称建立，不随域名或转发策略变化；多包汇总时必须再带包身份。

| 关系 | 必须明确的内容 | 规则 |
| --- | --- | --- |
| 站点监听 | nginx binding、具体端点、是否默认站点 | binding + endpoint 引用 nginx 的 HTTP 端点；default 必需，只表示显式默认，同一端点最多一个显式默认站点 |
| 本包路由 | 请求类别、路径、application binding、所选端点和协议 | 以 `target.kind = binding` 明确区分目标类别 |
| 远程路由 | 请求类别、路径、远程连接地址或显式上游池 | 以 `target.kind = remote` 标识；不计入本包监听或宿主端口发布 |
| 上游连接 | 连接地址、Host、TLS 验证名及信任根资源引用 | 与原始监听端点分开；通配监听地址不直接作为连接地址 |

本节给出 target、端点身份及远程上游池的具体 JSON 示例。每条 route 必须有 requestKind、path、target、protocol；同一站点内 requestKind + 规范 path 唯一。protocol 必须为一个具体 HTTP 版本组合；本包目标还必须从选中端点声明的能力中选出，信息不足时失败。

host 省略表示保留请求 Host。明文连接省略 tls；HTTPS 必须保存 tls 对象及显式 verify 值，制作期默认 verify=true。name 省略时仅允许从具体连接 DNS 名取得证书验证身份；使用 IP 且开启验证时必须显式 name。trust 省略表示目标运行环境的系统信任，显式值只接受绝对 `file:/` 资源引用；代理适配器负责提供实际 CA 配置，无法满足时失败。

#### 4.1 代理输入、最终监听与来源

这是本例的 profile 输入。省略本包上游表示自动组合已声明绑定，由 Routing 组合应用声明；格式模型负责保存和校验结果，Definition 负责解析 profile：

```ini
[api]
host = api.example.test
bind!public = https://*:8443,https://[::]:8443
certificate = file:/etc/demo/api.pem
certificate-key = file:/etc/demo/api.key
nginx:http2 = on

[api orders]
path = /orders
server = https://192.0.2.20:9443/orders
header!Host = orders.example.test
server-tls-verify = true
server-tls-name = orders.example.test
server-tls-trust = file:/etc/demo/upstream-ca.pem
```

| 输入或中间结果 | 文档数据 | 来源与处理 |
| --- | --- | --- |
| profile 段名 `api` | `name=api`，`id=site:api` | id 是本包内按规范站点名构造的键，不是域名摘要；多包汇总时还要带包身份 |
| `host=api.example.test` | `hosts=["api.example.test"]` | 用于入口主机匹配；不是上游 TLS 名，也不是应用容器 DNS 名 |
| 站点 api 的 bind!public | Nginx binding 的 `aliases=["api/public"]` | 以站点名/入口名形成规范别名 |
| 最终生成的两个 SSL listen | E_NGINX4、E_NGINX6 | 假定生成语义分别为 IPv4/IPv6 的 8443 SSL 监听；按最终配置回收结果，避免原生覆盖后与输入脱节 |
| 入口 HTTP/1、HTTP/2 与 gRPC 路由 | `protocols=["grpc+http2","http1","http2"]` | 结合最终入口协议设置和路由能力生成；http2 on 本身不能单独证明存在 gRPC 转发 |
| 未显式指定 default_server | 两个 listener 都为 `default=false` | 只记录显式默认标记；即使 Nginx 加载顺序使其接纳未匹配请求，也不把它猜成显式默认 |
| 本包 Web、OTLP 声明 | 自动生成普通 HTTP 根路由和 gRPC 基路径路由 | 请求分类分别选择后端，不把 4317 并入普通 HTTP 根路由 |
| 显式 orders 路径 | 生成一条 remote 路由 | 目标来自 profile，不查找本包中“碰巧监听 9443”的绑定 |

入口证书和私钥引用交给生成的代理资产及资源交付机制；本例 JSON 不复制其内容。上游 trust 是 CA 引用，属于另一段连接，不能拿 api.key 或入口域名自动替代。

#### 4.2 完整非空文档

以下 JSON 包含第 1 节的所有监听和代理关系，引用均在本例内可追踪。它是完整的格式样例。bindings 按 ID 排序；端点按第 3.3 节的内容顺序排列；路由按类别和路径稳定排列，数组顺序不替代最长路径匹配规则。

```json
{
	"version": 1,
	"bindings": [
		{
			"id": "025ae7cd729f6f9206ff7ba5504831755e33cd273e805563a1a71267f31d15bc",
			"owner": "application",
			"aliases": [
				"kestrel/endpoints/http",
				"web"
			],
			"endpoints": [
				{
					"id": "432783ea6b6e659516771ecfcb22caefaa4fc7a2d833b291299805eb5d28c8ab",
					"scheme": "http",
					"host": "0.0.0.0",
					"port": 8069,
					"path": "/",
					"protocols": [
						"http1"
					]
				}
			]
		},
		{
			"id": "35a5a0a9b3b5d7866e1da297abf88087c56e08c87b8ea4fa9dad6dcb1ab4e61f",
			"owner": "nginx",
			"aliases": [
				"api/public"
			],
			"endpoints": [
				{
					"id": "cdcccc23c6055cfcd5039aa9771da9de87894dc30420fc8d0ec583283c40eade",
					"scheme": "https",
					"host": "0.0.0.0",
					"port": 8443,
					"path": "/",
					"protocols": [
						"grpc+http2",
						"http1",
						"http2"
					]
				},
				{
					"id": "0a492aba7787bd961d4e94123c39f709e94ea9be1d38163d061c28da41c98370",
					"scheme": "https",
					"host": "::",
					"port": 8443,
					"path": "/",
					"protocols": [
						"grpc+http2",
						"http1",
						"http2"
					]
				}
			]
		},
		{
			"id": "a3bf9ca44465c933a625a621f10c1a9a4c632e9b0a7cb27e378c9134f6ca7920",
			"owner": "application",
			"aliases": [
				"kestrel/endpoints/telemetry",
				"telemetry"
			],
			"endpoints": [
				{
					"id": "918c6bae9bc0e06e61cafe03a2e37a2cb2a569f00834d177e3af7e74c45e83f4",
					"scheme": "http",
					"host": "0.0.0.0",
					"port": 4317,
					"path": "/opentelemetry",
					"protocols": [
						"grpc+http2"
					]
				}
			]
		},
		{
			"id": "c39af79ca4a8c092d137a696e477c3f4a0bcc5d45a10abede880c296b2ff742a",
			"owner": "application",
			"aliases": [
				"externals/garnet/default",
				"garnet"
			],
			"endpoints": [
				{
					"id": "dc2b1a1b180b183e8b4e9b7a8147a9909691fe8fcb96209b1133f851b81c7020",
					"scheme": "tcp",
					"host": "0.0.0.0",
					"port": 6379,
					"path": "/",
					"protocols": [
						"tcp"
					]
				}
			]
		}
	],
	"sites": [
		{
			"id": "site:api",
			"name": "api",
			"hosts": [
				"api.example.test"
			],
			"listeners": [
				{
					"binding": "35a5a0a9b3b5d7866e1da297abf88087c56e08c87b8ea4fa9dad6dcb1ab4e61f",
					"endpoint": "cdcccc23c6055cfcd5039aa9771da9de87894dc30420fc8d0ec583283c40eade",
					"default": false
				},
				{
					"binding": "35a5a0a9b3b5d7866e1da297abf88087c56e08c87b8ea4fa9dad6dcb1ab4e61f",
					"endpoint": "0a492aba7787bd961d4e94123c39f709e94ea9be1d38163d061c28da41c98370",
					"default": false
				}
			],
			"routes": [
				{
					"requestKind": "grpc",
					"path": "/opentelemetry",
					"target": {
						"kind": "binding",
						"binding": "a3bf9ca44465c933a625a621f10c1a9a4c632e9b0a7cb27e378c9134f6ca7920",
						"endpoint": "918c6bae9bc0e06e61cafe03a2e37a2cb2a569f00834d177e3af7e74c45e83f4"
					},
					"protocol": "grpc+http2",
					"connection": {
						"scheme": "http",
						"host": "127.0.0.1",
						"port": 4317,
						"path": "/opentelemetry"
					}
				},
				{
					"requestKind": "http",
					"path": "/",
					"target": {
						"kind": "binding",
						"binding": "025ae7cd729f6f9206ff7ba5504831755e33cd273e805563a1a71267f31d15bc",
						"endpoint": "432783ea6b6e659516771ecfcb22caefaa4fc7a2d833b291299805eb5d28c8ab"
					},
					"protocol": "http1",
					"connection": {
						"scheme": "http",
						"host": "127.0.0.1",
						"port": 8069,
						"path": "/"
					}
				},
				{
					"requestKind": "http",
					"path": "/orders",
					"target": {
						"kind": "remote"
					},
					"protocol": "http1",
					"connection": {
						"scheme": "https",
						"host": "192.0.2.20",
						"port": 9443,
						"path": "/orders"
					},
					"host": "orders.example.test",
					"tls": {
						"verify": true,
						"name": "orders.example.test",
						"trust": "file:/etc/demo/upstream-ca.pem"
					}
				}
			]
		}
	]
}
```

#### 4.3 完整文档逐项追踪

所有 ID 都由第 3.3 节列出的输入计算，下面用说明简称避免重复长摘要：

| 文档位置或字段 | 本例值 | 来源 → 处理 → 消费 |
| --- | --- | --- |
| `version` | 1 | 格式定义 → 读写双方检查 → 决定解析规则 |
| B_WEB 的 owner、aliases | application；配置路径和 web | web.option 的 PI → 规范化与合并 → 供路由引用及显式别名选择 |
| B_OTLP 的 owner、aliases | application；kestrel/endpoints/telemetry、telemetry | telemetry PI 的 path 为 Kestrel/Endpoints/telemetry，name 为 telemetry → 同样规范化 |
| B_GARNET 的 owner、aliases | application；externals/garnet/default、garnet | Garnet PI 的 path 为 Externals/Garnet/default，name 为 garnet → 选中服务器 default 的地址 |
| B_NGINX 的 owner、aliases | nginx；api/public | 生成后的代理监听及 profile 入口标识 → 归属代理组件，不归属应用 |
| 各 endpoint 的 scheme、host、port、path、protocols | 如 E_OTLP 的 http、0.0.0.0、4317、/opentelemetry、grpc+http2 | 第 1 节源值及第 3.2 节规则 → 规范监听快照 → 供能力与拓扑规划 |
| site 的 id、name、hosts | site:api、api、api.example.test | 第 4.1 节 profile → 本包唯一站点及入口域名 → 关联该包的 .web/nginx/demo.conf 中对应站点 |
| listener.binding | B_NGINX | 生成站点所使用的监听组 → 校验 owner 必须是 nginx |
| listener.endpoint | E_NGINX4 / E_NGINX6 | 同组中的具体地址 → 必须属于所引用 binding，不能只验证摘要在全局存在 |
| listener.default | false | 最终监听没有显式 default_server → 消费时不人为添加默认站点 |
| route.requestKind | grpc / http | 绑定协议组合或显式远程连接语义 → 选择请求类别和代理方式 |
| route.path | /opentelemetry、/、/orders | 前两项来自绑定 URL，第三项来自 profile 路径 → 按各请求类别的基路径规则匹配 |
| target.kind | binding / remote | 自动本包目标或显式远程 URL → 决定能否关联应用容器、发布本包端口 |
| 本包 target.binding / endpoint | B_OTLP/E_OTLP、B_WEB/E_WEB | 先选逻辑绑定，再选可连接端点 → 消费端据引用调整拓扑 |
| remote target | 只有 kind=remote | 远程 URL 已给出连接目标 → 不伪造本地 binding/endpoint 引用 |
| route.protocol | grpc+http2 / http1 | 从本包端点能力选定，或按显式远程 https 的普通 HTTP/1.1 语义确定 → 表示本条连接选定的能力；不同于端点完整 protocols 数组 |
| 本包 connection.scheme | http | 原监听是明文 HTTP → gRPC 也通过 http scheme 加 grpc+http2 表达，生成器再映射到相应代理指令 |
| 本包 connection.host | 127.0.0.1 | 0.0.0.0 按同机规则转成同族回环 → 实际连接地址，不回写监听 host |
| 本包 connection.port、path | 4317 /opentelemetry 或 8069 / | 保留选定端点的端口和路径 → 不额外拼接一遍路径前缀 |
| 远程 connection 的四项 | https、192.0.2.20、9443、/orders | 完整远程 URL 分解 → 用于连接远程服务，不解析成应用监听 |
| route.host | orders.example.test | header!Host → 覆盖上游 HTTP Host；本包两条路由省略则保留客户端 Host |
| tls.verify | true | server-tls-verify=true → 代理必须验证远程证书 |
| tls.name | orders.example.test | server-tls-name → SNI/验证身份；与连接 IP 分开 |
| tls.trust | file:/etc/demo/upstream-ca.pem | server-tls-trust → 目标环境中的 CA 资源引用；既不是文件内容，也不是制作机路径 |

本规范中端点路径仍表示其声明的请求范围；例如 Nginx 入口的根路径及协议集合说明入口能力，具体 gRPC 可达范围由 sites.routes 的 /opentelemetry 再约束，不能据此把所有 gRPC 路径开放给本包后端。

#### 4.4 请求如何经过这些关系

| 客户端请求 | 匹配与连接 | 值的去向 |
| --- | --- | --- |
| 普通 HTTP：`https://api.example.test:8443/products?id=7` | 入口 E_NGINX4 或 E_NGINX6 → http 根路由 → B_WEB/E_WEB → 127.0.0.1:8069 | 保留 /products?id=7 和原 Host；请求查询不进入 binding 身份 |
| gRPC：`/opentelemetry/opentelemetry.proto.collector.metrics.v1.MetricsService/Export` | grpc 类别 → /opentelemetry 路由 → B_OTLP/E_OTLP → 127.0.0.1:4317 | 完整 RPC 路径来自客户端协议方法；不在 .bindings 保存方法清单，不重复添加 /opentelemetry |
| 普通 HTTP：`/orders/42?expand=items` | /orders 比 / 更具体 → remote → 192.0.2.20:9443 | 保留路径和查询；上游 Host 和 TLS 名按配置为 orders.example.test |
| gRPC：`/other.Service/Call` | 无匹配 gRPC 范围 | 按既定映射规则产生未匹配 gRPC 响应，不回退到 Web 根路由 |
| 直接访问 Garnet 16379 发布端口 | 宿主映射 → 应用的 6379/tcp | 不经过任何 HTTP site/route，也不产生 HTTP Host 或 TLS 名推导 |

同一个例子也可以检查缺省和修改行为：

- 删除远程 route.host：保留原始客户端 Host；仍有 tls.name=orders.example.test，因此 TLS 名不会随着 Host 一起消失。
- 删除 profile 中的 server-tls-verify 输入：新模型的制作约定为默认验证，JSON 中仍必须明确写 verify=true；直接删除 JSON 的 tls.verify 是错误。
- 删除 tls.trust 输入：必须先明确实际信任根如何由部署平台提供，并由适配器映射到有效信任配置；不能仅靠“使用系统信任”四字就假定 Nginx 已配置 CA。若不能确定，制作/部署校验应报缺少信任来源。
- 将本包 Web 的源监听改成 127.0.0.1：同机连接仍可成立，但独立代理容器不能直接沿用 demo-app:8069；必须重新判断拓扑。
- 将入口域名改为 admin.example.test：site.hosts 与代理资产改变；本包 Web、OTLP、Garnet 的 endpoint 和 binding 身份不变。

#### 4.5 显式远程池与本包替代地址的区别

在独立变体中，把 orders 的单值 server 改为以下两项；其它入口配置不变。这是显式远程池结构示意，不加入上面的单目标完整例子：

```ini
[api orders]
path = /orders
server!east = https://192.0.2.20:9443/orders weight=2
server!west = https://192.0.2.21:9443/orders weight=1
server-balance = round-robin
```

路由局部表示如下。TLS 和 Host 继续使用上例的共同显式配置；两个目标都必须满足该证书名称约束：

```json
{
	"requestKind": "http",
	"path": "/orders",
	"target": {
		"kind": "remote",
		"balance": "round-robin",
		"members": [
			{"name":"east","weight":2,"connection":{"scheme":"https","host":"192.0.2.20","port":9443,"path":"/orders"}},
			{"name":"west","weight":1,"connection":{"scheme":"https","host":"192.0.2.21","port":9443,"path":"/orders"}}
		]
	},
	"protocol": "http1",
	"host": "orders.example.test",
	"tls": {"verify":true,"name":"orders.example.test","trust":"file:/etc/demo/upstream-ca.pem"}
}
```

east/west 来自具名 server 键；weight 来自各自参数；balance 来自显式策略；两份 connection 来自两个完整远程 URL。它们表示两个被明确指定的上游实例，因此可按策略分配请求。第 3.1.2 节的同一服务多个监听地址没有这些输入，不能被转换成这种池。

单目标使用路由的 connection；池使用 members[].connection，二者互斥，不能同时填写。池至少两个成员，name 忽略大小写唯一，weight 为正整数。balance 必需，可为 round-robin、least-connections、least-requests；具体代理不支持所选算法时必须报错。重试、会话保持和健康检查保存在下一节的 policy 中。现有池模型没有备份成员字段，本格式不虚构该能力。


#### 4.6 转发策略与健康检查

policy 可省略，表示失败次数 3、失败窗口 30 秒、重试条件 error/timeout、重试次数 1、无会话保持及无主动健康检查。出现 policy 时，其 failureCount、failureTimeout、retry、retryCount、affinity 均必需；cookie 和 health 可省略。时间统一用毫秒数，可以有小数；次数、权重使用整数，不接受字符串或隐式单位。

例如在第 4.5 节池配置中增加：

~~~ini
server-failure-count = 3
server-failure-timeout = 30s
server-retry = error,timeout
server-retry-count = 1
server-affinity = cookie
server-affinity-cookie = demo_session
server-health = /Ready?deep=true
server-health-interval = 10s
server-health-connect-timeout = 5s
server-health-send-timeout = 5s
server-health-read-timeout = 5s
server-health-failure-count = 3
server-health-recovery-count = 2
server-health-status = 200
server-health-header!X-Probe = bindings
~~~

对应 route.policy 为：

~~~json
{
	"failureCount": 3,
	"failureTimeout": 30000,
	"retry": ["error", "timeout"],
	"retryCount": 1,
	"affinity": "cookie",
	"cookie": "demo_session",
	"health": {
		"path": "/Ready?deep=true",
		"interval": 10000,
		"connectTimeout": 5000,
		"sendTimeout": 5000,
		"readTimeout": 5000,
		"failureCount": 3,
		"recoveryCount": 2,
		"status": [200],
		"headers": {"X-Probe": "bindings"}
	}
}
~~~

| 字段 | 来源与含义 | 约束、缺省或消费方式 |
| --- | --- | --- |
| failureCount / failureTimeout | server-failure-*；3 次失败、30×1000 毫秒窗口 | 非负；具体禁用语义由代理适配器映射 |
| retry / retryCount | server-retry-*；连接错误或超时后最多重试 1 次 | 条件集合非空且去重；次数非负，表示首次请求之后的重试次数 |
| affinity / cookie | server-affinity-*；采用 demo_session cookie 保持会话 | affinity 为 none/cookie；none 禁止 cookie；cookie 名遵循 HTTP token 字符规则，省略时由制作器确定稳定名称 |
| health.path | server-health 的原始请求目标 | 保留大小写、百分号编码和 query，不按绑定基路径小写化；必须单斜杠起始，不含 fragment、空白或控制符 |
| interval | 10s 转换为 10000ms | 正数，表示主动探测间隔 |
| connectTimeout / sendTimeout / readTimeout | 三项 5s 分别转换为 5000ms | 各自正数，不合并成一个总超时 |
| failureCount / recoveryCount | health 下的 3/2 | 均为正整数，表示探测失败/恢复阈值，与外层被动失败计数不同 |
| status | server-health-status=200 | 非空的 100～599 整数集合；输入 2xx 在制作期展开为 200～299 |
| headers | server-health-header!X-Probe | 去掉键前缀后得到 X-Probe；名称遵循 HTTP token 字符规则且忽略大小写唯一，值不含控制符；无额外头时写 {} |

retry 支持 error、timeout、invalid_header、http_500、http_502、http_503、http_504、http_403、http_404、http_429 或独占的 off。["off","timeout"]、负次数、零探测间隔、status:[600] 都失败。health 出现时表中所有字段必须完整，省略 health 表示不启用主动探测，不用 null 表示关闭。

这些字段保留用户策略，不能绕过具体代理的能力检查。例如不支持 cookie 会话保持或主动探测的 Nginx 发行版必须在生成阶段报告不支持；gRPC 适配器也不能将普通 HTTP 的自动重试直接套到已经发送的非幂等 RPC。

#### 4.7 名称、连接和缺省的边界

站点 hosts 可使用具体 DNS/IP、*.example.test、.example.test、example.*。它们来自入口域名规则，和后端连接的具体主机不同。["*.example.test"] 表示子域匹配，不允许把 *.example.test 写入 connection.host。

route.host 是上游 HTTP authority，可为 orders.example.test、orders.example.test:9443 或 [2001:db8::20]:9443；这些值来自显式 Host 覆盖，不反向改变连接端口或 TLS 名。含用户信息、路径、query、fragment 的值无效。tls.name 是具体证书名称或 IP，不含端口和通配符。

本包连接必须保持所选端点的 scheme、port、path，仅 host 根据拓扑选为可连接地址。例如 E_OTLP 的 0.0.0.0 替换为 127.0.0.1 或 demo-app，端口仍为 4317、path 仍为 /opentelemetry。remote 不引用本包身份，单连接和池都禁止使用 0.0.0.0、:: 作为连接目标。该结构校验不能代替现场 DNS、证书和网络连通性验证。

### 5. 承载位置与大小

三种格式均将同一 UTF-8 JSON 写为**应用安装根的普通 .bindings 文件**。它进入安装载荷、文件清单、大小及校验和统计，安装或卸载时随包管理；应用运行时不主动读取该交付文件。

| 格式 | 包内位置（安装根 /opt/demo） | 安装后位置 |
| --- | --- | --- |
| tar.gz | 归档根 .bindings，与 demo.dll 一样映射安装根 | /opt/demo/.bindings |
| deb | data.tar.gz 内 opt/demo/.bindings | /opt/demo/.bindings |
| rpm | CPIO payload 内 opt/demo/.bindings，并登记 RPM 文件表 | /opt/demo/.bindings |

权限为 0644，条目时间为 Unix epoch；文件无 BOM、无末尾换行。确定排序后，各格式的 .bindings 字节相同。包压缩头和其它包属性不属于该字节一致性约定。

#### 5.1 同一空文档怎样进入三种包

空文档为下面的一行，实际内容不包含代码块末尾换行，共 38 个 ASCII/UTF-8 字节：

~~~json
{"version":1,"bindings":[],"sites":[]}
~~~

~~~text
demo.tar.gz                 demo.deb                       demo.rpm
├─ .bindings                ├─ debian-binary                ├─ Header（含文件表）
├─ .version                 ├─ control.tar.gz/control      └─ CPIO payload
└─ demo.dll                 └─ data.tar.gz                     ├─ opt/demo/.bindings
                               ├─ opt/demo/.bindings          └─ opt/demo/demo.dll
                               └─ opt/demo/demo.dll
~~~

/opt/demo 来自包的 InstallPath；.bindings 文件名来自共享模型的保留常量；38 来自实际 JSON 编码长度。仅含这个空文档的包，其未压缩文件字节总量至少增加 38，Debian Installed-Size 按既有 KiB 向上取整规则计算。RPM 的摘要和文件数量也包含该文件。

Containerizer 先从相应包信息确定 InstallPath，再在映射后的安装路径找到 /opt/demo/.bindings，限长读取后交给共同模型。三种包均按普通文件安装和卸载该条目。安装路径仍是独立包属性；RPM 的 InstallPath 标签 1000003 不承载 JSON。

#### 5.2 文件承载的职责

普通文件使三种格式使用同一字节流，无需按原生元数据字段折行或为 JSON 定义私有 Header 标签。例如含 1000 字符别名的合法文档，仍按完整 JSON 文件处理；包适配器只负责载荷位置、权限、长度和校验和，不拆分字符串。文件存储仍须执行总长度、普通文件类型和唯一性校验，不能代替目标平台的原生包读取验收。

#### 5.3 容量限制与非法载荷

本工具采用逻辑 UTF-8 文档最大 1 MiB、JSON 最大深度 32。它们是工具资源策略，不是 tar/deb/rpm 格式的固有上限。读取先检查归档条目长度，模型再次检查输入字节长度及结构；制作在发布产物前检查序列化结果。

| 输入或操作 | 处理 | 数据来源与原因 |
| --- | --- | --- |
| 空文档 38 字节 | 通过长度与结构检查 | 顶层三个字段齐全 |
| 合法 JSON 正好 1,048,576 字节 | 通过长度门槛，继续所有语义校验 | 1 MiB=1024×1024，不等于内容必然有效 |
| 1,048,577 字节 | 拒绝 | 超限 1 字节；不截断、不丢末项 |
| 压缩后 80 KiB、条目内容 2 MiB | 拒绝 | 按解压后的逻辑文件长度检查 |
| 深度超过 32 | 拒绝 | 按 System.Text.Json 的容器深度计算 |
| 别名为 1000 个 ASCII 字符，整份文档未超限 | 不设单独字段行长门槛 | 普通文件无需 Debian continuation；仍校验别名与总长度 |
| 文件缺失或零字节 | 拒绝 | 空绑定结果必须写合法空文档 |
| 源载荷已有 .bindings 或 .bindings/child | 制作失败 | 保留路径不能覆盖用户文件或目录 |
| 两份同路径条目、符号链接或硬链接 | 读取失败 | 不选第一份/最后一份，不跟随链接 |

字符串 "服务" 直接 UTF-8 编码为 8 字节，使用 JSON Unicode 转义时为 14 字节；长度检查使用实际收到的 JSON 字节，不先解析无限输入再判断。格式化或不同等价转义不影响第 3.3 节的身份，但会影响文件实际大小。

### 6. 提取与诊断实例

以下是一个只涉及应用提取的完整输入。安装目录 /opt/demo 来自本例的 InstallPath，两个文件均由本次载荷选择收录；tar 的相对条目和 deb/rpm 的安装路径条目会映射到相同虚拟位置。/ 指虚拟安装文件系统根，并非 /opt/demo 的别名。

文件 /opt/demo/shared.option：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<option>
		<server enabled="true" url="http://*:8069,http://localhost:8069" protocols="Http1" />
	</option>
</configuration>
```

文件 /opt/demo/web.option：

```xml
<?xml version="1.0" encoding="utf-8"?>
<?import path="./shared.option"?>
<?zongsoft.tools.binding name="web" path="Server" filter="Enabled:true" template="${this.Url}" protocols="${this.Protocols}" required="true"?>
<configuration />
```

| 输入或中间值 | 如何取得 | 结果与用途 |
| --- | --- | --- |
| ./shared.option | 相对最终 web.option 所在目录解析 | 读取 /opt/demo/shared.option；制作机上同名文件不可见 |
| path=Server | 本次 Core 配置根下的 Server 项 | 默认别名 server；不依赖 PI 所处 XML 位置 |
| Enabled=true | filter 从当前项读取显式值 | 候选项通过；不存在或不匹配时没有结果，随后由 required=true 报错 |
| 两个完整 URL | template 读取当前项 Url | 默认 separators 包含逗号，得到两个地址；不切分模板表达式本身 |
| Http1 | protocols 模板读取同一项 | 规范为 http1，并应用到两个地址 |
| * 与 localhost | Core 解析 URL | 前者规范为 0.0.0.0；后者保留为 localhost，不查询 DNS 或替换成回环 IP |
| 8069 与 / | 端口来自 URL，路径由缺省根路径产生 | 两端点均有 port=8069、path=/，能力也相同，因此属于一个替代端点集合 |
| server、web | 配置路径和 PI name | 规范、去重、排序后成为 aliases；owner 固定为 application |
| endpoint.id、binding.id | 第 3.3 节的规范内容及 SHA-256 | 两个端点各有身份，包含两个端点的集合有一个绑定身份；别名不参与摘要 |
| shared.option 的独立读取 | 它自身也属于最终 .option 集合 | 没有 zongsoft.tools.binding PI，因此不额外生成绑定；它提供的配置不会合并到其它未导入文件 |
| version=1、sites=[] | 文件契约及无代理的输入 | 文档包含一个应用绑定、两个端点，没有配置代理站点 |

把 Import 改为 /opt/demo/shared.option，仍选择同一条目；改为 /shared.option 则选择虚拟根中的另一位置，缺失时失败。排除 shared.option 后，原必需 Import 失败，即使制作机源目录还保留该文件也不能读取。可选导入写 required="false"，只允许 Core 定义的缺失，不豁免 XML、循环或越界错误。

若另一个入包文件声明完全相同的端点并使用 name=api，结果合并 api 别名；若其集合为 localhost:8069 与 [::1]:8069，且 scheme、基路径和能力均相同，两组通过共同端点合并为三个替代地址。仅端口相同而端点不相交的集合不会合并；不同协议能力也不会被拼接。

文件结构错误提供 JSON 路径；语义检查提供字段或绑定位置及稳定规则标识，外层错误消息有中英文资源。例如 listener 把 binding 写成 B_NGINX、endpoint 写成 E_OTLP，规则 reference 表示端点不属于该绑定；当前语义位置可能为 /sites/target/endpoint，不承诺所有错误都含精确数组下标。

提取器使用 Core 指令的文件、行列及当前候选配置路径定位错误。例如 telemetry.option 第 2 行选中 Kestrel/Endpoints/telemetry，但 Url 为 http://*:0/opentelemetry，诊断信息包含以下部分（按字段展开的示意，不是逐字终端输出）：

~~~text
来源：telemetry.option:2（binding 声明）
配置路径：Kestrel/Endpoints/telemetry
属性：template
规则：address
~~~

文件与行列来自 Core PI 快照，配置路径来自选中项；字段求值失败时还记录 this 成员引用，例如 template/this.Url。上例端口 0 由地址解析拒绝，终端不打印原始 URL 或 Settings 全文。基础配置读取错误未能定位到具体 PI 时，报告正在加载的 option 文件及 configuration/read，行列为 0；不捏造位置。来源信息仅用于制作期诊断，不写入交付文件或身份。读取包时只能报告包文件与 JSON 内容位置，不虚构已经不存在的 option 行号。

<a id="routing-runtime"></a>

## 代理与运行端的共同语义

### 1. 路径和请求分类

#### 1.1 字面基路径

HTTP 声明路径不是路由模板或正则。空路径按 /；范围标识去掉非根路径的尾斜杠并忽略大小写，源配置不改写。当前契约每个非空段允许 ASCII 字母、数字及 -._~；拒绝中间空段、独立 . 或 .. 段、百分号编码、反斜杠、query、fragment、控制字符、花括号和通配符。此限制只约束声明的 HTTP 基路径，不限制业务参数字符集，不套用到 opc.tcp 路径。

请求按原始 URI 的 path 做 OrdinalIgnoreCase 基路径比较，不包含查询，不解码或折叠点段后重新选择后端。Nginx 使用 $request_uri；运行时使用 IHttpRequestFeature.RawTarget 的 path。origin-form 直接提取，absolute-form 按 HTTP 语法提取 path，空 path 为 /；OPTIONS * 不进入路径映射；非法目标返回 400。基路径之后的业务路径不受此大小写约定影响。

/opentelemetry 不把 /%6fpentelemetry、/x/../opentelemetry、/opentelemetry-extra 当作别名。选定区域后，剩余路径由应用正常解析；方法未命中不能换到另一区域。

| 声明 | 匹配 |
| --- | --- |
| / | 所属请求类别的全部有效 path |
| /metrics | 自身及 /metrics/ 开头的子路径 |
| /metrics/ | 与 /metrics、/METRICS 等效，自身及路径段子路径均匹配 |
| /opentelemetry | 自身及 /opentelemetry/…，不匹配原生 /opentelemetry.proto.… |

匹配重叠按**去除可选尾斜杠后的基路径长度**降序处理。等效范围同后端合并、不同独立后端冲突；/metrics/internal 可覆盖 /metrics。gRPC 挂载连接原生方法路径时只放一个斜杠。

同一入口、同一请求类别的声明若仅大小写或尾斜杠不同，视为同一范围；按后端身份合并或报告冲突。同一应用同区域可关联不同物理监听，运行时仍按实际连接选择。请求不做大小写转换或重定向。

#### 1.2 请求类别与失败

原生 gRPC 媒体类型为 application/grpc 或 application/grpc+某格式，允许媒体类型参数、大小写不敏感；application/grpc-web 及其变体不归入。分类不因 HTTP 版本不足或方法错误回退：HTTP/1 的 application/grpc 仍进入 gRPC 拒绝流程。普通 HTTP/2 不自动成为 gRPC。

普通 HTTP 与 protobuf+http* 共用 HTTP 类别；编码不产生第三套自动分流。相同路径分别声明这两种能力的不同后端仍冲突，不隐式按 Content-Type 划分 Protobuf 服务。

处理顺序：入口候选 → 请求类别 → 最长路径范围 → 唯一逻辑后端 → 同一服务的可连接地址与承载版本。

| 情况 | 行为 |
| --- | --- |
| 普通 HTTP 无范围 | 404 |
| gRPC 无范围 | HTTP 200、application/grpc、grpc-status=12，空 trailers-only 响应 |
| gRPC 的 HTTP 版本/方法不合法 | 400/405，不转普通 Web；即使无匹配范围也先执行这项检查 |
| 已选区域的未知 RPC | 该区域返回 UNIMPLEMENTED，不尝试更短范围或其它后端 |
| 后端连接失败 | 代理错误；不重新进行业务归属选择 |
| 制作期同范围歧义 | 终止制作，给出来源及冲突目标 |

#### 1.3 Handler

Core 的 HandlerAttribute 支持多次标注路径元数据，Diagnostics 协议库已依赖 Core；不为了标注给协议库增加 ASP.NET 托管依赖。现有 HandlerUtility/HandlerSelector 只处理 IHandler，全局服务扫描依据 ServiceAttribute，添加 Handler 不会把 gRPC Processor 自动注册为普通 IHandler。

Handler.Template 是完整挂载基路径，与 URL.path 忽略大小写及可选尾斜杠关联；binding.name 仍是部署别名。业务 gRPC 类型必须声明，根服务显式写 [Handler("/")]。不从类名、程序集、name 或 [handler] 推导路径。gRPC 不使用 Order；非零 Order 报错。模板只匹配基路径，调用方按协议填写服务名和方法名，gRPC 适配层对这部分保持精确校验。

同类型允许多个明确基路径，按继承规则读取后去重，各路径分别挂载，DI 实例/生命周期不重复创建。不同类型共享同一路径组成区域。同一应用内，多个监听使用同一路径，表示同一区域在这些监听上提供相同服务；需隔离实例时使用不同路径或宿主。

同一区域两个实现争用相同协议服务/方法、缺少对应监听、gRPC 端点没有已注册业务服务、非法路径均导致启动失败。网络端点的矛盾配置不能依赖跨插件提供程序枚举顺序覆盖。

新服务可采用小写 /grpc/<业务区域> 作为显式命名约定，例如 Handler("/grpc/opentelemetry") 与 http://*:4317/grpc/opentelemetry。/opentelemetry 同样有效。初始化器和代理不隐式添加 /grpc，原生 proto 方法名保持不变，客户端 Endpoint 必须使用相同基路径。它便于组织 URL，但不替代请求类别识别，也不能区分两个都只声明 /grpc 的独立后端；不同后端仍需 /grpc/opentelemetry、/grpc/orders 等各自范围。

### 2. Framework 适配

#### 2.1 共同语义及依赖

Core 的 HandlerAttribute 提供路径元数据，HandlerSelector 处理 IHandler；messaging 的收发地址由对应组件配置。

共同能力限定为 Core/Communication 的 NetworkAddress、NetworkProtocol：地址保存 Scheme、Host、HostType、AddressFamily、Zone、Port、Path，不设置应用方案白名单，也不施加绑定专用路径限制；协议以可空的 Name、Carrier 保留上层名称与承载，接受任意合法上层名称与 tcp、udp、http、http1、http2、http3 的组合，提供 HttpVersion、Transport。二者实现 `ISpanParsable<T>`（包含 `IParsable<T>`），公开 string 与 `ReadOnlySpan<char>` 的 Parse/TryParse；协议集合使用必须指定分隔符的重载。API 说明见 [Core 网络声明值](../../../framework/Zongsoft.Core/docs/networking.zh-Hans.md)。Core 不内置 grpc、protobuf、opc.tcp 等应用协议规则；裸上层名称不补全 Carrier。grpc → grpc+http2、protobuf → protobuf+http 的绑定约定由 Core 之外的绑定策略及运行适配层落实，再按相同上层名称归并集合，Web 与工具必须得到一致结果。Core 不包含 zongsoft.tools.binding PI、代理、包 JSON 或容器业务，不改变 EndpointConverter 对 System.Net.EndPoint 的转换；绑定路径限制、地址与协议的一致性、映射和版本选择由消费者负责。

Zongsoft.Web 提供只读 WebEndpointCollection、请求区域特征与端点约束；Grpc、Plugins.Web 消费它。WebEndpoint 由名称、NetworkAddress 和协议集合构造，Supports(httpVersion) 查询声明版本；WebEndpointCollection 只接受逻辑端点与协议规则，不持有配置源、不引用 Kestrel 类型。KestrelUtility.Convert 独立转换服务器配置，并可从同一次输入返回逻辑端点；显式应用协议与 HTTP 承载的组合不要求注册请求规则，裸名称通过传入规则补全。配置返回值由调用方释放，IPv6 TCP 适配通过单独的 KestrelUtility.Configure 完成。WebEndpointProtocol 为 HTTP 承载的上层协议提供默认承载、类别识别、请求校验、候选约束及无范围响应的窄扩展点；主库内置普通 HTTP，编码协议可注册默认承载并共享 http 类别。gRPC 专属规则由 Web.Grpc 的私有策略及 AddGrpcWebEndpoints 提供，GrpcInitializer.Registration 自动注册。规则必须先于快照完成注册；缺失、重名、独立类别重复、识别歧义及晚注册失败，不猜测为普通 HTTP。Grpc 项目在编译时依赖 Web，Web 不反向依赖 gRPC。Zongsoft.Core、Zongsoft.Web、Zongsoft.Plugins、Zongsoft.Plugins.Web 四个基础库由宿主统一提供，任何 .deploy 均不引用它们。通用中间件与匹配策略为注册入口的私有嵌套类，不为测试公开 API，不建立通用插件网络注册框架。

#### 2.2 启动与 Kestrel 投影

端点扩展的接入顺序：

1. 完成原有配置源、插件配置和服务发现。
2. 注册阶段只登记工厂与回调；全部服务注册完成后，从最终配置捕获同一次输入，应用默认简写策略并生成逻辑端点及转换配置，不在注册中临时构建服务提供程序。
3. 创建独立内存 Kestrel 投影：URL 去掉基路径，IPv4 通配 * 明确转成 0.0.0.0，protocols 转为原生枚举，保留证书/SNI/EndpointDefaults 等原生选项。
4. 在默认端点实际加载前，以 PostConfigure<KestrelServerOptions> 操作（在其它需被覆盖的后置配置之后注册）调用 options.Configure(投影, reloadOnChange:false)。原配置完整 URL 不变；禁止先加载原始端点再叠加投影。
5. 构建应用、初始化插件上下文、执行 gRPC 初始化及原有 Controller/Hub 映射。
6. 开始接受连接前完成区域、注册集合及方法冲突校验。

配置转换由独立工具提供，请求范围可单独注册。Plugins.Web 的 IServiceRegistration 注册器在 Web 宿主配置包含 Kestrel:Endpoints 子项且尚未注册 WebEndpointCollection 时，自动调用 AddWebEndpoints 组合转换、跨插件校验与范围管线，并调用 AddWebEndpointMonitoring 启用监视。显式接入优先保留自选配置节和监视选择；通用 WebApplicationBuilder.Build 不强制要求 Kestrel 配置，不禁止其它原生地址来源，不包装全部监听工厂。hosting 入口无须增加调用，业务插件继续自描述。非 Web 宿主或无端点声明时不自动接入。没有配置端点时转换保留原生选项；额外监听由宿主控制，启用范围约束时未匹配声明范围的请求被拒绝。跨插件配置冲突校验随端点功能接入。

相同 socket 的多个路径共用监听，其 scheme、TLS/SNI、连接级选项必须一致；物理 HTTP 承载能力可合并，逻辑绑定业务能力仍分开，不借此消除代理冲突。具体 IP 与同族通配等重叠 socket 冲突报错。

适配明确 * 为 IPv4 通配，[::] 为仅 IPv6 通配。Kestrel 的原生 * 会使用 AnyIP，因此必须进行上述地址投影。TCP transport 通过 SocketTransportOptions.CreateBoundListenSocket，在 Bind 前为管理的 IPv6 socket 设置 DualMode=false；不能先调用默认工厂再修改，因为默认工厂已设置双栈并完成 Bind。非管理端点保留原有工厂行为；两族并存必须验收。localhost 展开回环监听；其它 DNS 不冒充网卡，Kestrel 要求使用 IP/通配，域名由入口表达。HTTP/3 的 UDP/QUIC 监听另按平台能力验收，不能把 TCP socket 设置当作已处理 UDP。

明文 Kestrel 只接受 HTTP/1 或 HTTP/2 单一版本，http1 与 grpc+http2 共用明文 socket 报错。TLS 可按平台支持多版本；HTTP/3 要求 HTTPS/QUIC。语法可表达不等于适配器支持，不支持时报错。

#### 2.3 范围与服务注册

Zongsoft.Web 在路由前根据实际 Connection.LocalIpAddress/LocalPort、连接协议和原始 path 确定区域；不根据 Host/Forwarded 判断内部监听。禁止重叠监听后，本地端点可唯一定位 socket；将区域保存为请求特征。

GrpcInitializer 对各区域 MapGroup，并对已注册类型 MapGrpcService；端点携带区域元数据。端点选择策略排除其它监听/区域候选，忽略基路径大小写，仅精确校验原生 RPC 服务和方法名。只调用 UsePathBase 不构成隔离。

Controller、静态文件、Hub 保留原有路由。普通 HTTP 基路径表示现有服务范围，不自动给所有控制器加前缀；gRPC 专用监听不顺带开放这些普通路由。区域约束必须覆盖静态文件早返回和端点路由，身份/授权继续由应用负责。

设置 GrpcServiceOptions.IgnoreUnknownServices，关闭受全局注册顺序影响的内置 unknown-service/method fallback。框架为每个选定区域提供最低优先级 gRPC fallback；不得因未知方法回退到更短区域。fallback 保留取消和状态约定，经过既有身份/授权管线。

#### 2.4 反射与客户端

每个区域自动挂载区域感知的反射 wrapper。按当前区域的实际 ServiceDescriptor 集合创建、缓存上游 ReflectionServiceImpl，委托 ServerReflectionInfo；不重写协议。

list_services 只包含当前区域实际业务及反射服务。描述符仍以完整 proto 文件和依赖为单位，可能包含同文件其它符号；不承诺逐符号保密，不把反射当作授权。不能从其它区域注册表补齐未知服务。区域实例由应用所有，按实际释放契约处理，不释放容器共享业务实例。

Diagnostics 官方 OTLP exporter 使用含前缀的 Endpoint，不额外注入前缀。protocols/client 的 GrpcClientHandler(path, innerHandler) 为直接 GrpcChannel 调用校验并添加字面基路径，保留 query、Header、流和取消，不缓冲消息；构造时检查处理链，发送时防止同一请求重复应用。不从服务端 Handler 推断客户端地址，不引入 Web 服务端依赖。channel/handler 所有权遵守 GrpcChannelOptions.DisposeHttpClient；自有处理链示例显式设为 true，共享链由调用方管理。双语说明给出完整用法。

#### 2.5 配置变更

地址、协议、基路径和服务集合采用启动快照。网络配置变更只报告“需要重启”，不单独刷新 Kestrel 或路由；其它配置通知不受影响。修改后重新生成代理并按正常部署重启应用、重载代理。证书轮换沿用独立证书设施，不改变网络范围。该契约不承诺运行中热插拔。

### Web 上游成员归属

`Definition.ServerGroup.Member` 属于指定的上游组，保存成员名称、连接端点和权重。例如 east/west 两项的权重 2/1 来自同一组的具名 server 设置。共用的 Endpoint、Header 和策略值仍由 Definition 组织；不把跨路由复用的值强行归入某个成员。

### Nginx 生成与拓扑

先生成 http 级请求类别 map，再将“类别 + 原始 URI”映射到固定命名 location；名称包含稳定的包/站点前缀。同类路径按规范长度降序，正则与参数分别转义，以忽略大小写及路径段边界匹配。目标使用静态 proxy_pass/grpc_pass，不附加 URI 后缀或 rewrite；内部 418 分派保留方法、正文和 URI，不拦截真实上游 418。gRPC 目标与无匹配响应都先检查 HTTP 承载及 POST，405 带 Allow: POST，并保留 Header、TLS、流式超时和明确的安全重试策略。

最终监听元数据从最终指令树读取。允许可表达的原生 Header、证书和策略；动态 listen、不可解析的 include，以及会破坏自动分派的 location、rewrite、error_page、proxy_pass、grpc_pass 覆盖在制作时失败。

后端能力取信于声明，不探测服务器或查询框架支持列表。客户端到代理、代理到应用分别选择连接；HTTP/3 入口需要 QUIC/UDP 与 TLS。标准 proxy_pass/grpc_pass 无法表达 HTTP/3 上游时，应报告适配器映射限制，不据此判断后端不支持，也不补入未声明的 HTTP/2。同一服务语义内优先选择可用的最高声明版本；普通 HTTP 与 gRPC 不互借版本。原始 TCP/UDP 直接发布，不自动生成 Nginx stream。

同机连接将通配地址转为同族回环，具体 IP/DNS 保留。跨容器连接按目标网络选择明确可达的替代端点，保持 scheme、port、path；替代地址不转换为负载均衡池。Host、TLS 验证名和连接地址分别来自各自配置，不能因替换连接主机就猜测证书名。

相关上游契约：[gRPC HTTP/2](https://grpc.github.io/grpc/core/md_doc__p_r_o_t_o_c_o_l-_h_t_t_p2.html)、[GrpcChannel 子目录](https://learn.microsoft.com/en-us/aspnet/core/grpc/troubleshoot#calling-grpc-services-hosted-in-a-sub-directory)、[Nginx map](https://nginx.org/en/docs/http/ngx_http_map_module.html)、[proxy_http_version](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_http_version)、[grpc_pass](https://nginx.org/en/docs/http/ngx_http_grpc_module.html#grpc_pass)、[QUIC](https://www.rfc-editor.org/rfc/rfc9000.html)。适配器回归流程见 [SKILL](../SKILL.md#binding-validation)。

运行组件的专门配置和生命周期由其项目文档维护：[Garnet](../../../framework/externals/garnet/README.zh-Hans.md)、[ZeroMQ](../../../framework/messaging/zero/README.zh-Hans.md)、[MQTT](../../../framework/messaging/mqtt/README.zh-Hans.md)、[OPC](../../../framework/externals/opc/README.zh-Hans.md)、[Diagnostics](../../../framework/Zongsoft.Diagnostics/README.zh-Hans.md)、[Web gRPC](../../../framework/Zongsoft.Web/grpc/README.zh-Hans.md)。这些说明涵盖设置对象转换、插件装配、监听启停、发现/注册地址及客户端调用；工具只消费明确的绑定声明。
