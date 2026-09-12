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

S3 桶初始化支持 public/private、默认加密（sse-s3/sse-kms及可选KMS密钥标识）、版本控制（enabled/suspended）和 tag.* 桶标签。已有桶跳过，新建桶的指定配置全部完成才清除 pending；选项文本仅在打包端解析，共享 Bucket 模型校验结构，运行器使用标准 S3 API。

## 应用版本管理

- `PackCommand.VersionFile`（`src/PackCommand.Version.cs`）只加载源目录直属 `.version`，使用 Core `ApplicationVersion.Load(Stream)/Save(Stream)`，由打包器显式打开或创建直属文件；损坏或不可读时失败。`--name`、`--version` 为条件必填，空白名称或 Edition 等同省略，版本对象为空则采用源版本，多个 Edition 必须明确选择，名称及 Edition 忽略大小写匹配并保留文件拼写。
- 身份解析在完整变量初始化之前；无源文件时要求有效名称和非零版本。包内版本通过 `ApplicationIdentifier.Save(Stream)` 原样写入内存，不追加换行，`EntryCollection.SetVersion` 强制替换同安装位置旧条目，内容格式由 Core 决定，权限为 0644；内存条目统一经 `OpenRead()` 供各生成器读取。
- 全部制包成功后才保存源版本，只更新所选 Edition 并保留其他条目顺序；保存失败保留安装包并返回错误。Core 保存规范化格式，不保留原注释。测试使用临时目录，覆盖三格式内容、唯一性、长度、权限、RPM 摘要和失败时序。
