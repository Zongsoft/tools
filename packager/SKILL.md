---
name: zongsoft-tools-packager
description: 修改或审查 Zongsoft tools/packager 的 dotnet-pack 命令、变量规范化、打包项与排除规则、Unix 权限、systemd 服务、安装卸载脚本、tar.gz/deb/rpm 二进制生成、包元数据或格式测试时使用。
---

# Zongsoft Tools Packager

先阅读 [AGENTS.md](AGENTS.md)、[README.zh-Hans.md](README.zh-Hans.md) 和 [实现说明](docs/implementation.md)。

## 定位变更

- 构建与产物：`build.cake` 编排构建/制包，`.shared/Migration.props` 共享编译声明，独立 `migrator` 任务准备 `src/.migrator/linux-x64/` 和 `src/.migrator/linux-arm64/` 的完整 Native AOT 发布目录，主项目普通 Content 收入工具输出；主项目不建立 migrator 项目引用。

- 命令选项、默认值、输出路径：`PackCommand.cs` 及 `PackCommand.Tar.cs`、`.Deb.cs`、`.Rpm.cs`。
- 变量来源、`$(name)`/`%name%`、路径规范化：`Variables.cs`、`Normalizer.cs`。
- 打包项、别名、通配、排除、冲突和文件模式：`Package.cs`、`Utility.cs`。
- 服务发现、systemd 单元和生命周期脚本：`Scriptor.Systemd.cs`。
- tar.gz：`Package.Tar.cs`、`Generator.Tar.cs`。
- deb：`Package.Deb.cs`、`Generator.Deb.cs`。
- rpm：`Package.Rpm.cs`、`Generator.Rpm.cs`。
- 升级/卸载回归：`test/PackageLifecycleTests.cs`。
- 升迁测试：`test/` 保留输入、制包与 JSON 交接（调用独立 migrator apply/check 进程，运行器项目引用仅作构建依赖）；`migrator/test/` 的独立测试项目只引用 `migrator/src/`，覆盖数据库、S3 和 TDengine WebSocket；SQL 批次预处理测试位于主 `test/`。
- 安装升迁：`src/MigrationProfile.cs`、`src/MigrationLoader.cs`、`src/MigrationBundle.cs`、`.shared/`（MigrationProvider/通用参数/计划/资源）、`migrator/src/`（net10.0 Native AOT、TDengine 直接 WebSocket），配置契约见 [升迁指南](docs/migrations.zh-Hans.md)。

## 实现顺序

1. 明确变化属于共享包模型还是单一格式；共享语义先在 `Package`/命令层定义，再由各生成器编码。
2. 对路径先区分普通载荷与以 `/` 或 `\` 开头的根路径别名，检查规范化后路径不能意外逃逸预期包根。
3. 修改服务脚本时分别推演首次安装、升级、同版本覆盖、最终移除和显式 tar 卸载。
4. 修改 deb/rpm 编码时依据 [实现说明](docs/implementation.md) 核对字段、架构映射、偏移、对齐和载荷清单，并增加针对性断言。
5. 公开行为变化同步双语 README、实现说明、资源和测试；不要把格式知识只留在代码注释中。

## 跨格式检查

- 文件选择、目标路径、排除与重复冲突在三种格式中保持同一输入语义。
- Unix 主机保留源权限；Windows 推断的可执行/普通文件模式保持文档化规则。
- `/etc` 等根路径条目在 tar 的 `.root`、Debian `conffiles` 和 RPM 配置文件标记中各自正确表达。
- systemd 禁用值、服务文件优先级、宿主定位、环境变量和 `--urls` 生成规则保持兼容。
- Debian 升级动作和 RPM 非最终实例卸载不得执行最终删除逻辑；tar 卸载器只删除解析后的目标。
- 升迁只增加 `--migration` 选项；INI 解析复用 Core Profile。异常及提示使用带 ResXFileCodeGenerator 的双语资源，原生发布保留中英文资源和全球化能力。检查别名、参数逐级查找、无匹配脚本、完整原生目录的 RID/ELF 架构和执行权限、SQL 校验和、每次安装及失败重试均从头执行 SQL、失败 ready 标记、升迁先于启动、`DESTDIR` 不执行钩子，以及保留数据库/Bucket/状态。

## 安全验证

测试代码不添加文件头版权注释，不为本地化编写单元测试；不引入文化切换辅助类或翻译文案断言，保留异常类型、错误定位和敏感值不泄漏检查。

先运行受影响的测试项目；升迁目录或交接变更同时运行 `dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0` 与 `dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0`，再按需要从临时源目录生成代表性小包。可以使用 `tar -tf`、`dpkg-deb --info/--contents`、`rpm -qip/-qlp/--scripts` 等只读命令检查产物；工具不可用时记录未验证项。

不要安装生成包、执行其生命周期脚本、写入 `/opt` 或 `/etc`、管理 systemd，也不要使用真实应用产物中的密钥。发布 NuGet 工具包仅在用户明确要求时进行。

升迁目录：计划为 `.migration/migration.json`，SQL 批次位于 `.migration/.artifacts/`。检查指定 INI 缺失/无匹配的警告和全缺失普通包行为，同时保留参数文件、SQL 和无效 INI 的错误校验。

S3 桶初始化支持 public/private、默认加密（sse-s3/sse-kms及可选KMS密钥标识）、版本控制（enabled/suspended）和 tag.* 桶标签。省略 Encryption 时不写入 JSON、不调用加密 API，采用服务端默认；显式对象必须包含有效 Mode。已有桶通常跳过，带本地 pending 的未完成初始化会重试；新建桶的指定配置全部完成才清除 pending；选项文本仅在打包端解析，共享 Bucket 模型校验结构，运行器使用标准 S3 API。

SQL 批次按规范升迁器名称组织，例如 `.migration/.artifacts/mysql/0001.sql` 和 `.migration/.artifacts/postgres/0001.sql`。每次加载计划时各升迁器从 0001 独立计数，同类任务共享连续编号；PostgreSQL 别名统一归入 postgres。每个非空段落仍是独立任务，保留自己的连接参数及脚本列表；任务 Id 用于日志和状态，不作为目录名。同段落内 SQL 重叠匹配去重，跨段落、跨文件和重复指定 INI 不合并或去重。S3 配置直接保存在计划中，不生成空中间目录。

`MigrationLoader.Load` 的局部计数字典传给 `MigrationLoader.Database`，避免多次加载或失败重试继承编号。解析顺序保留；运行器当前串行，但不承诺跨任务执行顺序，数据库任务内部的脚本顺序继续保证。

## 打包器来源元数据

`Generator` 从自身程序集计算 `程序集名@版本号`（AssemblyName.Version 的完整文本），写入 tar 的 PAX 全局 `Packager` 属性、deb 的 `Packager` 控制字段、RPM 的 `RPMVERSION`（1064）。RPM 1015 保留维护者；不新增载荷文件，不修改源/包内 `.version` 或升迁计划。格式回归核对生成工具身份与应用版本、维护者彼此独立。

## 文档与验证记录

当前行为以双语 README、升迁指南和实现说明为准；验证记录与 AOT 警告审查保留对应阶段的日期、计数、路径及产物哈希，不应视为当前工具或容器状态。命令示例采用本机 hosting 已有的 `.deploy/default/migration/1.0.0/*.ini`，不得复制真实连接凭据。

## 应用版本管理

- `PackCommand.VersionFile`（`src/PackCommand.Version.cs`）只加载源目录直属 `.version`，使用 Core `ApplicationVersion.Load(Stream)/Save(Stream)`，由打包器显式打开或创建直属文件；损坏或不可读时失败。`--name`、`--version` 为条件必填，空白名称或 Edition 等同省略，版本对象为空则采用源版本，多个 Edition 必须明确选择，名称及 Edition 忽略大小写匹配并保留文件拼写。
- 身份解析在完整变量初始化之前；无源文件时要求有效名称和非零版本。包内版本通过 `ApplicationIdentifier.Save(Stream)` 原样写入内存，不追加换行，`EntryCollection.SetVersion` 强制替换同安装位置旧条目，内容格式由 Core 决定，权限为 0644；内存条目统一经 `OpenRead()` 供各生成器读取。
- 全部制包成功后才保存源版本，只更新所选 Edition 并保留其他条目顺序；保存失败保留安装包并返回错误。Core 保存规范化格式，不保留原注释。测试使用临时目录，覆盖三格式内容、唯一性、长度、权限、RPM 摘要和失败时序。

## 输入与归档改进

- 变量优先级为显式选项 > 环境 > 描述符默认值；身份由源版本规则决定。按使用展开，不预读文件，未知/循环引用失败。
- TextSource 统一源目录文件与文本：file: 强制文件，text: 原样文本，文件内容不展开或二次解释；pre/post 为严格文件列表。
- FileMatcher 统一载荷、INI、SQL 的 *、?、独立段 **；每个参数位置按固定前缀下相对路径 Ordinal 展开，保留任务与段落内去重规则。拒绝符号链接/reparse point。
- Entry.IsDirectory 保留空目录、源模式及时间；Generator.Entries 补齐 0755 父目录。目录不调用 OpenRead，不列入 Debian conffiles。tar 根目录别名卸载仅 rmdir 显式空目录。
- Debian/RPM 大载荷用 DeleteOnClose 独占临时流及增量摘要，不分配完整载荷/包体数组；Unix 临时文件 0600，异常也释放。Debian 六种关系字段由 Package.Deb 独立校验，不复用 RPM 语法。
- 实施清单及验收证据见 [docs/improvements.md](docs/improvements.md)，输入与归档回归位于 PackageInputTests、PackageArtifactTests。
