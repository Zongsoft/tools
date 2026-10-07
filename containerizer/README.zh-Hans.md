[English](README.md) | [简体中文](README.zh-Hans.md)

# Containerizer

将应用安装包和基础服务制作成可在**单台 Linux 主机**上安装、升级、恢复与卸载的容器交付包。制作端使用 Docker 或 Podman；交付包携带原生执行器，现场使用 Docker 与 Compose，无须安装 .NET SDK。

[快速开始](#快速开始) · [制作命令](#制作命令) · [配置清单](#配置清单) · [服务参数](#内置服务) · [本机预演](#本机预演) · [现场操作](#现场操作)

## 基础概念

| 名称 | 用途 |
| --- | --- |
| 应用包 | [Packager](../packager/README.zh-Hans.md) 生成的 Linux 应用安装包；提供程序、配置及启动信息 |
| 基础服务 | Redis、MySQL 等镜像服务，通过内置或自定义模板配置 |
| 入口服务 | 接收 Web 请求的代理；带 nginx 托管信息的应用会自动补齐 nginx |
| `name` | 目标主机上的安装身份；同名交付包用于同一部署 |
| `tag` / `version` | 交付物的可选标签 / 发行版本；标签不改变安装身份，发行版本独立于各应用和镜像版本 |
| `.settings` | 输出目录中的服务默认配置，供按组件列表制作或规划时使用 |
| `.container` | 可编辑的制作清单；`plan` 生成草稿，完整制作后记录实际使用的配置和镜像身份 |
| `.tar.gz` | 包含执行器、安装脚本、运行描述和所需资产的交付包 |

典型流程为：**打包应用 → 生成并编辑清单 → 制作交付包 → 本机预演 → 目标主机安装**。也可以只交付基础服务，或按组件列表一次完成制作。

## 使用条件

- 制作端安装本工具及其目标框架对应的 .NET 运行时，命令为 `dotnet containerize` 或 `dotnet-containerize`。从源码构建及安装本地工具包见 [开发指南](SKILL.md#构建与验证)。
- `plan` 只需要本地输入。`make` 和直接制作需要可用的 Docker/Podman 与 Compose 提供程序；首次准备镜像和系统依赖通常需要联网。
- 目标主机须为与交付包匹配的 Linux 发行版和架构，使用 root 执行现场命令。系统引擎及依赖由 bootstrap 准备。
- 可选发行版为 Ubuntu 22.04、Debian 12/13、RHEL/Rocky/AlmaLinux 9；架构为 x64 或 ARM64。RHEL 需要预先导入适用于该系统的 bootstrap 依赖集合。

这些是实现接受的目标配置；实际运行验证范围见 [平台与验证边界](docs/implementation.zh-Hans.md#平台与验证边界)。模板声明、ARM64 编译成功都不等同于目标环境验收。

## 快速开始

以下命令在同一个工作目录执行。先用 Redis 完成最小流程：

```console
dotnet containerize plan redis --name:example --distribution:debian --version:1.0 --output:.containerized
```

打开生成的 `.containerized/example@1.0-x64.container`，按需调整 Redis 设置，例如：

```ini
[redis]
tag=latest
repository=docker.io/library/redis
settings=port=16379;storage=temporary;persistence=none
```

制作并预演：

```console
dotnet containerize make .containerized/example@1.0-x64.container
dotnet containerize run .containerized/example@1.0-x64.tar.gz
```

`run` 显示实际访问地址并保持前台运行；按 **Ctrl+C** 结束并清理这次预演。上例的临时存储适合试用；正式数据服务应选择合适的存储、认证和持久化设置。

加入应用时，把 Packager 产物路径或候选目录放入组件列表，例如：

```console
dotnet containerize plan ./packages redis --name:example --distribution:debian --output:.containerized
```

显式文件按指定文件读取；目录先查直属文件，再查 `.packages` 子目录，每层先选择发行版对应的 `.deb` 或 `.rpm`，再选择有同名 `.sh` 的 `.tar.gz`。候选按名称前缀和目标架构筛选，多项时交互选择；不会凭文件日期自动选择版本。

## 制作命令

| 命令 | 输入与结果 |
| --- | --- |
| `dotnet containerize COMPONENT...` | 应用包、包目录或 `服务[@镜像标签]`；直接生成完成清单与交付包 |
| `dotnet containerize plan COMPONENT...` | 生成可编辑清单；不连接引擎、不下载或制作镜像 |
| `dotnet containerize plan FILE.container` | 从已有清单生成新草稿，可用 `--version` 指定新发行版本 |
| `dotnet containerize make FILE.container` | 按一个清单制作交付包 |
| `dotnet containerize FILE.container` | `make` 的简写 |
| `dotnet containerize run FILE.tar.gz` | 预演一个已制作的交付包；仅接受额外的 `--engine` 选项 |

制作端命名选项使用 `--选项:值`。组件列表入口可用以下选项：

| 选项 | 含义与默认值 |
| --- | --- |
| `--name` | 必填安装身份；以 ASCII 字母或数字开头，后续可含 ASCII 字母、数字、点、下划线、连字符，不含连续两点 |
| `--tag` | 可选交付标签，命名规则同 name；与服务镜像 tag 独立 |
| `--version` | 非零的 2–4 段数字版本；省略时采用日期版本，并在输出冲突时递增第四段 |
| `--distribution` | 必填；`ubuntu` 默认 `ubuntu@22.04`，`debian` 默认 `debian@13`；另可指定 `debian@12`、`rhel@9`、`rocky@9`、`almalinux@9`；`redhat` 解析为 `rhel` |
| `--architecture` | `x64`（默认）或 `arm64` |
| `--source` | 工作源目录，默认调用目录 |
| `--output` | 输出目录，默认最终 source |
| `--engine` | `auto`（默认）、`docker` 或 `podman` |
| `--imaging` | 基础服务镜像 `offline`（默认，随包携带）或 `online`（现场按固定摘要获取）；应用镜像始终随包携带 |
| `--bootstrap` | 系统依赖 `offline`（默认）或 `online`；两者均按制作时记录的版本、来源和校验值安装 |
| `--migration` | 从指定目录选择 Migrator 归档及配套脚本；省略则不添加升迁 |
| `--refresh` | 重建本次需要的应用公共运行环境，重新解析其系统底图；默认关闭，可写 `--refresh:false` |
| `--title` / `--description` | 清单中的描述信息 |

输入为 `.container` 时，仅允许覆盖 `version`、`source`、`output`、`engine` 和 `refresh`；其余内容直接编辑清单。`refresh` 不刷新基础服务 tag、bootstrap 或预演缓存，`plan` 不执行刷新。

`--source` 自身相对于调用目录解析；受其管理的本地相对输入和输出均相对于**最终 source**，包括 `./` 与 `../`。绝对路径保持原义。清单内的 `source` 相对于声明它的文件；模板配套文件相对于模板目录。`run` 没有 source 选项，归档相对于调用目录。

输出命名为 `name[-tag]@version-architecture.container` 与同名 `.tar.gz`。已有交付包不覆盖；可用 `make FILE.container --version:1.1` 制作下一版。失败保留输入草稿；成功时归档内外的完成清单一致。

## 配置清单

### 默认配置与变量

`output/.settings` 按组件名称分段，只接受 `tag`、`repository`、`settings`。存在某个段落不会自动选中该服务。

```ini
[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both;password=$(redis_password)

[mysql]
tag=latest
settings=root-password=$(mysql_root_password);database=example
```

显式组件配置优先于 `.settings`，再使用模板默认值；`settings` 按参数逐项合并。镜像 tag 缺省为字面 `latest`，不搜索“最新稳定版本”；`repository` 是不含协议、tag 或 digest 的完整镜像仓库地址。

依据任何 `.container` 制作或规划时，都不重新合并 `.settings`。**只有组件列表完整制作成功后**，才补充缺失的服务 tag；已有值、repository 和 settings 不自动改写。plan、失败制作和清单回放不补写默认文件。

变量来自共享 `.env` 流程及命令上下文，支持 `$(name)` 和 `%name%`；`$$(name)`、`%%name%%` 保留字面引用。不整份导出环境，也不因为变量和参数同名就自动绑定。内置绑定只有 MySQL 的 `mysql_root_password` 及 RustFS 的 `rustfs_access_key`、`rustfs_secret_key`。

`settings` 使用单行连接字符串：

```ini
settings=port=16379;password="a;b=""c"""
```

先拆分参数，再对每个值求值，密码变量中的分号不会产生新参数。单项 `password=` 表示显式空值并阻止回退；`false`、`0` 均为有效值。清单中的整条 `settings=` 表示没有显式参数，继续采用模板默认值或变量绑定。

plan 会保留服务设置和环境值中的变量引用；缺少必填服务参数时提示待填项并正常保存草稿，make 会在镜像操作前拒绝缺值。完成清单记录展开后的值，可能含凭据，应按实际内容管理访问权限和版本控制。

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

[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both
```

根字段为 `name`、`tag`、`version`、`engine`、`distribution`、`architecture`、`bootstrap`、`imaging`、`source`、`output`、`title`、`description`；另外支持 `stage=plan|complete` 和按顺序声明的 `migration#1`、`migration#2` 等归档路径。`stage` 由工具维护；手写清单可省略。

| 组件字段 | 使用场景 |
| --- | --- |
| `package` | 应用包路径；有此字段的段落表示应用 |
| `tag`、`repository`、`imaging` | 基础服务镜像选择，以及可选的局部交付方式覆盖 |
| `settings` | 基础服务参数，见下表 |
| `template` | 基础服务的内置模板名称或自定义模板路径；省略时使用组件名 |
| `environment!NAME` | 显式容器环境值；应用可覆盖包内服务环境，基础服务不得与参数映射的环境值冲突 |
| `dependences` | 应用的 `nginx[@tag]` 或 `runtime-*` 声明；运行时须与包内元数据一致，不是通用服务依赖列表 |
| `probe-host!站点名` | 应用 Web 站点的具体探测主机名，用于仅声明通配域名的站点 |
| `file!/容器绝对路径` | nginx 组件使用的显式资源文件；值为制作端文件路径 |
| `digest`、`identity`、`timestamp`、`size` | 工具记录的镜像身份及可选展示信息；详细含义见 [实现说明](docs/implementation.zh-Hans.md#镜像身份与缓存) |

应用不使用 `template`、`settings` 或服务镜像选择字段；程序入口、工作目录、运行时及基础健康检查由安装包提供或推导。需要改变应用配置时，在 Packager 的 scheme 中修改后重新打包。

### Web 应用与升迁

带完整 `.web/nginx` 交接资产的应用自动加入 nginx；多个应用共用该入口。没有托管资产时，应用有效的 `Listen` 端口直接发布到宿主回环地址。nginx 端口来自包内绑定，默认同端口发布；用 `容器端口:宿主端口` 列表调整或关闭：

```ini
[example-web]
package=./packages/example-web.deb
probe-host!tenant=demo.example.com

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/example/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/example/key.pem=./certs/key.pem
```

示例中的 `tenant` 和证书路径必须与实际包内容相符。具体探测名必须匹配站点域名；外部文件声明属于使用它的 nginx 组件。工具不猜测站点、不代签证书，也不修改应用的登录回调或重定向地址。

升迁来自 [Migrator](../migrator/README.zh-Hans.md) 的 `(migrate)@版本_linux-架构.tar.gz` 与同名 `.sh`，按版本升序执行，选中版本不得重复。制作只收集文件，不执行 SQL；现场安装及本机预演都会执行选中的升迁。修改升迁连接设置后应重新制作升迁包。

## 内置服务

全部服务提供 `port`；有数据挂载的服务另提供 `storage=persistent|temporary`，默认 `persistent`。下表端口均默认绑定 `127.0.0.1`；星号表示必填参数，其余未注明默认值的参数可省略。

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
| sqlserver | 1433 | `password*`、`accept-eula*`；仅声明 x64 |
| tdengine | 6041、6060 | `console-port=127.0.0.1:6060` |
| valkey | 6379 | 同 Redis |
| zookeeper | 2181 | — |

除 nginx 映射列表外，端口参数接受 `16379`（回环地址）、`192.0.2.10:16379`、`[::1]:16379` 或 `none`。只修改宿主映射，不改变容器监听；`none` 仅保留内部访问。辅助端口默认不发布，RustFS、TDengine 控制台除外。nginx、haproxy、memcached、otel 没有数据挂载，不接受 storage。

`temporary` 使用匿名卷：同一容器停止、启动、重启时保留，工具删除容器时清理。基础设施启动不会隐式重建已有容器。持久目录在普通卸载后保留，显式 purge 才清理；切换设置不自动迁移旧数据。

Redis/Valkey 的 `persistence` 可为 `both`、`rdb`、`aof`、`none`；AOF 每秒同步，RDB 使用默认快照周期。`password` 默认空，非空时同步配置认证和健康检查；`maxmemory`、`maxmemory-policy` 使用镜像接受的语法。storage 与 persistence 独立。

服务参数不会自动启用镜像中缺失的管理功能，也不负责修改已有数据库凭据。SQL Server 的 `accept-eula` 由操作者按许可填写。caddy、haproxy 是独立入口模板，不接收应用的 nginx 托管资产；其配置需要由所选镜像或自定义模板提供。

完整仓库、辅助端口和环境映射见 [模板参考](docs/templates.zh-Hans.md)。

## 本机预演

`run` 校验交付包后，在隔离的 Linux 容器内执行包中原有 `install.sh`，包括 bootstrap、镜像导入、升迁、应用启动和健康检查。外层使用所选 Docker/Podman，内部使用独立 Docker；只接受与外层引擎原生架构一致的包，外层无须 Compose。

- 每次创建新安装状态和测试数据；干净的底图及经过校验的基础设施/入口镜像可以复用。
- 优先使用交付端口，被占用时分配空闲端口并显示最终地址。Web 发布到所有 IPv4 接口，普通 TCP 服务发布到回环地址；局域网可达性仍取决于防火墙和虚拟机网络。
- 必需的 Web 入口探测失败会使预演失败；基础服务仅本机转发失败时告警，服务安装或自身健康失败仍会阻止就绪。
- 域名探测直接连接本机端口并保留 Host/SNI，HTTPS 验证证书；不修改 hosts、DNS 或信任库。浏览器访问域名仍需自行准备解析。重定向只报告目标，不跟随。
- 就绪后 Ctrl+C 并成功清理返回 0；启动期间取消返回 130。安装或探测失败会保留已建立的现场，等待 Ctrl+C 清理，仍返回失败。
- 同名应用已有预演现场时拒绝启动另一个；强制结束进程可能留下资源，按错误输出检查归属后处理。

预演会实际执行包内应用和升迁，应使用适合测试的配置。隔离容器共享宿主内核，不能代替真实目标主机验收。

## 现场操作

将交付包复制到匹配的 Linux 主机，解压到独立目录。以下命令在 root 会话中执行：

```sh
mkdir example-delivery
tar -xzf example@1.0-x64.tar.gz -C example-delivery
cd example-delivery
./install.sh
containerizer status --name example
```

安装器把所需资产保存到受管目录，并安装或复用 `/usr/local/bin/containerizer`。此后状态、启停、恢复和卸载不依赖最初的解压目录。

现场选项使用空格分隔；`BUNDLE` 表示交付归档或已解压目录：

| 命令 | 作用 |
| --- | --- |
| `containerizer install BUNDLE [--name NAME] [--no-start]` | 安装归档或已解压交付目录；同 name 已有部署时执行升级检查 |
| `containerizer prepare BUNDLE --name NAME` | 保存资产、准备引擎及镜像，停在 Prepared；不停止应用、不执行升迁 |
| `containerizer upgrade BUNDLE --name NAME [--no-start]` | 升级已有部署 |
| `containerizer list` | 列出安装记录 |
| `containerizer status --name NAME` | 输出安装状态 JSON |
| `containerizer logs [COMPONENT] --name NAME [--tail 100] [--follow]` | 查看或跟随容器日志 |
| `containerizer stop --name NAME` | 进入维护，停止应用和入口；基础设施继续运行 |
| `containerizer start --name NAME` | 启动已准备完成的应用和入口，健康通过后解除维护 |
| `containerizer restart [COMPONENT] --name NAME` | 默认重启应用和入口；指定组件可重启该组件，维护或未完成事务时拒绝 |
| `containerizer recover --name NAME [--retry-migration VERSION]` | 继续原失败/中断事务，恢复后保持待启动 |
| `containerizer uninstall --name NAME [--purge]` | 卸载；`./uninstall.sh [--purge]` 可从交付目录定位同一安装身份 |

`--no-start` 仍会启动基础设施并执行升迁，只让应用和入口停在 ReadyToStart。进入命令前已处于维护状态，也会保持待启动。

升级允许应用、应用配置及入口更新，拒绝改变基础设施镜像、有效配置或 bootstrap 包集合。失败保留维护状态和诊断，不自动启动旧版本，也不自动重跑失败 SQL。排查后执行 recover；只有确认可重试时才指定当前事务的失败升迁版本，随后显式 start。

多主机维护由操作者协调：分别 prepare、stop，在指定主机升级并使用 `--no-start`，全部必要升迁成功后再逐机 start。

| 现场位置 | 内容 |
| --- | --- |
| `/var/lib/containerizer/apps/NAME/` | 安装记录、发行资产、升迁状态 |
| `/var/lib/containerizer/data/NAME/` | 服务持久数据 |
| `/var/log/containerizer/NAME/` | 升迁尝试结果日志 |
| `/var/cache/containerizer/NAME/` | bootstrap 下载与暂存 |

普通卸载删除本应用的容器、匿名卷及网络，保留数据、发行资产、升迁记录和镜像，供排查、重装或后续 purge。purge 进一步删除核实归属的本地资产及独占镜像，最后删除安装记录；中断后可重复执行。全局执行器、Docker/Compose、原始交付介质及远端数据不属于 purge 范围。

## 镜像源与缓存

可在最终输出目录放置 `.mirrors`；run 则读取归档所在目录中的该文件，plan 不读取。示例：

```ini
docker.io=mirror.example.com/docker.io
mcr.microsoft.com=mirror.example.com/mcr
```

值为自选可信的 `主机[:端口][/路径前缀]`，多个地址用分号分隔，不带协议、tag、digest、凭据或末尾斜线。工具先复用校验通过的缓存，再依次尝试镜像源及原仓库；已固定的摘要和平台不能因回退而改变。配置不写入交付物，普通现场安装使用目标引擎自己的配置。

应用公共运行环境缓存在 Windows 的 `%LOCALAPPDATA%/Zongsoft/containerizer` 或 Linux 的 `~/.cache/Zongsoft/containerizer` 下（绝对路径 `XDG_CACHE_HOME` 可替代 Linux 缓存基目录），与应用包、应用数据分开。需要更新系统底图或运行时补丁时使用 make 的 `--refresh`；具体缓存分层与手动清理边界见 [实现说明](docs/implementation.zh-Hans.md#镜像身份与缓存)。

## 深入阅读

- [实现说明](docs/implementation.zh-Hans.md)：制作数据流、Web 交接、协议、安装状态和资源边界。
- [模板参考](docs/templates.zh-Hans.md)：内置服务映射及自定义模板格式。
- [开发指南](SKILL.md)：代码维护、构建、测试及隔离验证。
