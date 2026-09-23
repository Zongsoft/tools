# Zongsoft 升迁工具

数据库配置采用 provider/数据库/用户三级段落：provider 的 `Database` 指定默认库，该库可以没有数据库段。只初始化 `.migration` 引用的库及其用户，空段也算引用；已有设置及密码保留，权限仅追加。完整参数和默认值见[数据库指南](docs/databases.zh-Hans.md)。

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-migrate` 制作独立升迁执行包和启动脚本。制作时不连接数据库/S3。版本文件可提供版本号和 Edition，始终只读，不改写。执行生成的脚本时才进行数据库初始化、SQL 执行和 S3 桶配置。

## 安装

```powershell
dotnet tool install -g Zongsoft.Tools.Migrator
```

本地源码测试：在 migrator 目录执行 `dotnet cake --edition Release --target build`，准备三个 RID 并生成工具包。已有原生产物时可执行 `dotnet cake --edition Release --target compile`。首次本地安装使用 `dotnet tool install -g Zongsoft.Tools.Migrator --version 0.1.0 --source ./src/bin/Release --no-http-cache`；替换相同版本前先卸载该全局工具。Cake `pack` 会推送 NuGet，不用于本地测试。

## 用法

从 `D:/Zongsoft/hosting` 的 PowerShell 制作现有 Web 升迁输入：

```powershell
$env:scheme = 'default'
dotnet-migrate --name:zongsoft --version:1.0.0 --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

必填选项为 name、platform；可选 version、edition、architecture（默认 x64）、output（默认当前目录）、overwrite（默认 false）、title（默认输入名称）、summary 和 description。summary/description 采用 `file:`、`text:` 文本来源规则，文件相对当前目录。至少一个位置参数；每个参数支持变量、通配符及 `;`/`|` 列表，显式选项优先于环境变量。路径按参数位置展开，各模式按固定前缀下的相对路径 Ordinal 排序。缺失路径逐项警告，全缺失或无任务则失败；存在但无效的输入仍报错。只接受 `.migration`（INI 内容），导入同样检查扩展名；参数仍为 `.env`。SQL 内容不展开变量。

Linux 支持 glibc x64/arm64；win/windows 规范化为 win，仅支持 x64。unix 必须指定具体系统，osx/xos/macos 尚无运行器，均不生成产物。

## 版本选择

`--version` 接受非零的 `System.Version` 版本号（两段、三段或四段数字）、版本文件路径，或包含 `.version` 的现有目录路径。相对路径基于当前工作目录。省略选项、空串或全空白值只读取当前目录直属的 `.version`，不使用环境变量 `version` 代替。版本文件由 Core `ApplicationVersion` 读取，始终不修改；文件缺失、不可读或内容无效时，在生成任何产物之前报错退出。

未指定非空 `--edition` 时：单版本文件使用顶层版本，只有一个具名 Edition 时自动选择，多个 Edition 时必须明确指定。指定的 Edition 必须存在，忽略大小写匹配，并采用文件中的拼写。`--name` 仍必填，与版本文件中的应用名称无关。直接指定版本号时不读取版本文件，采用命令提供的 Edition。

在 `D:/Zongsoft/hosting` 中，以下两种方式均使用现有 Web 宿主版本文件（先按上例设置 `scheme`）：

```powershell
dotnet-migrate --name:zongsoft --version:web/default/.version --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
dotnet-migrate --name:zongsoft --version:web/default --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

省略 `--version` 时，在 `D:/Zongsoft/hosting/web/default` 中执行：

```powershell
dotnet-migrate --name:zongsoft --platform:linux --output:../../packages '../../.deploy/$(scheme)/migration/$(version)/*.migration'
```

版本路径支持变量；数字形式优先作为版本号，文件名为 `1.0.0` 时可用 `./1.0.0` 明确指定文件。最终版本和 Edition 用于 `$(version)`/`$(edition)`、计划身份及产物名称。升迁输入和输出的相对路径始终基于当前目录，不随版本文件目录改变。完整展开规则见[版本来源与变量](docs/migration.zh-Hans.md#版本来源与变量)。

## 产物与执行

名称已有 `-migrate`、`-migration`、`.migrate`、`.migration` 后缀时保留（忽略大小写），否则追加 `-migrate`。两文件使用同一前缀 `<升迁名称>[-edition]@<version>_<platform>-<architecture>`，扩展名分别为 `.tar.gz` 和 `.sh`/`.cmd`。不生成描述文件。例如：

```text
packages/zongsoft-migrate@1.0.0_linux-x64.tar.gz
packages/zongsoft-migrate@1.0.0_linux-x64.sh
```

将二者放在目标机同一目录，执行 `sh zongsoft-migrate@1.0.0_linux-x64.sh [apply|status|check] [状态目录]`。Windows 执行同名前缀的 cmd。默认 apply；每次执行全部 SQL，由脚本保证幂等。状态默认位于脚本目录 `.migration/<升迁名称>[-edition]/`，不含版本和 RID。不同版本共享执行锁、ready、status 和 S3 pending；执行失败不会写成功标记。

apply/status 每次创建独立临时目录，解压后调用原生执行器，结束清理并返回退出码。check 将脚本内嵌指纹与 ready 比较，不解压或启动运行器。成功为 0，执行失败为 1，无效动作/参数为 2。两份输出先完成暂存再发布，覆盖需显式 overwrite，失败恢复原输出。

## 与 packager 配合

在 hosting/web/default 的打包命令中使用 `--migrator:../../packages/zongsoft`；daemon 使用 `--migrator:../packages/zongsoft`。packager 根据最终 Edition、版本和 RID 查找准确配套文件，应用同一名称后缀规则。任一文件缺失即失败，不选择其他版本。安装包原样包含归档和脚本；安装时显式传入 `/var/lib/<包名>/packager` 状态目录，升迁失败阻止启动。实际宿主命令和连接参数不由本工具自动修改。

## 构建与测试

生成端目标为 .NET 8/9/10，执行器为 .NET 10 Native AOT。普通 build/test 不启动原生发布。显式 `dotnet cake --edition Release --target executor` 使用独立 Rocky Linux 9/glibc 2.34 Pod 构建 Linux 双架构，并在 Windows 本机发布 win-x64；缓存和挂载定义见 executor/build/migrator.linux-x64.yaml。工具使用仓库根 `.editorconfig`，Pod 与 CI 将该文件只读挂载到 `/.editorconfig`，并将根 `Directory.Build.props` 和 `Directory.Packages.props` 只读挂载到容器根目录。Linux 发布使用 `-p:ZongsoftGuidelinesSynchronization=` 禁用配置同步。Cake 使用相对 YAML 路径，修改 DNS 或挂载配置后需在无构建运行时重建专用 Pod，不能只 start。原生产物统一位于 `executor/src/bin/<配置>/net10.0/<RID>/publish/`；完整工具包要求同一配置下三个 RID 均已发布。普通构建和独立发布通过文件链接将它们收录到程序目录的 `.migrator/<RID>/`。NuGet 工具包在 `tools/.migrator/<RID>/` 仅保存一份，供三个目标框架共用；生成端在本地 `.migrator/` 目录不存在时定位此共享目录。CI 分别在 Linux、Windows 准备发布目录后汇集制包。日志和符号分别保存在各 RID 目录的 logs/ 与 symbols/，不收录到工具包。

```powershell
dotnet build Zongsoft.Tools.Migrator.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test test/Zongsoft.Tools.Migrator.Tests.csproj -f net10.0
dotnet test executor/test/Zongsoft.Tools.Migrator.Executor.Tests.csproj -f net10.0
```

详见[升迁指南](docs/migration.zh-Hans.md)、[实现说明](docs/implementation.zh-Hans.md)。

Privileges 使用 20 项跨驱动统一操作名称。ReadWrite 包含 Execute，所有读写授权均包含 Sequence 取值；驱动在指定库内展开原生权限，无法满足的操作或所有权条件明确失败。权限合并和角色范围限制见数据库指南。
