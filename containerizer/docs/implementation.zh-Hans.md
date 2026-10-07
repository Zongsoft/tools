[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# Containerizer 实现说明

本文描述当前源码的职责、数据契约与生命周期。命令和配置用法见 [README](../README.zh-Hans.md)，模板字段见 [模板参考](templates.zh-Hans.md)，开发操作见 [SKILL](../SKILL.md)。

## 架构边界

Containerizer 管理单节点交付。Packager 负责应用安装包，Migrator 负责升迁归档；Containerizer 消费这些产物及配套入口，不引用它们的可执行项目，不协调远端节点。

| 范围 | 入口与职责 |
| --- | --- |
| 制作输入 | `ManifestFactory` 整理命令上下文、变量、路径、版本冲突和输入选择；`ContainerManifest` 保存模型、读取 Profile、补全依赖并校验自身 |
| 服务规划 | `ServicePlanner` 为 plan/make 准备服务，报告缺值，调用 Web 规划并检查依赖和端口 |
| 应用与模板 | `ApplicationPlanner` 推导应用入口、环境、健康检查及构建时运行时；静态 `TemplateCatalog` 读取基础设施/入口模板 |
| 制作协调 | `DeliveryBuilder` 管理工作区、镜像阶段、bootstrap、升迁收集、校验与发布；`ServiceBuildContext` 保存创建时确定的只读组件关联 |
| 引擎与镜像 | `ContainerEngine` 执行实际引擎操作；`ImageReference` 管理仓库/tag/digest 规则；`ServiceImagePreparer` 适配健康检查、Redis/Valkey 认证和数据账号 |
| 应用镜像 | `ApplicationImageBuilder` 安装应用包并生成最终镜像；`RuntimeEnvironmentCache` 管理可复用的系统与运行时文件系统 |
| 本机预演 | `RunContext` 管理一个完整会话，其私有 `Endpoint`、`ImageCache` 负责转发与内部镜像存储 |
| 现场执行 | `InstallationManager` 控制安装事务；`DockerHost` 操作系统和 Docker；`InstallationStore` 保存状态、历史、资产及锁 |
| 共享源码 | `.shared/Containerization.props` 直接链接协议模型、文件校验与进程辅助源码；不生成共享 DLL |

制作端使用 Core Profile、ConnectionSettings 和仓库 `.shared` 变量流程。现场执行器只有 BCL 依赖，消费 JSON 和已生成资产，不解析 `.container`、Profile 或 YAML。生成的 `compose.yaml` 使用 JSON 语法，交由 Compose 消费。

会话、锁、资源生命周期和安装事务各有完整的协调边界；纯规则用无状态类型，副作用通过现有引擎/宿主/进程边界执行。可见性取决于生产调用：私有成员不因测试改成 internal/public，也不增加仅供测试使用的入口。

## 制作数据流

```text
命令上下文 + 本地输入
  → ManifestFactory / ContainerManifest
  → ServicePlanner
      ├─ plan：保存草稿
      └─ make：DeliveryBuilder
          → 解析固定镜像 → 制作应用 / 导出镜像 → 服务镜像适配
          → bootstrap / 配置 / 升迁 / Compose
          → 完成清单 / JSON / 校验和 → 压缩 → 发布
```

组件按声明顺序保留，依赖补入的 nginx 追加到列表。上下文直接关联组件，后续构建不依赖平行列表下标。镜像身份在写出前确定；序列化只读取模型。

plan 和 make 共用服务准备，但 plan 不连接引擎、不读取 `.mirrors`、不收集系统依赖。缺少必填设置时，plan 输出待填诊断并保存草稿；其他无效结构仍报错。运行时解析保留在应用镜像构建入口，不把 make 阶段的检查提前变成 plan 的外部操作。

### Profile、变量与路径

`.container` 接受固定根字段及组件字段，拒绝未知字段、重复组件/字段与嵌套组件。Profile 导入复用 Core 机制；导入文件参与声明检查，source 相对于实际声明文件解析。`.settings` 和模板读取禁止导入。

变量先由共享 Utility 收集：默认值、系统环境、从根到 source 的各级 `.env`、命令上下文按既有优先级合并，分段名称以下划线连接。最终根字段加入本次变量视图。输入选择和根路径需要立即求值；服务 settings/environment 在 plan 保留引用，make 按值求值。完成清单以字面值回放；由完成清单重新 plan 时转义字面引用，避免再次展开。

source 的 CLI 值相对于调用目录；其他受管本地路径相对于最终 source。模板配置文件相对于模板所在目录，Linux 容器路径使用独立校验，不受制作主机路径规则影响。生成清单把 source 写成相对于输出目录的路径，output 和本地组件输入写成相对于 source 的路径。

交付名称是 `name[-tag]@version-architecture`。版本使用 Core 数字版本规则：2–4 段、每段最多 65535、非零；自动版本使用年取模 1000、月、日，同名冲突递增第四段。显式版本冲突报错。自己的输入草稿可在成功制作后原位补全，但已有归档不可覆盖。

### 包和升迁输入

`PackageReader.Select` 返回路径与已读描述；同一次准备复用该描述，组件包路径变更后重新读取。Web 资源提取仍重新读取包，交付文件仍做完整性校验，不建立跨命令包元数据缓存。

支持 Packager 的 tar + 同名 Shell、deb 和 rpm；读取包身份、架构、安装目录、服务文件、`runtimeconfig` 与 Web 元数据。`Listen` 分别来自 tar PAX、Debian control 或 RPM 自定义标签。目录候选按当前目录、`.packages` 顺序查找，各层优先发行版原生格式再 tar；只对目标架构和名称前缀匹配项进行选择。

应用要求唯一且可解析的服务定义、`ExecStart` 和 `WorkingDirectory`。入口为前台参数数组；有歧义的 Shell 包装或复杂命令应在上游修正。应用配置随原包安装进镜像，不按文件扩展名拆成现场配置挂载。

升迁输入只识别明确选择的 `<前缀>(migrate)@<version>_linux-<architecture>.tar.gz` 与配套 `.sh`。CLI 目录候选可多选；清单按正整数 migration# 序号读取，校验版本升序、唯一与架构。没有选择项就没有升迁，不从应用包或默认目录补取。制作不解析业务 SQL、不连接数据库；记录归档和入口的组合哈希并原样交付。独立 `hosting/containerize.cmd` 包装脚本只负责菜单、文件选择与命令调用，工具不依赖这些脚本，也不读取其默认目录约定。

### 发布与输出

制作临时文件位于独立系统工作区；输出目录只接收完成清单、交付归档及需要补缺的 `.settings`。默认完整制作不先写草稿。

发布由输出锁与 `ArtifactPublisher` 协调，核对输入哈希，防止构建期间编辑被覆盖。外部清单与归档内副本字节一致；失败清理本次临时资源并保留既有输入。多个最终文件不能由一次文件系统操作原子提交，不宣称断电时具有跨文件事务保证。归档使用 gzip Fastest。

## 镜像身份与缓存

服务镜像的仓库、来源 tag、目标平台 manifest digest、多架构 index digest 和本地 image ID 是不同信息。`ImagePlan.Reference` 是现场使用的完整本地引用；`SourceTag`、`SourceReference` 记录来源，`Digest` 固定目标 manifest，`Id` 用于导入后的内容检查。操作系统固定 Linux，架构来自清单根字段。

先复用有身份与平台证据的本地镜像，证据不足时解析或拉取。固定 digest 的回放不得退回 tag、换平台或把索引摘要/image ID 当作目标 manifest。可用时记录创建时间和引擎报告大小，未知则省略；不为展示信息额外联网。

清单 identity 关联 repository、tag、Linux 和 architecture；修改这些值会使旧 digest、timestamp、size 失效。只修改交付版本、标签或宿主发行版不改变基础服务镜像身份。

制作端通过 image ID 导出基础服务，不在缓存镜像上附加发行专属标签。应用构建、账号探针及辅助构建器使用本次唯一资源名。清理删除本次容器及匿名卷、临时镜像和构建器；保留有用的来源镜像和工具镜像，不执行全局 prune。

| 缓存层 | 身份与内容 | 失效及更新 |
| --- | --- | --- |
| 引擎镜像 | 来源镜像、平台、摘要及已验证关联 | 内容/平台证据不匹配时重新解析；基础服务 tag 不受 --refresh 控制 |
| 应用公共运行环境 | 按引擎、发行版、架构、运行时家族及主次版本分组；系统文件、curl/CA 和运行时，不含应用 | 签名包含底图 digest、平台与安装配方；校验归档哈希、长度、环境及运行时要求 |
| bootstrap | 用户缓存中按规范化 source 哈希、发行版、架构隔离的系统包集合 | 显式导入优先，其次缓存；不受 --refresh 控制 |
| run 底图 | 外层引擎中的 systemd/转发工具底图，以配方和架构标识 | 校验归属；不包含任何已安装应用状态 |
| run 镜像存储 | 按应用隔离的内部 Docker 存储卷 | 仅在完整清理、环境匹配及干净标记成立时复用 |

文件缓存根为 Windows `%LOCALAPPDATA%/Zongsoft/containerizer`；Linux 使用绝对 `XDG_CACHE_HOME`，否则 `~/.cache`，再追加 `Zongsoft/containerizer`。公共运行环境位于 `runtime/<engine>/<profile>/`。

公共运行环境用单项锁协调。同一次制作同一环境最多 refresh 一次；refresh 也重新解析所需系统底图。新一代归档完整生成并校验后，原子替换 `environment.json` 指针；失败保留旧代。读取者持锁复制所选代，再清理过期归档。没有按时间自动过期的策略；缓存不包含应用配置、业务数据或数据库初始化结果。

手动清理前停止使用该缓存的制作/预演，只删除明确识别的缓存项。不要在活动进程持锁时删除 locks 目录，不要用全局镜像/卷 prune 代替定向清理；交付历史和现场升迁状态不属于这些可再生缓存。

### 镜像源

`RegistryMirrorSettings` 从指定目录读 Core Profile；共享 `RegistryMirrors` 处理精确 registry 匹配、来源顺序和取消。配置拒绝段落、重复仓库条目和空的候选地址。镜像源只改变获取路径，不改变逻辑仓库、固定摘要或平台。没有固定摘要时，第一个可用来源可解析 tag；身份确定后，其他来源必须匹配。

Podman 构建使用已验证的本地镜像并禁止拉取；Docker 使用独立 BuildKit builder 与临时配置，完成后清除 builder 及其缓存。run 将镜像源规则通过交付资产之外的临时 JSON 交给内部在线拉取流程。`.mirrors` 和实际镜像源地址不进入完成清单或交付 JSON；不修改全局引擎配置、软件包源或凭据。

## 应用、Web 与服务准备

### 运行时与应用健康

`ApplicationPlanner` 从入口 DLL 的 `runtimeconfig` 选择 .NET/ASP.NET Core 运行时；显式 runtime-* 依赖必须匹配。自包含入口不额外安装框架运行时，但仍准备探测依赖。构建安装同主次版本的当前可用补丁，校验不低于包要求的最低补丁，并记录最终镜像实际报告的版本。Ubuntu 22.04 的 .NET 9 及以后运行时使用 `ppa:dotnet/backports`；其他 Debian/Ubuntu 路径使用对应 Microsoft 包源，RPM 路径使用 RHEL 系列包源。

应用包在构建环境执行原安装流程，服务启动被构建适配控制；最终镜像只保留运行所需文件和前台入口，不把现场执行器作为应用启动器。

`Listen` 缺失或为空时使用 `kill -0 1` 检查进程。非空 `Listen` 必须为分号分隔的 HTTP/HTTPS 根 URL，不带凭据、非根路径、查询或片段。容器绑定从回环/通配/DNS 转换为全部接口；固定非回环 IP 不接受。

健康检查优先第一个 HTTP，否则第一个 HTTPS，在容器内 GET /；任何 HTTP 响应表示监听存活，不跟随跳转。HTTPS 必须有 DNS 身份，使用 SNI 和镜像信任库。连接、超时与 TLS 验证失败为不健康。默认检查间隔 10 秒、超时 5 秒、重试 12 次、启动宽限 30 秒；这不代表业务功能就绪。

### nginx 交接与渲染

`WebPackage` 只接受一个应用根下的 `.web/nginx/`，其中必须具有：

| 文件 | 含义 |
| --- | --- |
| `.bindings` | Profile/INI 格式的站点、域名、原始绑定和显式默认关系 |
| `<包名>.conf` | Packager 生成的本机配置 |
| `<包名>.conf.template` | 保留语义占位符的容器化渲染输入 |

每个 .bindings 站点段只接受 host、bind、default；列表用逗号或分号分隔。bind 使用带显式端口的 HTTP/HTTPS IP URL；default 必须引用同站点已声明绑定。host 支持具体名称以及 `*.example.com`、`.example.com`、`example.*` 形式。Containerizer 不重新解析任意 nginx 正文来猜测绑定。

`WebIngress` 把应用 ID 替入 `{{zongsoft:application}}`，将 `{{zongsoft:file:BASE64}}` 中的 UTF-8 路径替换为受管资源路径；未识别或未消除的标记报错。包内相对资源以应用根为基准，必须是存在的普通文件；外部绝对路径必须在 nginx 组件以 file! 显式提供。拒绝链接、非法路径和目标重叠，不从制作主机对应系统路径隐式取文件。

nginx 主配置按应用 ID 排序 include 各应用配置。交付使用 `config/nginx/nginx.conf`、`config/nginx/sites/<application>.conf` 及去冲突资源，均为只读挂载；应用声明顺序仍用于用户输出。

入口规划按监听地址/端口检查协议一致性、默认站点、主机名冲突及实际路由归属。同一监听优先采用唯一的显式 default；未声明时，按应用 ID 排序后的配置加载顺序和站点声明顺序选取首个站点，与生成的 include 顺序一致。发布端口要求 IPv4 通配监听；对应 IPv6 通配绑定存在时，站点/默认关系必须一致。nginx port 设置按原端口重映射或关闭，不修改原监听定义。仅通配域名且已发布的站点必须提供匹配的具体 probe-host；无域名站点按实际默认路由校验，不能因端口可连接就认定站点正确。

协议中 `ServicePlan.Web` 保存应用与站点身份、Hosts、ProbeHosts、Bindings；绑定包含 `IsExplicitDefault`、`IsDefault` 和 Publication。`PortPlan` 只表达物理映射。没有托管资产的应用由 `Listen` 直接生成 Web 记录及回环发布；声明 nginx 依赖却没有完整托管资产会失败。caddy/haproxy 不参与这套交接。

### 基础服务

`ServiceOptions` 管理环境合并、参数、端口、存储及 Redis/Valkey 持久化/认证命令。`ServiceImagePreparer` 在镜像固定后确认具有模板健康检查或镜像自带健康检查，设置认证环境并解析数字 UID/GID；`ContainerEngine` 保留实际引擎操作。

data-owner 可指定数字 UID:GID 或固定镜像中的账号/组；镜像以 root 入口再降权时仍可明确指定数据账号。持久目录启动前设置归属，同时保留清理标记。RHEL 系列绑定使用私有 SELinux 标签，不通过关闭 SELinux 适配。

临时数据使用匿名卷；基础设施启动使用 no-recreate，应用/入口重建时更新相应匿名卷。普通卸载移除本应用容器和临时卷，持久目录另按归属记录管理。

## bootstrap 与交付协议

bootstrap 为目标发行版/架构收集 Docker、Compose 及依赖，记录包名、版本、架构、来源 HTTPS URL、依赖、哈希和长度。Debian/Ubuntu 与 RPM 系列分别使用匹配的包收集脚本。RHEL 不用 Rocky/Alma 包冒充，必须导入经准备的集合。

显式集合位置为 `source/.containerizer/bootstrap/<distribution>_<architecture>/`，包含 `bootstrap.lock.json` 和 packages。`build/Import-Bootstrap.ps1` 用 Collection、Profile、BaseImageReference、Destination 参数从带 `metadata.tsv` 的集合生成此结构；BaseImageReference 必须是精确 `repository@sha256:...`。导入和缓存均在消费时校验，不因存在锁文件就跳过文件检查。

offline 携带包文件；online 保留同样的锁与元数据，在现场按记录下载并校验，再禁用在线软件源执行本地安装事务。已有 Docker 时，当前实现检查 daemon、Compose 和命令能力后复用，不按锁逐包重装或自动升级已有引擎。

交付根布局如下；只包含实际需要的可选目录：

```text
containerizer                  原生执行器
zh-Hans/                       本地化资源
install.sh / uninstall.sh       薄启动入口
README.md / README.zh-Hans.md   该交付物的操作说明
name[-tag]@version-arch.container
containerizer.json             执行计划
checksums.sha256                完整文件校验
compose.yaml                   已生成运行配置，无 build
images/                        离线镜像归档
packages/                      bootstrap 元数据与离线包
config/                        只读运行资产
migration/                     原始升迁归档及入口
```

当前 JSON 协议为 1，属性按模型 camelCase 输出，拒绝未知成员，不保留旧字段别名。包描述和 bootstrap 的完整底图引用叫 `baseImageReference`，布尔属性如 `isInMaintenance`、`isReinstall`、`isExplicitDefault` 直接体现模型语义。

`containerizer.json` 的哈希是发行内容身份；版本字符串本身不能替代内容身份。Files 列出其他资产的路径、长度、SHA-256；`checksums.sha256` 另包含 JSON 自身的哈希。`SourceHash` 指向内含完成清单。交付打开时检查协议、资产完整性、额外文件、路径、镜像平台、服务身份、Web 依赖关联及挂载约束。校验和用于一致性检测，不是数字签名或来源认证。

Compose 项目名来自规范化 name 加 name 哈希，不包含 tag 或版本。资产固定 image、platform、pull_policy=never、归属标签、日志轮转和只读/可写挂载。启动不构建或隐式拉取镜像；依赖顺序由 Manager 维护，单服务启动不连带重建依赖。

## 安装事务与恢复

安装使用应用锁；短时主机锁保护共享执行器、注册和目录归属。锁位于 `/run/containerizer`，独立于可被 purge 的资产；主机锁与应用锁文件名分开。

| 阶段 | 行为 |
| --- | --- |
| 校验与登记 | 校验交付、Linux/root/发行版/架构、磁盘与端口；检查共享执行器归属/协议能力，持久保存并复验资产，登记原事务 |
| `PrepareBootstrap` / `PrepareImages` | 准备引擎；离线导入或在线获取固定镜像，核对 ID/平台并设置现场引用 |
| 维护屏障 | 准备受管目录，先保存 `IsInMaintenance`，再停应用与入口 |
| `StartInfrastructure` / `CheckLocalInfrastructure` | 首装或普通卸载后重装时启动基础设施；升级时检查既有基础设施健康 |
| `ApplyMigration` | 保存尝试记录，调用原升迁入口，成功后再次 check |
| `ReadyToStart` | --no-start 或既有维护状态下停在此处 |
| `StartApplications` / `StartIngress` / `CheckHealth` | 依次启动应用、入口并检查健康 |
| `CommitRelease` | 恢复重启策略、写成功历史及当前发行指针，清除 pending |

prepare 在镜像准备后返回 `Prepared`；可能已安装共享执行器及系统依赖，不是纯只读检查。它不进入停机/升迁阶段。再次安装同一已成功发行会检查镜像与健康，不重新执行 SQL。

升级比较 name、distribution、architecture、project、dataRoot；基础设施 ID、image ID/digest、有效配置哈希及 bootstrap 包名/版本/哈希集合必须一致。有效配置涵盖环境、参数、端口、挂载、模板产生的运行字段与配置文件内容。普通卸载再安装也不能绕过这些检查。

### 维护和失败

`EnterMaintenanceAsync` 是统一停机入口。Manager 先保存维护标志；`DockerHost` 在修改容器前保存原重启策略，再设置 restart=no 并停机。一次停机内只校验、物化一次历史发行计划，按归属标记识别应用/入口容器；基础设施不会被 stop 命令关闭。保存失败时不越过持久化屏障；部分停机失败保留已写状态供后续处理。

阶段开始、成功、失败及升迁尝试均持久化。失败保持原事务和诊断，已产生的系统/数据变化不回滚，不自动启动旧应用。recover 校验原事务资产和 ID，继续该事务并保持 `ReadyToStart`；不能借恢复换包或清除已有失败记录。start 只接受可启动状态；维护或 pending 时拒绝 restart。

升迁按自身版本保存状态，成功记录须经原入口 check 核验后复用；相同版本的新归档也不能靠版本字符串跳过 check。Started、Failed、`Interrupted`、Unknown 均要求显式 retry-migration，且只接受当前事务对应版本。执行前保存尝试，apply 后 check 成功才记 `Succeeded`。日志记录版本、操作和退出码，不把任意脚本输出或 SQL 全文持久化到 Containerizer 日志。

### 资产保存与清理

`InstallationStore` 管理 `apps/<name>/installation.json`、`releases/<id>/assets`、成功历史及升迁目录。受管资产复制并校验后，运行挂载与恢复均使用稳定路径，原始解压目录不参与后续依赖。全局执行器通过归属哈希及协议范围检查复用或替换，未识别的同名文件报冲突；不按应用版本号更换执行器。

普通卸载保留注册、发行资产、升迁记录、日志、持久目录及镜像，删除本应用容器、匿名卷和网络。重装恢复基础设施容器并继续核验既有状态，不重新初始化已保留数据。

purge 顺序为：保存 `Uninstalling` → 维护/宿主资源清理 → 保存资源完成检查点 → 删除发行资产 → 完成目录/日志等清理 → 最后删除安装注册。失败保存 `CleanupFailed` 和残留原因；重试跳过已完成的资源阶段。Manager 保持顺序和断点，Store 承担历史、发行资产和最终注册的文件操作。

删除前核对实际所有权、目录标记、父子关系、链接和挂载边界；缺少证据不能视为清理成功。删除本应用镜像引用时保留其他容器或部署仍使用的内容。共享执行器、引擎、其他应用、原始交付介质和远端数据库不清理；不执行逆向升迁或全局 prune。

## 预演会话与进程执行

run 先由共享 `DeliveryBundle` 验证归档，再创建带会话及应用归属标签的特权 systemd 容器，不挂载外层引擎 socket。外层原生架构必须匹配交付；内部 Docker 使用 overlay2 和独立存储，containerd 使用会话匿名卷。

安装状态与业务数据每次新建。内部 `ImageCache` 清除容器、业务卷、自定义网络、应用/旧镜像及多余标签，只保留当前基础设施/入口 image ID；停止内部引擎后才写干净标记。纯应用交付不保留空镜像存储。复用检查归属、挂载占用、环境、干净标记及镜像身份；清理失败则丢弃本次缓存，未知归属或仍被占用的卷报错。

TCP 转发通过内部 socat 接到实际宿主发布端口，外层读取真实端口映射；端口冲突有界重试。Web 探测连接本机 IPv4，保持请求域名、Host、SNI，禁用代理及自动重定向，使用系统证书验证。HTTP 5xx 失败，其他响应包括 404 可证明入口响应；不同于容器内只判断监听存活的检查。没有 Web 语义的普通 TCP 入口只测连接；UDP、集群通告地址和应用层回调不会自动改写。

`RunContext` 保持前台会话，建立后的失败现场保留到取消；清理使用独立时限，不沿用已经取消的工作令牌。退出码区分就绪后退出、启动取消和原始失败；清理失败不报告成功。应用锁与引擎标签分别防止并发会话和误删强制退出后遗留现场。

`IProcessRunner` 同时提供捕获输出 `RunAsync` 和实例流式 `StreamAsync`；引擎、预演安装与现场 logs 均通过注入的 runner。参数通过 `ArgumentList` 传递，UTF-8 stdout/stderr 分别处理；捕获模式持续排空双管道并限制诊断缓冲，流式模式逐行转发并返回原退出码。取消终止进程树。制作输出使用现有本地化回调和颜色，不通过测试包装入口改变行为。

## 平台与验证边界

| 层次 | 当前可据以判断的范围 |
| --- | --- |
| 源码实现 | 制作端 Windows/Linux 路径处理；Docker/Podman 制作与预演分支；目标 Linux x64/ARM64；发行版接受范围见 README |
| 自动化验证 | 制作端全部项目目标框架严格构建；两个测试项目；执行器 Linux 测试；配置、缓存、Web、进程取消、安装状态与清理失败路径 |
| 原生编译 | Linux x64、ARM64 Native AOT 可编译；x64 协议入口可运行；ARM64 编译不代表 ARM64 运行 |
| 隔离实际运行 | Windows + rootful Podman + Debian 13 x64 的制作/预演路径，包含 Web 和基础服务访问、端口避让、会话数据隔离及清理 |
| 尚无完整目标验收 | 外层 Docker、rootless、其他发行版组合、真实 ARM64、域名 HTTPS 完整应用、独立生产主机及真实业务升迁 |

内置模板的架构列表是输入能力声明，不是每个镜像 tag 的兼容保证。Native AOT 仍有 Linux 系统库依赖；预演底图准备 ICU 等所需库，独立目标也须满足其运行依赖。用专属隔离环境验证软件包事务和生命周期，不把单元测试、交叉编译或共享内核预演描述为生产环境验收。

退出码由当前入口保留：0 成功；2 输入；3 平台/能力；4 获取、完整性或执行；5 bootstrap 事务；6 健康；7 升迁、恢复或执行器取消；8 现场锁冲突；9 卸载清理；制作/预演启动取消为 130。具体命令的阶段诊断与残留状态应和退出码一起判断。
