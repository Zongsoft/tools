# 实现细节

[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

本文说明当前源码的职责、处理顺序和维护约束。使用语法与选项见 [README](../README.zh-Hans.md)；重构验收和真实业务样本的未完成事项在源码仓库的 `REFACTOR-TASKS.md` 中记录。

## 入口与职责

`Program.Main` 使用 Zongsoft.Core 的 `CommandLine` 解析命令，建立取消令牌，并调用 `Deployer.CreateVariables` 与 `DeployManyAsync`。没有参数时读取当前目录的 `.deploy`。正常结果退出 0，失败退出 1，取消退出 130。执行不依赖交互控制台句柄，日志输出到 `TextWriter`。

| 文件/类型 | 职责 |
| --- | --- |
| `Deployer.Command.cs` | 组合变量，定位目标应用配置。 |
| `Deployer.cs` | 管理一次调用、遍历清单、收集请求及预检查错误。 |
| `DeploymentSession.cs` | 持有本次计数、活动清单栈、包请求、选中版本和展开暂存列表。 |
| `DeploymentPlan.cs`、`DeploymentOperation.cs`、`PackageSelection.cs` | 保存计划、操作来源、包版本与哈希；每种类型独立维护。 |
| `Deployer.Execution.cs` | 验证目标冲突、锁与历史记录，然后执行操作。 |
| `DeploymentResolverBase`、`DeploymentResolverManager`、`NugetResolver` | 将本地文件、delete/remove 和 NuGet 条目转换为计划。 |
| `DeploymentPath` | 验证目标边界、拒绝写入链接路径，并提供部署清单路径标识。 |
| `NugetUtility` | 包源、元数据、版本、下载、缓存路径及包摘要。 |
| `NugetGraph` | 一次依赖求解的根请求、约束、候选回溯及循环诊断。 |
| `NugetAssets` | 显式缓存路径适配及 TFM/RID/内容资产选择。 |
| `NugetRuntime` | 固定 RID 图谱、平台/架构别名及回退链。 |

`Deployer` 对同一实例的并发调用报错；顺序调用创建新会话并重置该变量上下文的包缓存。`DeploymentCounter` 分别记录复制成功、跳过、删除和失败。预演不把计划文件计为实际复制。

## 复用现有库

Release 引用 Zongsoft.Core 7.59.0，当前 Debug 配置引用本地 Core 程序集；NuGet.* 保持 7.9.0，版本在本工具项目文件中维护。没有为此次整理增加直接 NuGet 包依赖。

| 能力 | 复用接口与本工具保留部分 |
| --- | --- |
| 命令行 | Core `CommandLine.Parse/Get`；工具只组织选项、退出码和取消。 |
| 环境字典 | Core `DictionaryExtension.ToDictionary`，指定不区分大小写的比较器。 |
| INI 清单 | Core `Profile`、`ProfileOptions`、`ProfileContext` 及章节/条目集合；工具不再实现 INI 解析器。 |
| 本地化 | `ResXFileCodeGenerator` 生成 `Properties.Resources` 强类型属性；调用处直接取属性并使用 `string.Format` 格式化；日志和报告保留原始消息。 |
| 包元数据和内容规则 | NuGet `NuspecReader`、`PackageIdentity`、`VersionRange`；`GetContentFiles` 每个包资产展开读取一次，取代逐文件重新解析 XML。 |
| TFM | NuGetFramework 表达框架身份，其框架/平台版本使用 System.Version；NuGetVersion 和 VersionRange 表达包版本与约束。无调用的 TargetFramework/TargetVersion 已删除。工具保留包括 ^ 在内的显式框架过滤规则，与资产的最近兼容组选择分别处理。 |
| RID | NuGet `JsonRuntimeFormat.ReadRuntimeGraph`、`RuntimeGraph.ExpandRuntime`；删除手写 JSON 图谱解析与队列遍历。 |

Core 7.59.0 由 ProfileReader 内置处理导入，提供循环/深度保护；ProfileOptions 提供两个 Action<ProfileContext> 导入回调。deployer 使用 Importing 登记导入文件哈希，根描述文件单独登记。appsettings 点号键、目标边界、链接拒绝和文件所有权仍由部署工具处理。

## 从清单到执行

1. **准备变量与会话。** 验证覆盖策略和布尔选项，确定目标根；locked 模式先读取锁文件。
2. **解析全部输入。** 展开章节与条目，收集文件、删除项和普通 NuGet 根请求。先检查过滤条件，再替换有效分支中的变量。
3. **求解包依赖。** 统一处理同次命令中的普通根包，选出满足约束的依赖闭包。
4. **展开包资产。** 将包占位操作替换为实际文件操作，保留原清单顺序和来源。
5. **验证计划。** 检查路径、源文件哈希、重复/冲突目标、报告路径、版本锁和旧文件所有权。任一预检查失败都不进入目标文件执行。
6. **执行或预演。** 正常执行前再次检查目标路径与源哈希；预演只保留计划。完成或失败均可写显式报告。

解析和在线求解阶段可能下载到 NuGet 缓存，因此 dry-run 不表示完全不写磁盘。`offline=true` 使用已解压且有有效 nuspec 的本地缓存；缺包失败。

## 变量、清单与路径

`DeploymentEntry.Get` 在变量展开前以源条目的第一个冒号拆分解析器名与参数。没有冒号的条目直接使用 `path` 名称。`DeploymentResolverManager.GetResolver` 将 null、空字符串及纯空白名称解析为默认路径解析器；`path`、`nuget` 和 `delete`/`remove` 按不区分大小写的方式匹配，未知名称返回 null。空名称选择默认路径解析器是其功能约定。默认路径解析器自身的 Name 为空字符串，`path` 是选择该实例的解析器名。Windows 字面绝对源路径使用 `path:D:\dir\files.ext` 或 `path:D:/dir/files.ext`，避免盘符被解析为解析器名；相对路径可以省略前缀或显式使用 `path:`。

变量名不区分大小写，覆盖顺序为环境变量、目标应用 `appsettings.json`、命令选项。目标目录先由命令或环境确定，才能加载该目录的配置。JSON 支持注释和尾随逗号；嵌套对象和数组生成 `Database.Name`、`Items[0].Name` 等键。

`Normalizer` 处理 `$(name)` 和 `%name%`，保留 URL 中的斜线。部署路径展开发现未定义变量时报错；被过滤掉的分支无需提供变量。过滤组合保持从左到右求值，源过滤与目标过滤都必须满足。

`NugetAssets.ResolveLibraryPath` 集中处理显式缓存路径的框架适配：只解析相对 NuGet_Packages 根的路径，识别 lib 目录；路径内的框架优先于 Framework 变量，通过官方 NuGetFrameworkUtility.GetNearest 选择最近框架并保留后续子路径。根外、未指定目标框架或无适用目录时返回原路径。原 NugetRegulator、DirectoryRegulator 和 IDirectoryRegulator 已移除。

`DeploymentUtility.GetFiles` 先展开包目录并适配框架，再用 Core Searcher 搜索目录及文件，捕获信息用于保留目录后缀。`GetPackageFiles` 的资产目录已完成 RID/TFM 选择，传入 resolveLibrary: false 直接枚举，避免重复框架匹配；content 中名为 lib 的子目录不会被重新解释为框架入口或产生额外文件。

glob 的 `**` 匹配零层或多层目录，`?` 匹配目录名中的单字符；展开时保留目标相对结构，并跳过目录链接遍历。Core Searcher 返回逻辑路径及通配捕获，部署适配层据此生成目标后缀。

目标路径先绝对化，再以相对路径验证其位于 destination 内；写路径及其祖先拒绝 reparse point，包括悬空链接。源文件可以位于目标根外。清单源路径通过解析链接取得标识，活动栈检测直接/间接循环，最大深度为 64；重复清单在前一次展开完成后可用于另一个目标目录。`#@import` 有独立活动集合，并将实际读取的导入文件纳入哈希。缺失的可选 import 保留 Core 原有语义。

## 包版本与资产

各 NuGet 组件是独立类型，不再作为 NugetUtility 的 partial 文件。每次求解创建私有 NugetGraph 实例；回溯分支复制选择表，有序活动链用于循环诊断。NugetResolver 使用显式栈展开已求解的依赖，保留先根后依赖的深度优先顺序，并在资产枚举中检查取消。托管与原生资产共用 RID 遍历，但各自独立选择回退组。

类型的 XML 注释说明职责，关键流程注释记录顺序与状态边界。构造、搜索、约束收集、框架匹配及缓存细节保持私有；内部入口仅用于生产组件协作，不为单元测试扩大可见性。测试通过实际部署与包访问入口验证行为。

NugetUtility 只保留以本次变量字典为作用域的包访问缓存；键同时包含包源、绝对缓存根、离线与预发布模式，切换这些配置不会复用其它上下文的结果。重复的 DownloadDependentPackageAsync 入口及无调用的 Output/NugetOutput 已删除。依赖回归通过实际部署入口检查计划和输出文件。

普通根包版本固定；依赖按所有已知版本范围筛选，优先选最低可用版本，并回溯解决后续冲突。无解、循环或搜索上限触发错误。当前上限为 10,000 次搜索、512 个选中包；不同 TFM 的约束均参与检查。

默认忽略 `System.`、`Microsoft.Extensions.`、`Zongsoft.` 传递依赖，自定义前缀追加并忽略大小写；明确写出的根请求不被忽略。未固定版本默认不选预发布，`prerelease=true` 或依赖明确要求预发布下限时允许预发布候选。

这个求解器处理部署文件集合，不执行 MSBuild/buildTransitive，也不实现完整 restore 的全部冲突消解规则。插件目录不同不等于程序集加载隔离；需要独立版本时应先确认宿主契约，再拆分调用。

包根 `.deploy` 优先于普通资产展开；显式包内路径只请求指定文件。普通包选择策略如下：

- 托管资产按 RID 回退寻找有兼容 TFM 的 `runtimes/{rid}/lib/{tfm}` 组，选中后替代整个普通 `lib/{tfm}` 组；找不到时使用普通 lib。
- 原生资产独立选择首个存在的 `runtimes/{rid}/native` 组，不强制与托管资产来自同一层 RID。
- `contentFiles/any/{tfm}` 应用 nuspec 的 include/exclude、copyToOutput 和 flatten；无复制规则的内容不输出。旧 content 目录继续递归复制。
- 同目标、同内容的包资产保留 Duplicate 来源记录，实际只复制一次；不同内容报冲突。显式 delete 结束此前去重范围。
- 不跨插件目录移动公共 DLL；lib 中的 XML 文档继续保留。

## 固定 RID 图谱

图谱在仓库的 `src/Resources/PortableRuntimeIdentifierGraph.json` 中，以逻辑名 `Deployer.RuntimeGraph.json` 嵌入程序集。构建不读取 `$(MSBuildToolsPath)` 下的图谱；运行不查询 SDK 目录，也不下载图谱。三种目标框架使用同一快照。

来源为 [dotnet/runtime v10.0.0 的 PortableRuntimeIdentifierGraph.json](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.NETCore.Platforms/src/PortableRuntimeIdentifierGraph.json)。图谱与许可证保留上游原始字节及格式（当前为 LF 换行），不做换行、编码或缩进转换；.gitattributes 对这两个文件设置 `-text`，避免 Git 自动转换。仓库图谱与上游原件的 SHA-256 均为 `2057797FE984CF7BEB26911F3BF7EB58B6854B403563AA661196A2FD16A77BE9`。上游 MIT 许可证保存在 `src/Resources/dotnet-runtime-LICENSE.txt`，工具包带有 `licenses/dotnet-runtime-LICENSE.txt`。

NuGet 的图谱扩展按先近后远的顺序返回候选。工具只在查询前规范化 `windows→win`、`mac/macos→osx`、`x32→x86` 别名；例如 linux-musl-x64 通过图谱回退，而不是用字符串截断猜测。未知 RID 没有自动补造的回退关系。

更新图谱时应选择明确的上游版本，保留图谱及许可证的原始字节，核对并更新本节哈希，并运行竞争候选顺序回归；升级本地 SDK 不会自动升级工具内的 RID 策略。

## 报告、锁与清理

计划记录清单哈希、包版本/引入者、包内容哈希及每个操作的源、目标、状态和文件哈希。包内容哈希包含排序后的相对路径和文件哈希，排除 nupkg、nupkg.sha512、.nupkg.metadata，拒绝将缓存元数据变化误当内容变化。

锁定模式从锁中选择根包与传递依赖版本，并核验清单、计划和包内容；锁与源清单路径绑定，不是跨机器可任意搬移的 restore 锁。report 和 lockFile 不允许同路径，失败报告也不能覆盖锁。报告写入失败会将最终结果标为失败。

previous 只接受同一目标根的成功报告。按目标路径取最后有效操作，Duplicate 不撤销所有权；只有末次 Copy/Copied 的文件才有资格自动清理。prune 还要求本次不再选择且当前哈希未改变，执行删除前再次核验。用户修改、原已存在但跳过的文件、显式删除后重建的文件均不据旧复制记录清理。

执行阶段 I/O 失败停止后续操作，但已完成写入不回滚；路径检查也不是对并发恶意修改文件系统的原子事务。验证资产文件集合不等同宿主实际加载通过。

## 开发约定与验证

非测试手写 C# 文件使用工具现有 `/* */` MIT 版权头；使用 Tab、CRLF，并以常量、字段、构造、属性、公共/内部/私有方法或实际职责组织 `#region`。方法按处理阶段留空行，不合并多条语句到一行，不使用 using 类型别名。`#endregion` 紧接分区内容，前面不留空行。自动生成 Designer 保持生成器管理。

```powershell
dotnet restore Zongsoft.Tools.Deployer.slnx --source https://api.nuget.org/v3/index.json -p:GeneratePackageOnBuild=false
dotnet build Zongsoft.Tools.Deployer.slnx -f net10.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net8.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net9.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net10.0 --no-restore -p:GeneratePackageOnBuild=false
```

测试使用临时目标和合成本地包；RID 回退顺序验证最终文件内容，本地化验证 en/zh-Hans 模板与参数。真实业务完整部署和 Linux/macOS 原生首次调用仍应按任务清单单独验收。本页及英文版随工具包放入 docs，README 保留入口。

### 资源生成与调用

在 Visual Studio 中，将中性资源 `src/Properties/Resources.resx` 的“自定义工具”设为 `ResXFileCodeGenerator`，运行“运行自定义工具”生成同目录的 `Resources.Designer.cs`。项目已保留 Generator、LastGenOutput、AutoGen 和 DependentUpon 元数据。不要对 zh-Hans 资源生成另一份同名访问类；它是由同一个资源访问类定位的卫星资源。

先同步中英文 resx 的键和格式参数，再生成代码并编译；例如 `Review.Missing` 对应生成属性 `Properties.Resources.Review_Missing`。调用方式为 `string.Format(Properties.Resources.Review_Missing, path)`，不再拼接字符串资源键或调用 ResourceUtility/ResourceManager 查询。生成类按 CurrentUICulture 选择语言，并保留标准 Culture 覆盖属性。新增资源后，仅运行 dotnet build 不等于已执行 Visual Studio 自定义工具。

Designer 的属性和注释由生成器维护；仅清理生成空白行中的尾随空格和保持 CRLF，不手写资源属性。回归使用现有 en/zh-Hans 完整模板与路径参数测试。

## Profile 导入回调

Reader 使用 ProfileOptions.MaximumDepth（默认 64，仅接受正数），根文件计为第一层；deployer 沿用默认值。Importing 在文件打开及递归检查之后、解析之前执行；Imported 在子文件完成并合并之后执行，根文件不通知。可选文件缺失跳过，其他错误传播；子文件沿用相同空行选项与导入配置。

deployer 只订阅 `Importing`；哈希按路径另行读取，并非解析字节的严格快照。验证及独立的快照/合并后续任务见 [PROFILE-IMPORT-TASKS.md](../PROFILE-IMPORT-TASKS.md)。

Core 将逐行解析、导入路径和递归保护集中在内部 ProfileReader，私有 Context 记录当前 Profile、章节和行号。Profile.Load 保留转发入口；子文件解析成功后由父 Profile.Import 合并有效引用及登记关系，然后通知 Imported，最后清理活动状态。

ProfileOptions 的 Importing/Imported 均为 Action<ProfileContext>。上下文的 FilePath 是导入绝对路径，Depth 是层数，Referer 是直接引用者；Profile 在导入前为 null，导入后为子文件，前后使用不同的只读上下文。Reader 浅复制包含 MaximumDepth 的选项和委托引用，没有导入开关；业务回调通过异常终止整个加载，不支持静默跳过。deployer 在 Importing 中使用 context.FilePath 记录哈希，不需要注册 Imported。

## Core Profile 声明与保存

Core 7.59.0 将本地有序声明与合并后的有效视图区分，导入覆盖替换引用，条目来源指向实际声明文件；同文件重复键仍报错，本地与导入按读取顺序覆盖。ProfileReader 管理读取，ProfileWriter 管理保存。

Core 的无显式目标 Save() 仅将自身及导入子树中修改的声明写回各自来源；显式路径、Stream、TextWriter 只输出当前文件声明。未修改文件不重写。多文件输出先全部准备、再逐个提交，不是跨文件事务。deployer 只读取描述文件并使用 importing 记录哈希，不调用这些保存入口，也不重新维护导入或写入实现。严格匹配解析字节的哈希快照仍是独立后续任务。

## 本地搜索与源链接

本地模式由 Core Searcher 处理，工具不再维护通配递归算法。搜索结果保留逻辑名称，读取实际目标。选中目录链接作为载荷根时允许展开，内部目录链接跳过，文件链接按原名称读取目标内容。递归模式不穿过目录链接匹配后续段。选中链接悬空或循环会在输出写入前失败；目标路径校验继续执行。

链接 INI 和 .deploy 的相对引用以逻辑配置目录为基准。单模式结果按逻辑相对路径执行 Ordinal 排序，多参数顺序不变。参见 [Core 本地搜索](../../../framework/Zongsoft.Core/docs/searcher.zh-Hans.md)和[任务清单](../LOCAL-SEARCHER-TASKS.md)。

部署操作 Source 保留逻辑路径，链接源另记 ResolvedSource。复制和摘要读取实际目标；锁定比较包含目标路径，执行前重新核对解析结果，因此改指向等内容文件也会失败。NuGet 框架适配及 expansion 后缀由部署适配层负责。

`Searcher.Search` 通过 `Searcher.Target` 选择文件、目录或两者（默认 Both）；`Match.Origin` 提供逻辑固定目录前缀，用于计算相对输出路径。
