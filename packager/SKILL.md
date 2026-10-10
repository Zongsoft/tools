---
name: zongsoft-tools-packager
description: 修改打包命令、应用版本管理、载荷路径、三种包格式和安装生命周期。
---

# Packager 开发流程

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[实现说明](docs/implementation.zh-Hans.md)。

- 命令与版本：PackCommand*、PackageOptions、共享 Utility 中的来源组合及 Core TemplateEvaluator。直属 .edition/ApplicationManifest 优先，缺失才读 .version/ApplicationIdentifier；清单选择显式 Edition、Current、唯一 Edition 或顶层版本。成功后清单输入成组回写 .edition 和 .version（缺失则创建），持久化本次 Current 和最终标识；仅标识输入只保存 .version；两者均无则成组创建，不覆盖并发创建的文件。双文件失败回滚。源清单不入包，包内只生成最终单行标识。
工具通过共享 Utility 显式启用 TemplateEvaluatorOptions.Fallback；同一命名空间按来源顺序查询，全部未找到才逐级进入父命名空间及全局，最后查询来源已声明的默认值。原始值读取、递归模板和指令参数评估使用同一设置。Profile 变量视图不自行展开模板。

- 变量按显式选项、近层至远层 `.env`、系统环境的顺序查询，全部缺失后再查询描述符默认值；共用 Utility/Profile.Load，章节层级以点号连接为命名空间，条目名中的点号和连字符改为下划线。先用环境和选项固定 source，再加载 `.env`；不搜索子目录、不以 `.env` 重定位 source，保留显式身份与源版本文件规则。
- 所有变量（包括 `framework`）统一遵循首个命中生效：null、空字符串、false 和 0 都不会触发下层回退。只有未提供该值时才继续查找；需要有效值的业务操作负责校验并在无法继续时报告错误。
- 主页使用 `--homepage`/`homepage`；厂家使用 `--manufacturer`/`manufacturer`，null/空字符串默认 `Zongsoft`，保留纯空白边界。三格式分别写 tar PAX `Manufacturer`、Debian `Manufacturer`、RPM `VENDOR`(1011)；维护者独立保存，`--maintainer` 默认 `Zongsoft`。
- 包模型与编码：Package*、Generator*；长度、对齐、校验和、字节序为精确契约。
- 依赖：Dependency 解析统一 name[:range]，保留原生端点、开闭边界及 OR 分组；[v) 等同 [v,)，区间内逗号不可分组。Debian 用分配律展开替代项（单组最多 1024 个关系组），RPM 用 with/or 与 RichDependencies 能力；~ /^ 端点声明 TildeInVersions / CaretInVersions。版本比较交给目标系统，虚拟提供者按各平台原生语义；其他关系字段不复用区间转换。
- 安装及服务：ApplicationHost 固定共享的宿主/listen 结果，Scriptor.Systemd 组合生命周期；Generated 宿主的有效 Listen 同时写入三格式包头，既有服务、禁用宿主及空值不声明，不增加元数据载荷文件；推演安装、升级、覆盖和最终卸载，保留 Debian/RPM 阶段差异。
- Web 托管：Web/Definition、Configurator、Installation；严格导入、后端整组替换和有效值求值顺序不可颠倒。生成 .web/nginx 配置，Delivered 独立于生命周期和激活开关；测试采用临时目录与 Nginx/systemctl 替身，不运行真实安装。字段、模块要求和容器化约定见 [Web 指南](docs/web.zh-Hans.md)。
- 外部升迁：Migration.cs；按名称和最终应用身份定位 `<name>[-<edition>](migrate)@<version>_<RID>` 原样产物，name 原样使用，Edition 可省略，安装调用其脚本。变量展开后，裸名称从 source 逐级查到根，每层先查目录本身再查直属 `.migration/`；带 / 或 \ 的路径只查显式目录，./ 可限定源目录。两文件都缺失才查下一位置，半套或元数据无效即失败，不跨目录拼配；测试覆盖查找起点、最近目录、终止条件和三格式原样收录。生成/执行逻辑归独立 [migrator](../migrator/SKILL.md)。
- 搜索与文本：Utility.Search 调用 [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) Searcher；TextSource 解释 file:/text:，pre/post 为文件列表。

变更先定位职责，保留用户修改。通用变量来源组合通过 tools/.shared 源码链接，升迁协议仍仅通过产物交接。新增诊断用双语资源并运行 ResXFileCodeGenerator。遵守 CRLF/Tab、版权头和区域分类规范；测试不编写本地化断言。

运行本工具回归、全 TFM 严格构建和 IDE0049 verify。使用临时输入检查三格式路径、权限、来源元数据与启动门禁；不执行真实安装脚本。独立 migrator 以产物为边界，不增加项目引用、升迁协议共享源码或原生产物收集。更新 README 与实现说明。
