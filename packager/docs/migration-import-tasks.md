# 升迁配置导入任务清单


> 2026-09-13 后续重构：以下保留各阶段实施记录。当前导入已内化到 Reader，ProfileOptions 提供 Action<ProfileContext> 成对通知，上下文包含 FilePath、Depth、Referer、Profile；通用指令接口与注册集合已移除；现行方案与验收见 Core 的 PROFILE-BUILTIN-IMPORT-TASKS.md 及各项目机制文档。

用户要求：让 packager 支持导入。

- [x] I01：通过 Core ImportDirective 加载 INI 与 ENV，逐文件校验并保留错误来源。
- [x] I02：依据条目来源解析 SQL 路径与查找参数，保留覆盖、顺序及去重规则。
- [x] I03：补充嵌套导入、ENV 合并、循环、来源定位和失败分支测试。
- [x] I04：完成 packager 三目标框架构建和测试回归。
- [x] I05：同步双语 README、升迁指南、实现说明和项目约定。
- [ ] I06：Linux/macOS 原生文件链接行为验证（未执行不能勾选）。

本轮只在临时目录解析输入及验证产物，不执行真实迁移、安装或发布。

## 验证记录（2026-09-13，Windows）

- 使用已构建的本地 Core 7.59.0 包；通过临时包源和隔离缓存还原，不修改包版本、不引入项目引用。
- `dotnet build Zongsoft.Tools.Packager.slnx --no-restore --no-incremental -p:PublishAot=false`：packager 的 net8.0/net9.0/net10.0 及 migrator 的 net10.0 构建通过，0 警告、0 错误。
- `dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0 --no-build --no-restore -p:PublishAot=false`：264/264 通过，无失败或跳过，含 90 项升迁加载聚焦测试。报告：`test/TestResults/migration-imports-full.trx`；聚焦报告：`test/TestResults/migration-imports-focused.trx`。
- `dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0 --no-build --no-restore -p:PublishAot=false`：33/33 通过，无失败或跳过。报告：`migrator/test/TestResults/migration-imports-regression.trx`。
- 原 Windows 链接拒绝测试已由 LocalSearcher 接入改为逻辑来源接受测试，当前验证见 ../LOCAL-SEARCHER-TASKS.md。Linux/macOS 原生验证、Native AOT 发布与实际安装未执行。
- CRLF、手写代码 Tab 缩进、Markdown 本地链接目标及 `git diff --check` 检查通过。

| 行为 | 测试证据 |
| --- | --- |
| “让 packager 支持导入” | `MigrationLoaderTests.Load_ImportDirectivesLoadIniAndParameters` |
| 嵌套来源、参数范围与交错顺序 | `MigrationImportTests.Load_NestedImports_UsesEachSqlOriginAndItsOwnParameters`、`Load_InterleavedOrigins_PreservesEffectiveEntryOrder`、`Load_ImportedBuckets_UseSourceSpecificParameters` |
| ENV 合并及读取顺序覆盖 | `Load_ParameterImports_ComposeSelectedCandidateWithoutOtherCandidates`、`Load_ImportedAndLocalSameKey_UsesLastDeclarationSource` |
| 子文件校验与实际来源错误 | `Load_InvalidImportedProfile_ReportsChildPath`、`Load_DuplicateImportedParameterKey_ReportsChildWithoutValues`、`Load_ImportedSqlFailure_ReportsChildEntry`、`Load_ImportedParameterExpansionFailure_ReportsSourceAndNoSecret` |
| 循环、可选导入、深度、链接和失败重试 | `Load_ImportCycle_FailsAndSameLoaderCanRetry`、`Load_ParameterImportCycle_ReportsParameterSource`、`Load_MissingOptionalImport_LeavesLocalTask`、`Load_DefaultImportDepth_Enforces64Files`、`Load_ImportedLink_IsRejected` |

除首行外，测试均位于 [MigrationImportTests.cs](../test/MigrationImportTests.cs)。来源规则详见 [中文指南](migrations.zh-Hans.md#导入配置文件) 和 [English](migrations.md#importing-configuration-files)。

## 内置导入后续验收（2026-09-13）

- [x] 后续内置导入接入完成：MigrationProfile 通过 ProfileImportOptions 配置前后通知，源码无通用指令接口；主测试 264/264、migrator 33/33 和三框架构建通过。

- [x] ProfileContext 接入：MigrationProfile 前置回调使用 context.FilePath，后置使用 context.Profile；两者均为 Action<ProfileContext>。最终本地 Core 7.59.0 包三框架构建通过，net10.0 主测试 264/264、migrator 33/33，最终 TRX 为 profile-context-final-packager.trx 和 profile-context-final-migrator.trx。

- [x] MaximumDepth 与资源清理接入验证：沿用 Core 默认 64 层，清理主项目 MigrationWildcardInvalid 和运行器 DatabaseProviderUnknown 的中英文版本，重新生成属性；最终本地 Core 包三框架构建、主测试 264/264 和运行器 33/33 通过。
