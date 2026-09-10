# 升迁功能验证

2026-09-10：职责拆分、Native AOT 发布和文件交接验证。契约见 [升迁指南](migrations.zh-Hans.md)，第三方警告逐条说明见 [AOT 警告审查](aot-warning-review.md)。

## 自动化回归

**140 通过、0 失败、0 跳过**：打包器 108 项，独立运行器 32 项。测试经公开输入入口验证，不使用公共 SQL 分段器、程序集别名、using 别名或本地化断言。

```powershell
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -c Release -f net10.0 --no-restore
dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -c Release -f net10.0 --no-restore
```

| 要求 | 证据 |
| --- | --- |
| SQL 分段与顺序 | `Load_SqlServerBatches_SortsFilesRemovesBomAndHashesPreparedUtf8Content`；`MigrationSqlBatchTests` 全部经真实 INI/SQL 和 `Load` 验证 |
| 三格式 SQL 字节、权限和校验和 | `Bundle_PreprocessedSqlServerBatches_PreserveContentOrderAndChecksums` |
| 收集完整 RID 目录 | `Bundle_ProviderAndArchitecture_CollectsCompleteSelectedNativeDirectory`；另覆盖缺入口、非 ELF、架构错误 |
| 两端 JSON 交接 | `MainPlan_RuntimeAppliesPreparedSqliteScriptsInOrderAndRejectsTampering`，独立进程验证顺序、指纹、重试和篡改失败 |
| 主项目依赖隔离 | `MainAssembly_DoesNotReferenceRuntimeOrDatabaseDrivers` |
| 执行文件范围 | `Context_ScriptOutsideMigrationDirectory_IsRejected` |

主测试包中的最小 ELF 仅用于归档结构断言，不当作原生执行证据。进程交接回归使用普通构建的独立 migrator；下面另行执行真实 AOT 产物。运行器测试只引用 `migrator/src`，主测试对 migrator 仅保留构建依赖。

## 原生发布

解决方案 Release 非增量构建通过，0 警告、0 错误；主项目覆盖 net8.0/net9.0/net10.0，migrator 为 net10.0。固定现有 NuGet 版本，无驱动升级、警告屏蔽或托管备用入口。

独立 Rocky Linux 9.8/glibc 2.34 容器安装 .NET SDK 10.0.111、clang/lld 21，使用同发行版 ARM64 RPM sysroot 交叉编译。未修改 framework Pod 或 Podman 虚拟机配置。`dotnet cake --target=migrator --edition=Release` 已实际成功执行，复用本次准备的 `zongsoft-packager-aot-builder`；新建 Pod 分支的 YAML 和脚本已检查，未另建第二套环境重复验证。

| RID | 编译和检查 | 实际执行 |
| --- | --- | --- |
| linux-x64 | Native AOT 成功；ELF x86-64，入口 0755；两种 .so 架构正确；中文资源存在 | 全部七种升迁器通过 |
| linux-arm64 | Native AOT 交叉编译成功；ELF AArch64，入口 0755；两种 .so 架构正确；中文资源存在 | 按约定未执行验证 |

主 ELF 及 SQLite 库最高要求 GLIBC_2.34，DuckDB 库最高 GLIBC_2.25。目标机还需 ICU、OpenSSL、libstdc++、libgcc 等系统库。中文原生入口用法已手动检查。Windows 挂载盘会将模式投影为 0777，因此安装包明确写入入口 0755、普通运行文件 0644；归档权限单独验证。

首次完整发布每个 RID 有 **51 条第三方 IL 警告（含 6 条 IL3050）**，自身源码无 IL 警告。逐条审查覆盖 SqlClient 诊断、配置和类型反射、DuckDB 复杂值转换、MySQL schema、ConfigurationManager。下列基础升迁路径已实测；CLR UDT、加密 enclave、DuckDB 复杂 CLR 值映射等驱动扩展能力未据此宣称可用。增量发布可能复用原生产物而不重复输出警告，完整清单保存在审查文档中。

原生产物：`src/.migrator/linux-x64/`、`src/.migrator/linux-arm64/`。构建日志、ELF 依赖和符号：`migrator/src/bin/aot/<RID>/`。x64 可执行文件 SHA-256：`2444F288F31593BF8592F7D69BF3B0FB4FA2497F5536D5B2067E7A8532E578F3`。

## 无 .NET 环境的升迁执行

在独立 Rocky Linux 9.8 容器执行真实 ELF，无 .NET SDK/运行时。网络服务只使用私有隔离网络，无宿主端口映射、无真实服务器或凭据。

| 升迁器 | 实际验证 |
| --- | --- |
| SQLite、DuckDB | apply/status/check、重复执行计数=2、SQL 错误、篡改清除 ready |
| MySQL 8.4.8、PostgreSQL 18.3、SQL Server 2025 | 建库、上述执行与失败行为、错误密码拒绝 |
| TDengine 3.4.2.5 | 直接 WebSocket 建库建表写入、重复执行、查询真实数据、SQL/凭据错误和篡改拒绝 |
| RustFS 1.0.0-rc.5 | 公私桶创建、重复执行、错误凭据；匿名公开 GET 返回上传内容，私有 GET 返回 403 |

23 个隔离计划、SQL、脚本、逐提供器执行日志及数据/权限检查记录位于 `migrator/src/bin/aot/verification/`。测试容器和私有网络已清理。SQLite/DuckDB 还通过包内入口在未设置 `LD_LIBRARY_PATH` 时运行，确认运行器自行定位附带原生库。

## hosting 与三种包格式

使用 `D:\Zongsoft\hosting` 的 daemon、web/default 临时副本，保留各自 pack.cmd 的名称、载荷和 Web Nginx 钩子；仅使用容器内 SQLite 测试参数。SQL 采用真实 `framework/upgrading/database/zongsoft.upgrading-sqlite.sql` 副本。

两个宿主分别生成 tar.gz、deb、rpm，共六包，全部检查通过：原生入口 SHA-256 与发布输出相同；入口/计划/SQL 的模式分别为 0755/0600/0644；SQL 字节与计划 SHA-256 一致；保留两种原生库及语言资源；无旧升迁程序集、deps.json 或 runtimeconfig.json；安装脚本包含升迁及 ExecStartPre 门禁。

两个 tar 包的 `.migration/` 和 `migration/` 提取到独立无网络、无 .NET 的 Linux 容器；实际 `migrate.sh apply/status/check`、重复 apply、篡改失败和 ready 失效均通过。精简 Rocky 镜像最初缺少 `cmp`，补充 `diffutils` 后验证成功，指南已注明该系统依赖。未执行完整宿主安装、systemd 或 Nginx 服务操作。

制包脚本、六包、检查脚本和结果位于 `src/bin/aot-hosting/`。仅为验证使用已有冲突提示（service/appsettings/web.config），不改变宿主条目规则，不修改真实 hosting 文件。

## 工具包与本机工具

最终包通过隔离构建目录生成，避免旧输出混入：

```powershell
dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release --artifacts-path src/bin/aot-final-artifacts -o src/bin/aot-packages
```

三个工具 TFM 均包含两个完整 RID 目录；逐一检查原生入口哈希、语言资源及 .so，不包含旧 flat 托管运行器或符号。主项目仅以普通 Content 声明接收目录，不引用或构建 migrator。

本机全局 `zongsoft.tools.packager` 已从最终本地包替换，版本仍为 0.8.3；原包备份于 `src/bin/aot-previous-tool/`，随后卸载并仅从本地 `src/bin/aot-packages/` 安装，没有公共 NuGet 推送。

已核对三个 TFM 的主程序集、两个 RID 的入口/原生库/资源，共 27 项安装文件哈希，与最终 nupkg 完全一致，结果位于 `src/bin/aot-packages/installed-hashes.json`。直接调用 `C:\Users\95558\.dotnet\tools\dotnet-pack.exe`，使用真实 daemon DLL 和 Upgrading SQL 生成包；包内 ELF 与最终发布相同，提取后在无网络、无 .NET 容器中实际 apply/check/status 通过。产物位于 `src/bin/aot-global-smoke/`。

## 静态检查

检查源码边界、资源生成配置、CRLF/Tab、文档相对链接和 `git diff --check`。生产代码保留版权头与 region，测试无版权头。主项目资源通过 ResGen 强类型生成更新 Designer，保留 ResXFileCodeGenerator 配置。没有向公共 NuGet 发布。


## Web 1.0.0 真实安装验证（2026-09-10）

本轮执行真实 `D:\Zongsoft\hosting\web\default\pack.cmd`（rpm、1.0.0、development、Debug、net10.0、x64、default），输入为 hosting 新增的 `.deploy/default/migration/1.0.0/*.ini`。补全 MySQL/S3 参数、段落和以 INI 为基准的 SQL 路径。新增 INI 文件名通配符处理；文件按 Ordinal 排序，参数文件继续逐级查找。

真实 dnf 安装暴露并修复 RPM 缺陷：虚构 PayloadIsGzip 依赖、错误的 signature tag、文件摘要算法声明、缺失不可变区域与数据排序。现在以 V4 region、SHA-256 文件/载荷摘要和正确的签名区摘要完成正常安装；`rpm -Kv` 全通过，最终 `rpm -V` 无差异。SHA256HEADER 标签采用 RPM 4.16.1 源码中的273（SIG_BASE+17），不能使用格式文档表格中误列的272，见 [rpmtag.h](https://github.com/rpm-software-management/rpm/blob/rpm-4.16.1-release/lib/rpmtag.h)。

隔离 Pod 使用 MySQL8.4.8、Redis8.10.1、RustFS1.0.0-rc.5；Rocky9.8 Web 容器安装 ASP.NET10.0.11、systemd、Nginx。初始业务表为0，原生升迁执行15份SQL后为33张；四个私有桶创建成功且匿名403。初次与最终包重装均执行真实生命周期，最终 complete/check、Web与Nginx active、直连/代理 Application和Administrator登录200。移除ready后服务拒绝启动，恢复后启动成功。SQL15项哈希及0600/0755权限检查通过。

宿主已有Data插件产物与Core接口不匹配，以及Redis SDK/依赖版本不一致，均刷新产物后收入最终包；没有修改宿主业务代码或原SQL。Security初始化INSERT不幂等，因此备份本轮测试数据库后重建空库验证最终包，而非跳过升迁。全局工具从本地nupkg更新，27项文件哈希一致。

本轮最终回归：打包器117项、migrator32项，全部通过。新增 `Load_MigrationIniGlob_ExpandsHostingVariablesOrdersFilesAndUsesAncestorParameters` 覆盖真实hosting路径表达式；`Rpm_GzipPayload_DeclaresSupportedRpmlibRequirementsAndAlignedVersions` 与 `Rpm_ImmutableRegionsAndDigests_CoverActualHeaderPayloadAndFileBytes` 覆盖真实包依赖、区域、精确数据布局及摘要。

最终包：`D:\Zongsoft\hosting\web\default\zongsoft.web@1.0.0_linux-x64.rpm`，SHA-256为`32816C55A157FA1CD7EB9FFB40F2AD714D469EACD5B867340AE79BCC8BF8FE77`。本轮容器、卷和安装包按用户要求保留运行；访问端口8069/8080/9000/9001/3306/6379仅绑定127.0.0.1。完整人工说明、备份、HTTP和SQL检查记录位于 `hosting/web/default/bin/migration-verification/1.0.0/README.md`。

## 升迁目录与 DEB/TAR 真实安装验证（2026-09-10）

本轮将计划统一为 `.migration/migration.json`，预处理 SQL 统一为 `.migration/.artifacts/<任务编号>/<序号>.sql`。指定的升迁 INI 不存在或文件名通配符无匹配时警告并跳过；全部缺失生成普通包，已存在 INI 的格式、参数和 SQL 错误仍会终止打包。双语 README、升迁指南、实现说明与 AGENTS/SKILL 已同步。

两套回归测试分别为128项和33项，全部通过；解决方案覆盖主项目 net8.0/net9.0/net10.0 与 migrator net10.0，构建0警告0错误。主要证据：

| 要求 | 测试与实际验证 |
| --- | --- |
| 缺失输入警告并继续 | `Load_MissingFilesAmongExistingFiles_WarnsAndPreservesValidTasksInOrder`、`Load_AllRequestedFilesMissing_ReturnsNoPlanAndWarnsForEveryExpandedPath`；全局 CLI 通过真实终端输出两条警告并成功生成普通 tar 包 |
| 完整输入仍严格校验 | `Load_ExistingInvalidInputAfterMissingFile_StillFails`、`Load_MissingParametersOrSqlAfterMissingIni_StillFails` |
| 全缺失时无升迁门禁 | `Package_AllMigrationFilesMissing_PackagesServiceWithoutRuntimeOrStartupGate` 覆盖三格式；另检查全局 CLI 归档不含运行器和升迁调用 |
| 新目录贯穿交接 | 三格式包回归、独立 SQLite 进程交接、运行器路径边界检查；下述 DEB/TAR 实际安装及15份批次逐字节 SHA-256 校验 |

两个 RID 已重新 Native AOT 发布，检查 ELF x86-64/AArch64 和原生库架构；本轮 ARM64 未执行运行验证。第三方裁剪警告继续按 [警告审查](aot-warning-review.md) 记录，不据普通构建0警告推断 AOT 无警告。更新后的 x64 原生入口 SHA-256 为 `B2039454F1582FB84F82D9090D9D06590D0734A53ED92196E368B0A60FD9802C`。

通过本地 `src/bin/layout-packages/Zongsoft.Tools.Packager.0.8.3.nupkg` 更新全局工具，27项主程序集/运行器文件与工具包哈希一致。使用该全局命令实际执行 hosting 的 `web/default/pack.cmd` 两次，分别选择 deb、tar，其他输入为1.0.0、development、Debug、net10.0、x64、default，采用真实1.0.0升迁 INI 和 SQL。

| 项目 | DEB | TAR.GZ |
| --- | --- | --- |
| Pod | `zongsoft-layout-deb` | `zongsoft-layout-tar` |
| 安装环境 | Ubuntu24.04、ASP.NET10、systemd、Nginx | Rocky9.8、ASP.NET10、systemd、Nginx |
| 实际安装 | `apt-get install -y /opt/zongsoft.web@1.0.0_linux-x64.deb` | 解压后实际执行生成的 `install.sh` |
| 初始化结果 | 0→33张表，15份SQL，4个私有桶 | 0→33张表，15份SQL，4个私有桶 |
| 完成与门禁 | 原生check及Shell check成功；移除ready拒绝启动，恢复后active | 同左 |
| HTTP | 8070直连、8081代理、管理员登录均200 | 8071直连、8082代理、管理员登录均200 |
| RustFS | 9010 S3、9002控制台；四桶匿名访问403 | 9011 S3、9003控制台；四桶匿名访问403 |
| 数据端口 | MySQL3307、Redis6380 | MySQL3308、Redis6381 |

两个 Pod 使用各自独立的 MySQL8.4.8、Redis8.10.1、RustFS1.0.0-rc.5 容器及数据卷，用户密码与 hosting 配置一致。业务数据核对：Security_User=2、Province=34、City=344、District=3181、Street=39936。四桶为 attachments、learning、temporary、upgrading，ACL 无公共授权。两个包均核对计划0600、原生入口和Shell0755、SQL0644；归档与安装后的批次哈希均15/15通过。DEB另通过 `dpkg -V zongsoft.web`。

保留安装包：

- `D:\Zongsoft\hosting\web\default\zongsoft.web@1.0.0_linux-x64.deb`，76,765,842字节，SHA-256：`23C3BBB066E6572A5297EB6708ABC02323837A5B69556EE90BE2E0DBC752A0C3`。
- `D:\Zongsoft\hosting\web\default\zongsoft.web@1.0.0_linux-x64.tar.gz`，76,826,252字节，SHA-256：`7968F8E24D20134BFB5798830399A9CA0ED63BF8C407ED3918BDF7D26C58A6F5`；同目录保留生成的 `.sh` 安装入口。

人工验证记录与可重复的只读归档检查脚本位于 `D:\Zongsoft\hosting\web\default\bin\migration-verification\layout\README.md`，同目录保存 packages.json、各格式的 HTTP/桶/SQL校验/数据库/门禁证据。所有端口只绑定127.0.0.1。两个新 Pod 保留运行，前轮 RPM Pod 和安装包也继续保留；AOT构建容器已停止但保留缓存。本轮未向公共NuGet源发布。

### RPM 安装包补充更新（2026-09-10）

上轮只重新生成了 DEB/TAR，hosting 根目录中的 RPM 仍是旧产物。本次实际执行 pack.cmd 重新生成同名 RPM，计划为 `.migration/migration.json`，15份SQL均在 `.migration/.artifacts/0002-mysql/`。RPM清单、实际cpio载荷、15份SQL校验和、权限及原生入口哈希检查通过；`rpm -Kv` 的四项摘要全部OK。

当前 `zongsoft.web@1.0.0_linux-x64.rpm` 为76,899,072字节，SHA-256：`2E716BB66DB1818051BED03BF85F113CA9602B409F2B69ECD93FD40BEADC2BE9`。旧RPM已备份至 `hosting/web/default/bin/migration-verification/layout/previous-rpm/`。本次只更新并检查安装包，没有重新安装到已运行的RPM验证容器，其已安装版本仍为前轮产物。详细结果为同目录 `rpm-package.json`。


## S3 三项初始化配置（待验证）

新增默认加密、版本控制和桶标签的输入解析、计划描述及标准S3请求实现；同步双语指南与README。按用户要求，本次不运行构建、测试、AOT发布或容器验证。前述验证结论不覆盖这三项新增能力；当前全局工具、原生产物和已保留安装包尚未更新这些改动。
