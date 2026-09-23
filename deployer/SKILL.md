---
name: zongsoft-tools-deployer
description: 修改或审查 Zongsoft tools/deployer 的 .deploy 描述语法、变量与过滤表达式、路径/NuGet/delete 解析器、目录调节、目标框架就近选择、覆盖策略、部署命令或双语文档时使用；不用于 framework/upgrading 的进程外升级部署器。
---

# Zongsoft Tools Deployer

先阅读 [AGENTS.md](AGENTS.md)、[README.zh-Hans.md](README.zh-Hans.md) 和任务涉及的解析器、路径适配及命令入口。

## 处理流程

1. 从 `Program.cs`、`Deployer.Command.cs` 确认参数如何进入变量集以及默认 `.deploy` 的处理。
2. 从 `Deployer.cs`、`DeploymentContext.cs` 跟踪章节目标、条目执行顺序、计数和错误输出。
3. 语法或解析问题读取 `DeploymentEntry.cs`、`DeploymentResolverManager.cs` 和对应 resolver；不要只修 README 示例。
4. 路径选择问题读取 `DeploymentUtility.GetFiles`、`NugetAssets.ResolveLibraryPath/GetPackageFiles`和 `Normalizer.cs`。
5. NuGet 问题读取 `NugetResolver.cs`、`NugetUtility.cs`、`NugetGraph.cs`、`NugetAssets.cs`、`NugetRuntime.cs`，分别检查编排、包访问、版本求解、资产选择和 RID 回退。它们是独立类型；Graph 每次求解新建实例，资产展开用显式栈保持先根后依赖顺序。

## 必守语义

- 条目省略解析器名时直接使用 `path` 进行本地路径解析，解析器名不区分大小写；`nuget` 下载并解析包；`delete`/`remove` 删除目标文件且不接受目标路径部分。
- 源路径支持 `*`、`?`、`**`，目标路径由章节目录和条目目标共同决定。
- 显式 NuGet 缓存路径由 NugetAssets.ResolveLibraryPath 调整 lib 框架目录，路径内框架优先于 Framework 变量，保留后续路径和通配符后缀；仅检查缓存根以内的路径，根外或无匹配时保留原路径。普通包资产已选择框架，枚举时使用 resolveLibrary: false，内容中的 lib 子目录按普通目录处理。
- `DeploymentEntry.Get` 在变量展开前取第一个冒号划分解析器；没有冒号时名称为 `path`。GetResolver 对 null、空字符串和纯空白名称返回默认路径解析器，这是功能约定；未知名称返回 null。Windows 字面绝对路径使用 `path:D:/...` 或 `path:D:\dir\files.ext`，也可用变量/相对路径，不要把盘符误当解析器。
- 条目过滤支持变量存在、否定、候选值、`&`/`|` 组合以及目标框架版本比较。
- 变量支持 `$(name)` 与 `%name%`，名称不区分大小写；环境、`appsettings.json`、命令选项按既有覆盖顺序合并。
- 变量值按需递归展开，循环、缺失及超过 64 层失败；`destination` 先只用全部命令选项和环境变量定位，再加载目标配置，不受选项遍历顺序影响。
- 未指定 NuGet 包内路径时优先处理根 `.deploy`；否则先统一求解普通根包依赖闭包，再选择目标 RID/TFM 的托管、原生和内容资产。默认/自定义依赖忽略前缀不区分大小写，明确根请求不受过滤。
- 普通 NuGet 根请求在一次命令内统一求解，根版本固定，依赖选择满足全部范围的最低可用版本；无解与循环失败。同目标同内容的包资产去重，不同内容报冲突；显式 delete 保留顺序。该求解不等于完整 MSBuild restore。用最终文件版本和宿主首次调用验证。
- `overwrite` 支持 `alway`（始终覆盖）、`never`（仅复制目标不存在的文件）、`newest`（源文件修改时间不早于目标时复制），默认值为 `newest`。

## 安全验证

检查 Cake 还原、编译和测试使用同一 `Configuration`（来自 `--edition`）；测试项目只从 `test/*.csproj` 收集，避免执行构建输出中的副本。测试项目通过主项目获得 Core 依赖，不单独引用本地 Release DLL。

创建独立临时源目录和目标目录，确保目标解析后的绝对路径仍位于临时根下，再覆盖：

- 普通文件、目录和通配符复制；
- 变量替换与真假过滤分支；
- 三种覆盖策略和 `delete`；
- 本地可控 NuGet 包、依赖忽略和多目标框架选择；
- 缺失文件、无效语法、下载失败和路径冲突。

先构建 `Zongsoft.Tools.Deployer.slnx`，测试项目为 `test/Zongsoft.Tools.Deployer.Tests.csproj`，覆盖 net8.0/net9.0/net10.0。使用可重复的隔离测试；不要以真实应用部署代替测试。不要使用真实私有源凭据或运行发布推包。

命令支持管道与重定向，输出使用 TextWriter。失败返回非零退出码；预检查失败时不写目标，执行阶段失败停止后续操作。默认覆盖为 newest，正常跳过/删除单独统计。

部署会话持有计划和活动清单栈；包缓存以变量上下文隔离，每次会话重置。目标写入须位于 destination 内，拒绝链接路径；本地读取源可以位于根外。嵌套 .deploy 和 #@import 均检测循环。

dry-run/offline/explain/report、lockFile/locked、previous/prune 的行为见双语 README。只在用户指定 prune 且有前次成功报告时清理未修改的自有文件；Duplicate 是无操作记录，Copy→Delete 的最后有效操作会撤销所有权。dry-run 不写目标，但显式报告可写，在线解析可写缓存。日志、错误原因和报告保留原文，不执行脱敏替换；主命令的 explain/detail 输出操作及其来源。

真实宿主验证需要用户明确指定样例与操作范围；部署、Cake 构建和打包分别验证，记录实际执行范围与结果。

## 实现与代码约定

类型的 XML 注释说明主要功能与职责边界；流程注释重点解释回溯、状态隔离、执行顺序和所有权等不直观约定。访问级别遵循最小可见性，类内实现保持 private，跨类型生产协作才使用相应入口，不为测试扩大访问范围。

实现细节见 [中文](docs/implementation.zh-Hans.md) / [English](docs/implementation.md)。优先复用已引用的 Zongsoft.Core 和 NuGet API：CommandLine、Profile/集合、DictionaryExtension，以及 NuspecReader.GetContentFiles、NuGet.Frameworks、JsonRuntimeFormat/RuntimeGraph。Core 7.59.0 的 ProfileReader 内置导入，递归共享读取器并保护循环及 ProfileOptions.MaximumDepth 指定的层数上限（默认 64，正整数，根文件计一层）。ProfileOptions.Importing/Imported 均为 Action<ProfileContext>，上下文提供 FilePath、Depth、Referer、Profile；前置 Profile 为 null，后置为合并后的子文件，前后分别构造。deployer 通过 Importing 的 context.FilePath 记录导入文件哈希，根文件单独记录。回调抛异常终止整个加载，不提供禁用或跳过导入。合并按读取顺序替换有效引用并保留本地声明；ProfileWriter 按来源保存，deployer 不调用保存入口。

RID 资源使用 src/Resources/ 中固定的 dotnet/runtime v10.0.0 图谱，禁止从 MSBuildToolsPath 或运行机器 SDK 目录取图谱。图谱及许可证等第三方文件保留上游原始字节，不转换换行、编码或缩进；.gitattributes 的 -text 防止 Git 自动转换。更新快照时同步双语实现文档的版本/哈希及上游许可证，核对上游原件哈希并验证竞争候选顺序。通用包版本在仓库根 Directory.Packages.props 维护，NuGet.* 专用依赖在本项目通过 VersionOverride 维护。

非测试手写 C# 文件包含既有 /* */ 版权头，使用 Tab/CRLF。沿用中文 #region 分区，按字段、构造、属性、方法或职责组织；展开一行多语句并在方法阶段之间留空行，不使用 using 类型别名；#endregion 前不留空行。DeploymentPlan、DeploymentOperation、PackageSelection、DeploymentSession 各自独立文件，NugetRuntime 专管 RID 适配。README 引用 docs 的中英文实现文档，文档及图谱许可证随工具包分发。
资源维护：中性 Resources.resx 使用 ResXFileCodeGenerator 生成 Resources.Designer.cs；修改中性/中文资源后在 Visual Studio 运行自定义工具。调用处直接使用 Properties.Resources 的生成属性，使用 string.Format 格式化模板。卫星资源不生成第二套同名访问类。dotnet build 不自动触发该 IDE 自定义工具，提交生成输出时只允许空白规范化，不手写属性。

## 本地搜索

本地通配搜索统一调用 Core Searcher.Search，以 Searcher.Target 指定 Files、Directories 或 Both（默认），链接以逻辑名称匹配，内容取实际目标。独立选中的目录链接可展开，载荷内部目录链接跳过，文件链接保留名称并读取目标。INI/.deploy 按逻辑来源解析相对路径。详细规则见[本地搜索与源链接](docs/implementation.zh-Hans.md#本地搜索与源链接)。

代码规范采用 `Zongsoft.CodeAnalysis`，版本由仓库根 `Directory.Packages.props` 管理，检查方法与 SDK 要求见 [仓库规范](../AGENTS.md#代码规范检查)。
