## 硬性要求

- 新建文本文件使用 CRLF 换行符，代码文件使用 Tab 缩进；保持既有生成文件和局部格式。
- 第三方文件保留上游原始字节、编码、换行及缩进，不应用本仓库的格式转换；通过 Git 属性防止自动换行转换。
- 开始工作前阅读目标工具就近的 `AGENTS.md`、`SKILL.md`、README、解决方案、项目文件和构建脚本。
- 四个工具保留独立解决方案、构建和发布流程；根 Directory.Packages.props 仅集中管理 Core、代码分析器和测试等通用包版本。专用依赖在所属项目使用 PackageReference 的 VersionOverride 维护；引用关系、目标框架和工具版本仍在各项目中定义。
- 保留用户未提交的修改，不重置、不覆盖、不格式化任务范围外的文件。
- 文档不重复记录工具或依赖的当前版本号，优先引用项目和版本配置，安装示例自动读取版本；仅在语法示例、最低兼容要求或固定第三方快照等必要场景保留具体版本。中英文文档同步更新。

## 仓库概览

- `deployer`：读取 `.deploy` 描述文件，将本地文件或 NuGet 包内容部署到目标目录；命令为 `dotnet-deploy`/`dotnet deploy`。
- `packager`：生成 `.tar.gz`、`.deb`、`.rpm` 和安装生命周期脚本；命令为 `dotnet-pack`。
- `migrator`：独立制作升迁归档和脚本，并提供 Native AOT 执行器；命令为 `dotnet-migrate`。
- `regular`：面向 Windows 的正则表达式 WinForms 测试器，并通过独立启动器提供 `dotnet-regular` 工具命令。

`deployer`、`packager` 与 `migrator` 支持 .NET 8、9、10；deployer 在构建时生成 NuGet 工具包，packager 独立构建，migrator 先准备三平台原生执行器再生成工具包。`regular` 的 GUI 目标为 `net10.0-windows`，单个工具包由 `net10.0` 启动器携带 Windows x64 GUI 发布产物生成；开发时仍直接运行 GUI `.exe`。

## 操作边界

- 部署、删除目标文件、下载安装包、安装生成的系统包、执行生命周期脚本、操作 systemd 或写入系统目录都具有外部副作用；未经明确要求不得对真实环境执行。
- 各子项目的 Cake `clean` 会删除构建输出，`pack` 会使用 `NUGET_API_KEY` 推送 NuGet 包；默认只运行聚焦构建/测试任务，不运行发布任务。
- 不提交 NuGet 凭据、私有源令牌、真实服务器地址、应用密钥或打包进产物的敏感配置。
- `Properties/Resources.Designer.cs` 和 WinForms `*.Designer.cs` 是生成代码；优先修改对应资源或设计器来源，避免手工无关重排。

## 验证

- 文档改动检查相对链接、CRLF、`git diff --check` 和实际差异，不必构建。
- 代码改动从对应工具的 `.slnx` 或 `.csproj` 开始；不要因单工具改动构建其他工具。
- `regular` 的 Cake `build` 任务仅本地制包，`pack` 依赖 `build` 并推送单个工具包；本地测试使用隔离 `--tool-path`。
- 文件系统行为使用临时源目录、临时目标目录和本地包缓存验证。
- 跨平台包格式要在适用平台验证元数据与归档内容；检查包不等于安装包，默认禁止 `sudo`、`dpkg -i`、`rpm -U` 和服务启停。

## 代码规范检查

所有 C# 项目通过根 `Directory.Build.props` 统一引用 `Zongsoft.CodeAnalysis`，版本由 `Directory.Packages.props` 管理，设置 `PrivateAssets="all"`，分析器不作为工具的运行时依赖分发。编辑器配置来自 Zongsoft guidelines；C# 规则由 NuGet 包提供，不在仓库中降低诊断级别。使用 .NET SDK 10.0.401 或具有 Roslyn 5.9 及以上版本编译器的工具链。

对受影响项目执行 `dotnet build <项目> -p:ZongsoftCodeStyleStrict=true`，多目标项目不指定 `-f` 以覆盖全部框架；deployer 可额外指定 `-p:GeneratePackageOnBuild=false`。另执行 `dotnet format style <项目> --no-restore --verify-no-changes --diagnostics IDE0049`，这项规则不能只用构建检查。测试继续按各工具的既有测试项目运行。

全部工具统一继承仓库根 `.editorconfig`，不在工具目录保留副本。根 `Directory.Build.props` 管理语言版本、作者/公司/版权、通用 NuGet 元数据、工具包图标和分析器引用，默认启用 `ZongsoftCodeStyleStrict=true` 并设置 `ZongsoftGuidelinesSynchronization`；实际构建时由分析器包同步根配置。工具名称、版本、目标框架、测试及发布属性保持项目自身定义。分析器版本由根 Directory.Packages.props 统一维护；配置模板在 guidelines 维护，消费仓库同步后提交。AOT Pod 和 CI 将该文件只读挂载到容器 `/.editorconfig`，供 `/workspace` 下的源码继承；容器同时只读挂载根 `Directory.Packages.props` 和 `Directory.Build.props` 到容器根目录；Linux 发布脚本通过 `-p:ZongsoftGuidelinesSynchronization=` 禁用同步，不向只读配置写入。生成资源通过 `ResXFileCodeGenerator` 更新，不手写访问属性；WinForms Designer 文件仅同步必要事件绑定，不重排布局。
