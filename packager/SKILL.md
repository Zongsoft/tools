---
name: zongsoft-tools-packager
description: 修改打包命令、应用版本管理、载荷路径、三种包格式和安装生命周期。
---

# Packager 开发流程

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[实现说明](docs/implementation.zh-Hans.md)。

- 命令与版本：PackCommand*、PackageOptions、共享 Utility 中的来源组合及 Core TemplateEvaluator。直属 .edition/ApplicationManifest 优先，缺失才读 .version/ApplicationIdentifier；清单选择显式 Edition、Current、唯一 Edition 或顶层版本。成功后清单输入成组回写 .edition 和 .version（缺失则创建），持久化本次 Current 和最终标识；仅标识输入只保存 .version；两者均无则成组创建，不覆盖并发创建的文件。双文件失败回滚。源清单不入包，包内只生成最终单行标识。
工具通过共享 Utility 显式启用 TemplateEvaluatorOptions.Fallback；同一命名空间按来源顺序查询，全部未找到才逐级进入父命名空间及全局，最后查询来源已声明的默认值。原始值读取、递归模板和指令参数评估使用同一设置。Profile 变量视图不自行展开模板。

- 变量按显式选项、source 直属 `.env`（含显式导入）、系统环境的顺序查询，全部缺失后再查询描述符默认值；共用 Utility/Profile.Load，章节层级以点号连接为命名空间，条目名中的点号和连字符改为下划线。先用环境和选项固定 source，再加载 `.env`；不搜索子目录、不以 `.env` 重定位 source，保留显式身份与源版本文件规则。
- 所有变量（包括 `framework`）统一遵循首个命中生效：null、空字符串、false 和 0 都不会触发下层回退。只有未提供该值时才继续查找；需要有效值的业务操作负责校验并在无法继续时报告错误。
- 主页使用 `--homepage`/`homepage`；厂家使用 `--manufacturer`/`manufacturer`，null/空字符串默认 `Zongsoft`，保留纯空白边界。三格式分别写 tar PAX `Manufacturer`、Debian `Manufacturer`、RPM `VENDOR`(1011)；维护者独立保存，`--maintainer` 默认 `Zongsoft`。
- 包模型与编码：Package*、Generator*；长度、对齐、校验和、字节序为精确契约。
- 依赖：Dependency 解析统一 name[:range]，保留原生端点、开闭边界及 OR 分组；[v) 等同 [v,)，区间内逗号不可分组。Debian 用分配律展开替代项（单组最多 1024 个关系组），RPM 用 with/or 与 RichDependencies 能力；~ /^ 端点声明 TildeInVersions / CaretInVersions。版本比较交给目标系统，虚拟提供者按各平台原生语义；其他关系字段不复用区间转换。
- 安装及服务：ApplicationHost 固定共享的宿主结果，Scriptor.Systemd 组合生命周期；监听由运行配置提供，服务启动参数保留原义。包级绑定的字段、承载及当前实现边界见 [文件规范](docs/implementation.zh-Hans.md#binding-document)。推演安装、升级、覆盖和最终卸载，保留 Debian/RPM 阶段差异。
- 应用绑定：BindingExtractor 使用最终安装路径的只读 IFileProvider，由 Core 处理逐文件配置及显式 Import；PhysicalPath 为空，包装条目流，避免 Core 把 FileStream 当作制作机来源。/ 指虚拟安装文件系统根，相对路径基于声明文件，排除与别名已生效。导入结束后再求值有效 zongsoft.tools.binding PI，使用独立且关闭递归/回退的 TemplateEvaluator、显式 this 视图及 Settings 取键；FilterExpression 共用过滤语法。逐声明检查 required，再按相交端点传递合并同类替代地址及别名，发布前校验文档与容量。提取完成后由 Web/Routing 按请求类别组合站点，Writer 以 binding/endpoint 身份生成容器模板；最后统一写入根 JSON。
- Web 托管：Web/Definition、Configurator、Installation；严格导入、后端整组替换和有效值求值顺序不可颠倒。生成 .web/nginx 配置，Delivered 独立于生命周期和激活开关；测试采用临时目录与 Nginx/systemctl 替身，不运行真实安装。字段、模块要求和容器化约定见 [Web 指南](docs/web.zh-Hans.md)。
- 外部升迁：Migration.cs；按名称和最终应用身份定位 `<name>[-<edition>](migrate)@<version>_<RID>` 原样产物，name 原样使用，Edition 可省略，安装调用其脚本。变量展开后，裸名称从 source 逐级查到根，每层先查目录本身再查直属 `.migration/`；带 / 或 \ 的路径只查显式目录，./ 可限定源目录。两文件都缺失才查下一位置，半套或元数据无效即失败，不跨目录拼配；测试覆盖查找起点、最近目录、终止条件和三格式原样收录。生成/执行逻辑归独立 [migrator](../migrator/SKILL.md)。
- 搜索与文本：Utility.Search 调用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) Searcher；TextSource 解释 file:/text:，pre/post 为文件列表。

变更先定位职责，保留用户修改。通用变量来源组合通过 tools/.shared 源码链接，升迁协议仍仅通过产物交接。新增诊断用双语资源并运行 ResXFileCodeGenerator。遵守 CRLF/Tab、版权头和区域分类规范；测试不编写本地化断言。

运行本工具回归、全 TFM 严格构建和 IDE0049 verify。使用临时输入检查三格式路径、权限、来源元数据与启动门禁；不执行真实安装脚本。独立 migrator 以产物为边界，不增加项目引用、升迁协议共享源码或原生产物收集。更新 README 与实现说明。

输出逻辑统一复用仓库 `.shared/Utility.Output.cs`：FormatOutput/FormatError 负责格式化，WriteOutput/WriteError 负责终端与文本写入器分流，HandleFailure 负责命令异常输出和已处理标记；各工具只决定退出码和业务诊断，不另建相同的包装层。用 Core CommandOutletContent 分段保存正文和参数：警告为暗黄色正文配粗体青色参数，错误为暗红色正文配粗体紫色参数。追加内容时从 Last 节点追加。Terminals 负责颜色；重定向和调用方提供的错误写入器保留原通道及纯文本；不要把 ANSI 序列写入报告或诊断回调。

帮助语法统一使用共享 FormatHelp/HighlightSyntax，计时统一使用 MeasureOutput，不在各工具复制正则、计时器或颜色拼接。帮助入口在业务解析之前处理，返回 0 且不读取 .env、不写入产物；测试须验证重定向为纯文本、所有语法片段保留，以及耗时仅输出一次。计时文案由各工具资源提供，资源访问属性通过 ResXFileCodeGenerator 生成。

<a id="binding-validation"></a>

## 绑定开发与验证

先阅读 [文件契约](docs/implementation.zh-Hans.md#binding-document) 和 [代理与运行端语义](docs/implementation.zh-Hans.md#routing-runtime)。绑定提取使用独立 TemplateEvaluator（InvariantCulture、Recursive=false、Fallback=false），只注册 this 视图；Resolving 适配配置导航和显式 Settings 索引。Core 继续负责表达式、XML、Import 和 Settings 语法。共享 FilterExpression 负责过滤，TFM 专用比较留在 deployer。

| 改动范围 | 应检查的输入与结果 |
| --- | --- |
| 指令/导入 | 完整指令名、文件级/元素内位置、完整加载后求值、相对/绝对虚拟路径、排除/重命名、可选缺失、循环/越界、不同独立文件和导入覆盖分别贡献。 |
| 参数/模板/过滤 | 缺失与显式空值、未知/重复属性、XML 单次实体解码、精确项/直接子项/根、相对/根导航、字面/动态 Settings 索引、部署模板转义、左到右逻辑组合、required 的零结果与错误边界。 |
| 规范化/身份 | IPv4/IPv6/zone/IDNA、HTTP 与 OPC 路径、组合逆序/简写/泛称归并、输入重排、Unicode 固定编码、端点交集合并、别名歧义、ID 重算和跨 binding 错配。 |
| 文件/包 | 有效空文档、1 MiB 边界、深度 32、缺失/null/未知/重复字段、保留路径冲突、普通文件/重复/链接、三格式同字节、0644/epoch、大小/文件表/校验和。 |
| 代理 | HTTP/gRPC 分类、同范围冲突、最长路径、原始 query/大小写/百分号/点段、400/405/404/状态 12、替代地址与远程池、Host/SNI/CA、最终原生覆盖、请求流/响应流/trailers/取消/deadline/大消息。 |
| 容器消费 | auto/none/显式映射、别名与 ID 重复覆盖、TCP/UDP 同号、地址族、回环与 IPv4 网络可达性、源端点与连接引用、显式健康命令及默认进程检查。 |
| 运行协作 | Kestrel 同输入投影、同 socket 及跨插件冲突、区域与原生 RPC 精确匹配、区域反射、普通路由约束、OTLP 三信号实际到达、直接 GrpcChannel 前缀仅一次与处理链释放。 |

现有回归入口包括 BindingsDocumentTest、WebRoutingTest、WebBindingsTest、WebPolicyTest，以及 Containerizer 的 ApplicationBindingsTests、BindingProtocolTests、ListenerPackageTests。按目标项目执行严格构建、IDE0049 和既有测试；新增行为需要测试时遵循项目测试流程。对生成的 Nginx 配置先做隔离 `nginx -t`，再做实际请求；HTTP/3 的 QUIC/TLS 入口、HTTP 上游、gRPC 流分别验收。包检查使用读取工具，不安装真实系统包或启停现场服务。运行日志放在隔离证据目录，不写回维护文档作为阶段记录。
