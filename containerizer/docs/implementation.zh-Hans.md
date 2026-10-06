[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# 实现与验证记录

本轮基线为 [TASK#2](../TASK%232.md)、[TASK#3](../TASK%233.md)、[TASK#4](../TASK%234.md) 与 [TASK#5](../TASK%235.md)，与早期方案冲突时以最新确认的设计为准，不保留旧格式兼容。本文记录实现边界、验证证据及尚未完成的运行验收。

## Web 绑定交接

[TASK#5](../TASK%235.md) 记录最终的 `.bindings`/模板契约、协议 1、入口发布及 Host/SNI 探测。Release 严格构建与 IDE0049 检查通过；制作端 363 项、执行器 77 项、packager 642 项测试通过。Windows/rootful Podman 的真实验收使用隔离 Debian x64 交付：Nginx 80/8080 与应用内部 8069、端口占用避让、Host 保持、共享 PEM 只读资源，以及关闭 HTTPS 发布而内部 TLS 正常工作。两个全局工具均从本地 feed 替换，全部目标框架 DLL 与原生载荷哈希和编译产物一致。

回环 HTTP 访问成功，但即使引擎记录 0.0.0.0 发布，本机 WLAN 地址仍拒绝连接。局域网转发属于尚未完成的环境验收；run 单独显示网卡地址，不宣称已验证可达。IPv6 外部接入与 ARM64 真机未验证，ARM64 证据仅为交叉编译。下文较早的测试计数属于各自记录的改动，不是当前汇总。

## make 加速与 TDengine

本轮设计、实施、端口实测、缓存清理及优化前后数据统一记录在 [TASK#4](../TASK%234.md)，该任务与早期方案冲突时以 TASK#4 为准，不保留兼容层。公共运行环境缓存、`--refresh`、两个 hosting 脚本入口及 TDengine Explorer 默认映射已实现。使用说明见 [README](../README.zh-Hans.md#发布缓存与回放)。

## 架构

| 部分 | 实现 |
| --- | --- |
| 制作入口 | Core 命令、显式 CLI/根字段优先级、Profile/ConnectionSettings、source 路径及共享变量流程 |
| 输入 | 独立读取 packager tar PAX、Debian ar/gzip、RPM header/gzip-cpio；升迁仅选择产物，不解释业务载荷 |
| 来源 | 内置/自定义模板、实际配置值记录、本节点依赖及端口校验 |
| 镜像 | 原生引擎缓存核验、tag/默认仓库选择、平台 manifest 摘要、可选创建时间/大小、Docker 格式导出及保留包内配置的应用镜像 |
| Bootstrap | 对应发行版的精确依赖收集、可复用/导入锁，在线/离线使用同一组包哈希 |
| 发布 | 系统临时暂存、平铺清单/归档发布及回滚、归档根清单与来源哈希一致、成功后仅补默认缺项 |
| 执行器 | Native AOT、JSON 源生成、稳定资产/注册记录/独立锁、持久阶段/维护/升迁尝试 |
| 生命周期 | 十一个命令、升级限制、显式恢复和按归属卸载/清理 |

两端通过[共享协议配置](../.shared/Containerization.props)链接源码，仅制作端携带 Core，已删除 YAML 解析器及 YamlDotNet 依赖，不引用其它工具的可执行项目作为库。

制作端的镜像 manifest、索引及配置摘要使用 Core `Checksum` 解析和比较，`.container` 校验复用同一套 OCI SHA-256 适配。非法十六进制输入按校验错误处理，输出引用保持标准的小写格式。制作端的文件/文本哈希已使用 `Checksum.Compute`，模板 JSON 数组读取也改为 Core `GetArray`。Profile、ConnectionSettings 及共享变量流程继续复用；引号处理和归档/路径边界校验保留各自格式要求。现场执行器继续使用原有仅依赖 BCL 的实现。

生成的 `.container` 服务段落不再包含 `platform`、`architecture`，输入也不再接受这两个段落条目。镜像身份和引擎校验统一使用 Linux 与根架构；修改根架构使旧摘要及展示元信息失效，仅修改 Linux 发行版仍保留基础服务摘要。根部/服务 tag 的含义不同，服务 imaging 仍作为显式覆盖保留。执行器 JSON 和 Compose 的平台要求不变。

制作端记录有效服务配置哈希并保护 Compose 资产。packager 按 hosting/.deploy/<scheme>/ 选定的应用配置保留在应用镜像内。应用只构建一次最终镜像，从清理后的安装文件系统复制运行内容，保留应用配置，移除安装介质、包脚本数据库及宿主服务定义。包中 .bindings 和 .conf.template 驱动 Web 规划及配置渲染，应用镜像中的原文件仍保留。Web 渲染器为生成配置指定 `config/nginx/nginx.conf` 和 `config/nginx/sites/<应用名>.conf`；配置来源可声明交付相对路径，通用收集流程仅复制并记录只读挂载，不包含 Nginx 专用规则，其它文件仍按目标隔离。模板/入口资产直接从已校验的发行目录只读挂载，不再创建可编辑的运行副本、compose.env 或三方比较。正常完成、可处理失败和取消时清理制作工作目录及本次引擎资源。

基础服务镜像解析复用已验证的来源/共享缓存引用，不在制作端引擎创建交付标签。交付标签写入 JSON/Compose，在目标端校验镜像后创建；离线镜像按镜像 ID 导出。重复制作不同发行版本不会向制作端缓存增加项目/版本标签。临时检查容器连同其匿名卷一起删除，可复用的来源、运行环境及工具镜像保留；清理不执行全局引擎缓存 prune。

## 源码组织

参考 packager 按职责组织文件的方式，将相关实现归入所属类型：

| 类型族 | 文件及职责 |
| --- | --- |
| 命令 | [ContainerizeCommand](../src/ContainerizeCommand.cs) 共享执行流程；`.Plan.cs`、`.Make.cs` 定义派生命令，`.Help.cs` 排版帮助；通过重写选择清单/构建行为，避免判断命令具体类型 |
| 制作端模型 | [ContainerManifest.Component](../src/ContainerManifest.cs)、[PackageReader.Descriptor](../src/PackageReader.cs) 嵌套于所属类型；`PackageReader.Tar/Deb/Rpm.cs` 分开各格式读取 |
| 服务准备 | [ServiceBuildContext](../src/ServiceBuildContext.cs) 承载有效设置、环境、资产及服务计划；[ServiceDefaults](../src/ServiceDefaults.cs) 读取共享默认值；[ComponentSelector](../src/ComponentSelector.cs) 选择输入组件 |
| 镜像 | [ContainerEngine](../src/ContainerEngine.cs) 封装引擎操作；`ApplicationImageBuilder.Image.cs` 将镜像准备与应用元数据处理分开，[BootstrapPackageBuilder](../src/BootstrapPackageBuilder.cs) 制作引擎依赖包 |
| 交付 | `DeliveryBuilder.Services.cs` 准备服务配置；`DeliveryBuilder.Delivery.cs` 定位执行器并生成交付说明/脚本 |
| 协议 | [DeliveryPlan](../.shared/DeliveryPlan.cs) 描述交付计划；[Installation.Models](../.shared/Installation.Models.cs) 嵌套事务、归属、升迁状态；[BootstrapPlan.Package](../.shared/BootstrapPlan.cs) 嵌套依赖包元数据；JSON 属性名保持不变 |
| 宿主机路径 | [Installation.Paths](../.shared/Installation.cs) 集中 FHS 数据、状态、日志、缓存、运行期及执行器根路径；制作端与现场共用 `GetDataPath`，应用包路径另行保留 |
| 执行器 | [DeliveryBundle](../.shared/DeliveryBundle.cs) 校验并打开交付资产；[ExecutorArguments](../executor/src/ExecutorArguments.cs) 解析命令；[IInstallationHost](../executor/src/IInstallationHost.cs) 定义安装宿主边界。`DockerHost.Bootstrap/Services/Storage.cs`、`InstallationManager.Installation/Migrations/Recovery/Transaction/Uninstall.cs` 分开宿主机操作与事务阶段 |

普通字符串采用插值表达式，分行生成 Dockerfile 使用 `StringBuilder`；数组初始化采用集合表达式，需要类型推断的地方显式声明目标类型。

复杂条件按职责拆分：端口冲突、镜像健康检查、受管目录边界使用有业务含义的判断方法；两端共用 `Files.IsLinuxPath` 校验 Linux 路径，制作端共用 `Distribution.IsDebian` 判断安装包体系。资源键和 JSON 属性名保持不变，交付计划文件命名为 `containerizer.json`；制作端与执行器共用 `DeliveryPlan.FileName`，当前交付协议为 1。

制作端的发行/日期版本、升迁排序去重及 .NET 运行时选择使用 Core `Versioning.Version.Number`。各数字段范围为 0–65535，`1.0` 与 `1.0.0` 等形式按数字相等比较，但保留显式发行/升迁版本的文本。镜像 tag 仍为字符串。执行器保留 BCL 版本处理及源生成 JSON，不引入 Core 依赖。

制作端通过 `CommandContext.Output`、`Terminal`、`CommandOutletContent` 输出颜色/样式及分行帮助，由 Terminal 处理重定向纯文本；原本写入 stderr 的诊断改用 `Terminal.Default.Error`。共享子进程转发及仅依赖 BCL 的执行器保留原有输出流。

## FHS 存储与清理

[README 的目录布局](../README.zh-Hans.md) 将服务数据及持久安装/升迁状态放入 `/var/lib/containerizer`，尝试日志放入 `/var/log/containerizer`，可重新获取的 bootstrap 资产放入 `/var/cache/containerizer`，锁放入 `/run/containerizer`。日志与缓存复用已有目录归属记录及标记。Bootstrap 在副作用前向当前安装对象登记归属，后续保存事务不会丢失该记录；已删除缓存可以按原归属重建。普通卸载保留这些资产，purge 校验身份、链接和挂载边界后清理，注册记录最后删除。默认服务数据不能指向其它应用命名空间或受管状态、日志、缓存、运行期根目录。只有一个数据挂载时直接使用服务目录；模板有多个数据挂载时保留各自命名的子目录。因此 MySQL 的宿主路径为 `/var/lib/containerizer/data/<name>/mysql`，容器内仍挂载到 `/var/lib/mysql`。只读配置挂载不计入数据挂载数量。不自动搬迁已安装的现场数据。

FHS 与命名重构通过 Windows 制作端 221 项、执行器 60 项测试；隔离 Linux 构建容器中全部 60 项执行器测试通过，包括链接及挂载边界检查。全部制作端目标框架通过 Release 严格编译，并单独验证 IDE0049。这些属于文件系统、事务及构建验证，不代表现场安装验收。

## 本机预演架构与验证

`RunCommand` 负责命令校验及 Ctrl+C 注册。`RunContext` 管理一次会话及清理；其 `.Environment.cs` 分部文件准备干净底图并调用原安装器，嵌套 `Endpoint` 处理端口分配及本机探测。复用 `ContainerEngine`、`BuildStorage`、`ProcessRunner`、`Files` 和分段样式 `Output`。`DeliveryBundle` 移入链接共享源码，使制作端和执行器使用相同的资产校验，不互相引用可执行项目。

执行器通过双语资源显示安装及恢复阶段说明，持久化的阶段标识保持不变。`run` 仅通过安装进程的 `LANG` 传递本机界面语言，应用继续使用包中定义的环境。

内部安装状态及测试数据属于外层验证容器。内部 Docker 使用 overlay2，按应用保存一个带标签的镜像存储卷；containerd 使用会话匿名卷。嵌套 `RunContext.ImageCache` 在复用前检查归属、挂载占用、环境配置及干净标记。清理删除内部容器、数据卷、自定义网络、应用及旧镜像、多余标签，停止引擎后才写入干净标记；仅保留当前基础设施/入口镜像 ID，仅含应用时不保留空存储。镜像身份及平台匹配才跳过导入，与交付版本号无关。内部清理失败，外层容器和整份缓存删除仍使用独立的取消时限；不强制删除非本工具或仍被挂载的卷，错误指出遗留资源。应用锁阻止两个本机管理会话并存，引擎标签识别进程被强制结束后的现场。不执行全局 prune，不改写应用配置。这份缓存独立于外层引擎镜像，会额外占用磁盘。

JSON 的 ServicePlan.Web 保存应用/站点身份、主机名、探测身份、原始绑定、显式/实际默认关系及发布引用；PortPlan 只保存物理映射。WebPackage 校验最终 INI 交接，WebIngress 规划路由/发布并渲染语义模板、收集资源。共享 BCL 校验器验证 JSON 关联。删除旧端口 scheme/hostname 字段和简单代理后备分支。

2026-10-05 的真实验收在 Windows/rootful Podman 中使用专用 Debian 13 x64 离线测试交付包，包含真实打包的执行器及隔离 Redis/MySQL/Nginx 镜像，不带生产应用配置或升迁。原安装脚本完成 bootstrap 和安装阶段；Windows 收到 HTTP 200、Redis PONG、MySQL 协议 10 握手。第二轮复用干净底图，避让已占用的本机 80 端口，从分配地址收到 HTTP 200，且不存在首轮写入的 Redis 键。两次 Ctrl+C 都删除了外层容器及各自记录的两个存储卷；同一应用的并发尝试被拒绝。

单元测试覆盖 Docker/Podman 编排、安装失败保留、启动取消（包括 create 中断）、端口绑定有界重试、独立清理令牌、清理失败报告、既有现场/架构预检、可选与必需入口、HTTP 404 及外部跳转。配合执行器既有升迁失败测试，本轮不执行真实 SQL 升迁。脚本检查使用隔离副本及假工具。

Ubuntu 22.04 仅在早期原型中验证了 systemd 启动。其它发行版、外层 Docker、rootless 引擎、真实 ARM64 及域名 HTTPS 应用尚未完成 run 全流程验收。两个原生执行器分别交叉编译，不能视为目标运行验收。验证底图显式安装当前原生执行器需要的 ICU；独立 Linux 现场仍需要执行器运行依赖。特权嵌套共享引擎内核，不能证明生产主机兼容性。

资源键采用点分隔，异常键以 .Message 结尾；中英文条目保持一致的多行 XML 格式，通过 ResXFileCodeGenerator 生成强类型访问器。最终制作端 236 项、执行器 60 项测试全部通过，Release 严格构建零警告且 IDE0049 独立检查通过；本机全局工具已更新，10 个安装后的二进制/资源文件哈希与构建输出一致。

本次修复让 Redis/Valkey 数据账号从固定镜像解析，即使镜像声明以 root 启动也会处理显式 data-owner。数字归属在部署前写入挂载记录，同时保留 purge 校验所需的归属标记。隔离 Debian 13 x64 交付包使用 Redis 8.10.2、持久存储及 AOF/RDB，已完成原始安装流程；数据目录及 appendonlydir 归属 UID/GID 999，本机 PING、SET 成功，Redis 容器重启后键值仍保留。没有执行生产应用或升迁。制作端命令使用所选执行程序；现场命令统一使用 BootstrapPlan.ENGINE。资源文本通过参数接收外层/内部引擎名及选项列表，Docker/Podman 连接失败建议按选择显示，run 不要求外层 Compose。两个 hosting 脚本只传递引擎选项，没有执行引擎命令。Release 严格构建、制作端 248 项及执行器 63 项测试、IDE0049 检查通过；两个原生执行器已重建，ARM64 仍仅交叉编译。

## 本机镜像源

制作端使用 Core Profile 读取最终输出目录中可选的 `.mirrors`。链接共享源码 `RegistryMirrors` 处理精确仓库映射、来源顺序、失败回退和取消。镜像解析将逻辑仓库及已验证的平台/摘要与实际下载来源分开，已验证的本地绑定可以在修改镜像源后继续复用；`ImagePlan.SourceRepository` 仅在制作期间使用，不进入协议 JSON。

Podman 构建使用已解析的本地镜像 ID，设置 `--pull=never`。Docker 使用临时 BuildKit 配置及独立 builder，按构建器宿主架构通过相同策略准备工具镜像，成功或失败后清除 builder、其缓存及临时配置。`run` 在交付资产之外的 `/run` 中向安装器传递已验证的 JSON，执行器仅在在线拉取时使用；普通现场安装不携带本机规则。不修改全局引擎配置或软件包源地址。

hosting 的 `.mirrors` 包含 [DaoCloud](https://github.com/DaoCloud/public-image-mirror) 列出的 11 个 Registry。已有别名保留，新增项使用其推荐的仓库前缀形式；旧 Kubernetes 及实验性 Ollama 入口均已注释说明。接受配置不代表所有仓库/内容都已通过实际下载验证。

## 依据及剩余发布门槛

最终全部制作端框架通过 Release 严格构建，制作端 304 项及执行器 76 项测试通过，四个项目的 IDE0049 独立检查通过。隔离安装验证 plan 即使遇到无效 `.mirrors` 也不读取它；工具包/原生/资源内容及经用户授权的全局安装均与本地构建哈希核对。

### 交付归档压缩对比（2026-10-06）

使用 hosting 输出目录当时唯一的 `zongsoft@26.10.5-x64.tar.gz`（958,123,274 字节），在 Windows x64、Intel Core i7-8700K、.NET 10.0.12 上比较 gzip 档位。先解压为完全相同的 2,497,001,472 字节 tar 输入；在系统临时目录交替执行 Optimal/Fastest，每档三次，保留正常文件缓存，计入输出刷新到磁盘的时间。SHA-256 校验在计时之外执行。以下采用中位数，MB 为十进制单位。

| 档位 | 压缩耗时 | 压缩大小 | 解压为 tar 耗时 |
| --- | ---: | ---: | ---: |
| Optimal | 36.98 秒 | 958.10 MB | 5.06 秒 |
| Fastest | 13.79 秒 | 1,196.22 MB | 6.43 秒 |

Optimal 三次压缩为 37.106、36.978、36.590 秒，Fastest 为 13.794、13.903、13.604 秒。Fastest 压缩耗时减少 23.18 秒（62.7%，速度约 2.68 倍），体积增加 238.12 MB（24.85%），解压没有加速。六次解压后的完整 tar 长度和 SHA-256 均与输入一致，原交付归档的 SHA-256 在测试前后相同。

另直接调用工具编译产物中的 `Files.Archive`，对原包解出的相同 86 个文件各制作一次：Optimal 为 35.95 秒、958,124,266 字节；Fastest 为 14.25 秒、1,196,221,435 字节。实际 `Files.Extract` 分别为 6.48 和 7.30 秒。两份重新生成的交付物均通过原有 `DeliveryBundle` 清单、长度及校验和验证。重新生成的 tar 元数据可能变化，因此整体归档不要求与原包字节相同。

依据用户频繁在本机制作和验证的用途，默认改用 Fastest，不新增选项、格式或压缩依赖；接受这一样本约 25% 的大小增长。该结论针对压缩阶段，本轮没有重新执行完整 make、run 或现场安装，不能宣称整个命令快 2.68 倍。公共运行环境缓存仍处于设计阶段，本次没有实现缓存策略变更。

本次通过全部制作端目标框架及执行器的 Release 严格构建、380 项现有测试，以及四个项目的 IDE0049 独立检查。另补充一处声明与 using 语句之间的空行以满足 ZS2003，没有改变该处运行行为。自动审批当时拒绝删除本次系统临时测速目录，仅返回 blocked by policy；随后操作者确认已手动删除测速副本。

### 镜像源及代理测速（2026-10-06）

经用户授权，在 Windows/rootful Podman、Debian 13 x64 上使用 919,052,946 字节的离线交付包，包含 hosting 的 Redis/MySQL/Nginx/RustFS 及 daemon/Web 安装包。采用临时应用身份与输出目录，原交付物保持不变，不带升迁。外层验证底图及制作端基础服务/系统镜像已缓存；每次 run 的安装状态、测试数据重新创建，仅内部基础镜像存储区分首次和重复运行。结束后移除临时代理覆盖，持久服务/脚本及镜像仓库配置保持原样。

| 操作，均配置 `.mirrors` | 虚拟机代理开启 | 虚拟机代理关闭 |
| --- | ---: | ---: |
| `make` 完整制作 | 195.9 秒 | 191.0 秒 |
| `run` 内部镜像缓存为空 | 212.8 秒 | 192.6 秒 |
| `run` 复用已校验镜像缓存 | 163.4 秒 | 151.2 秒 |

run 包含归档校验/解压，以显示就绪为终点，不计人工验证和退出清理（约 19–22 秒）。这些是单次观测，不构成性能保证。代理开启和关闭时，缓存分别节省 49.3 秒和 41.5 秒，因此保留。离线镜像导入、文件复制与健康检查不受仓库镜像源加速；make 仍通过原软件包源安装应用依赖。

独立 manifest 查询通过临时原生 CLI 配置绕过虚拟机已有镜像源，以免把引擎自动转发误算成源站访问。开启代理时 DaoCloud/Docker Hub 为 1.4/3.5 秒；关闭后 DaoCloud 在 2.2 秒成功，直接访问 Docker Hub 达到 20 秒超时。真实制作端在关闭代理时通过 DaoCloud 拉取未缓存的 Redis 夹具耗时 13.5 秒；切换到不可访问的新镜像源后，按已验证的逻辑身份复用只需 0.9 秒。hosting 扩充后的 11 条配置解析通过。实际归档包含双语 README，没有 `.mirrors`，交付计划没有镜像源地址。

清理检查发现，制作时检查数据账号的临时容器可能遗留镜像声明的匿名卷。共享构建资源删除已增加 `--volumes`，保留具名卷及绑定资产，仅删除该临时容器的匿名卷。成功与账号解析失败用例覆盖该行为，真实缓存 Redis 账号探针验证卷清单不变。本轮测试容器、隔离镜像存储、夹具镜像、基础服务临时标签及两个匿名辅助卷均已删除，原用户资源保留。x64/ARM64 原生载荷已重新编译，ARM64 与外层 Docker 仍不属于真实运行验收范围。

执行审批层拒绝删除系统临时测速目录，仅返回 `blocked by policy`，未说明具体原因。因此复制的归档、隔离工具及诊断文件保留，需人工删除；本轮引擎测试资源已成功清理。

### 重复运行缓存及 RustFS 控制台（2026-10-06）

RustFS 模板默认发布回环地址的 9001 控制台端口，复用通用 `console-port` 覆盖参数。EMQX、NATS、ClickHouse、OTLP HTTP 及 Nacos 补充辅助端口声明，仍由操作者选择发布。三个 RustFS 默认/关闭/自定义用例及七个辅助端口用例覆盖共享设置行为。

独立的 356 MB Debian 13 x64 交付包包含 Redis、RustFS 和 Nginx Web 夹具，在 Windows/rootful Podman 下以相同应用名、相同交付版本连续运行两次。内部引擎报告 overlay2；Web 返回 HTTP 200，RustFS 的 `/rustfs/console/` 返回 HTTP 200/HTML，端口根路径则返回 S3 403。含解包/校验的两次就绪耗时为 51.5 秒和 43.0 秒，不据此预测生产包耗时。第二次复用已校验的 Redis/RustFS 镜像 ID，Web 镜像重新导入；第一次写入的 Redis 数据在第二次不存在。每次退出后只读检查缓存，确认恰好保留两个当前基础服务镜像 ID，没有容器记录及数据卷目录，并存在干净标记。测试容器及夹具缓存已删除，外层卷清单保持一致；没有运行生产应用或升迁。

回归覆盖引擎配置变化、未清理缓存、外来/占用卷、创建中断、失败后整份丢弃、删除前重新检查归属、纯应用空缓存及同版本内容变化。模拟镜像清理超时后，仍用独立令牌删除外层容器。Release 严格构建、制作端 271 项/执行器 73 项测试及 IDE0049 检查通过；两个 Linux 执行器已重建，ARM64 仍仅交叉编译。本机全局工具已从本地包重新安装，十一项已安装二进制/资源/模板哈希与构建输出一致；旧的闲置 VFS 验证底图在检查归属及占用后删除。每次仍执行归档校验、资产复制、bootstrap 安装及健康检查，加速不会跳过这些验收步骤。

本轮新增默认/plan/make 入口，复用 Core Profile、ConnectionSettings、共享变量及 ArtifactPublisher。完成 `.settings`、repository、草稿/完成阶段、摘要身份校验、参数级合并、显式空值、逐值求值、临时卷、端口控制及 Redis/Valkey 设置。新增回归覆盖无引擎 plan、缺值告警、草稿补全/失败保留、特殊字符回放、Nginx 包片段交接，以及执行器的 --no-recreate 和卸载匿名卷命令。临时数据跨重启/卸载的真实引擎验收仍待完成。两个 hosting 默认文件和脚本已迁移，18 项 CMD 检查全部在隔离目录、假工具中进行，不触碰真实服务。

应用不使用模板，按包内有效 `Listen` 元数据生成监听响应检查；元数据缺失或为空时检查 PID 1 存活。`PackageReader` 读取 tar PAX `Listen`、Debian control `Listen` 和 RPM 应用字符串标签 `1000001`；非空无效值报错。推导宿主绑定全部接口，探测使用容器回环地址。HTTP/HTTPS GET `/` 要求收到 HTTP 响应，不以状态码推断业务健康，也不跟随重定向；HTTPS 使用包内 DNS 身份、SNI 和系统信任库。自动检查安装 curl/CA 依赖，包括自包含宿主；RHEL 系列保留已有 curl 提供者，例如 curl-minimal。监听及配置规则在制作端展开。交付与安装记录统一使用协议 1，执行器最小/最大版本与其一致；基础设施模板语法独立保持版本 1。拒绝其它交付 schema，不提供兼容分支。

TASK#2 重构后，套件通过制作端 207 项及执行器 34 项测试。解决方案全部项目通过 Release 严格构建，四个来源/测试项目通过独立 IDE0049 检查。测试覆盖忽略配套应用模板、拒绝应用模板字段、包元数据、日期版本、tag/默认值优先级、缓存身份与固定摘要回放、平铺交付发布及回滚、离线 APT 缓存及镜像导入/重新标记。此前 packager 集成检查在临时目录生成 tar/deb/rpm 并用原生工具查询元数据，没有安装包；回环探测覆盖 HTTP 响应、TLS 信任/SNI、超时及连接拒绝。

决策 35 对制作端镜像／容器使用每次独立的资源名称，并按镜像 ID 导出。应用成品在发布后清理，失败／取消也使用不受取消影响的有限超时清理已登记资源。应用与 bootstrap 共用 ContainerEngine.BuildAsync：Podman 使用 --layers=false 和 --force-rm；Docker 创建独立 Buildx docker-container 构建器，载入成品后删除构建器及缓存卷，不改变默认构建器。清理警告保留原始结果并继续其它清理，共享基础服务／系统底图及其它构建器保留；不执行全局 prune 或强制删除镜像。此前只检查命名镜像，遗漏了 Podman 中间缓存镜像；修正后的真实验证对比全部镜像 ID，并检查 external 构建容器。

本地验证包括制作端全部目标框架严格编译，解析/路径/元数据测试，以及假宿主上的生命周期测试：升迁恢复、已校验的发行配置资产、停机前失败、清理重试；另覆盖安全解包与未知协议字段拒绝。Native AOT 已生成 x64、ARM64 ELF；x64 在专属 Rocky 构建容器中执行了协议入口。ARM64 是交叉编译，不是硬件运行验收。

TASK#1 本地 NuGet 制包和内容检查确认包含两个共享原生文件、对应中文资源及 30 个基础设施/入口模板，没有按目标框架重复收录，不含 YamlDotNet 或运行时分析器载荷；制作端帮助入口运行通过。此前验证了隔离工具安装、已安装原生文件哈希、已取消的 `--bundle` 选项拒绝行为、Shell/bootstrap 导入语法及 14 项 hosting CMD 隔离检查，覆盖共享引用、引号、调用方状态和失败退出码。

决策 35 的工具包检查还修正了 NuGet 对无扩展名原生执行器的目录处理：执行器位于 `.containerizer/linux-{x64,arm64}/containerizer`，各自在旁边保留一层 `zh-Hans`。本地工具包及用户明确要求更新的全局安装，其制作端、原生执行器和资源文件哈希均与构建结果一致，已安装制作端的帮助入口运行通过。这次安装检查没有执行节点制作或现场生命周期操作。

TASK#1 实施前的 2026-10-04，真实 hosting `containerize.cmd` 已成功完成制作，配置为 Redis、MySQL、daemon、Web、Ubuntu 22.04 x64、自动引擎、离线 bootstrap/镜像及原始 `.migration`。保留的交付物为 `zongsoft@26.10.4.3_ubuntu-22.04_x64`，序号用于保留此前产物。清单记录完整 source/output 路径及基础设施实际版本。节点锁中 113 个资产的长度和哈希全部吻合，升迁归档/脚本与输入字节一致，应用没有模板身份或哈希。

生成归档在专属 Ubuntu 22.04 x64 systemd 容器中安装成功；目标禁用外网，起初没有 Docker。Bootstrap 从 67 个锁定的本地包完成安装，Podman 导出的镜像通过镜像 ID 校验后由 Docker 导入并重新标记。嵌套 Docker 在验证夹具中采用 VFS。原始升迁除四个所选组件外还需要本地 S3 端点，因此另设隔离 RustFS 夹具并共享目标网络命名空间。升迁一次成功，登记状态为 `Installed` 且没有维护标记，四个服务均 healthy。Redis 返回 PONG，升迁数据库包含 33 张表，Web `/Application` 返回 HTTP 200。daemon 检查 PID 1 存活，Web 使用元数据推导的回环监听检查。归档、清单及验证记录保留在 hosting 输出目录供人工查看。这证明该隔离组合，不等同于纯净虚拟机或其它发行版/架构验收。

| 组合或验收项 | 状态 |
| --- | --- |
| 制作端单测、执行器模拟事务 | 本地已验证，可用下述命令复现 |
| 决策 35 制作端资源清理 | Podman 重复构建／导出、失败、取消后镜像清单不变已实测；Docker 独立构建器生命周期通过模拟验证，真实 Docker 待验收 |
| Linux x64 AOT 构建及构建容器内协议启动 | 已验证 |
| Linux ARM64 AOT 构建与 ELF 架构 | 已验证；运行待验收 |
| Podman/Docker 导出至 Docker 导入/启动往返 | 四个 hosting 服务在隔离 Ubuntu 22.04 x64 完成 Podman 导出至 Docker 导入/启动 |
| 纯净断网 Ubuntu、Debian 12/13、RHEL、Rocky、Alma，两个架构 | 待验收 |
| 真实 packager daemon/Web 及 Automao 安装包 | hosting daemon/Web 制作及隔离安装已验证；Automao 待验收 |
| 内置模板对应锁定上游镜像版本 | 所选 Redis/MySQL 摘要在本隔离组合已验证；其它组合待验收 |
| 包内配置保留及安装介质排除 | 变更后须隔离镜像验证 |
| 掉电、磁盘满、维护/清理过程中引擎或宿主重启 | 待真实故障注入；模拟事务覆盖部分失败路径 |
| 真实数据库、部分 SQL 失败及跨节点实施流程 | 原始 hosting 升迁在隔离 MySQL/S3 夹具成功；部分 SQL 失败及跨节点流程待验收 |

未通过相关门槛的发行版/模板/平台组合不声明为发布支持。RHEL 导入必须来自对应授权仓库并独立验证依赖闭包，元数据/哈希吻合不构成此项证明。按[模板契约](templates.zh-Hans.md)移除原始 YAML 输入。此前 hosting 安装证据对应旧制作流程，不构成 TASK#1 的现场验收。

发布前需评审的工程限制：文件系统状态写入和 Docker 重启策略变更不能组成一个原子事务，边界上的宿主突然掉电仍需真实启动/恢复验证；状态采用原子替换，但目录项在掉电时的持久性尚未证明。模板健康检查依赖上游工具和默认布局，每个准入镜像摘要都需复核。并非所有 packager 布局都能可靠推断启动方式，有歧义的包须在上游修正。

本次结构、终端及版本整理通过 10 项隔离制作命令检查，包括重定向纯文本；Web 交接改动后的托管执行器协议入口输出 minimum=maximum=1。当前本地工具包及全局安装已包含最新制作端与 Linux Native AOT 载荷，并将安装文件哈希与本地构建产物核对。

Core 的 `Version.Number` 到 long/ulong 转换已改为显式转换，避免文本输出误选数字重载；Core 在 net8.0/net9.0/net10.0 各通过 62 项 Versioning 测试，容器化制作端 Debug 联合编译及 207 项测试使用本地新 Core。Release 仍按项目配置引用 NuGet Core 包。

本次删除应用配置外置，通过 Windows 制作端 214 项、执行器 34 项测试；执行器测试也在隔离 Linux 构建容器中通过，包含只读资产权限检查。使用真实 Podman 为临时小型 tar 安装包制作镜像，确认 JSON、插件选项、证书和 Nginx 配置在最终镜像内字节不变，应用没有配置挂载，Nginx 副本与原文件相同，安装输入及日志已移除，当时完成命名镜像/容器清理，后续发现无标签阶段镜像残留，已按上文修正缓存清理。Release 严格构建及独立 IDE0049 检查通过。两个架构的 Native AOT 执行器已重建，x64 协议入口通过；ARM64 仍是交叉编译，没有硬件运行验收。本次未安装节点交付物或操作现有服务。

本次缓存清理修正通过制作端 221 项及执行器 34 项测试、解决方案全部项目 Release 严格编译及独立 IDE0049 检查。隔离的真实 Podman 夹具连续两次构建／导出，并分别验证故意失败及取消；每次完整镜像 ID 清单均恢复原状，external 构建容器无残留。本机未安装 Docker 命令，Docker 通过模拟验证私有构建器创建、缓存归属、失败与取消。小型嵌套 Package、Paths、Component、Descriptor 模型已并入所属类型文件，API 和 JSON 结构保持不变；共享源码整理后重新构建两个原生执行器。

## 复现本地检查

```powershell
dotnet build Containerizer.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test Containerizer.slnx
dotnet format style src/Zongsoft.Tools.Containerizer.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style executor/src/Zongsoft.Tools.Containerizer.Executor.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style test/Zongsoft.Tools.Containerizer.Tests.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style executor/test/Zongsoft.Tools.Containerizer.Executor.Tests.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

使用 `executor/build/containerizer.linux-x64.yaml` 创建专属构建 Pod，再执行 `setup.sh`、`publish.sh <RID> <configuration>`；编译器只安装在该构建环境内。各次发布在输出旁保存编译日志和 ELF 依赖报告。`build/Import-Bootstrap.ps1` 仅把审阅过的依赖集合转换为本地配置档，不运行包。

发布、真实包安装、服务控制、升迁和破坏性生命周期验收需要明确的目标环境；本地验证不得把用户现有服务当成测试夹具。

补充：入口服务重建时更新临时匿名卷，随后只清理由旧受管容器捕获、已经不再挂载的卷；没有重建则不清理。选项语义参见 [Docker Compose up](https://docs.docker.com/reference/cli/docker/compose/up/)。本轮工具包检查确认 30 个模板、两个原生执行器和单层中文资源；已按用户要求安装到全局工具并核对安装哈希，两个实际 hosting source 的 plan 在临时输出目录通过验证，未执行现场安装。
