---
name: zongsoft-tools-containerizer
description: 修改容器制作清单、服务规划、镜像与缓存、本机预演、交付协议或 Linux 原生执行器，并选择相应的隔离验证。
---

# Containerizer 开发指南

先阅读 [AGENTS.md](AGENTS.md)、[双语 README](README.zh-Hans.md)、[实现说明](docs/implementation.zh-Hans.md)、[模板参考](docs/templates.zh-Hans.md)，以及 [解决方案](Containerizer.slnx)、受影响项目和 [构建脚本](build.cake)。命名与格式遵循仓库规则及 Zongsoft guidelines。

README 面向使用者；implementation 解释当前契约和实现；本文件提供维护方法。同步中英文正式文档，只记录最终行为、能力与限制，不加入任务编号、讨论备选、临时性能测量或逐次测试计数。

## 修改入口

| 需求 | 主要源码 |
| --- | --- |
| CLI、source、版本、输入选择 | src/ManifestFactory.cs、ContainerizeCommand*、ContainerManifest.cs |
| .settings、参数和环境 | src/ServiceDefaults.cs、ServiceSettings.cs、ServiceOptions.cs |
| 服务准备、模板、镜像规则 | src/ServicePlanner.cs、TemplateCatalog.cs、ImageReference.cs |
| 包元数据与应用入口 | src/PackageReader*、ApplicationPlanner.cs、ApplicationHealth.cs |
| Web 交接及资源 | src/WebPackage.cs、`WebIngress`*；核对 Packager 实际生成资产 |
| 应用镜像、运行环境缓存 | src/ApplicationImageBuilder*、RuntimeEnvironmentCache.cs |
| 引擎操作、服务镜像适配 | src/ContainerEngine.cs、ServiceImagePreparer.cs、BuildResources.cs |
| bootstrap、最终交付与发布 | src/BootstrapPackageBuilder.cs、`DeliveryBuilder`*、BuildStorage.cs、ComposeWriter.cs |
| 预演会话、端口和内部缓存 | src/RunCommand.cs、`RunContext`* |
| 协议、完整性及进程 | .shared 下的模型、DeliveryBundle.cs、Files.cs、IProcessRunner.cs、ProcessRunner.cs |
| 安装状态、恢复和卸载 | executor/src/InstallationManager*、InstallationStore.cs、`DockerHost`* |

制作端可用 Core；执行端保持 BCL-only。共享内容继续源码链接，不引入共享 DLL、DI 容器或通用状态机。已有 `IProcessRunner` 和 `IInstallationHost` 是生产调用边界；不为测试增加接口、回调、反射访问或包装入口。原本只需 private 的成员保持 private，覆盖不到的细节如实说明。

## 修改时保持的契约

- CLI source 自身相对于调用目录，其余受管本地路径相对于最终 source；不要把 Linux 目标路径交给宿主平台路径规范化。
- plan 共用规划但不连接引擎，不读取 .mirrors，不写 .settings。只有组件列表完整制作成功后补缺 tag；任何清单回放不重新合并默认文件。失败保留编辑后的草稿。
- 输入包描述只在同次准备中复用；提取、哈希和交付完整性校验不能因复用描述而跳过。组件顺序由只读组件关联保留。
- JSON 协议当前为 1，字段按模型语义命名、camelCase 序列化，不增加旧字段别名或为命名调整递增协议号。修改模型时同时检查制作、消费、生成上下文和断言。
- 区分来源 tag、目标 manifest digest、index digest、image ID 和完整本地 reference。固定身份不得回退；制作导出不污染来源镜像的发行标签。
- 缓存保留锁、哈希校验及代际原子切换。runtime 缓存不能收录应用或业务数据；run 缓存必须完成内部清理并停引擎后才标记干净。
- 维护状态先保存，再调用宿主；宿主先保存原重启策略再修改容器。恢复沿用原事务，失败升迁只允许显式重试，不自动恢复旧应用或重跑 SQL。
- Store 管文件操作，Manager 管清理顺序和检查点。purge 先核对归属、链接和挂载，安装注册最后删除；应用锁始终在可删除资产之外。
- 保留现有本地化、颜色和 stdout/stderr 语义。资源在中英文 .resx 中同步修改，通过 ResXFileCodeGenerator 更新访问器。
- 不运行真实现场安装，不使用真实数据库连接，不修改全局工具或其他容器，不为适配实现重写用户样例。

## 构建与验证

使用 [仓库要求的 SDK/分析器工具链](../AGENTS.md#代码规范检查)。目标框架、工具版本和专用依赖以各项目文件为准；通用版本与构建属性由仓库根配置维护。

默认使用 Release，它从包源引用 Core；Debug 使用相邻 framework 仓库的本地 Core 输出。不要为单工具任务顺带构建其他工具或修改其依赖。

### 托管代码

从本目录执行：

```powershell
dotnet restore Containerizer.slnx
dotnet build Containerizer.slnx -c Release --no-restore -p:ZongsoftCodeStyleStrict=true
dotnet test test/Zongsoft.Tools.Containerizer.Tests.csproj -c Release --no-build --no-restore
dotnet test executor/test/Zongsoft.Tools.Containerizer.Executor.Tests.csproj -c Release --no-build --no-restore

$projects = @(
	'src/Zongsoft.Tools.Containerizer.csproj',
	'executor/src/Zongsoft.Tools.Containerizer.Executor.csproj',
	'test/Zongsoft.Tools.Containerizer.Tests.csproj',
	'executor/test/Zongsoft.Tools.Containerizer.Executor.Tests.csproj'
)
foreach($project in $projects)
{
	dotnet format style $project --no-restore --verify-no-changes --diagnostics IDE0049
	if($LASTEXITCODE -ne 0) { throw "Style check failed: $project" }
}
```

多目标制作项目不要加 -f，确保覆盖全部框架。先用相关既有入口测试验证修改，再完成本工具回归；不要增加只复刻实现的测试或扩大成员作用域。涉及文件行为使用临时 source/output 和本地测试包。

### 按职责选择回归

| 修改范围 | 应检查行为 |
| --- | --- |
| 输入与默认值 | source 相对路径、变量逐值求值/转义、显式空值、默认合并、包选择、组件顺序、自动 nginx、plan 无引擎、成功补缺及失败保留草稿 |
| 镜像与缓存 | 目标 digest 与 index/ID 区分、固定回放、平台拒绝、镜像源回退、缓存锁/校验/refresh 失败保留、临时资源及匿名卷清理 |
| 应用和 Web | 包格式、入口/运行时歧义、`Listen`、绑定默认关系、通配探测名、资源归属、重复/冲突映射、Host/SNI、证书、404/5xx 与重定向 |
| 进程与预演 | stdout/stderr、退出码与取消、创建中断、失败现场保留、就绪后退出、独立清理令牌、端口占用、数据隔离和缓存复用 |
| 执行器 | 在宿主调用边界检查磁盘状态；保存失败、部分停机失败、原事务恢复、升迁不自动重跑、普通卸载重装及 purge 中断重试 |

测试宿主应从已有生产边界观察持久状态和副作用，不以调用私有方法替代流程验证。未覆盖项与未完成的平台运行验证明确区分。

### Linux 与 Native AOT

执行器或共享进程代码改变后，在 [专属 AOT Pod](executor/build/containerizer.linux-x64.yaml) 中执行 Linux 测试及两个架构的发布。先检查当前 Pod/VM 状态及 YAML 中宿主挂载路径；已有适用环境可复用，路径不同则配置专属环境。只管理本工具资源，结束后恢复任务前的启动状态。

```powershell
podman container exists zongsoft-containerizer-aot-builder
if($LASTEXITCODE -eq 0)
{
	podman start zongsoft-containerizer-aot-builder
}
else
{
	podman kube play executor/build/containerizer.linux-x64.yaml
}
if($LASTEXITCODE -ne 0) { throw 'Unable to prepare the dedicated AOT environment.' }
podman exec zongsoft-containerizer-aot-builder bash executor/build/setup.sh
podman exec zongsoft-containerizer-aot-builder /aot/dotnet/dotnet test executor/test/Zongsoft.Tools.Containerizer.Executor.Tests.csproj -c Release --artifacts-path /aot/artifacts/containerizer-tests -p:ZongsoftGuidelinesSynchronization= -p:ZongsoftCodeStyleStrict=true
podman exec zongsoft-containerizer-aot-builder bash executor/build/publish.sh linux-x64 Release
podman exec zongsoft-containerizer-aot-builder bash executor/build/publish.sh linux-arm64 Release
```

Pod 将仓库配置只读挂载到容器根，Linux 命令使用 -p:ZongsoftGuidelinesSynchronization= 禁用配置同步。publish.sh 保留 ELF、依赖和编译日志，x64 还执行 --protocol；ARM64 只完成编译时不能标成运行通过。共享源码变更后制作工具包前要更新两个原生载荷。

真实镜像和生命周期验证使用测试配置及专属隔离容器，不操作已有部署；应用监听通过不等于业务测试通过。平台能力与证据边界同步到双语 implementation，不以测试数或一次耗时作为支持声明。

### 本地制包

Cake 任务边界：

| 任务 | 行为 |
| --- | --- |
| default / test | 恢复并运行两个测试项目，不发布 |
| executor | 准备专属 Pod 并构建两个 Linux AOT 载荷 |
| compile | 要求已有对应配置的两个载荷，构建解决方案并生成本地工具包 |
| build | executor + compile |
| pack | build 后推送 NuGet；发布才使用 |

需要制包时执行 `dotnet cake --target build --edition Release`；已有最新载荷时可使用 compile。普通源码验证不必制包。需要验证工具安装时使用独立 tool-path，版本从项目读取：

```powershell
$toolVersion = dotnet msbuild src/Zongsoft.Tools.Containerizer.csproj -getProperty:Version -p:Configuration=Release -nologo
dotnet tool install Zongsoft.Tools.Containerizer --tool-path ./.cache/tool --version $toolVersion --source ./src/bin/Release --no-http-cache
./.cache/tool/dotnet-containerize --help
```

核对工具包中的模板、两个原生载荷及本地化资源；排除符号/诊断文件。普通任务不执行 pack，不更新全局工具。

## 文档与收尾

纯文档修改不构建。核对命令解析、字段、默认值、错误/状态与源码，保留用户关心的效果；细节放 implementation 或模板参考，操作步骤放本文件。

检查双语对应、相对链接及锚点、CRLF、Tab 和 Shell LF；不要格式化第三方或任务外文件。最后查看实际差异并执行：

```powershell
git -c core.whitespace=cr-at-eol diff --check
git status --short
```

保留任务开始时的未提交修改；不重置、覆盖或提交用户工作。报告本次变更、已验证范围及实际限制，不把未执行的验收写成通过。
