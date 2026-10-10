---
name: zongsoft-tools-migrator
description: 修改独立升迁输入、SQL 批次、原生执行、产物命名和 AOT 构建。
---

数据库修改同时核对 [完整参数及默认值](README.zh-Hans.md#database-configuration)：provider/库/用户层级、默认库隐式声明、空段引用、仅引用库入计划、四阶段初始化和授权、既有设置/密码保持、pending恢复。`.shared/MigrationPlan.Database.cs` 存放初始化描述，任务通过 DatabaseIndex 引用其数组位置（从零开始）；SQL去重按声明来源和实际目标。[Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) 仍负责导入及覆盖，声明事件只补充跨库顺序与空段来源。S3保持独立原有路径。

# Migrator 开发流程

协议调整须同步生成端与执行器：计划名称使用 Name，状态文件对应 name；Steps 无独立 Id，Settings 保存连接设置，Database 无 Id，Step.DatabaseIndex 为可空整数，Amazon S3 省略；状态使用 phase/step/databaseIndex 定位。数据库索引必须在 Databases 数组范围内且 provider 匹配；索引 0 必须序列化。从 Steps/Scripts 数组顺序执行，SQL 文件名为不补零的序号。序列化与指纹共享全局 WhenWritingNull，不能删掉 Source/Content 等无条件忽略注解。

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[升迁指南](README.zh-Hans.md#package-phase)和[实现说明](docs/implementation.zh-Hans.md)。

版本来源由私有 VersionSource 解析，数字 --version 优先；省略/空白和显式目录只查直属 .edition/ApplicationManifest，缺失才读 .version/ApplicationIdentifier。显式文件名以 .version 结尾时按标识读取，其余按清单。产物 Edition 只来自显式非空 --edition，环境和 .env 不参与；未指定时仅借用 Current、唯一 Edition 或顶层版本的版本号，产物无 Edition。多个且无 Current 时要求选择，显式 Edition 按清单匹配；标识文件允许显式 Edition 覆盖。name 独立必填；源文件始终只读，错误保留完整路径。最终版本与 Edition 回填后建立本次变量视图，失败不改已有产物。

输入处理在 src/MigrationLoader* 与 MigrationProfile，生成文件集在 MigrationBundle，归档和脚本在 Generator。协议及参数描述在 .shared。数据库/Amazon S3/锁/状态在 executor/src。保持这些边界；packager 只消费产物。

归档根目录直接收录计划 migration.json、指纹 id、内部入口、原生执行器及依赖，SQL 路径为 .artifacts/<provider>/<序号>.sql；不套 .migration 目录。生成端、双平台启动脚本及执行器使用同一布局；执行器以计划文件所在目录为根，保留 .artifacts 路径边界及校验和检查。外部启动器使用独立临时目录并清理，默认状态目录和 packager 的安装根 .migration 收纳目录独立于归档布局。

工具通过共享 Utility 显式启用 TemplateEvaluatorOptions.Fallback；同一命名空间按来源顺序查询，全部未找到才逐级进入父命名空间及全局，最后查询来源已声明的默认值。原始值读取、递归模板和指令参数评估使用同一设置。Profile 变量视图不自行展开模板。

通用变量按显式选项、近层至远层 `.env`、系统环境的顺序查询，全部缺失后再查询描述符默认值，先通过共享 Utility/Profile.Load 加载再解析版本来源；章节层级以点号连接为命名空间，条目名中的点号和连字符改为下划线，同次命令全部输入共用变量。连接参数使用 `<输入名>.ini` 或 `<provider>.ini`，保持就近选择完整配置及显式导入规则，自动查找。迁移示例、测试与文档时同步导入路径，不能把通用 `.env` 改成 `.ini`。

所有变量（包括 `framework`）统一遵循首个命中生效：null、空字符串、false 和 0 都不会触发下层回退。只有未提供该值时才继续查找；需要有效值的业务操作负责校验并在无法继续时报告错误。

核对两端名称契约：外部产物为 `<name>[-<edition>](migrate)@<version>_<RID>`，name 原样使用；计划名称及默认状态目录保留既有升迁后缀规范化规则。Edition 可省略，版本与 RID 明确。脚本和归档必须同前缀，check 不解压，状态跨版本保留，外部调用可覆盖 state。已有输入无效不能按缺失跳过。

先运行针对性测试，再运行 test、executor/test 全回归、全 TFM 严格构建及 IDE0049 verify。真实执行使用隔离 SQLite/DuckDB 或测试服务，不能使用真实连接参数。资源经 ResXFileCodeGenerator 生成，不为本地化写测试。

Native AOT 显式发布 Linux x64/arm64 与 Windows x64 到 `executor/src/bin/<配置>/net10.0/<RID>/publish/`；生成端从发布目录链接文件，构建和独立发布布局为 .migrator/<RID>/；NuGet 包在 tools/.migrator/<RID>/ 共用一份，生成端在本地 .migrator/ 不存在时定位该共享目录。检查 ELF/PE、原生库、资源、权限和第三方警告，日志与符号保留在 RID 下的独立目录。Pod 使用相对 YAML 路径，继承根中央包和公共构建属性；Linux 发布禁用只读规范配置的同步。DNS 或挂载修改需重建专用 Pod；保留缓存，不修改其他容器。普通 dotnet build/test 不启动环境。文档集中维护命令用法、输入协议和实现机制；临时验证文件在结束后清理。

权限变更同时更新 MigrationPrivileges 与驱动展开：统一名称只映射库内能力，ReadWrite 含 Execute，基础读写含 Sequence 取值，CreateTable/CreateIndex 含适用的序列创建。允许粗粒度合并，禁止公开 NativePrivileges 或隐式实例权限。角色范围和 PostgreSQL 所有权不足明确报不支持；测试覆盖合并去重、范围拒绝、权限阶段失败及指纹。
