## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `dotnet-deploy` .NET 工具，读取 INI 风格 `.deploy` 文件，把本地文件或 NuGet 包内容解析并写入目标目录；详细流程见 [SKILL.md](SKILL.md)。

## 职责边界

- `Deployer` 与 `DeploymentContext` 编排描述文件、变量、目标目录和计数。
- `IDeploymentResolver`、`DeploymentResolverBase`、`DeploymentResolverManager` 定义条目解析扩展点；默认路径、`nuget`、`delete`/`remove` 语义应保持清晰分离。
- `IDirectoryRegulator` 及 Directory/NuGet regulator 负责目录或框架路径调节，不应混入复制策略。
- `Normalizer` 负责 `$(name)`、`%name%` 变量替换；`AppSettingsUtility`、环境变量和命令选项共同提供变量。

## 高风险契约

- 保持章节目标、条目 `resolver:argument = destination <filter>`、通配符、条件组合和目标框架比较的兼容性。
- 变量名不区分大小写，加载顺序为环境变量、目标应用 `appsettings.json`、命令选项，后加载值覆盖先前值。
- NuGet 解析器会访问包源、解析依赖、选择最适用的框架资产并可能递归执行包内 `.deploy`；修改时检查循环、路径越界和依赖忽略规则。
- `delete`/`remove`、覆盖策略和目标规范化可破坏现有文件。所有测试目标必须是已确认的临时目录。
- 命令、选项或消息变化时同步 `README.md`、`README.zh-Hans.md` 和 `.resx`；生成的 `Resources.Designer.cs` 不手工维护。

## 验证

- 最小构建：`dotnet build Zongsoft.Tools.Deployer.slnx -f net10.0`。
- 解析行为使用临时 `.deploy`、临时源/目标和本地 NuGet 缓存覆盖路径、变量、过滤、覆盖、删除和失败分支。
- 目标框架或依赖解析变化再覆盖 net8.0、net9.0、net10.0 代表性包。
- 未经明确要求，不对真实应用目录执行 `dotnet deploy`，不访问私有源，不运行 Cake `pack`。
