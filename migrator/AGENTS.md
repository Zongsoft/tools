## 概述

数据库 `.env` 使用 provider/数据库/用户三级结构，`Bootstrap` 为唯一引导库参数。provider `Database` 指定默认库，默认库可无显式段；非默认库须声明。只将 `.migration` 引用的库及全部用户加入计划，空段算引用。Core 决定导入覆盖，数据库声明事件恢复跨库 SQL 顺序及空段来源；同一来源/实际目标去重。计划分离 Databases 初始化描述和以从零开始的 DatabaseIndex 引用 Databases 数组的 Steps，默认值参与指纹。执行先全计划/SQL校验，再全部建库、全部建用户、顺序 SQL、追加授权。已有库设置/用户密码保持；新库多命令配置由无凭据 pending 恢复。S3 解析及执行契约保持不变。完整参数见 [数据库指南](docs/databases.zh-Hans.md)。

遵循 [../AGENTS.md](../AGENTS.md)，开始前阅读 [SKILL.md](SKILL.md)、双语 README 和 docs/implementation.md。dotnet-migrate 制作升迁归档和脚本；executor 在目标机执行。两端仅通过 .shared 链接源码，不生成共享 DLL，不引用 packager。

## 契约

- 计划名称使用 Name，状态文件对应 name；计划成员使用 Steps、Step.Settings、Database.Settings；Step 无 Id，执行顺序和日志序号来自数组位置，Database 无 Id，Step.DatabaseIndex 按 Databases 数组位置引用；校验索引范围及 provider 一致性，S3 不接受索引。SQL 序号不补零；不按文件名排序执行。源码生成 JSON 统一 WhenWritingNull，格式化输出复制其选项，保留 Source/Content 等无条件 JsonIgnore。

- 输入仅支持 .migration（Core Profile INI 格式），参数为 .env。导入复用 Core Reader，所有来源均校验；条目路径与参数定位跟随 entry.Profile.FilePath。参数查找不隐式补齐较近配置。
- 按显式参数位置处理输入，模式内按相对路径 Ordinal 排序，任务按连续声明来源划分，同一来源段落内去重。SQL 分段仅在 MigrationLoader.Database 私有实现，按规范 provider 共用连续编号；每次 Load 重新计数。S3 不生成空目录。
- 支持六种数据库和 S3；MigrationProvider 管参数规则，MigrationPlan 管结构；TDengine 用 WebSocket。执行器每次 apply 执行全部 SQL，脚本负责幂等；无成功文件历史、自动回滚或并发调度。
- 产物名为 <规范名称>[-edition]@<version>_<runtime>.tar.gz 与同名 .sh/.cmd；不生成描述文件；版本文件只读，不创建或保存。两文件先暂存后发布，覆盖失败恢复原文件。
- --version 为可选字符串：空值只读当前目录直属 .version，忽略环境 version；版本号优先，否则展开文件路径，现有目录追加 .version。VersionSource 私有嵌套类复用 ApplicationVersion；Edition 按文件选择并保留拼写，name 必填且独立。解析失败含完整文件路径，不改动输出；回填最终版本和 Edition 后才初始化生成变量，输入和输出仍相对当前目录。
- 计划为 .migration/migration.json，SQL 为 .migration/.artifacts/<provider>/<序号>.sql。计划使用源码生成 JSON，数组顺序参与指纹计算；Source/Content 不入 JSON。PAX 元数据为 Migrator 与 Runtime。
- 外部入口 [apply|status|check] [state]，check 比较内嵌指纹与 ready，不解压。默认 state 不含版本/RID；安装方显式传入目录。apply/status 清理临时解压目录并返回退出码。
- 执行使用状态锁，失败使 ready 失效，S3 pending 支持配置重试；不记录凭据，不删除数据库/桶。可选 S3 加密、版本控制和标签均使用标准 API；未指定时不发配置请求。

## 构建与规范

生成端 net8/9/10，执行器 net10 AOT。executor/build 是独立 Rocky Linux 9 环境；普通 build/test 不启动容器。Cake executor 准备 Linux x64/arm64 与 Windows x64，产物统一位于 `executor/src/bin/<配置>/net10.0/<RID>/publish/`，生成端 Content Link 映射为构建和独立发布目录的 .migrator/<RID>/，NuGet 包在 tools/.migrator/<RID>/ 仅收录一份供全部 TFM 共用；logs/ 与 symbols/ 在 publish/ 同级，Linux 编译缓存保留在 /aot。compile 校验当前配置的三个发布目录再制工具包；build 聚合两者，pack 推送公共 NuGet，未授权不执行。

所有项目继承仓库根 .editorconfig；AOT Pod 与 CI 将其只读挂载到 /.editorconfig，工具目录不保留副本。通用包版本继承根 Directory.Packages.props；数据库驱动与 AWS SDK 版本在 executor 项目以 VersionOverride 定义。公共构建属性与分析器引用继承根 Directory.Build.props；AOT Pod 与 CI 将两个 props 文件只读挂载到容器根目录，Linux 发布传入 -p:ZongsoftGuidelinesSynchronization= 禁止向只读配置同步。所有项目 CodeAnalysis 版本继承根 Directory.Packages.props，严格构建和 IDE0049 verify；资源用 ResXFileCodeGenerator。生产代码 MIT 头、Tab/CRLF、方法空行/中文 region，sh LF；测试不加版权、不用别名、不做本地化测试。

测试位于 test 和 executor/test，仅枚举这些目录下项目。生成端测试可对执行器设 ReferenceOutputAssembly=false 作为构建依赖，交接调用独立进程；不得引用两端同名协议类型。真实运行仅在临时目录或隔离 Linux 环境，不能操作用户现有服务、更新全局工具或发布包。ARM64 不要求硬件执行；AOT 第三方警告不得隐藏。

统一权限由 .shared/MigrationPrivileges.cs 规范化和展开，Permission 的有效 20 项能力参与计划指纹。ReadWrite 包含 Execute，任意读写包含 Sequence 取值。不得公开 NativePrivileges；允许库内原生权限颗粒度合并，不自动授予实例权限。PostgreSQL DDL 检查对象所有权；Roles 检查目标库范围；不支持用 MigrationPrivilegeException（NotSupportedException）报告，凭据和服务端错误仍不输出。
