## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `dotnet-pack` 工具，生成 tar.gz、Debian、RPM 包及安装生命周期脚本；详细流程见 [SKILL.md](SKILL.md) 和 [docs/implementation.md](docs/implementation.md)。

## 代码风格

- 不使用 `using 名称 = 类型或命名空间;` 别名语法；两端交接测试调用独立 migrator 进程，不直接引用运行器协议类型，不使用程序集别名。
- 生产代码使用项目既有的 `/* */` MIT 版权头，单元测试代码不添加文件头版权注释；方法之间留一个空行，按职责用 `#region` / `#endregion` 分组。保持 CRLF 换行和 Tab 缩进。

## 职责边界

- `PackCommand*` 解析命令、初始化变量、加载打包项并选择包类型。
- `Package*` 保存跨格式包模型和格式特定元数据；`Generator*` 负责编码具体容器、头部和载荷。
- `Scriptor.Systemd` 解析/生成服务单元与安装卸载脚本。
- `MigrationProfile`/`MigrationLoader` 使用 Zongsoft.Core Profile 解析升迁 INI/参数；`MigrationBundle` 收集安装时运行器；`.shared` 提供链接到两端的协议源码、MigrationProvider 参数规则、MigrationUtility 通用参数方法及其本地化资源；独立 `migrator/src` 执行建库、SQL、S3 初始化及状态检查。主项目不引用或构建 migrator；Cake 构建流程先独立发布运行器再制作工具包。`.shared/Migration.props` 共享源码和资源声明，Cake 独立 Native AOT 发布到 `src/.migrator/linux-x64/` 和 `src/.migrator/linux-arm64/`，主项目将其作为普通 Content 复制到输出和 NuGet 工具包，运行时按 `.migrator/<RID>/` 约定收集完整目录，不分析程序集依赖。migrator 目标为 net10.0，TDengine 使用 ClientWebSocket 直接访问 taosAdapter；升迁资源配置 ResXFileCodeGenerator 并保留中英文资源与全球化支持。
- `MigrationLoader.Database` 的私有批次实现仅在主项目预处理 SQL，生成安装根 `.migration/.artifacts/` 下的有序 UTF-8 批次；`.migration/` 保留计划、运行器与入口，执行器逐文件提交不再分段。计划使用源码生成 JSON，连接工厂不使用反射。
- `Migrator.Database` 为抽象 partial 基类，嵌套六种数据库实现；共用 ADO.NET 执行辅助，TDengine 独占 WebSocket 会话。`Migrator.Create` 显式选择类型。AOT 环境为独立 Rocky Linux 9/glibc 2.34 容器，不修改参考 framework Pod；普通 `dotnet build/test` 不启动容器。
- `Normalizer`、`Variables` 和 `Utility` 负责变量、路径、Runtime Identifier 与 Unix 权限等共享语义。

## 高风险契约

- tar、deb、rpm 必须对相同输入保持一致的目标路径、根路径别名、排除、权限和生命周期意图，同时尊重各格式的元数据规范。
- 安装、升级、覆盖安装和最终卸载的脚本阶段不同；修改卸载保护时保留 Debian 动作参数和 RPM 剩余实例语义。
- 根路径条目、安装目录、符号链接和 systemd 服务可写系统位置或删除文件；生成测试只检查隔离产物，不执行安装脚本。
- 二进制格式中的长度、偏移、对齐、校验和、字节序、cpio/tar 路径与 RPM 标签属于精确契约，避免无关重构。
- 公开选项或行为变化应同步 `README.md`、`README.zh-Hans.md`、`docs/implementation.md`、资源文本和测试。
- 升迁失败必须阻止服务启动；每次安装和重试均执行全部 SQL，幂等性由脚本作者保证，不维护逐文件成功历史或跳过逻辑。不自动删库删桶、不记录凭据日志。`migration.json` 含参数值，模式保持 `0600`，真实凭据不用于测试。

## 验证

- 本地化不编写单元测试；不切换测试文化、不匹配翻译文案。错误测试验证异常类型、文件/段落/参数标识和敏感值不泄漏。

- 打包器验证：`dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0`，保留输入、SQL 批次预处理、制包及 JSON 交接测试。
- 运行器验证：`dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0`，只引用 `migrator/src`，不引用主项目；迁移两端协作逻辑时运行这两个测试项目。
- 构建：`dotnet build Zongsoft.Tools.Packager.slnx -f net10.0`；全部工具 TFM 使用不带 `-f` 的解决方案构建验证。
- 使用临时发布目录生成包，检查清单、权限、元数据、脚本和归档路径；格式工具只做只读检查。
- 未经明确要求，不执行生成的 `install.sh`/`uninstall.sh`，不调用 `dpkg -i`、`rpm -U`、`systemctl`、`sudo`，不运行 Cake `pack`。

- `--migration` 指定 INI 缺失或模式无匹配只警告并跳过，全部缺失按普通包处理；存在的 INI 内容、参数与 SQL 仍严格校验。生成内容仅保留 `.migration/` 目录，SQL 位于 `.migration/.artifacts/`。

S3 桶初始化支持 public/private、默认加密（sse-s3/sse-kms及可选KMS密钥标识）、版本控制（enabled/suspended）和 tag.* 桶标签。已有桶跳过，新建桶的指定配置全部完成才清除 pending；选项文本仅在打包端解析，共享 Bucket 模型校验结构，运行器使用标准 S3 API。
