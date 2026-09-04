---
name: zongsoft-tools-deployer
description: 修改或审查 Zongsoft tools/deployer 的 .deploy 描述语法、变量与过滤表达式、路径/NuGet/delete 解析器、目录调节、目标框架就近选择、覆盖策略、部署命令或双语文档时使用；不用于 framework/upgrading 的进程外升级部署器。
---

# Zongsoft Tools Deployer

先阅读 [AGENTS.md](AGENTS.md)、[README.zh-Hans.md](README.zh-Hans.md) 和任务涉及的解析器、调节器及命令入口。

## 处理流程

1. 从 `Program.cs`、`Deployer.Command.cs` 确认参数如何进入变量集以及默认 `.deploy` 的处理。
2. 从 `Deployer.cs`、`DeploymentContext.cs` 跟踪章节目标、条目执行顺序、计数和错误输出。
3. 语法或解析问题读取 `DeploymentEntry.cs`、`DeploymentResolverManager.cs` 和对应 resolver；不要只修 README 示例。
4. 路径选择问题读取 `DirectoryRegulator.cs`、`NugetRegulator.cs`、`TargetFramework.cs`、`TargetVersion.cs` 和 `Normalizer.cs`。
5. NuGet 问题读取 `NugetResolver.cs`、`NugetUtility.cs`、`NugetOutput.cs`，检查包源、缓存、依赖过滤和框架资产选择。

## 必守语义

- 空解析器名表示本地路径解析；`nuget` 下载并解析包；`delete`/`remove` 删除目标文件且不接受目标路径部分。
- 源路径支持 `*`、`?`、`**`，目标路径由章节目录和条目目标共同决定。
- 条目过滤支持变量存在、否定、候选值、`&`/`|` 组合以及目标框架版本比较。
- 变量支持 `$(name)` 与 `%name%`，名称不区分大小写；环境、`appsettings.json`、命令选项按既有覆盖顺序合并。
- 未指定 NuGet 包内路径时，优先处理根 `.deploy`，否则选择最接近 `Framework` 的 `lib/{framework}` 资产；保留依赖忽略前缀规则。
- `overwrite` 的 `alway`、`never`、`newest` 是现有公开拼写和行为，除非明确做兼容性变更，不擅自更名。

## 安全验证

创建独立临时源目录和目标目录，确保目标解析后的绝对路径仍位于临时根下，再覆盖：

- 普通文件、目录和通配符复制；
- 变量替换与真假过滤分支；
- 三种覆盖策略和 `delete`；
- 本地可控 NuGet 包、依赖忽略和多目标框架选择；
- 缺失文件、无效语法、下载失败和路径冲突。

先构建 `Zongsoft.Tools.Deployer.slnx`。没有现成测试项目时，优先增加可重复的隔离测试；不要以真实应用部署代替测试。不要使用真实私有源凭据或运行发布推包。
