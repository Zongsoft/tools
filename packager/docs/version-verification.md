# .version 重构验证

本文按实施阶段记录当时的验证结果；测试计数、产物路径、哈希和容器/全局工具状态不代表当前实时状态。旧路径与旧行为保留为历史证据，当前功能契约见[双语 README](../README.zh-Hans.md)、[升迁指南](migrations.zh-Hans.md)和[实现说明](implementation.md)。

首次重构在 Windows 临时目录验证源版本管理与内存包条目，未安装系统包、操作现有容器、执行 S3 实连验证或更新全局工具。后续 Core 重载适配及全局工具更新见文末记录。

## 本地 Core 依赖

使用 `D:/Zongsoft/framework/Zongsoft.Core/src/Zongsoft.Core.csproj` 当前源码，版本仍为 `7.59.0`，先构建再使用 `dotnet pack --no-build` 生成本地 NuGet 包。没有修改 Core API、项目版本或引入跨仓库项目引用。

隔离材料位于打包器 `src/bin/version-work/`：`packages/Zongsoft.Core.7.59.0.nupkg`、`cache/` 和 `NuGet.Config`。配置以包源映射将 `Zongsoft.Core` 指向本地包，其余依赖还原自 NuGet。首次验证使用的复现命令如下；Core 重载适配使用文末的新隔离目录，避免还原到同版本旧包：

```powershell
dotnet restore test/Zongsoft.Tools.Packager.Tests.csproj --packages D:/Zongsoft/tools/packager/src/bin/version-work/cache --configfile src/bin/version-work/NuGet.Config
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0 --no-restore
dotnet build src/Zongsoft.Tools.Packager.csproj --no-restore
```

Core 本地构建成功，报告其源码已有的 12 条警告（过时 PasswordUtility、未使用 category 参数）；本轮未改动该库。打包器最终 net8.0、net9.0、net10.0 构建均为零警告、零错误。

## 回归结果

打包器全部 **183** 项通过，零失败、零跳过，其中新增 **55** 项：

| 要求 | 验证证据 |
| --- | --- |
| 源文件缺失、损坏、直属查找、名称与 Edition 选择、覆盖、非零版本 | `VersionFileTests` 的 38 个用例；包含零版本的两段、三段、四段表示。 |
| 保留其他 Edition 的名称、版本和顺序，Core 格式保存 | `Save_SelectedEdition_PreservesOtherVersionsAndOrder`、`Save_SingleVersion_UsesCoreFormat`。 |
| 内存独立流与普通文件读取 | `Entry_MemoryContent_OpensIndependentReadableStreams`、`Entry_FileContent_StillReadsSource`。 |
| 三格式版本字节、唯一位置、权限、长度与 RPM 摘要 | `Package_VersionIdentity_ReplacesOldEntriesAcrossSelectionModes`，三格式 × 递归、显式、排除、根别名、普通与根别名同时存在五种选择。 |
| 保存异常保留产物并定位文件 | `Save_Failure_ReportsPackageAndPreservesArtifact`，另有下述实际命令检查。 |

## hosting 临时副本

从 hosting 的 `daemon/bin/Debug/net10.0`、`web/default/bin/Debug/net10.0` 复制实际宿主 DLL、runtimeconfig/deps 等宿主文件到 `src/bin/version-work/hosting/`，用于制包检查，不将真实应用配置或凭据纳入测试。daemon 源 `.version` 从该发布目录复制，内容为 `zongsoft.daemon@1.0.0.7`；web 临时源使用 `Zongsoft.Hosting.Web` 的 Default/Preview 两个 Edition，验证选中项更新。

- daemon 省略名称、Edition 和版本，成功生成 tar.gz、deb、rpm。
- web 指定小写名称、`--edition:default` 和 `--version:1.2.3`，成功生成三格式，身份采用源拼写 `Zongsoft.Hosting.Web` / `Default`；Preview 保持 `2.0.0`。
- 输出路径 `../packages/$(name)/$(edition)/$(version)/` 展开为 `packages/Zongsoft.Hosting.Web/Default/1.2.3/`。
- 首次验证独立读取六个包：`.version` 安装目标唯一，当时按 UTF-8 无 BOM、CRLF、末尾换行、0644 检查（后续已按用户要求取消包内末尾换行）；RPM 中记录的 SHA-256 与实际字节匹配。具名包内内容为 `Zongsoft.Hosting.Web-Default@1.2.3`，由 `ApplicationIdentifier` 生成。
- 预置同名包制造制包失败，命令返回非零，源版本字节与原有包均未改变。
- 以只读共享方式锁定源版本文件，使读取成功、保存失败：实际包已生成并保留，命令返回非零并说明源文件更新失败，没有输出整体成功提示。
- 未知源身份变量、显式空名称、无效版本均返回非零；显式空 Edition 成功按未指定处理。

制包和归档检查均为只读验收，不代表安装或服务启动验证。包文件保留于 `src/bin/version-work/hosting/packages/`；原始日志和检查脚本保留于 `src/bin/version-work/`。文件名、SHA-256、模式和长度明细见该临时目录中的 `hosting/archive-checks.json`。

## Core 流重载适配与全局工具替换

采用最新 Core `7.59.0` 源码重新制作本地依赖。源 `.version` 使用 `ApplicationVersion.Load(Stream)` / `Save(Stream)`，显式打开或创建直属文件；包内版本直接采用 `ApplicationIdentifier.Save(Stream)`，按用户调整不再追加末尾 CRLF，也不做其他内容转换。

本次隔离目录为 `src/bin/core-overloads/`，其 `NuGet.Config` 和 `cache/` 用于还原最新本地 Core；复现时将前文还原命令中的 `version-work` 改为 `core-overloads`。`test/PackageVersionTests.cs` 中 `Package_VersionIdentity_ReplacesOldEntriesAcrossSelectionModes` 的 15 种组合检查无尾部换行的实际内容和摘要。最终简化 `VersionFile.Load` 为四个参数，去掉 `hasName`、`hasVersion`，空白名称与空版本对象均按未提供处理；`Load_SingleVersion_UsesOmittedValuesAndCanonicalName` 补充空白名称及版本覆盖组合，`Load_MissingIdentityOrZeroVersion_Fails` 保留缺失源文件时的必填校验。全套 187 项通过、零跳过，Release 的 net8.0/net9.0/net10.0 构建均零警告、零错误。

该阶段的全局工具版本保持 `0.9.0`，实际内容已替换为该阶段的编译产物；后续源码变更不自动同步全局工具。由于同版本 `dotnet tool update` 未替换文件，备份原包后使用本地唯一包源卸载重装；没有推送公共 NuGet。96 个已安装工具文件的 SHA-256 全部与本地包一致。沿用两个已有 Native AOT 目录，均与此前发布记录哈希一致，未重新发布或运行 S3 升迁。

- 本地工具包：`src/bin/core-overloads/tool/Zongsoft.Tools.Packager.0.9.0.nupkg`。
- 工具包 SHA-256：`2C41F075619C38B002C85A7FFE848ACB92BFBF876E9452DB25576D71405DD6FB`。
- 文件核对：`src/bin/core-overloads/installed-hashes.json`。
- 使用 PATH 中的 `C:/Users/95558/.dotnet/tools/dotnet-pack.exe`，对 hosting daemon、web/default 的临时宿主副本分别生成三格式，共六个包；命令显式传入空名称和空白名称并省略版本，均使用源身份成功制包；包内版本字节、唯一位置、0644 和 RPM SHA-256 检查通过。
- 人工查看产物：`src/bin/core-overloads/hosting/packages/daemon/` 与 `src/bin/core-overloads/hosting/packages/web/`。源版本仍采用 Core 的规范多行格式，包内版本不带追加换行。

本轮未改动 hosting 原目录或已有容器，也未执行任何生成包的安装脚本。
