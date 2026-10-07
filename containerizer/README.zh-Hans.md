# Zongsoft 容器化工具

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Containerizer)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Containerizer)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-containerize` 把应用和基础服务打包成**面向单台 Linux 主机**的容器交付物：一个 `.tar.gz` 归档，内含 Compose 配置、所需镜像以及自带的原生执行器。现场安装、升级、停止、恢复和卸载整栈各只需一条命令，目标主机既不需要 .NET SDK，也不需要本工具。

制作端可运行于 Windows 或 Linux，使用 Docker 或 Podman。交付物面向 Ubuntu 22.04、Debian 12/13 或 RHEL 系列 9，架构为 x64 或 ARM64。

> 🚨 注意：`dotnet-containerize` 本身只写本地文件。在主机上执行 `install.sh` 会安装容器引擎、导入镜像、启动容器、执行升迁，并可能修改数据库。正式主机操作前请先用 `run` 预演，并阅读交付包内的说明文件。

## 目录

- [功能特性](#功能特性)
- [基础概念](#基础概念)
- [使用条件](#使用条件)
- [安装](#安装)
- [快速开始](#快速开始)
- [命令参考](#命令参考)
- [配置清单](#配置清单)
- [内置服务](#内置服务)
- [本机预演](#本机预演)
- [现场操作](#现场操作)
- [镜像源与缓存](#镜像源与缓存)
- [参考示例](#参考示例)
- [相关文档](#相关文档)

## 功能特性

- **单机单包**：应用、基础服务、入口、升迁、bootstrap 包和镜像统一装进 `name[-tag]@version-architecture.tar.gz`。
- **先审阅再制作**：`plan` 只生成可编辑的 `.container` 清单，不连接引擎；`make` 把审阅过的清单制作成交付包。
- **三十个内置服务**：Redis、MySQL、MariaDB、PostgreSQL、MongoDB、Kafka、Nacos、RustFS、nginx 等，均以单行 `settings` 配置，也可用自定义模板扩展。
- **应用来自 Packager 安装包**：支持 `.deb`、`.rpm` 或 `.tar.gz` 加配套启动脚本；入口、运行时、环境与健康检查均从包元数据推导。
- **自动 Web 入口**：带 Packager `.web/nginx` 交接资产的应用自动获得受管 nginx 入口，站点、端口与证书由工具渲染，多个应用可共用同一个入口。
- **有序升迁**：Migrator 归档按版本升序在安装和预演时执行，按版本保存状态，失败只能显式重试，不会自动重跑。
- **离线或在线获取**：镜像和系统依赖既可随包携带，也可在现场按制作时记录的摘要获取。
- **制作端即可预演**：`run` 在一次性 Linux 容器内安装真实交付包，并打印实际响应成功的访问地址。
- **现场无须 SDK**：交付包内的 Native AOT 执行器完成全部现场操作，包括维护、恢复和清理。
- **镜像源**：可选的 `.mirrors` 文件重定向镜像下载，但不改变已固定的摘要与平台。
- **中英双语**：控制台输出、交付包内说明和本文档均提供英文与简体中文。

## 基础概念

| 名称 | 含义 |
| --- | --- |
| **交付包** | 在单台 Linux 主机上安装一整套应用所需的全部内容：`.tar.gz` 归档、配套 `.container` 清单以及归档内的资产。 |
| **应用包** | [Packager](../packager/README.zh-Hans.md) 生成的 Linux 安装包，提供程序、配置及启动信息。 |
| **基础服务** | Redis、MySQL 等镜像服务，由内置或自定义模板描述。 |
| **入口服务** | 接收 Web 请求的代理；带 nginx 交接资产的应用会自动补齐 nginx。 |
| **`name`** | 目标主机上的安装身份。同名交付包属于同一部署。 |
| **`tag`** | 可选的交付标签，用于区分构建，但不改变安装身份。 |
| **`version`** | 交付物的发行版本，独立于各应用和镜像版本。 |
| **`.settings`** | 输出目录中的服务默认配置，供按组件列表制作或规划时使用。 |
| **`.container`** | 可编辑的制作清单。`plan` 生成草稿，完整制作后记录实际使用的配置和镜像身份。 |
| **执行器** | 交付包内的原生 `containerizer` 程序，负责全部现场操作。 |

制作流程如下：

1. 用 [Packager](../packager/README.zh-Hans.md) **打包应用**，用 [Migrator](../migrator/README.zh-Hans.md) **制作升迁包**。
2. 用组件列表 **规划（plan）** 出可编辑的 `.container` 清单。
3. **审阅**共享的 `.settings` 默认值，并按本版发行需要编辑清单。
4. **制作（make）** 交付包：解析并构建镜像、收集资产、写出校验和并发布归档。
5. 用 `run` 在**本机预演**该归档。
6. 把归档**送到**匹配的主机，解压后执行 `./install.sh`。

| 工具负责 | 工具不负责 |
| --- | --- |
| 打包应用、固定镜像身份、记录交付包校验和 | 制作应用安装包或升迁归档（分别由 Packager、Migrator 负责） |
| 生成 Compose 配置、bootstrap 依赖及生命周期命令 | 编译、部署或配置应用的业务代码 |
| 依据 Packager 交接资产渲染 nginx 站点并发布端口 | 申请证书、修改 DNS，或改写应用的登录回调与重定向地址 |
| 按顺序执行升迁并保存各版本状态 | 解析 SQL，或判断失败的升迁是否可以重试 |
| 在单台主机上安装、升级、停止、启动、恢复和卸载 | 协调多台主机（由操作者编排各机顺序） |

## 使用条件

- **制作端**：以 .NET 全局工具方式安装本工具，并具备与其目标框架之一匹配的 .NET 运行时。命令为 `dotnet containerize` 或 `dotnet-containerize`。
- `plan` 只需要本地输入。`make`、直接制作和预演需要可用的 Docker 或 Podman 引擎及 Compose 提供程序；首次准备镜像、运行时和系统依赖通常需要联网。
- **目标主机**：与交付包匹配的 Linux 发行版和架构，并在 root 会话中操作。容器引擎及其依赖由 bootstrap 准备。
- **接受的发行版**：Ubuntu 22.04、Debian 12/13、RHEL/Rocky/AlmaLinux 9，架构为 x64 或 ARM64。RHEL 需要预先导入适用于该系统的 bootstrap 依赖集合。

这些是实现接受的目标配置，实际运行验证范围更窄，见[平台与验证边界](docs/implementation.zh-Hans.md#平台与验证边界)。模板声明或 ARM64 编译成功都不等同于目标环境验收。

## 安装

以 .NET 全局工具方式安装、更新、查看和卸载：

```console
# 安装
dotnet tool install -g Zongsoft.Tools.Containerizer

# 更新
dotnet tool update -g Zongsoft.Tools.Containerizer

# 查看
dotnet tool list -g
dotnet-containerize --help

# 卸载
dotnet tool uninstall -g Zongsoft.Tools.Containerizer
```

### 从源码安装

从源码构建出的 `.nupkg` 可以在不发布到 [nuget.org](https://nuget.org) 的情况下安装测试。工具包内嵌两个 Native AOT 执行器载荷，完整的本地构建会先准备它们；命令和专属 AOT 环境见[开发指南](SKILL.md#构建与验证)。在源码目录中制包并安装到独立工具目录：

```powershell
dotnet cake --target build --edition Release

$toolVersion = dotnet msbuild src/Zongsoft.Tools.Containerizer.csproj -getProperty:Version -p:Configuration=Release -nologo
dotnet tool install Zongsoft.Tools.Containerizer --tool-path ./.cache/tool --version $toolVersion --source ./src/bin/Release --no-http-cache
./.cache/tool/dotnet-containerize --help
```

`--source` 把安装限制在本地目录，避免选中同名的 NuGet.org 包；`--no-http-cache` 禁用下载缓存。若要替换当前用户的全局工具，把 `--tool-path` 换成 `-g` 即可。

> 🚨 注意：本地测试不要执行 Cake 的 `pack` 任务，它会推送包到 NuGet.org。

## 快速开始

以下流程使用真实的 [Zongsoft hosting](https://github.com/Zongsoft/hosting) 仓库：它在单台 Debian 13 主机上交付 `zongsoft.daemon`、`zongsoft.web` 两个宿主，以及 Redis、MySQL、RustFS 和 nginx。其他项目的步骤完全相同，hosting 只是提供了现成的输入。

> 💡 提示：hosting 仓库用 [containerize.cmd](https://github.com/Zongsoft/hosting/blob/main/containerize.cmd) 包装了下述全部命令，把源目录固定为 hosting 根目录、输出固定为 `.containerized`。这里展示的直接命令正是该脚本最终调用的命令，在任何项目中都可用。

### 步骤 1：准备应用包和升迁包

按 [hosting README](https://github.com/Zongsoft/hosting/blob/main/README.zh-Hans.md#安装包与升迁包) 编译、部署并打包宿主。在每个宿主目录中，`deploy.cmd` 在其格式提示处输入 `deb` 即可打包刚部署的内容，[pack.cmd](https://github.com/Zongsoft/hosting/blob/main/daemon/pack.cmd) 则只打包已有文件。两种方式都会得到：

```text
daemon/.packages/zongsoft.daemon@1.0.0-x64.deb
web/default/.packages/zongsoft.web@1.0.0-x64.deb
```

再在 hosting 根目录用 [migrate.cmd](https://github.com/Zongsoft/hosting/blob/main/migrate.cmd) 制作升迁包：

```text
.migration/zongsoft(migrate)@1.0.0_linux-x64.tar.gz
.migration/zongsoft(migrate)@1.0.0_linux-x64.sh
```

容器化工具不编译宿主、也不制作升迁，只消费这些产物。

### 步骤 2：声明共享的服务默认值

服务默认值位于 `output/.settings`，由该项目所有交付包共享。hosting 把它的版本化默认值放在 [.containerized/.settings](https://github.com/Zongsoft/hosting/blob/main/.containerized/.settings)，本交付包用到的段落如下：

```ini
[mysql]
tag=8.4.11
settings=storage=persistent;root-password=$(mysql_root_password)

[redis]
tag=8.10.2
settings=storage=persistent;persistence=both;password=$(redis_password)

[rustfs]
tag=1.0.1
settings=storage=persistent;access-key=$(rustfs_access_key);secret-key=$(rustfs_secret_key)

[nginx]
tag=1.30.5
```

其中的 `$(...)` 引用通过共享变量流程读取 hosting 根目录的 [.env](https://github.com/Zongsoft/hosting/blob/main/.env)，因此 `.settings` 中不会写入任何凭据。完整规则见[服务默认值与变量](#服务默认值与变量)。

### 步骤 3：规划清单

在 hosting 根目录执行。与服务同名的裸名称表示选中该内置服务，文件或目录则按应用包输入处理：

```cmd
dotnet-containerize plan ^
	daemon web/default ^
	redis mysql rustfs nginx ^
	--name:zongsoft ^
	--version:1.0 ^
	--distribution:debian@13 ^
	--architecture:x64 ^
	--migration:.migration ^
	--output:.containerized
```

| 参数或选项 | 作用 |
| --- | --- |
| `daemon`、`web/default` | 宿主目录；每个目录都会按交付名称、发行版和架构查找匹配的安装包。 |
| `redis mysql rustfs nginx` | 内置基础服务与入口服务。此处 `nginx` 可省略：带完整 `.web/nginx` 交接资产的应用会自动补齐；显式声明则可以固定其镜像 tag 与端口映射。 |
| `--name:zongsoft` | 安装身份，同时作为安装包搜索前缀和升迁归档名称。 |
| `--version:1.0` | 本次交付的发行版本。省略时使用日期版本，hosting 自己的交付包因此形如 `zongsoft@26.10.6-x64.tar.gz`。 |
| `--distribution:debian@13` | 目标发行版；只写 `debian` 等价于 `debian@13`。 |
| `--migration:.migration` | 在该目录中查找 `zongsoft(migrate)@<版本>_linux-<架构>` 归档及其启动脚本。 |
| `--output:.containerized` | 清单以及随后交付归档的输出目录。 |

### 步骤 4：审阅清单

`plan` 生成 `.containerized/zongsoft@<版本>-x64.container` 后即结束：不连接引擎，也不下载镜像。本交付包的草稿如下：

```ini
name=zongsoft
version=1.0
distribution=debian@13
architecture=x64
source=..
output=.containerized
bootstrap=offline
imaging=offline
migration#1=.migration\zongsoft(migrate)@1.0.0_linux-x64.tar.gz

[redis]
tag=8.10.2
repository=docker.io/library/redis
settings=storage=persistent;persistence=both;password=$(redis_password)

[mysql]
tag=8.4.11
repository=docker.io/library/mysql
settings=storage=persistent;root-password=$(mysql_root_password)

[rustfs]
tag=1.0.1
repository=docker.io/rustfs/rustfs
settings=storage=persistent;access-key=$(rustfs_access_key);secret-key=$(rustfs_secret_key)

[zongsoft.daemon]
package=daemon\.packages\zongsoft.daemon@1.0.0-x64.deb

[zongsoft.web]
package=web\default\.packages\zongsoft.web@1.0.0-x64.deb

[nginx]
tag=1.30.5
settings=port=80:18080,443:none
```

清单中的 `source` 与 `output` 按相对彼此的形式保存。缺少必填服务参数时会在保存草稿的同时给出警告；交付内容在这里编辑，而不是重新生成。详见[清单字段](#清单字段)和 [Web 交接与端口发布](#web-交接与端口发布)。

### 步骤 5：制作交付包

```cmd
dotnet-containerize make .containerized/zongsoft@1.0-x64.container
```

`make` 会解析并固定全部镜像、构建应用镜像、收集 bootstrap 包、升迁和配置、核对校验和，然后发布：

```text
.containerized/zongsoft@1.0-x64.container
.containerized/zongsoft@1.0-x64.tar.gz
```

归档内的完成清单与外部文件完全一致，因此交付的内容就是审阅过的内容。已有交付包不会被覆盖；用 `make FILE.container --version:1.1` 制作下一版。需要按更新后的系统底图或运行时补丁重建应用公共运行环境时，使用 `--refresh`。

### 步骤 6：本机预演

```cmd
dotnet-containerize run .containerized/zongsoft@1.0-x64.tar.gz
```

`run` 校验归档后，在一次性 Linux 容器内执行其原有 `install.sh`，并打印实际响应成功的访问地址。保持窗口打开，按 **Ctrl+C** 删除该环境及其测试数据。检查范围和退出码含义见[本机预演](#本机预演)。

### 步骤 7：安装到目标主机

把归档复制到匹配的 Linux 主机，解压到独立目录，并在 root 会话中执行启动脚本：

```sh
mkdir zongsoft-delivery
tar -xzf zongsoft@1.0-x64.tar.gz -C zongsoft-delivery
cd zongsoft-delivery
./install.sh
containerizer status --name zongsoft
```

此后状态查询和生命周期命令都不依赖最初的解压目录。详见[现场操作](#现场操作)。

## 命令参考

### 语法

```text
dotnet containerize <components...> [options]
dotnet containerize plan <components... | file.container> [options]
dotnet containerize make <file.container> [options]
dotnet containerize      <file.container> [options]     # make 的简写
dotnet containerize run  <archive.tar.gz> [--engine:auto|docker|podman]
```

`dotnet-containerize` 是以上所有形式的等价入口。选项写作 `--选项:值`；制作端命令成功返回 `0`，输入非法返回 `2`，被取消返回 `130`。其他退出码用于指出失败阶段，见[退出码](docs/implementation.zh-Hans.md#平台与验证边界)。

### 组件

位置参数按书写顺序解析：

| 组件 | 解释 |
| --- | --- |
| `服务[@tag]` | 内置服务名（见[内置服务](#内置服务)），可选用 `@tag` 固定镜像 tag。含 `/` 或 `\` 的值一律不作为服务名。 |
| 安装包文件 | 应用安装包：`.deb`、`.rpm`，或 `.tar.gz` 及其配套 `.sh`。 |
| 目录 | 在该目录及其 `.packages/` 子目录中查找与交付名称前缀、发行版和架构匹配的安装包；候选多项时交互选择，且绝不按文件日期选择版本。 |
| `FILE.container` | 必须是唯一参数；表示规划或制作该清单，而不是按组件列表制作。 |

### 选项

以下选项属于组件列表入口；输入为 `.container` 时只允许覆盖 `version`、`source`、`output`、`engine` 和 `refresh`，其余内容直接编辑清单。

| 选项 | 默认值 | 含义 |
| --- | --- | --- |
| `--name:<name>` | **必填** | 安装身份。以 ASCII 字母或数字开头，后续可含 ASCII 字母、数字、点、下划线、连字符，且不含连续两点。 |
| `--distribution:<发行版[@版本]>` | **必填** | 目标发行版。`ubuntu` 等价于 `ubuntu@22.04`，`debian` 等价于 `debian@13`；另接受 `debian@12`、`rhel@9`、`rocky@9`、`almalinux@9`，`redhat` 解析为 `rhel`。 |
| `--version:<version>` | 日期版本 | 非零的 2–4 段数字发行版本。省略时采用日期版本，并在输出名称冲突时递增第四段。 |
| `--tag:<tag>` | 空 | 可选交付标签，命名规则同 `name`；与服务镜像 tag 相互独立。 |
| `--architecture:<x64\|arm64>` | `x64` | 目标架构。 |
| `--source:<目录>` | 调用目录 | 工作源目录。 |
| `--output:<目录>` | 最终 source | 接收清单和交付归档的目录。 |
| `--engine:<auto\|docker\|podman>` | `auto` | 制作端与 `run` 使用的容器引擎。 |
| `--imaging:<offline\|online>` | `offline` | 基础服务镜像随包携带，或现场按固定摘要获取。应用镜像始终随包携带。 |
| `--bootstrap:<offline\|online>` | `offline` | 系统依赖随包携带，或现场下载；两者都按制作时记录的版本、来源和校验值安装。 |
| `--migration:<目录>` | 无 | 在该目录查找 Migrator 归档及配套启动脚本；省略则不添加升迁。 |
| `--refresh[:布尔值]` | `false` | 重建本次需要的应用公共运行环境，并重新解析其系统底图。 |
| `--title:<文本>`、`--description:<文本>` | 空 | 清单中的描述信息。 |

输入为 `.container` 时，`plan` 不执行刷新，且 `--refresh` 也不会改变基础服务镜像 tag、bootstrap 包或预演缓存。

### 路径与输出命名

`--source` 自身相对于调用目录解析；受其管理的本地输入和输出（包括 `./` 与 `../`）均相对于**最终 source**，绝对路径保持原义。清单内的 `source` 相对于声明它的文件，模板配套文件相对于模板目录。`run` 没有 source 选项，其归档相对于调用目录。

交付物命名为 `name[-tag]@version-architecture.container` 与 `name[-tag]@version-architecture.tar.gz`。已有归档不会被覆盖；制作失败保留审阅过的草稿，制作成功则原位更新自己的草稿。完整路径契约见[路径、变量与输出](docs/implementation.zh-Hans.md#profile变量与路径)。

### 示例

先规划再制作一个只含基础服务的交付包，然后预演：

```console
dotnet-containerize plan redis --name:example --distribution:debian --version:1.0 --output:.containerized
dotnet-containerize make .containerized/example@1.0-x64.container
dotnet-containerize run .containerized/example@1.0-x64.tar.gz
```

跳过规划一步直接制作，并就地固定镜像 tag（输出默认为 source 目录）：

```console
dotnet-containerize redis@8.10.2 --name:example --distribution:debian --version:1.0
```

交付一个带证书、且仅在本机开放的 Web 应用：

```ini
[example-web]
package=./packages/example-web.deb
probe-host!tenant=demo.example.com

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/example/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/example/key.pem=./certs/key.pem
```

## 配置清单

### 服务默认值与变量

`output/.settings` 保存共享默认值，按组件名称分段，每段只接受 `tag`、`repository` 和 `settings`：

```ini
[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both;password=$(redis_password)

[mysql]
tag=latest
settings=root-password=$(mysql_root_password);database=example
```

某个段落不会自动选中该服务，选中只由组件列表或清单决定。取值按参数逐项合并：显式组件配置优先于 `.settings`，`.settings` 优先于模板默认值。镜像 tag 缺省为字面 `latest`，工具不会搜索“最新稳定版本”。

`plan`、失败的制作和清单回放都不会改写 `.settings`。**只有按组件列表完整制作成功**，才补充缺失的服务 tag；已有的 tag、repository 和 settings 不会被改写。

变量来自环境、从文件系统根目录到 source 各级 `.env`，以及命令选项。引用形式为 `$(name)` 或 `%name%`，`$$(name)` 与 `%%name%%` 保留字面引用。与参数同名的变量不会被自动绑定：内置绑定只有 MySQL 的 `mysql_root_password` 及 RustFS 的 `rustfs_access_key`、`rustfs_secret_key`；其他取值必须显式提供，如 hosting 示例所示。

服务参数使用单行连接字符串：

```ini
settings=port=16379;password="a;b=""c"""
```

参数先拆分、再对每个值求值，因此密码变量中的分号不会产生新参数。单项 `password=` 表示显式空值并阻止回退；`false` 与 `0` 都是有效取值；整条 `settings=` 为空表示没有显式参数，继续采用模板默认值或变量绑定。

> 🚨 注意：完成清单记录的是展开后的取值，其中可能包含凭据。请把它当作含密配置管理其访问权限和版本控制。

### 清单字段

```ini
name=example
version=1.0
distribution=debian@13
architecture=x64
source=.
output=.containerized
bootstrap=offline
imaging=offline
migration#1=.migration\example(migrate)@1.0.0_linux-x64.tar.gz

[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both
```

根字段为 `name`、`tag`、`version`、`engine`、`distribution`、`architecture`、`bootstrap`、`imaging`、`source`、`output`、`title`、`description`，另有工具维护的 `stage=plan|complete` 和按序声明的 `migration#1`、`migration#2` 等归档路径。手写清单可以省略 `stage`。

| 组件字段 | 使用场景 |
| --- | --- |
| `package` | 应用安装包路径；有此字段的段落表示应用。 |
| `tag`、`repository`、`imaging` | 基础服务镜像选择，以及可选的局部交付方式覆盖。 |
| `settings` | 基础服务参数，见[内置服务](#内置服务)。 |
| `template` | 内置模板名称或自定义模板路径；省略时使用组件名。见[模板参考](docs/templates.zh-Hans.md)。 |
| `environment!NAME` | 显式容器环境值。应用可覆盖包内服务环境；基础服务不得与参数映射的环境值冲突。 |
| `dependences` | 应用的 `nginx[@tag]` 或 `runtime-*` 声明；运行时须与包内元数据一致。它不是通用服务依赖列表。 |
| `probe-host!站点名` | 用于仅声明通配域名的 Web 站点的具体探测主机名。 |
| `file!/容器绝对路径` | nginx 组件使用的额外资源文件；值为制作端文件路径。 |
| `digest`、`identity`、`timestamp`、`size` | 工具记录的镜像身份及可选展示信息，含义见[镜像身份与缓存](docs/implementation.zh-Hans.md#镜像身份与缓存)。 |

应用不使用 `template`、`settings` 或服务镜像选择字段：其程序入口、工作目录、运行时及基础健康检查由安装包元数据提供或推导。需要改变应用自身配置时，请修改 Packager 的 scheme 并重新打包。

### Web 交接与端口发布

带 Packager 完整 `.web/nginx` 交接资产（`.bindings`、`<包名>.conf`、`<包名>.conf.template`）的应用会自动加入受管 nginx 入口，多个应用可共用同一个入口。没有托管资产时，应用有效的 `Listen` 端口直接发布到宿主回环地址。

nginx 端口来自包内绑定，默认按同端口发布；用 `容器端口:宿主端口` 列表调整或关闭：

```ini
[zongsoft.web]
package=web\default\.packages\zongsoft.web@1.0.0-x64.deb

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/zongsoft/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/zongsoft/key.pem=./certs/key.pem
```

上例中容器端口 80 优先使用宿主端口 18080，容器端口 443 仅保留内部访问。已发布的仅通配域名站点还需要匹配的具体 `probe-host!站点名`，站点名和证书路径必须与实际包内容相符。外部文件声明属于使用它的 nginx 组件；端口在写入清单时即已固定，之后修改 `.settings` 不会改变已保存的交付包。工具不猜测站点、不代签证书，也不修改应用的登录回调或重定向地址。

### 升迁

升迁来自 [Migrator](../migrator/README.zh-Hans.md) 的 `<名称>(migrate)@<版本>_linux-<架构>.tar.gz` 归档及同名 `.sh`，可在规划时用 `--migration:<目录>` 选择，或在清单中写成 `migration#N` 字段。选中的版本不得重复，并按升序执行。

制作只收集文件，不执行 SQL；现场安装和本机预演都会执行它们。修改升迁的连接设置后，应重新制作升迁包。

## 内置服务

全部服务都提供 `port`；有数据挂载的服务另提供 `storage=persistent|temporary`，默认 `persistent`。端口默认绑定 `127.0.0.1`。星号表示必填参数，未注明默认值的参数可省略。

| 服务 | 默认宿主端口 | 专属设置 |
| --- | --- | --- |
| caddy | 80 | — |
| clickhouse | 8123 | `native-port` |
| consul | 8500 | — |
| elasticsearch | 9200 | `password*` |
| emqx | 1883 | `dashboard-port`、`websocket-port` |
| etcd | 2379 | — |
| grafana | 3000 | `admin-password*` |
| haproxy | 80 | — |
| influxdb | 8086 | — |
| kafka | 9092 | — |
| loki | 3100 | — |
| mariadb | 3306 | `root-password*`、`database`、`user`、`password` |
| memcached | 11211 | — |
| mongodb | 27017 | `root-user*`、`root-password*` |
| mosquitto | 1883 | — |
| mysql | 3306 | `root-password*`、`database`、`user`、`password` |
| nacos | 8848 | `mode=standalone`、`auth-token*`、`identity-key*`、`identity-value*`、`console-port`、`grpc-port` |
| nats | 4222 | `monitoring-port` |
| nginx | 取自 Web 绑定 | `port=80:18080,443:none` 等映射列表 |
| opensearch | 9200 | `password*` |
| otel | 4317 | `http-port` |
| postgresql | 5432 | `password*`、`user=postgres`、`database=postgres` |
| prometheus | 9090 | — |
| rabbitmq | 5672 | `user*`、`password*`、`management-port` |
| redis | 6379 | `persistence=both`、`password`、`maxmemory`、`maxmemory-policy` |
| rustfs | 9000、9001 | `access-key*`、`secret-key*`、`console-port=127.0.0.1:9001` |
| sqlserver | 1433 | `password*`、`accept-eula*`；仅 x64 |
| tdengine | 6041、6060 | `console-port=127.0.0.1:6060` |
| valkey | 6379 | 同 Redis |
| zookeeper | 2181 | — |

除 nginx 映射列表外，端口参数接受 `16379`（回环地址）、`192.0.2.10:16379`、`[::1]:16379` 或 `none`。它只改变宿主发布，不改变容器监听；`none` 表示仅在交付内部可访问。辅助端口默认不发布，RustFS、TDengine 控制台除外。nginx、haproxy、memcached、otel 没有数据挂载，不接受 `storage`。

`temporary` 使用匿名卷：同一容器停止、启动、重启时保留数据，工具删除该容器时一并清理。启动基础设施不会隐式重建已有容器。持久目录在普通卸载后保留，显式 purge 才清理；切换设置不会迁移旧数据。

Redis、Valkey 的 `persistence` 可为 `both`、`rdb`、`aof` 或 `none`；AOF 每秒同步，RDB 使用默认快照周期。`password` 默认空，非空时同时配置认证和匹配的健康检查。`maxmemory`、`maxmemory-policy` 使用所选镜像接受的语法，storage 与 persistence 相互独立。

服务参数不会启用镜像中缺失的管理功能，也不会修改已有数据库中的凭据。SQL Server 的 `accept-eula` 由操作者按许可填写。caddy、haproxy 是独立入口模板，不接收应用的 nginx 托管资产，其配置需要由所选镜像或自定义模板提供。完整仓库、辅助端口和参数映射见[模板参考](docs/templates.zh-Hans.md)。

## 本机预演

`run` 校验交付包后，在隔离的 Linux 容器内执行包中原有的 `install.sh`，覆盖 bootstrap、镜像导入、升迁、应用启动和健康检查。外层使用所选 Docker/Podman，内部使用独立 Docker；交付包必须与外层引擎的原生架构一致，外层无须 Compose。

- 每次会话都从全新的安装状态和测试数据开始；干净的底图以及校验通过的基础设施、入口镜像可复用。
- 优先使用交付端口，冲突时分配空闲端口，并打印实际响应成功的地址。Web 入口发布到所有 IPv4 接口，普通 TCP 服务发布到回环地址，因此局域网可达性仍取决于防火墙和虚拟机网络。
- 必需的 Web 入口探测失败会使预演失败；仅基础设施本机转发失败会告警，而安装失败或服务自身健康失败仍会阻止就绪。
- 域名探测连接本机端口并保留 Host 与 SNI，HTTPS 会验证证书；不修改 hosts、DNS 和信任库，因此用浏览器访问域名仍需自行准备解析。重定向只报告目标，不跟随。
- 就绪后按 **Ctrl+C**，清理成功时返回 `0`；启动期间取消返回 `130`。安装或探测失败会保留已建立的现场直到按 Ctrl+C，并保留失败状态。
- 同一应用已有预演环境时不允许启动第二个会话。强制结束进程可能留下资源，请按输出中的归属信息确认后再清理。

> 🚨 注意：预演会实际执行包内应用和升迁，应使用适合测试的配置。容器共享宿主内核，不能代替真实目标主机验收。

## 现场操作

把交付包复制到匹配的 Linux 主机并解压到独立目录，以下命令在 root 会话中执行：

```sh
mkdir example-delivery
tar -xzf example@1.0-x64.tar.gz -C example-delivery
cd example-delivery
./install.sh
containerizer status --name example
```

安装器把所需资产保存到受管位置，并安装或复用 `/usr/local/bin/containerizer`。此后状态、生命周期、恢复和卸载都不依赖解压目录。

现场选项使用空格分隔，`BUNDLE` 表示交付归档或已解压目录：

| 命令 | 作用 |
| --- | --- |
| `containerizer install BUNDLE [--name NAME] [--no-start]` | 安装归档或已解压目录；同名已有部署会执行升级检查。 |
| `containerizer prepare BUNDLE --name NAME` | 保存资产并准备引擎和镜像，停在 `Prepared`；既不停止应用，也不执行升迁。 |
| `containerizer upgrade BUNDLE --name NAME [--no-start]` | 升级已有部署。 |
| `containerizer list` | 列出安装记录。 |
| `containerizer status --name NAME` | 以 JSON 输出安装状态。 |
| `containerizer logs [COMPONENT] --name NAME [--tail 100] [--follow]` | 查看或跟随容器日志。 |
| `containerizer stop --name NAME` | 进入维护并停止应用和入口；基础设施继续运行。 |
| `containerizer start --name NAME` | 启动已准备完成的应用和入口，健康检查通过后解除维护。 |
| `containerizer restart [COMPONENT] --name NAME` | 重启应用和入口，或指定组件；维护中或存在未完成事务时拒绝。 |
| `containerizer recover --name NAME [--retry-migration VERSION]` | 继续原失败或中断事务，恢复后保持待启动状态。 |
| `containerizer uninstall --name NAME [--purge]` | 卸载；`./uninstall.sh [--purge]` 可从交付目录定位同一安装身份。 |

`--no-start` 仍会启动基础设施并执行升迁，只让应用和入口停在 `ReadyToStart`。命令执行前已处于维护状态时，同样不会自动启动应用。

### 交付包内容

| 路径 | 内容 |
| --- | --- |
| `install.sh`、`uninstall.sh` | 调用包内执行器的薄启动入口。 |
| `containerizer`、`zh-Hans/` | 原生 AOT 执行器及其本地化资源。 |
| `name[-tag]@version-arch.container` | 与外部副本一致的完成清单。 |
| `containerizer.json`、`checksums.sha256` | 执行计划与文件校验和；计划的哈希标识发行内容。 |
| `compose.yaml`、`config/` | 已生成的运行配置和只读运行资产。 |
| `images/`、`packages/`、`migration/` | 离线镜像、bootstrap 包和升迁归档，按需存在。 |
| `README.md`、`README.zh-Hans.md` | 该交付物的操作说明，含建议的升迁顺序。 |

### 现场目录

| 现场位置 | 内容 |
| --- | --- |
| `/var/lib/containerizer/apps/NAME/` | 安装记录、发行资产、升迁状态。 |
| `/var/lib/containerizer/data/NAME/` | 服务持久数据。 |
| `/var/log/containerizer/NAME/` | 升迁尝试结果日志。 |
| `/var/cache/containerizer/NAME/` | bootstrap 下载与暂存。 |

普通卸载删除本应用的容器、匿名卷及网络，保留数据、发行资产、升迁记录和镜像，供排查、重装或后续 purge。purge 进一步删除核实归属的本地资产及独占镜像，最后删除安装记录；中断后可重复执行。全局执行器、Docker/Compose、原始交付介质及远端数据不属于 purge 范围。

### 升级与升迁

升级允许应用、应用配置及入口更新，拒绝改变基础设施镜像身份、有效配置或 bootstrap 包集合——先普通卸载再安装也无法绕过这些检查。

失败会保留维护状态和诊断，不自动启动旧版本，也不自动重跑失败 SQL。排查后执行 `recover`；只有确认可重试时，才指定当前事务中的失败升迁版本，随后显式启动。

多主机维护由操作者编排：逐机 prepare 和 stop，对指定主机使用 `--no-start` 升级，待全部必要升迁成功后再逐机启动。

## 镜像源与缓存

可在最终输出目录放置可选的 `.mirrors` 文件；`run` 读取所选归档旁的该文件，`plan` 完全不读取：

```ini
docker.io=mirror.example.com/docker.io
mcr.microsoft.com=mirror.example.com/mcr
```

值为自选可信的 `主机[:端口][/路径前缀]`，多个地址用分号分隔，不带协议、tag、digest、凭据或末尾斜线。工具先复用校验通过的缓存，再依次尝试各镜像源，最后回到原仓库；任何回退都不会改变已固定的摘要和平台。这些规则不写入交付物：现场普通安装使用引擎自身的配置。公开镜像源清单见 [hosting 镜像源说明](https://github.com/Zongsoft/hosting/blob/main/README.zh-Hans.md#镜像配置)，完整的解析与缓存规则见[镜像源](docs/implementation.zh-Hans.md#镜像源)。

应用公共运行环境缓存在 Windows 的 `%LOCALAPPDATA%/Zongsoft/containerizer` 或 Linux 的 `~/.cache/Zongsoft/containerizer` 下（绝对路径 `XDG_CACHE_HOME` 可替代 Linux 缓存基目录），其中不含应用安装包和应用数据。需要更新系统底图或运行时补丁时使用 `make --refresh`；缓存分层与手动清理边界见[镜像身份与缓存](docs/implementation.zh-Hans.md#镜像身份与缓存)。

## 参考示例

- Zongsoft hosting 项目
	- [容器交付](https://github.com/Zongsoft/hosting/blob/main/README.zh-Hans.md#容器交付) —— 脚本流程、镜像交付选项与 Nginx 端口
	- [`containerize.cmd`](https://github.com/Zongsoft/hosting/blob/main/containerize.cmd) —— plan/make/run 交互包装脚本
	- [`.containerized/.settings`](https://github.com/Zongsoft/hosting/blob/main/.containerized/.settings) —— 所有交付包共享的服务默认值
	- [`.containerized/.mirrors`](https://github.com/Zongsoft/hosting/blob/main/.containerized/.mirrors) —— 制作与预演使用的镜像源
	- [`.env`](https://github.com/Zongsoft/hosting/blob/main/.env) —— 服务默认值引用的变量
	- [`migrate.cmd`](https://github.com/Zongsoft/hosting/blob/main/migrate.cmd) —— 升迁归档制作
	- [`daemon/pack.cmd`](https://github.com/Zongsoft/hosting/blob/main/daemon/pack.cmd) 与 [`web/default/pack.cmd`](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) —— 应用安装包
	- [Web 宿主](https://github.com/Zongsoft/hosting/tree/main/web/default) —— `.web/nginx` 交接资产与 `web.profile`

- 相关工具
	- [Packager](../packager/README.zh-Hans.md) —— 应用安装包及其 [Web 托管配置](../packager/docs/web.zh-Hans.md)
	- [Migrator](../migrator/README.zh-Hans.md) —— 升迁归档与启动脚本
	- [Deployer](../deployer/README.zh-Hans.md) —— 把插件部署进这些包所承载的宿主

## 相关文档

- [实现说明](docs/implementation.zh-Hans.md)：制作数据流、Web 交接、交付协议、安装状态、缓存与验证边界。
- [模板参考](docs/templates.zh-Hans.md)：内置仓库、端口、参数映射及自定义模板格式。
- [开发指南](SKILL.md)：代码维护、构建、测试及隔离验证。
