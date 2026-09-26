## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本工具生成 tar.gz、deb、rpm 安装包。开始修改前阅读 [SKILL.md](SKILL.md)、双语 README 和 [实现说明](docs/implementation.zh-Hans.md)。

## 代码规范

生产代码采用既有 MIT 版权头；测试不加版权头，不使用类型或程序集别名。Tab、CRLF、方法间空行、适当中文 region；sh 使用 LF。CodeAnalysis 版本继承根 Directory.Packages.props，严格构建和 IDE0049 验证均需通过。资源经 ResXFileCodeGenerator 生成，不编写本地化测试。

## 职责与契约

- PackCommand 解析源目录，再由 VersionFile 使用 Core ApplicationVersion.Load/Save 管理直属 .version。最终身份确定后建立本次命令独立的 Variables 视图，并传给包、脚本和文本来源，不保留进程级变量状态。tar 归档与脚本成组发布，deb/rpm 单文件发布；成功制包后才原子保存源版本文件，保存失败保留包并报错。
- 包内 .version 通过 ApplicationIdentifier.Save(Stream) 原样写入内存，不额外追加换行，强制替换同目标旧条目。所有编码器经 Entry.OpenRead 读取。
- Package 是跨格式模型，Generator 编码容器；ApplicationHost 一次解析宿主、服务来源及最终 listen，Scriptor.Systemd 负责服务与生命周期组合。自动生成的服务文件直接以内存条目入包，先验证文件名和存在的宿主候选目录。tar/deb/rpm 保持路径、排除、权限和阶段意图一致，各自遵循格式规范。
- Web/Definition 通过 Core Profile 的 RequireImports 和导入回调收集声明，先处理覆盖再求值。Configurator.Nginx 生成安装根 .web/nginx/<PackageName>.conf；普通载荷是否包含 Profile 仍由 arguments/exclude 决定，生成目标冲突失败。Delivered 在生命周期之前完成弃用清理与 Tar 重定位，DESTDIR 只交付；HOSTER_WEB_ACTIVATION 控制新链接与 Nginx 操作，弃用和普通卸载仍清理本包文件/匹配链接。Web 步骤独立于自定义主钩子和 daemon 开关。
- Migrator 仅按 --migrator 输入名称、最终 Edition/Version/Runtime 查找并验证既有 tar.gz 与脚本；先展开变量；无 / 或 \ 时从最终 source 逐级查找父目录至根，每层先查目录本身，再查直属 `.migration/`，不遍历其他子目录；有分隔符时只定位显式目录，相对目录基于 source。统一补升迁后缀；只有两文件都缺失才继续查下一位置，半套或元数据/RID 错误立即失败，不能跨目录拼配或选择其他身份。全部缺失报告预期文件名和已检查目录。--migrator 空值或全空白视为未指定。升迁输入解析和 Native AOT 发布由独立 migrator 工具负责。
- packager 不引用独立 migrator 项目或共享升迁协议，不分发原生产物；通用变量解析源码由 tools/.shared 链接到三个工具。两个输入文件原样加入安装根 .migration/，脚本 0755、归档 0600，载荷冲突失败。
- 安装调用外部脚本 apply，systemd 门禁调用 check，均明确传入 /var/lib/<包名>/packager。升迁失败阻止启动；无 daemon 也执行，DESTDIR 不执行钩子；卸载保留状态。
- 三格式的安装/升级/覆盖/卸载阶段不同，保留 Debian configure 和 RPM 剩余实例语义。根路径别名、符号链接与安装目录必须规范化，避免越界目标。
- Core Searcher 处理本地通配与链接，按逻辑来源定位相对路径。变量依次加载默认值、环境、从根到最终 source 的 `.env`、显式选项；共享 Utility 用 Profile.Load 加载，多级段落与条目以下划线拼名。先解析并固定 source，再加载 `.env`，不反向推导源目录；身份仍仅来自显式选项与源版本文件。TextSource 的 file:/text: 只解释一次。
- --listen 生成宿主 --urls，完整地址保留；已有服务 ExecStart 不改写。Debian 依赖使用 name (>= version)，RPM 使用 name >= version。
- 安装包来源元数据使用 Packager:程序集名@版本，独立于应用 .version。大载荷使用 DeleteOnClose 临时流和增量摘要，不分配完整包体。

## 验证与边界

`dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0`；`dotnet build Zongsoft.Tools.Packager.slnx -p:ZongsoftCodeStyleStrict=true` 覆盖所有 TFM，另执行 IDE0049 verify。

Cake restore/build/test 使用同一 edition，默认 test；build 只构建本工具并制 NuGet 包，不启动容器。pack 会推送 NuGet，未获明确授权不能运行。Debug 引用本地 Core，Release 使用 NuGet，测试不能混用。

文件系统测试使用临时目录，包内容检查不等于安装；不得擅自执行安装、systemd、sudo 或真实数据库/存储操作。交接集成通过产物或独立进程，不跨工具引用协议类型。公开行为修改同步双语 README、实现说明、资源和测试。
