## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本工具生成 tar.gz、deb、rpm 安装包。开始修改前阅读 [SKILL.md](SKILL.md)、双语 README 和 [实现说明](docs/implementation.md)。

## 代码规范

生产代码采用既有 MIT 版权头；测试不加版权头，不使用类型或程序集别名。Tab、CRLF、方法间空行、适当中文 region；sh 使用 LF。CodeAnalysis 1.1.0 严格构建和 IDE0049 验证均需通过。资源经 ResXFileCodeGenerator 生成，不编写本地化测试。

## 职责与契约

- PackCommand 解析源目录，再由 VersionFile 使用 Core ApplicationVersion.Load/Save 管理直属 .version。最终身份确定后初始化变量。成功制包后才保存源文件；失败不改写，保存失败保留包并报错。
- 包内 .version 通过 ApplicationIdentifier.Save(Stream) 原样写入内存，不额外追加换行，强制替换同目标旧条目。所有编码器经 Entry.OpenRead 读取。
- Package 是跨格式模型，Generator 编码容器；Scriptor.Systemd 负责宿主、服务和生命周期。tar/deb/rpm 保持路径、排除、权限和阶段意图一致，各自遵循格式规范。
- Migrator 仅按 --migrator 输入名称、最终 Edition/Version/Runtime 查找并验证既有 tar.gz 与脚本；先展开变量；无 / 或 \ 时从最终 source 逐级查找父目录至根，不查子目录；有分隔符时只定位显式目录，相对目录基于 source。统一补升迁后缀；只有两文件都缺失才继续向上，半套或元数据/RID 错误立即失败，不能跨目录拼配或选择其他身份。全部缺失报告预期文件名和已检查目录。--migrator 空值或全空白视为未指定。升迁输入解析和 Native AOT 发布由独立 migrator 工具负责。
- packager 不引用独立 migrator 项目或共享协议，不分发原生产物。两个输入文件原样加入安装根 .migration/，脚本 0755、归档 0600，载荷冲突失败。
- 安装调用外部脚本 apply，systemd 门禁调用 check，均明确传入 /var/lib/<包名>/packager。升迁失败阻止启动；无 daemon 也执行，DESTDIR 不执行钩子；卸载保留状态。
- 三格式的安装/升级/覆盖/卸载阶段不同，保留 Debian configure 和 RPM 剩余实例语义。根路径别名、符号链接与安装目录必须规范化，避免越界目标。
- Core Searcher 处理本地通配与链接，按逻辑来源定位相对路径。变量显式选项优先于环境，再取默认；TextSource 的 file:/text: 只解释一次。
- --listen 生成宿主 --urls，完整地址保留；已有服务 ExecStart 不改写。Debian 依赖使用 name (>= version)，RPM 使用 name >= version。
- 安装包来源元数据使用 Packager:程序集名@版本，独立于应用 .version。大载荷使用 DeleteOnClose 临时流和增量摘要，不分配完整包体。

## 验证与边界

`dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0`；`dotnet build Zongsoft.Tools.Packager.slnx -p:ZongsoftCodeStyleStrict=true` 覆盖所有 TFM，另执行 IDE0049 verify。

Cake restore/build/test 使用同一 edition，默认 test；build 只构建本工具并制 NuGet 包，不启动容器。pack 会推送 NuGet，未获明确授权不能运行。Debug 引用本地 Core，Release 使用 NuGet，测试不能混用。

文件系统测试使用临时目录，包内容检查不等于安装；不得擅自执行安装、systemd、sudo 或真实数据库/存储操作。交接集成通过产物或独立进程，不跨工具引用协议类型。公开行为修改同步双语 README、实现说明、资源和测试。
