[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# 实现与验证记录

本轮基线为 [TASK#2](../TASK%232.md)，与 PLAN/TASK#1 冲突时以本轮为准，不保留旧格式兼容。本文记录实现边界、验证证据及尚未完成的运行验收。

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

制作端记录有效服务配置哈希并保护 Compose 资产。packager 按 hosting/.deploy/<scheme>/ 选定的应用配置保留在应用镜像内。应用只构建一次最终镜像，从清理后的安装文件系统复制运行内容，保留应用配置，移除安装介质、包脚本数据库及宿主服务定义。声明 nginx 依赖时仅复制包中 .web/nginx/*.conf，应用镜像中的原文件仍保留。模板/入口资产直接从已校验的发行目录只读挂载，不再创建可编辑的运行副本、compose.env 或三方比较。正常完成、可处理失败和取消时清理制作工作目录及本次引擎资源。

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
| 执行器 | [DeliveryBundle](../executor/src/DeliveryBundle.cs) 校验并打开交付资产；[ExecutorArguments](../executor/src/ExecutorArguments.cs) 解析命令；[IInstallationHost](../executor/src/IInstallationHost.cs) 定义安装宿主边界。`DockerHost.Bootstrap/Services/Storage.cs`、`InstallationManager.Installation/Migrations/Recovery/Transaction/Uninstall.cs` 分开宿主机操作与事务阶段 |

普通字符串采用插值表达式，分行生成 Dockerfile 使用 `StringBuilder`；数组初始化采用集合表达式，需要类型推断的地方显式声明目标类型。

复杂条件按职责拆分：端口冲突、镜像健康检查、受管目录边界使用有业务含义的判断方法；两端共用 `Files.IsLinuxPath` 校验 Linux 路径，制作端共用 `Distribution.IsDebian` 判断安装包体系。资源键和 JSON 属性名保持不变，交付计划文件命名为 `containerizer.json`；制作端与执行器共用 `DeliveryPlan.FileName`，协议仍为 1。

制作端的发行/日期版本、升迁排序去重及 .NET 运行时选择使用 Core `Versioning.Version.Number`。各数字段范围为 0–65535，`1.0` 与 `1.0.0` 等形式按数字相等比较，但保留显式发行/升迁版本的文本。镜像 tag 仍为字符串。执行器保留 BCL 版本处理及源生成 JSON，不引入 Core 依赖。

制作端通过 `CommandContext.Output`、`Terminal`、`CommandOutletContent` 输出颜色/样式及分行帮助，由 Terminal 处理重定向纯文本；原本写入 stderr 的诊断改用 `Terminal.Default.Error`。共享子进程转发及仅依赖 BCL 的执行器保留原有输出流。

## FHS 存储与清理

[README 的目录布局](../README.zh-Hans.md) 将服务数据及持久安装/升迁状态放入 `/var/lib/containerizer`，尝试日志放入 `/var/log/containerizer`，可重新获取的 bootstrap 资产放入 `/var/cache/containerizer`，锁放入 `/run/containerizer`。日志与缓存复用已有目录归属记录及标记。Bootstrap 在副作用前向当前安装对象登记归属，后续保存事务不会丢失该记录；已删除缓存可以按原归属重建。普通卸载保留这些资产，purge 校验身份、链接和挂载边界后清理，注册记录最后删除。默认服务数据不能指向其它应用命名空间或受管状态、日志、缓存、运行期根目录。只有一个数据挂载时直接使用服务目录；模板有多个数据挂载时保留各自命名的子目录。因此 MySQL 的宿主路径为 `/var/lib/containerizer/data/<name>/mysql`，容器内仍挂载到 `/var/lib/mysql`。只读配置挂载不计入数据挂载数量。不自动搬迁已安装的现场数据。

FHS 与命名重构通过 Windows 制作端 221 项、执行器 60 项测试；隔离 Linux 构建容器中全部 60 项执行器测试通过，包括链接及挂载边界检查。全部制作端目标框架通过 Release 严格编译，并单独验证 IDE0049。这些属于文件系统、事务及构建验证，不代表现场安装验收。

## 依据及剩余发布门槛

本轮新增默认/plan/make 入口，复用 Core Profile、ConnectionSettings、共享变量及 ArtifactPublisher。完成 `.settings`、repository、草稿/完成阶段、摘要身份校验、参数级合并、显式空值、逐值求值、临时卷、端口控制及 Redis/Valkey 设置。新增回归覆盖无引擎 plan、缺值告警、草稿补全/失败保留、特殊字符回放、Nginx 包片段交接，以及执行器的 --no-recreate 和卸载匿名卷命令。临时数据跨重启/卸载的真实引擎验收仍待完成。两个 hosting 默认文件和脚本已迁移，18 项 CMD 检查全部在隔离目录、假工具中进行，不触碰真实服务。

应用不使用模板，按包内有效 `Listen` 元数据生成监听响应检查；元数据缺失或为空时检查 PID 1 存活。`PackageReader` 读取 tar PAX `Listen`、Debian control `Listen` 和 RPM 应用字符串标签 `1000001`；非空无效值报错。推导宿主绑定全部接口，探测使用容器回环地址。HTTP/HTTPS GET `/` 要求收到 HTTP 响应，不以状态码推断业务健康，也不跟随重定向；HTTPS 使用包内 DNS 身份、SNI 和系统信任库。自动检查安装 curl/CA 依赖，包括自包含宿主；RHEL 系列保留已有 curl 提供者，例如 curl-minimal。监听及配置规则在制作端展开。交付与安装记录统一使用协议 1，执行器最小/最大版本及现有模板协议号均为 1，拒绝其它 schema 值。

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

本次结构、终端及版本整理通过 10 项隔离制作命令检查，包括重定向纯文本；托管执行器协议入口输出 minimum=maximum=1。当前本地工具包及全局安装已包含最新制作端与 Linux Native AOT 载荷，并将安装文件哈希与本地构建产物核对。

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
