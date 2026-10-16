# Zongsoft 部署工具

![License](https://img.shields.io/github/license/Zongsoft/Zongsoft.Tools.Deployer)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Deployer)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Deployer)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/Zongsoft.Tools.Deployer?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

终端用颜色区分警告、错误和成功提示；重定向输出为纯文本，并保留原输出通道。

运行 `dotnet deploy --help` 或 `dotnet deploy -h` 查看常用选项：命令名为粗体青色，选项名为青色，占位符及候选值为绿色。帮助直接返回 `0`，不读取 `.env` 或处理输入文件。 部署流程开始后，结束时显示一行总耗时，命令名称和秒数以粗体青色突出；失败退出也显示已用时间，耗时本身不表示成功。

-----

## 概述

这是一个应用部署工具，通过指定的部署文件来指示部署工具复制特定文件到目标位置。

建议在部署项目目录定义一个名为 `.deploy` 的默认部署文件，部署文件为 `.ini` 格式的纯文本文件。

## 格式

部署文件为 `.ini` 格式的纯文本文件，其内容由中括号包裹的 **章节**_(`Section`)_ 和 **条目**_(`Entry`)_ 两种内容组成，其中 **章节** 部分表示部署的目标目录。

**章节** 指定结构性的目录层级；**条目** 的源路径和输出路径支持 Core `${name}` 模板表达式，变量来自命令选项、环境变量、source 直属 `.env`（含显式导入）和输出目录的应用配置。

每个条目由 **键** 和 **值** 两部分组成，以等于号 _(`=`)_ 分隔，其中 **值** 可省略。

- **键** 由 _解析器名_ 和 _解析参数_ 两部分组成，以冒号 _(`:`)_ 分隔；
	- 解析器名：省略、留空或指定为 `path` 时采用默认的路径解析器，此外还支持 `template`、`nuget` 和 `delete`/`remove` 解析器。解析器名不区分大小写。
	- 解析参数：由指定的解析器进行解析，详情参考下面的 _解析参数_。

- **值** 由 _目标路径_ 和 _过滤条件_ 两部分组成。
	- 目标路径：表示部署的目标路径，缺失则表示目标目录由所在 **章节** 指定，且目标文件名与原文件同名。
	- 过滤条件：表示解析的前置条件，详情参考下面的 _过滤条件_。

### 解析参数

#### 路径解析器

默认解析器名称为 `path`，名称可以省略，表示将解析参数表示的源文件 _(支持通配符匹配)_ 复制到目标位置。

解析参数表示待部署的源文件路径，源文件路径支持 `*`、`?` 以及 `**` 三种通配符，其中 `**` 表示多级目录匹配。

Windows 绝对源路径含有盘符冒号时，使用 `path:` 前缀，例如 `path:D:/dir/files.ext` 或 `path:D:/dir/files.ext`；否则 `D:/...` 中的 `D` 会被当成解析器名。也可使用相对于部署文件的路径，或通过变量展开绝对路径。

```ini
[plugins zongsoft data]
path:D:/Zongsoft/framework/Zongsoft.Data/src/Zongsoft.Data.plugin

[plugins zongsoft data mysql]
drivers/mysql/src/Zongsoft.Data.MySql.plugin
```

`path:` 前缀选择默认路径解析器，冒号之后才是源路径。相对路径也可显式指定该前缀，例如 `path:drivers/mysql/src/Zongsoft.Data.MySql.plugin`。此示例引用 [framework](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Data) 中实际存在的插件文件，并假定部署文件位于 `D:/Zongsoft/framework/Zongsoft.Data` 目录；请根据本地仓库位置调整路径。

> 💡 提示：通常不应该在 `.deploy` 文件中指定绝对路径，因为源路径默认基于所在 `.deploy` 文件的路径，而目标路径则基于部署宿主程序或部署参数指定的目标路径的相对位置，参考 [framework](https://github.com/Zongsoft/framework) 中的相关项目的部署文件。

#### 模板解析器

`template:` 读取文本文件，以本次部署的 Core `TemplateEvaluator` 求值后生成配置。它与 `path:` 使用相同的相对路径、通配搜索、章节目标和过滤规则；显式指定目标名称时按该名称输出，否则移除源文件名末尾的 `.template`。普通路径复制不会自动解释模板内容。

```ini
# hosting/daemon/.deploy
template:../.deploy/.shared/app.option.template = Zongsoft.Hosting.Daemon.option

[plugins zongsoft externals aliyun]
template:../.deploy/.shared/Zongsoft.Externals.Aliyun.option.template
```

模板只使用 Core 语法，没有另一套变量或条件语言。`${mysql:server}` 原样插值；XML 文本及属性用 `${mysql:server#xml}` 转义，JSON 用 `${title#json}` 生成完整 JSON 值（字符串包含引号，不应再套引号）。`.env` 值是字符串，`#json` 不猜测数字或布尔类型。没有通用 INI/YAML 转义，按目标格式组织内容。`\${runtime}` 输出字面 `${runtime}`，`\\` 输出一个反斜杠。

```xml
<connection value="${mysql:connection#xml}" />
```

模板按 UTF-8 读取，也识别 UTF BOM，输出为无 BOM 的 UTF-8，保留模板换行。整个计划在任何写入或删除前完成模板求值；缺失的显式模板文件、未定义变量、循环、非法表达式或源文件变化都会阻止执行。通配符没有匹配结果时不产生操作。

默认 `newest` 对模板比较生成内容：内容不同就更新，即使目标时间更新；相同则记为 `Unchanged` 并保留时间。`never` 保留已有目标，`alway` 总是写入。普通文件复制继续按修改时间处理 `newest`。

`.env` 只提供变量，不作为文件载荷复制；目录搜索选中的 `.env` 也会跳过。模板后缀约定为 `*.template`。

#### Delete 解析器

解析器名称为：`delete` 或 `remove`，表示删除指定的目标文件。

解析参数表示待删除的目标文件，目标文件的完整路径由所在 **章节** 指定的目录与解析参数组合而成。

> 🚨 注意：该解析器不支持 _目标路径_ 部分，因此不能包含它。

##### 示例

将目标位置 `~/plugins/zongsoft/messaging/mqtt` 目录中的 `Zongsoft.Messaging.Mqtt.option` 文件删除。

```ini
[plugins zongsoft messaging mqtt]
nuget:Zongsoft.Messaging.Mqtt
delete:Zongsoft.Messaging.Mqtt.option
```

> 💡 提示：示例中的 `nuget:Zongsoft.Messaging.Mqtt` 包中的部署文件包含默认的配置文件 _(即 `Zongsoft.Messaging.Mqtt.option`)_，但实际项目并不需要该配置文件，所以随后即将该配置文件删掉。

#### NuGet 解析器

解析器名称为：`nuget`，表示下载 NuGet 包并执行相应部署，同时还会下载指定包的相关依赖包。

解析参数格式：`package@version/path`，其中 `@version` 和 `/{path}` 可选。
- 如果未指定版本或版本为 `latest`，选择最新稳定版本；显式设置 `--prerelease:true` 才把预发布版本纳入该选择。明确指定的预发布版本仍可使用。
- 如果未指定路径则：
	- 若该包的根目录包含 `.deploy` 文件，则执行该部署文件，不额外复制默认资产或下载未使用的依赖；
	- 否则解析依赖闭包并部署最适用的资产：优先使用目标 RID 的托管运行时组，无适用运行时组才使用 `lib/{framework}`；同时选择目标 RID 的原生资产和符合规则的内容文件。
		> `{framework}` 表示最接近 `${Framework}` 变量声明的 *目标框架* 版本。

> 💡 提示：_**Z**ongsoft_ 的 NuGet 包内根目录通常有一个名为 `.deploy` 的部署文件，包内的 `artifacts` 目录则存放着它的插件文件(`*.plugin`)_(至少一个)_、配置文件(`*.option`)、[数据映射文件](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Data)(`*.mapping`)等附属文件。

> 💡 注意：名为 `NuGet_Server` 变量定义了该解析器的 NuGet 包源，如果未定义则采用 `https://api.nuget.org/v3/index.json` 作为其默认值。

##### 依赖包

_**N**uget_ 包下载器默认会忽略以 `System.`、`Microsoft.Extensions.`、`Zongsoft.` 打头的依赖包；
还可以通过 `--ignoreDependentPrefix` 命令选项指定需要忽略的依赖包前缀，多个前缀使用 `,` 或 `;` 或 `|` 符进行分隔。前缀匹配不区分大小写；忽略规则只作用于依赖边，不屏蔽清单明确请求的根包。

##### 示例

- 获取 `Zongsoft.Plugins` 包的最新版本，并将包中的 `/plugins` 目录中的 `Main.plugin` 插件文件部署到目标的 `~/plugins` 目录中。
	> ```ini
	> [plugins]
	> nuget:Zongsoft.Plugins/plugins/Main.plugin
	> ```

- 获取 `Zongsoft.Data` 包的 `6.2.0` 版本，并执行包中的 `.deploy` 部署文件。
	> ```ini
	> [plugins zongsoft data]
	> nuget:Zongsoft.Data@6.2.0
	> nuget:Zongsoft.Data@6.2.0/.deploy
	> ```
	> **注：** 因为 `Zongsoft.Data` 包的根目录包含 `.deploy` 文件，所以上述两种写法是一样的效果。

- 部署 `MySql.Data` 包的 `8.3.0` 版本。_(假设指定了 `Framework` 变量值为 `net8.0`)_
	> ```ini
	> nuget:MySql.Data@8.3.0
	> ```

	> 1. 首先下载 `MySql.Data@8.3.0` 包以及它的依赖包（忽略以 `System.` 和 `Microsoft.Extensions.` 打头的依赖包）：
	> ```
	> BouncyCastle.Cryptography     2.2.1
	> Google.Protobuf               3.25.1
	> K4os.Compression.LZ4.Streams  1.3.5
	> ZstdSharp.Port                0.7.1
	> ```
	> 2. 依次获取上述依赖包中最接近 `Framework` 变量指定的 `net8.0` *目标框架* 版本的库文件。
	> 3. 复制下载的 NuGet 包中的库文件到目标目录。

### 过滤

在条目的尾部以 `<` 和 `>` 括起来的部分即为过滤条件，不满足过滤条件的条目会被忽略。

支持多个条件组合，每个条件由变量名和比较值组成，变量名若以 `!` 打头则表示对该条件的匹配结果取反；如果要比对多个值则以逗号分隔。如下所示：

```plaintext
../.deploy/${scheme}/options/app.${environment}.option       = web.option    <application>
../.deploy/${scheme}/options/app.${environment}.option       = web.option    <!application>
../.deploy/${scheme}/options/app.${environment}-debug.option = web.option    <preview:A,B,C>
../.deploy/${scheme}/options/app.${environment}-debug.option = web.option    <!preview:X,Y,Z>
../.deploy/${scheme}/options/app.${environment}-debug.option = web.option    <application | debug:on>
../.deploy/${scheme}/options/app.${environment}-debug.option = web.option    <!application & !debug:on>
```

> 1. `<application>` 表示存在名为 `application` 的变量(*不论其内容*)，则结果为真。
> 2. `<!application>` 表示不存在名为 `application` 的变量(*不论其内容*)，则结果为真。
> 3. `<preview:A,B,C>` 表示名为 `preview` 的变量值为“`A`,`B`,`C`”(*忽略大小写*)中的任何一个，则结果为真。
> 4. `<!preview:X,Y,Z>` 表示名为 `preview` 的变量值不是“`X`,`Y`,`Z`”(*忽略大小写*)中的任何一个，则结果为真。
> 5. `<application | debug:on>` 表示存在名为 `application` 的变量(*不论其内容*) **或者** 名为 `debug` 的变量值为 `on`(*忽略大小写*)，则结果为真。
> 6. `<!application & !debug:on>` 表示不存在名为 `application` 的变量(*不论其内容*) **并且** 名为 `debug` 的变量值不是 `on`(*忽略大小写*)，则结果为真。

支持对 *目标框架* 进行匹配及版本比较，如果 *目标框架* 以`^`符结尾则表示当前部署 *目标框架* 版本必须大于或等于该版本，如下所示：

```plaintext
${NUGET_PACKAGES}/mysql.data/8.1.0/lib/netstandard2.1/*.dll     <framework:net7.0^>
${NUGET_PACKAGES}/mysql.data/6.10.9/lib/netstandard2.0/*.dll    <framework:net5.0,net6.0>
```

## 变量

工具明确启用 Core 的变量回退：具名变量按当前命名空间、各级父命名空间、全局的顺序查询；每一级先按来源优先级查询全部来源；全部缺失则报告缺少变量。无命名空间的引用仍写作 `${name}`，命中空值也停止回退。

每个命名空间内依次查询命令选项、目标应用的 `appsettings.json`、source 直属 `.env`（含显式导入）、系统环境变量。首个命中生效，`appsettings.json` 中显式声明的 null 也会阻止低优先级来源回退。配置和命令选项名忽略大小写；系统环境通过 Core `Variables.Environments()` 实时读取，只提供默认命名空间，名称及原始值不改写，Windows 忽略大小写，Unix/Linux 区分大小写。

所有变量（包括 `framework`）统一遵循首个命中生效：null、空字符串、false 和 0 都不会触发下层回退。只有未提供该值时才继续查找；需要有效值的业务操作负责校验并在无法继续时报告错误。

只自动读取 source 直属 `.env`，不搜索父目录、子目录或各个清单所在目录；如需父目录配置，显式使用 `#@import ../.env`；同次调用的全部清单共用变量集合。INI 文件使用 Core `Profile.Load` 读取，支持 `#@import`。根条目属于默认命名空间，章节层级以点号组成命名空间，条目名的点号和连字符改为下划线：`[io rustfs]` 下的 `access_key=example` 生成 `io.rustfs:access_key=example`，根级 `environment=Development` 生成 `environment`。缺失的 `.env` 跳过，读取或解析失败终止初始化。值仅在使用时展开，不修改进程环境变量。

变量值可递归引用其他变量，不受命令选项顺序影响；仅在使用时展开，缺失、循环引用或超过 64 层会报错。`output` 可引用命令选项、环境变量及已加载的 `.env` 变量；目标目录确定后才加载其中的 `appsettings.json`。

- 如果 `appsettings.json` 中定义了名为 `ApplicationName` 的属性，则可以使用 `application` 作为该属性的变量别名。
- 名称为 `Framework` 的变量表示 .NET *目标框架* 标识，有关该 *目标框架* 标识的定义请参考：https://learn.microsoft.com/zh-cn/dotnet/standard/frameworks

可以通过命令选项、环境变量或 `.env` 来指定 NuGet 相关参数：
- `NuGet_Server` 表示 NuGet 服务器信息，默认值为：`https://api.nuget.org/v3/index.json`
- `NuGet_Packages` 表示 NuGet 包的目录，默认值为：`%USERPROFILE%/.nuget/packages`

### 源目录、导入与 hosting

`--source` 自身相对于调用时的当前目录解析；它不能依赖尚未加载的 `.env`。确定 source 后，才加载其直属 `.env`（含显式导入）并求值 `--output`。本地相对 `.deploy` 参数、output、report、lockFile、previous、NuGet_Packages 和本地 NuGet_Server 都相对于 source，包含 `./` 与 `../`；绝对路径保持原义。`.deploy` 内的源路径和 `#@import` 仍相对于声明文件。

部署 API 的输出目录参数为 `outputDirectory`；`DeploymentContext.OutputDirectory` 保存当前 `.deploy` 的输出目录。条目和操作通过 `Output` 表示输出，报告和锁文件的 JSON 使用 `Operations[].Output` 记录输出路径。

工具不识别 scheme、environment 或 network；它们都是普通变量。例如 `hosting/daemon/.env` 主动导入方案：

```ini
#@import ../.env
#@import ../.deploy/${scheme}/.env

name=Zongsoft.Hosting.Daemon
title=Zongsoft Daemon
```

`hosting/.deploy/default/.env` 可以再 `#@import ../.env` 复用 `hosting/.deploy/.env`。其他文件只通过显式 `#@import` 导入，路径相对于声明文件。同一 Profile 的后续同名声明覆盖此前声明，因此项目与方案的覆盖关系由 `.env` 写法决定。导入参数只能引用命令选项、当前文件此前已读条目（含已完成导入）和系统变量，不能引用后续声明。全部 `.deploy` 和模板共用同次调用的变量，不按模板物理目录重载变量。

从 hosting 根目录部署 daemon 的配置及插件：

```powershell
dotnet deploy --source:daemon --output:'bin/${edition}/${framework}' --scheme:default --environment:production --network:internal --edition:Release --platform:linux --architecture:x64 --prerelease:true
```

根目录 `deploy.cmd`/`pack.cmd` 询问宿主源目录。框架覆盖留空时，部署及打包使用 `.env` 的 framework，Cake 使用 build.cake 默认值；两者须一致。deploy.cmd 只构建和部署，pack.cmd 独立打包；填写覆盖值时，两个脚本须使用同一 framework。`edition` 在部署中用于编译目录，安装包 Edition 则是独立身份参数。参见 [hosting README](https://github.com/Zongsoft/hosting/blob/main/README.zh-Hans.md#部署)。

## 安装

- 查看工具
```bash
dotnet tool list
dotnet tool list -g
```

- 首次安装
```bash
dotnet tool install -g zongsoft.tools.deployer
```

- 升级更新
```bash
dotnet tool update -g zongsoft.tools.deployer
```

- 卸载
```bash
dotnet tool uninstall -g zongsoft.tools.deployer
```

### 从本地源码安装（用于测试）

运行 `dotnet cake --edition Release` 可完成 Release 构建及 .NET 8/9/10 回归测试，并生成本地工具包，不会推送 NuGet。还原、编译和测试使用同一配置；Debug 引用本地 Core DLL，Release 使用项目声明的 Core NuGet 包。

源码编译后无需发布到 NuGet.org，即可从生成的 `.nupkg` 安装。以下命令使用 .NET 10 SDK，在 `D:/Zongsoft/tools/deployer` 目录执行；其他检出位置使用对应目录。

部署器已启用 `GeneratePackageOnBuild`，Release 构建会同时生成工具包：

```powershell
dotnet build src/Zongsoft.Tools.Deployer.csproj -c Release
```

确认构建成功且 `src/bin/Release/Zongsoft.Tools.Deployer.<version>.nupkg` 已生成后，首次安装执行：

```powershell
$toolVersion = dotnet msbuild src/Zongsoft.Tools.Deployer.csproj -getProperty:Version -nologo
dotnet tool install -g Zongsoft.Tools.Deployer --version "$toolVersion" --source ./src/bin/Release --no-http-cache
```

若已安装该工具，尤其是重新编译了同一版本，先卸载，再执行上面的本地安装命令：

```powershell
dotnet tool uninstall -g Zongsoft.Tools.Deployer
```

安装命令从项目文件自动读取版本号；包文件名中的 `<version>` 表示该值。`--source` 限定本次安装只使用本地目录，避免选中 NuGet.org 的同名包；`--no-http-cache` 禁用下载缓存，选项说明见 [.NET 工具安装文档](https://learn.microsoft.com/zh-cn/dotnet/core/tools/dotnet-tool-install)。安装后使用 `dotnet tool list -g` 核对版本。这里的“本地”指包来源，`-g` 仍会替换当前用户的全局工具。只做本地测试不要运行 Cake 的 `pack` 任务，它会推送到 NuGet.org。

## 执行

- 在目标(宿主)目录执行默认部署：
```bash
dotnet deploy --edition:Debug --framework:net10.0 --platform:win --architecture:x64
```

- 如果目标(宿主)目录没有默认部署文件(`.deploy`)，则必须手动指定部署文件名(支持多个部署文件)。以下示例假定 `Zongsoft.Data@6.2.0` 已下载并解压到 NuGet 包目录：
```bash
dotnet deploy --edition:Debug --framework:net10.0 --platform:win --architecture:x64 "${NUGET_PACKAGES}/zongsoft.data/6.2.0/.deploy"
```

- 为了部署方便可以在目标(宿主)项目创建相应版本的部署脚本文件，譬如：
	- deploy-debug.cmd
		> `dotnet deploy --edition:Debug --framework:net10.0 --platform:linux --architecture:x64`
	- deploy-release.cmd
		> `dotnet deploy --edition:Release --framework:net10.0 --platform:linux --architecture:x64`

### 命令选项

命令支持普通终端和重定向/管道执行。成功返回 `0`，解析、依赖或文件操作失败返回 `1`，取消返回 `130`。默认先生成完整计划，预检查失败时不写入目标；正常覆盖跳过和删除有独立统计。

缺失的普通路径源文件（包括包内 `.deploy` 引用的可选文件）和部署清单只产生警告：每项计入跳过，继续处理其余输入。默认 `.deploy`、显式清单参数及没有 `.deploy` 的目录参数均适用，方便不同环境统一传入可选文件。警告在 `--verbosity:quiet` 下也会输出，并保留在报告诊断中，不导致非零退出码。NuGet 包缺失、无效变量或语法、源链接失效、访问错误，以及计划生成后文件丢失仍作为失败处理。

```text
dotnet deploy [--选项:值 ...] [部署文件或目录 ...]
dotnet-deploy [--选项:值 ...] [部署文件或目录 ...]
```

两个入口等价，没有子命令。无位置参数时读取 source 直属的 `.deploy`；可按顺序传入多个部署文件，目录参数使用其直属 `.deploy`。相对 `.deploy` 参数基于 source，清单内相对源路径基于所在清单，目标路径基于 `output`。选项亦可写为 `--key=value`，含空格的值应按终端语法加引号。任意其他 `--name:value` 也会成为用户变量，供清单和配置引用；下表仅列出工具自己识别的控制选项。部署文件中的 `path`、`template`、`nuget`、`delete`/`remove` 是解析器，不是子命令。

| 选项 | 默认值 | 用途 |
| --- | --- | --- |
| `--source:<目录>` | 调用时当前目录 | 工作源目录；选项自身相对于调用目录解析，只使用命令选项和系统变量定位，必须存在。 |
| `--output:<目录>` | source | 目标根目录；先用命令选项、环境变量及 `.env` 变量解析，再加载该目录的 `appsettings.json`。 |
| `--verbosity:<级别>` | `normal` | `quiet`、`normal`、`detail`；无法转换的值报错。 |
| `--overwrite:<策略>` | `newest` | `alway`（保持现有枚举拼写）、`never`、`newest`。这不是布尔开关。 |
| `--expansion[:布尔值]` | `false` | 目录通配匹配时，将匹配到的相对目录层级保留到目标路径；`false` 只保留通配捕获部分。 |
| `--ignoreDeploymentFile[:布尔值]` | `false` | 遇到源 `.deploy` 文件时按普通文件复制，不递归执行该清单。 |
| `--ignoreDependentPrefix:<前缀列表>` | 内置前缀 | 跳过匹配前缀的依赖边；用 `,`、`;` 或 `|` 分隔。 |
| `--offline[:布尔值]` | `false` | 只使用本地 NuGet 缓存。 |
| `--prerelease[:布尔值]` | `false` | 选择 latest 时允许预发布版本。 |
| `--dry-run[:布尔值]` | `false` | 生成并校验计划，不修改目标。 |
| `--explain[:布尔值]` | `false` | 输出逐项计划/执行结果。 |
| `--report:<文件>` | 无 | 将计划及结果原子写入 JSON 文件。 |
| `--lockFile:<文件>` | 无 | 成功部署后保存锁定计划；`locked` 时读取它。 |
| `--locked[:布尔值]` | `false` | 要求现有锁定计划与本次计划匹配。 |
| `--previous:<文件>` | 无 | 读取上次报告，识别不再选中的目标文件。 |
| `--prune[:布尔值]` | `false` | 配合 `previous` 删除内容未变的旧文件；改动过的文件保留。 |

所有布尔选项支持裸开关，以及 `true/false`、`1/0`、`yes/no`、`on/off`、`enable(d)/disable(d)`，大小写不敏感；其他值按 Core `Switch` 约定视为 false。可用 `${name}` 引用变量，名称遵循 Core 模板标识符规则，支持命名空间及成员/索引访问，按需递归展开；用到的值缺失、循环或超过 64 层即报错，先展开再转换类型。枚举选项沿用 Core 的转换规则，不额外检查枚举成员是否已定义；调用方应提供有效枚举项。未选中的部署分支不展开。

NuGet 相关的 `NuGet_Server` 与 `NuGet_Packages` 可通过命令选项、环境变量或 `.env` 提供；`Framework`、`Platform`、`Architecture`、`edition` 等是供解析器和部署文件使用的普通变量。示例：

```powershell
dotnet deploy --output:'bin/${edition}/${framework}' --edition:Release --framework:net10.0 --dry-run --report:deploy-plan.json .deploy extra.deploy
```

### NuGet 包

同一次命令的普通 NuGet 根包共同解析依赖图，固定明确的根版本，选择满足全部范围的最低可用依赖版本。无解、循环、降级约束或同目标不同内容的包资产冲突会在写入前报错。这是面向部署资产的严格求解，不执行 MSBuild/buildTransitive，也不等同于完整的 dotnet restore。不同插件目录不自动代表加载隔离；确有独立加载环境时可以分开调用。包内 .deploy 和显式包内路径按清单展开，其中本地缓存路径仍是明确指定的文件请求。

同目标同内容的包资产仅复制一次，计划保留重复来源并计为跳过；显式删除会结束该目标此前的去重范围。不会跨插件目录自动移动或合并 DLL。

显式引用 `NuGet_Packages` 缓存内的库路径时，会选择最适用的框架目录；`nuget:包名@版本/lib/框架/文件` 也遵循此规则。路径中已指定框架时以该框架为匹配基准，例如 `lib/net9.0/*.dll`；`lib/*.dll` 使用 `Framework` 变量。匹配后保留后续子路径和通配符展开的目录结构，缓存根外的路径不调整。

#### 最近适配

假设 `Framework` 变量为 `net9.0`，当某部署文件中有如下部署项：
```ini
${NUGET_PACKAGES}/mysql.data/8.3.0/lib/net9.0/*.dll
```

但上述包库目录并未包含 `net9.0` 框架版本，因此本工具会采用最适用(*接近*)该框架版本的库文件。即该路径将被重新定向为：
```ini
${NUGET_PACKAGES}/mysql.data/8.3.0/lib/net8.0/*.dll
```

## 其他

### 参考范例

- NuGet 包
	- [`Zongsoft.Data.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Data/src/Zongsoft.Data.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data)
	- [`Zongsoft.Data.MySql.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Data/drivers/mysql/src/Zongsoft.Data.MySql.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data.MySql)
	- [`Zongsoft.Security.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Security/src/Zongsoft.Security.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security)
	- [`Zongsoft.Security.Web.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Security/api/Zongsoft.Security.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security.Web)
	- [`Zongsoft.Administratives.deploy`](https://github.com/Zongsoft/administratives/blob/main/src/Zongsoft.Administratives.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives)
	- [`Zongsoft.Administratives.Web.deploy`](https://github.com/Zongsoft/administratives/blob/main/src/api/Zongsoft.Administratives.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives.Web)

- 宿主项目
	- [`daemon.deploy`](https://github.com/Zongsoft/hosting/blob/main/daemon/.deploy)
	- [`terminal.deploy`](https://github.com/Zongsoft/hosting/blob/main/terminal/.deploy)
	- [`web.deploy`](https://github.com/Zongsoft/hosting/blob/main/web/default/.deploy)

### 计划、锁定与清理

默认覆盖策略为 `newest`；无法转换的覆盖值、无效过滤条件和有效路径中的未定义变量均报错。目标写入限制在 `output` 内且拒绝链接路径；清单及 `#@import` 都检测循环。过滤组合从左到右求值。`**` 匹配零层或多层目录，复制目录会保留其内部相对结构。

| 选项 | 行为 |
| --- | --- |
| `--dry-run:true` | 生成计划，不复制、删除或创建目标目录；显式 `report` 仍会写出。在线求解可能写 NuGet 缓存。 |
| `--offline:true` | 仅使用已解压且含有效 nuspec 的本地包缓存；缺包失败，不访问包源。 |
| `--explain:true` | 显示计划执行结果和包/文件来源，消息及路径按原文输出。 |
| `--report:./deployment.json` | 保存成功或失败报告，含来源清单、包版本/引入者、文件哈希、重复/跳过/失败原因。 |
| `--lockFile:./deployment.lock.json` | 非预演且成功执行后写锁文件，记录选中版本、包内容与清单/源文件哈希。 |
| `--locked:true` | 使用 `lockFile` 中的包版本并校验计划与内容，不修改锁文件。锁与源清单路径绑定。 |
| `--prerelease:true` | 未固定版本时允许选择预发布包；依赖显式要求预发布下限时也允许匹配该范围。 |
| `--previous:./previous.json` | 对照同一目标根的前次成功报告，将不再选中的旧文件列为 Stale，默认保留。 |
| `--prune:true` | 必须同时指定 previous；仅删除其末次有效操作确认为已复制、内容哈希未变且本次不再选中的文件。用户修改、未拥有文件和跨根路径不会被自动清理。 |

布尔选项可以只写名称，也可显式使用 `true/false`。锁文件用于需要重现同一部署计划的场景，尤其有浮动 NuGet 版本或依赖时；`--locked` 会核对版本与内容，但不是进程互斥锁。普通部署不需要锁文件。未设置锁定/清理/报告选项时不会隐式创建这些文件或清理旧内容。报告/锁文件不能覆盖已知源清单、源文件或计划目标文件；应为它们指定独立路径。执行中发生 I/O 错误会停止后续操作；已完成的写入不自动回滚。

```powershell
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --offline:true --dry-run:true --report:./preview.json .deploy
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --lockFile:./deployment.lock.json --report:./completed.json .deploy
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --lockFile:./deployment.lock.json --locked:true .deploy
```

RID 回退通过 NuGet.RuntimeModel 使用仓库内固定的 dotnet/runtime v10.0.0 图谱，见[实现细节](docs/implementation.zh-Hans.md)；支持 `windows→win`、`mac/macos→osx`、`x32→x86` 别名。`contentFiles/any/{tfm}` 读取 nuspec 的 include/exclude、copyToOutput、flatten；未声明复制的内容不部署。`content` 目录作为部署内容递归复制。选定 lib 组中的 XML 文档包含在部署内容中。

包访问、依赖求解、资产选择和 RID 回退分别由独立类型负责，框架与版本模型复用 NuGet/.NET 类型，职责与行为见[实现细节](docs/implementation.zh-Hans.md)。

变量按环境变量、source 直属 `.env`（含显式导入）、目标目录 appsettings.json、命令选项的顺序加载；JSON 嵌套键可用 `${Database_Name}`、`${Items_0_Name}` 引用。变量替换保留 URL 的斜线。

回归命令（不会推送工具包）：

```powershell
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net10.0 -p:GeneratePackageOnBuild=false
```

Profile 导入复用 Core：ImportDirective 通过共享 ProfileReader.Session 执行导入及递归保护，通过 ProfileOptions.Loading 登记导入文件哈希。详见[实现细节](docs/implementation.zh-Hans.md#profile-导入回调)。

Core Profile 的来源与覆盖规则，以及读取和保存职责，见[实现说明](docs/implementation.zh-Hans.md#core-profile-声明与保存)。

本地源搜索及链接规则见[实现文档](docs/implementation.zh-Hans.md#本地搜索与源链接)。
详细规范检查及资源生成要求见 [仓库说明](../AGENTS.md#代码规范检查)。
