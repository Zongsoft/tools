## 硬性要求

- 新建文本文件使用 CRLF 换行符，代码文件使用 Tab 缩进；保持既有生成文件和局部格式。
- 开始工作前阅读目标工具就近的 `AGENTS.md`、`SKILL.md`、README、解决方案、项目文件和构建脚本。
- 三个工具相互独立，没有仓库级解决方案或中央包版本；依赖和目标框架应在各自项目内维护。
- 保留用户未提交的修改，不重置、不覆盖、不格式化任务范围外的文件。

## 仓库概览

- `deployer`：读取 `.deploy` 描述文件，将本地文件或 NuGet 包内容部署到目标目录；命令为 `dotnet-deploy`/`dotnet deploy`。
- `packager`：生成 `.tar.gz`、`.deb`、`.rpm` 和安装生命周期脚本；命令为 `dotnet-pack`。
- `regular`：面向 Windows 的正则表达式 WinForms 测试器。

`deployer` 与 `packager` 支持 .NET 8、9、10 并在构建时生成 NuGet 工具包；`regular` 目标为 `net10.0-windows`。

## 操作边界

- 部署、删除目标文件、下载安装包、安装生成的系统包、执行生命周期脚本、操作 systemd 或写入系统目录都具有外部副作用；未经明确要求不得对真实环境执行。
- 各子项目的 Cake `clean` 会删除构建输出，`pack` 会使用 `NUGET_API_KEY` 推送 NuGet 包；默认只运行聚焦构建/测试任务，不运行发布任务。
- 不提交 NuGet 凭据、私有源令牌、真实服务器地址、应用密钥或打包进产物的敏感配置。
- `Properties/Resources.Designer.cs` 和 WinForms `*.Designer.cs` 是生成代码；优先修改对应资源或设计器来源，避免手工无关重排。

## 验证

- 文档改动检查相对链接、CRLF、`git diff --check` 和实际差异，不必构建。
- 代码改动从对应工具的 `.slnx` 或 `.csproj` 开始；不要因单工具改动构建其他工具。
- 文件系统行为使用临时源目录、临时目标目录和本地包缓存验证。
- 跨平台包格式要在适用平台验证元数据与归档内容；检查包不等于安装包，默认禁止 `sudo`、`dpkg -i`、`rpm -U` 和服务启停。
