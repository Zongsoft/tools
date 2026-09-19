## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `dotnet-deploy` .NET 工具，读取 INI 风格 `.deploy` 文件，把本地文件或 NuGet 包内容解析并写入目标目录；详细流程见 [SKILL.md](SKILL.md)。

## 职责边界

- `Deployer` 与 `DeploymentContext` 编排描述文件、变量、目标目录和计数；`DeploymentSession`/`DeploymentPlan` 先解析检查后执行，写入须通过 `DeploymentPath`。
- `IDeploymentResolver`、`DeploymentResolverBase`、`DeploymentResolverManager` 定义条目解析扩展点；默认路径（`path`，GetResolver 对 null、空字符串和纯空白名称也返回默认实例）、`nuget`、`delete`/`remove` 语义应保持清晰分离；解析器名不区分大小写，Windows 字面绝对源路径示例使用 `path:` 前缀。
- `NugetAssets.ResolveLibraryPath` 负责显式缓存路径的框架适配；`DeploymentUtility.GetFiles` 展开文件和通配符，已选定包资产使用 `resolveLibrary: false` 避免重复匹配。路径选择不混入复制策略。
- `Normalizer` 负责 `$(name)`、`%name%` 变量替换；`AppSettingsUtility`、环境变量和命令选项共同提供变量。

## 高风险契约

- 章节指定目标目录，条目格式为 `resolver:argument = destination <filter>`；源路径支持通配符，过滤条件支持逻辑组合和目标框架版本比较。
- 变量名不区分大小写，加载顺序为环境变量、目标应用 `appsettings.json`、命令选项，后加载值覆盖先前值。
- NuGet 解析器会访问包源、解析依赖、选择最适用的框架资产并可能递归执行包内 `.deploy`；修改时检查循环、路径越界和依赖忽略规则。
- `delete`/`remove`、覆盖策略和目标规范化可破坏现有文件。所有测试目标必须是已确认的临时目录。
- 命令、选项或消息变化时同步 `README.md`、`README.zh-Hans.md` 和 `.resx`；中性资源 `Resources.resx` 使用 `ResXFileCodeGenerator` 重新生成 `Resources.Designer.cs`，业务代码通过生成属性取资源，不使用字符串键动态查询；生成属性不手工维护。

## 验证

- Cake 的 `restore` 显式传递 `--edition` 对应的 `Configuration`，与编译和测试一致；测试项目只从 `test/*.csproj` 收集，不递归扫描构建输出中的副本。测试通过主项目获得 Core 依赖，不单独添加本地 Release DLL 引用。

- 最小构建：`dotnet build Zongsoft.Tools.Deployer.slnx -f net10.0`。
- 解析行为使用临时 `.deploy`、临时源/目标和本地 NuGet 缓存覆盖路径、变量、过滤、覆盖、删除和失败分支。
- 目标框架或依赖解析变化再覆盖 net8.0、net9.0、net10.0 代表性包。
- 未经明确要求，不对真实应用目录执行 `dotnet deploy`，不访问私有源，不运行 Cake `pack`。

- 测试项目在 `test/`，支持 net8.0/net9.0/net10.0；验证范围包括计划、锁定、清理、Profile 导入、本地搜索及 NuGet 资产选择。命令和实现说明见双语 README 与 docs；避免通过 Cake 发布任务验证。

## 代码与实现文档

- 非测试手写 C# 使用既有 /* */ 版权头、Tab/CRLF、适量中文 #region 分区；方法内按阶段留空行，不写一行多语句，不使用 using 类型别名；`#endregion` 前不留空行。
- 类型使用 XML 注释说明主要职责，关键流程注释说明顺序、边界或设计原因。访问级别按封装与生产协作需要确定，不为单元测试把 private 改为 internal 或 public；通过可用的生产入口验证行为。
- 优先复用已引用的 Core/NuGet API，复用边界见 [实现细节](docs/implementation.zh-Hans.md)；不以通用 API 替换并丢失路径边界、循环保护或所有权契约。
- RID 图谱为仓库内固定资源，不引用构建 SDK 私有目录中的文件；更新时同步版本、哈希、许可证和回归。
- RID 图谱、上游许可证等第三方文件保留原始字节和格式，不做 CRLF 或缩进转换；对应 .gitattributes 设置 -text，更新后校验与上游文件的哈希一致。
- 实现变化同步 docs 中英文文档、双语 README 入口与 SKILL；打包时检查文档和上游许可证均随包分发。
- 日志与报告按原文输出，不做脱敏；使用生成资源属性和 string.Format，不增加 Diagnostics 消息包装层。

## 本地搜索

本地通配搜索统一复用 Core Searcher，链接以逻辑名称匹配，内容取实际目标。独立选中的目录链接可展开，载荷内部目录链接跳过，文件链接保留名称并读取目标。INI/.deploy 按逻辑来源解析相对路径。详细规则见[本地搜索与源链接](docs/implementation.zh-Hans.md#本地搜索与源链接)。

代码规范采用 `Zongsoft.CodeAnalysis`，版本由仓库根 `Directory.Packages.props` 管理，检查方法与 SDK 要求见 [仓库规范](../AGENTS.md#代码规范检查)。
