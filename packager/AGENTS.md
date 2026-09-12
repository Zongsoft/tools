## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `dotnet-pack` 工具，生成 tar.gz、Debian、RPM 包及安装生命周期脚本；详细流程见 [SKILL.md](SKILL.md) 和 [docs/implementation.md](docs/implementation.md)。

## 代码风格

- 不使用 `using 名称 = 类型或命名空间;` 别名语法；两端交接测试调用独立 migrator 进程，不直接引用运行器协议类型，不使用程序集别名。
- 生产代码使用项目既有的 `/* */` MIT 版权头，单元测试代码不添加文件头版权注释；方法之间留一个空行，按职责用 `#region` / `#endregion` 分组。保持 CRLF 换行和 Tab 缩进。

## 职责边界

- `PackCommand*` 解析命令、初始化变量、加载打包项并选择包类型。
- `Package*` 保存跨格式包模型和格式特定元数据；`Generator*` 负责编码具体容器、头部和载荷。
- `Scriptor.Systemd` 解析/生成服务单元与安装卸载脚本。
- `MigrationProfile`/`MigrationLoader` 使用 Zongsoft.Core Profile 解析升迁 INI/参数；`MigrationBundle` 收集安装时运行器；`.shared` 提供链接到两端的协议源码、MigrationProvider 参数规则、MigrationUtility 通用参数方法及其本地化资源；独立 `migrator/src` 执行建库、SQL、S3 初始化及状态检查。主项目不引用或构建 migrator；Cake 构建流程先独立发布运行器再制作工具包。`.shared/Migration.props` 共享源码和资源声明，Cake 独立 Native AOT 发布到 `src/.migrator/linux-x64/` 和 `src/.migrator/linux-arm64/`，主项目将其作为普通 Content 复制到输出和 NuGet 工具包，运行时按 `.migrator/<RID>/` 约定收集完整目录，不分析程序集依赖。migrator 目标为 net10.0，TDengine 使用 ClientWebSocket 直接访问 taosAdapter；升迁资源配置 ResXFileCodeGenerator 并保留中英文资源与全球化支持。
- `MigrationLoader.Database` 的私有批次实现仅在主项目预处理 SQL，生成安装根 `.migration/.artifacts/` 下的有序 UTF-8 批次；`.migration/` 保留计划、运行器与入口，执行器逐文件提交不再分段。计划使用源码生成 JSON，连接工厂不使用反射。
- `Migrator.Database` 为抽象 partial 基类，嵌套六种数据库实现；共用 ADO.NET 执行辅助，TDengine 独占 WebSocket 会话。`Migrator.Create` 显式选择类型。AOT 环境为独立 Rocky Linux 9/glibc 2.34 容器，不修改参考 framework Pod；普通 `dotnet build/test` 不启动容器。
- `Normalizer`、`Variables` 和 `Utility` 负责变量、路径、Runtime Identifier 与 Unix 权限等共享语义。

## 高风险契约

- tar、deb、rpm 必须对相同输入保持一致的目标路径、根路径别名、排除、权限和生命周期意图，同时尊重各格式的元数据规范。
- 安装、升级、覆盖安装和最终卸载的脚本阶段不同；修改卸载保护时保留 Debian 动作参数和 RPM 剩余实例语义。
- 根路径条目、安装目录、符号链接和 systemd 服务可写系统位置或删除文件；生成测试只检查隔离产物，不执行安装脚本。
- 二进制格式中的长度、偏移、对齐、校验和、字节序、cpio/tar 路径与 RPM 标签属于精确契约，避免无关重构。
- 公开选项或行为变化应同步 `README.md`、`README.zh-Hans.md`、`docs/implementation.md`、资源文本和测试。
- 升迁失败必须阻止服务启动；每次安装和重试均执行全部 SQL，幂等性由脚本作者保证，不维护逐文件成功历史或跳过逻辑。不自动删库删桶、不记录凭据日志。`migration.json` 含参数值，模式保持 `0600`，真实凭据不用于测试。

## 验证

- 本地化不编写单元测试；不切换测试文化、不匹配翻译文案。错误测试验证异常类型、文件/段落/参数标识和敏感值不泄漏。

- 打包器验证：`dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0`，保留输入、SQL 批次预处理、制包及 JSON 交接测试。
- 运行器验证：`dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0`，只引用 `migrator/src`，不引用主项目；迁移两端协作逻辑时运行这两个测试项目。
- 构建：`dotnet build Zongsoft.Tools.Packager.slnx -f net10.0`；全部工具 TFM 使用不带 `-f` 的解决方案构建验证。
- 使用临时发布目录生成包，检查清单、权限、元数据、脚本和归档路径；格式工具只做只读检查。
- 未经明确要求，不执行生成的 `install.sh`/`uninstall.sh`，不调用 `dpkg -i`、`rpm -U`、`systemctl`、`sudo`，不运行 Cake `pack`。

- `--migration` 指定 INI 缺失或模式无匹配只警告并跳过，全部缺失按普通包处理；存在的 INI 内容、参数与 SQL 仍严格校验。生成内容仅保留 `.migration/` 目录，SQL 位于 `.migration/.artifacts/`。

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

监听地址选项为 `--listen`，对应 Variables.Listen；生成 systemd 服务时写入宿主 `--urls`。纯端口转为 `http://127.0.0.1:<port>`，完整地址保留；省略时不追加 `--urls`，已有 service 文件的 ExecStart 不改写。
