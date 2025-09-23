# Zongsoft 部署工具

![License](https://img.shields.io/github/license/Zongsoft/Zongsoft.Tools.Deployer)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Deployer)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Deployer)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/Zongsoft.Tools.Deployer?style=social)

README: [English](README.md) | [简体中文](README-zh_CN.md)

-----

## 概述

这是一个应用部署工具，通过指定的部署文件来指示部署工具复制特定文件到目标位置。

建议在部署项目目录定义一个名为 `.deploy` 的默认部署文件，部署文件为 `.ini` 格式的纯文本文件。

## 格式

部署文件为 `.ini` 格式的纯文本文件，其内容由中括号包裹的 **章节**_(`Section`)_ 和 **条目**_(`Entry`)_ 两种内容组成，其中 **章节** 部分表示部署的目标目录。

**章节** 和 **条目** 值均支持以美元符接圆括号 `$(...)` 或双百分号 `%...%` 格式的变量引用，引用的变量为部署命令传入的选项或环境变量。

每个条目由 **键** 和 **值** 两部分组成，以等于号 _(`=`)_ 分隔，其中 **值** 可省略。

- **键** 由 _解析器名_ 和 _解析参数_ 两部分组成，以冒号 _(`:`)_ 分隔；
	- 解析器名：如果缺失则表示采用默认的路径解析器，除此还支持 `nuget` 和 `delete` 这两种解析器。
	- 解析参数：由指定的解析器进行解析，详情参考下面的 _解析参数_。

- **值** 由 _目标路径_ 和 _过滤条件_ 两部分组成。
	- 目标路径：表示部署的目标路径，缺失则表示目标目录由所在 **章节** 指定，且目标文件名与原文件同名。
	- 过滤条件：表示解析的前置条件，详情参考下面的 _过滤条件_。

### 解析参数

#### 路径解析器

默认解析器(**无名称**)，表示将解析参数表示的源文件 _(支持通配符匹配)_ 复制到目标位置。

解析参数表示待部署的源文件路径，源文件路径支持 `*`、`?` 以及 `**` 三种通配符，其中 `**` 表示多级目录匹配。

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
- 如果未指定版本或版本为 `latest` 则表示最新版本；
- 如果未指定路径则：
	- 若该包的根目录包含 `.deploy` 文件，则优先部署该部署文件；
	- 部署该包的 `lib/{framework}` 库文件目录下的所有文件。
		> `{framework}` 表示最接近 `$(Framework)` 变量声明的 *目标框架* 版本。

> 💡 提示：_**Z**ongsoft_ 的 NuGet 包内根目录通常有一个名为 `.deploy` 的部署文件，包内的 `artifacts` 目录则存放着它的插件文件(`*.plugin`)_(至少一个)_、配置文件(`*.option`)、[数据映射文件](https://github.com/Zongsoft/Framework/tree/master/Zongsoft.Data)(`*.mapping`)等附属文件。

> 💡 注意：名为 `NuGet_Server` 变量定义了该解析器的 NuGet 包源，如果未定义则采用 `https://api.nuget.org/v3/index.json` 作为其默认值。

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
../.deploy/$(scheme)/options/app.$(environment).option       = web.option    <application>
../.deploy/$(scheme)/options/app.$(environment).option       = web.option    <!application>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <preview:A,B,C>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <!preview:X,Y,Z>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <application | debug:on>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <!application & !debug:on>
```

> 1. `<application>` 表示存在名为 `application` 的变量(*不论其内容*)，则结果为真。
> 2. `<!application>` 表示不存在名为 `application` 的变量(*不论其内容*)，则结果为真。
> 3. `<preview:A,B,C>` 表示名为 `preview` 的变量值为“`A`,`B`,`C`”(*忽略大小写*)中的任何一个，则结果为真。
> 4. `<!preview:X,Y,Z>` 表示名为 `preview` 的变量值不是“`X`,`Y`,`Z`”(*忽略大小写*)中的任何一个，则结果为真。
> 5. `<application | debug:on>` 表示存在名为 `application` 的变量(*不论其内容*) **或者** 名为 `debug` 的变量值为 `on`(*忽略大小写*)，则结果为真。
> 6. `<!application & !debug:on>` 表示不存在名为 `application` 的变量(*不论其内容*) **并且** 名为 `debug` 的变量值不是 `on`(*忽略大小写*)，则结果为真。

支持对 *目标框架* 进行匹配及版本比较，如果 *目标框架* 以`^`符结尾则表示当前部署 *目标框架* 版本必须大于或等于该版本，如下所示：

```plaintext
%NUGET_PACKAGES%/mysql.data/8.1.0/lib/netstandard2.1/*.dll     <framework:net7.0^>
%NUGET_PACKAGES%/mysql.data/6.10.9/lib/netstandard2.0/*.dll    <framework:net5.0,net6.0>
```

## 变量

本工具会依次加载环境变量、部署应用程序的`appsettings.json`文件内容、调用本工具的命令选项到变量集中，如果有重名则后加载的会覆盖之前加载的同名变量值。注意：变量名不区分大小写。

- 如果 `appsettings.json` 中定义了名为 `ApplicationName` 的属性，则可以使用 `application` 作为该属性的变量别名。
- 名称为 `Framework` 的变量表示 .NET *目标框架* 标识，有关该 *目标框架* 标识的定义请参考：https://learn.microsoft.com/zh-cn/dotnet/standard/frameworks

可以通过命令选项或环境变量来指定 NuGet 相关参数：
- `NuGet_Server` 表示 NuGet 服务器信息，默认值为：`https://api.nuget.org/v3/index.json`
- `NuGet_Packages` 表示 NuGet 包的目录，默认值为：`%USERPROFILE%/.nuget/packages`

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


## 执行

- 在目标(宿主)目录执行默认部署：
```bash
dotnet deploy -edition:Debug -framework:net8.0
```

- 如果目标(宿主)目录没有默认部署文件(`.deploy`)，则必须手动指定部署文件名(支持多个部署文件)：
```bash
dotnet deploy -edition:Debug -framework:net8.0 MyProject1.deploy MyProject2.deploy MyProject3.deploy
```

- 为了部署方便可以在目标(宿主)项目创建相应版本的部署脚本文件，譬如：
	- deploy-debug.cmd
		> `dotnet deploy -edition:Debug -framework:net8.0`
	- deploy-release.cmd
		> `dotnet deploy -edition:Release -framework:net8.0`

### 命令选项

- `verbosity` 选项
	- `quiet` 只显示必要的输出信息，通常只显示错误信息。
	- `normal` 显示警示和错误信息，如果未指定该选项，其为默认值。
	- `detail` 显示所有的输出信息，在排查问题时可以启用该选项。
- `overwrite` 选项
	- `alway` 始终复制并覆盖目标文件。
	- `never` 只有当目标文件不存在才复制。
	- `newest` 只有当源文件的最后修改时间晚于或等于目标文件的最后修改时间才执行文件复制部署，如果未指定该参数，其为默认值。
- `destination` 选项
	> 指定的部署目的目录，如果未指定该选项则默认为当前目录。

### NuGet 包
如果部署项为 NuGet 包目录下中的库文件，会优先匹配 `Framework` 变量指定的 *目标框架* 版本的库文件。

#### 最近适配

假设 `Framework` 变量为 `net9.0`，当某部署文件中有如下部署项：
```ini
%NUGET_PACKAGES%/mysql.data/8.3.0/lib/net9.0/*.dll
```

但上述包库目录并未包含 `net9.0` 框架版本，因此本工具会采用最适用(*接近*)该框架版本的库文件。即该路径将被重新定向为：
```ini
%NUGET_PACKAGES%/mysql.data/8.3.0/lib/net8.0/*.dll
```

## 其他

### 参考范例

- NuGet 包
	- [`Zongsoft.Data.deploy`](https://github.com/Zongsoft/Framework/blob/master/Zongsoft.Data/src/Zongsoft.Data.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data)
	- [`Zongsoft.Data.MySql.deploy`](https://github.com/Zongsoft/Framework/blob/master/Zongsoft.Data/drivers/mysql/Zongsoft.Data.MySql.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data.MySql)
	- [`Zongsoft.Security.deploy`](https://github.com/Zongsoft/Framework/blob/master/Zongsoft.Security/src/Zongsoft.Security.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security)
	- [`Zongsoft.Security.Web.deploy`](https://github.com/Zongsoft/Framework/blob/master/Zongsoft.Security/api/Zongsoft.Security.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security.Web)
	- [`Zongsoft.Administratives.deploy`](https://github.com/Zongsoft/Administratives/blob/master/src/Zongsoft.Administratives.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives)
	- [`Zongsoft.Administratives.Web.deploy`](https://github.com/Zongsoft/Administratives/blob/master/src/api/Zongsoft.Administratives.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives.Web)

- 宿主项目
	- [`daemon.deploy`](https://github.com/Zongsoft/hosting/blob/main/daemon/.deploy)
	- [`terminal.deploy`](https://github.com/Zongsoft/hosting/blob/main/terminal/.deploy)
	- [`web.deploy`](https://github.com/Zongsoft/hosting/blob/main/web/default/.deploy)
