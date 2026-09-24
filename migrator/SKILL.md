---
name: zongsoft-tools-migrator
description: 修改独立升迁输入、SQL 批次、原生执行、产物命名和 AOT 构建。
---

数据库修改同时核对 [完整参数及默认值](README.zh-Hans.md#database-configuration)：provider/库/用户层级、默认库隐式声明、空段引用、仅引用库入计划、四阶段初始化和授权、既有设置/密码保持、pending恢复。`.shared/MigrationPlan.Database.cs` 存放初始化描述，任务通过 DatabaseIndex 引用其数组位置（从零开始）；SQL去重按声明来源和实际目标。Core仍负责导入及覆盖，声明事件只补充跨库顺序与空段来源。S3保持独立原有路径。

# Migrator 开发流程

协议调整须同步生成端与执行器：计划名称使用 Name，状态文件对应 name；Steps 无独立 Id，Settings 保存连接设置，Database 无 Id，Step.DatabaseIndex 为可空整数，Amazon S3 省略；状态使用 phase/step/databaseIndex 定位。数据库索引必须在 Databases 数组范围内且 provider 匹配；索引 0 必须序列化。从 Steps/Scripts 数组顺序执行，SQL 文件名为不补零的序号。序列化与指纹共享全局 WhenWritingNull，不能删掉 Source/Content 等无条件忽略注解。

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[升迁指南](README.zh-Hans.md#package-phase)和[实现说明](docs/implementation.zh-Hans.md)。

命令身份先由 MigrateCommand.Version.cs 的私有 VersionSource 解析：--version 可为版本号、文件或现有目录，省略/空白只读当前目录直属 .version，不能由环境或 .env 中的 version 代替。ApplicationVersion 选择 Edition，保留文件拼写；name 保持独立必填。最终值回填后建立本次命令独立的变量视图，按需展开、再转换；源版本文件始终不写入，失败不能修改已有产物。

输入处理在 src/MigrationLoader* 与 MigrationProfile，生成文件集在 MigrationBundle，归档和脚本在 Generator。协议及参数描述在 .shared。数据库/Amazon S3/锁/状态在 executor/src。保持这些边界；packager 只消费产物。

通用变量按默认值、系统环境、从根到工作目录的直属 `.env`、显式选项顺序覆盖，先通过共享 Utility/Profile.Load 加载再解析版本来源；各级段落与条目以下划线拼名，同次命令全部输入共用变量。连接参数使用 `<输入名>.ini` 或 `<provider>.ini`，保持就近选择完整配置及显式导入规则，自动查找不回退旧 `*.env`。迁移示例、测试与文档时同步导入路径，不能把通用 `.env` 改成 `.ini`。

核对两端名称契约：缺少既定后缀时补 -migrate，Edition 可省略，版本与 RID 明确。脚本和归档必须同前缀，check 不解压，状态跨版本保留，外部调用可覆盖 state。已有输入无效不能按缺失跳过。

先运行针对性测试，再运行 test、executor/test 全回归、全 TFM 严格构建及 IDE0049 verify。真实执行使用隔离 SQLite/DuckDB 或测试服务，不能使用真实连接参数。资源经 ResXFileCodeGenerator 生成，不为本地化写测试。

Native AOT 显式发布 Linux x64/arm64 与 Windows x64 到 `executor/src/bin/<配置>/net10.0/<RID>/publish/`；生成端从发布目录链接文件，构建和独立发布布局为 .migrator/<RID>/；NuGet 包在 tools/.migrator/<RID>/ 共用一份，生成端在本地 .migrator/ 不存在时定位该共享目录。检查 ELF/PE、原生库、资源、权限和第三方警告，日志与符号保留在 RID 下的独立目录。Pod 使用相对 YAML 路径，继承根中央包和公共构建属性；Linux 发布禁用只读规范配置的同步。DNS 或挂载修改需重建专用 Pod；保留缓存，不修改其他容器。普通 dotnet build/test 不启动环境。文档集中维护命令用法、输入协议和实现机制；临时验证文件在结束后清理。

权限变更同时更新 MigrationPrivileges 与驱动展开：统一名称只映射库内能力，ReadWrite 含 Execute，基础读写含 Sequence 取值，CreateTable/CreateIndex 含适用的序列创建。允许粗粒度合并，禁止公开 NativePrivileges 或隐式实例权限。角色范围和 PostgreSQL 所有权不足明确报不支持；测试覆盖合并去重、范围拒绝、权限阶段失败及指纹。
