# 升迁实现说明

生成端 src 使用 Core Profile 与 Searcher 解析输入，MigrationLoader.Database 预处理 SQL 批次，AmazonS3 解析桶选项。MigrationBundle 收集完整原生产物和计划；Generator 使用 System.Formats.Tar 写 PAX，记录 Migrator（程序集名@版本）和 Runtime。项目不引用 packager。

`MigrateCommand.Version.cs` 的私有嵌套类型 `VersionSource` 负责版本来源解析。主流程先从初始变量中移除环境变量 `version`，仅将本次命令选项传入解析器。解析器展开选项值，优先识别版本号，目录追加 `.version`，并使用 `File.OpenRead` 和 `ApplicationVersion.Load(Stream)` 只读加载文件。Edition 通过文件的忽略大小写集合选择并保留原拼写；读取或格式异常补充完整路径，非零版本和 Edition 校验均先于输出处理。

进程入口向 Core CommandLine 传递表达式前，逐项引用选项值和位置参数并转义反斜杠，保留空值与含空格的 Windows 路径，避免相邻参数被合并。命令将解析后的版本号和 Edition 回填变量字典后才调用 `Normalizer.Initialize`。`--name` 独立于版本文件，输入和输出路径始终以当前目录为基准；全过程不保存版本文件。计划协议和原生执行器不参与源版本查找，只接收最终身份。命令测试覆盖版本号、文件、目录及默认来源、Edition 选择、变量展开，以及失败时源文件和已有输出保持不变。

.shared 通过 Compile Link 分别编译到生成端与 executor，不生成共享 DLL。MigrationPlan 采用 partial 和嵌套 Step/Script/Bucket、源码生成 JSON；Source/Content 仅在生成端扩展，参数及计划校验由共享代码负责。源码生成 JSON 的字段和指纹规则见升迁指南。

executor 使用显式工厂选择六种数据库或 S3，实现连接、建库、顺序提交、校验和、锁、状态及 pending。所有 SQL 每次 apply 均执行，不维护逐文件成功历史。数据库/S3 驱动只在执行器声明，TDengine 使用 WebSocket。

外部脚本与归档同名，仅扩展名不同。脚本写入确切归档名、无版本状态名及计划指纹；第二参数可覆盖状态目录。check 只比较 ready；apply/status 创建临时目录，解压内部 .migration/，调用内部入口传入动作和持久状态目录，返回执行器退出码并清理临时目录。packager 的安装脚本与 systemd drop-in 均调用这个外部入口，不理解 SQL 或计划字段。

两个产物先写入输出目录下的私有暂存目录，再备份/替换目标；异常恢复原输出。归档中的 migration.json 权限为 0600，其他数据 0644，入口 0755。Unix 下外部归档 0600，脚本 0755。归档含展开后的凭据，不应上传到公共源。

Linux 运行需要 glibc >=2.34、libgcc、libstdc++、zlib、ICU、OpenSSL、CA 证书，部分认证需要 Kerberos/GSSAPI；Shell 需要 sh、tar/gzip、核心工具和 cmp。Windows 需要系统 PowerShell、tar.exe 和发行目录内的原生 DLL。目标机无需 .NET。AOT 不启用 invariant globalization，保留中英文资源；原生产物位于 `executor/src/bin/<配置>/net10.0/<RID>/publish/`，同级 logs/ 和 symbols/ 分别保存日志/架构检查及符号。普通构建和独立发布通过文件链接收录到程序目录的 .migrator/<RID>/；NuGet 制包仅在 tools/.migrator/<RID>/ 保存一份，并移除包内各框架的重复副本。MigrationBundle 先检查程序目录的 .migrator/，该目录不存在时定位 tools/<TFM>/any/ 上两级的共享目录。不向源码目录写入原生产物。第三方 AOT 警告保留并审查，不屏蔽。

通用包版本由仓库根 Directory.Packages.props 管理；数据库驱动与 AWS SDK 在执行器项目通过 VersionOverride 维护。所有项目使用根 Directory.Packages.props 指定的 CodeAnalysis 版本，严格构建之外运行 IDE0049 verify。资源使用 ResXFileCodeGenerator；不编写本地化测试。普通构建不依赖 packager，也不启动容器。

## Native AOT 构建脚本

构建脚本位于 executor/build：

- `setup.sh`：仅在专用 Rocky Linux 9 x64 容器中准备环境；安装编译工具与开发库，下载并校验 .NET SDK 10.0.401，解包 ARM64 RPM 到 /aot/sysroots/linux-arm64，提供交叉编译所需头文件、库及启动目标文件。缓存保留在 /aot，不执行 ARM64 软件包的安装脚本。
- `clang-arm64.sh`：ARM64 交叉编译的 clang 包装入口；禁用宿主默认 clang 配置，指定 ARM64 sysroot 中的 GCC 目录，并透传编译参数。它由 publish.sh 配合 SysRoot 和 CppCompilerAndLinker 使用，避免链接到宿主 x64 工具链。
- `publish.sh`：Linux x64/arm64 编译缓存保留在容器 /aot；校验完成后，将完整发布产物复制到工作区中 executor 对应的 publish/ 目录，符号单独存放。
- `publish.ps1`：在 Windows 本机使用 SDK 默认 publish/ 目录发布 win-x64 执行器，检查 PE 架构并分离符号文件。

这些脚本只服务于构建，不随升迁包在目标机执行。

## Native AOT 验证

原生发布后检查 ELF/PE 架构、依赖文件、全球化资源及 Linux 执行权限。使用无 .NET 的隔离目标验证 SQLite/DuckDB 的 apply/status/check、重复执行、失败重试、锁、临时目录清理及跨版本状态。ARM64 可只检查编译产物与架构。

固定版本依赖包含裁剪和动态代码警告，涉及 SqlClient 的诊断载荷、反射配置、SQL CLR 类型及参数转换，ConfigurationManager 的反射，以及 DuckDB 的复杂集合/结构转换、MySqlConnector 的诊断路径。完整日志位于 `executor/src/bin/<Configuration>/net10.0/<RID>/logs/publish.log`。保留并逐项审查警告；普通构建成功或原生文件生成成功均不能证明驱动高级功能可用。网络认证、复杂数据类型及真实 S3 服务需按实际使用场景验证。

生成端回归覆盖输入、SQL 批次、命名、覆盖恢复和进程交接；执行器回归覆盖计划校验、数据库执行、状态、S3 pending 及 TDengine 会话。命令见 [README](../README.zh-Hans.md#构建与测试)。
