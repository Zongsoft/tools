## 概述

本目录遵循 [../AGENTS.md](../AGENTS.md)。本项目是 `dotnet-pack` 工具，生成 tar.gz、Debian、RPM 包及安装生命周期脚本；详细流程见 [SKILL.md](SKILL.md) 和 [docs/implementation.md](docs/implementation.md)。

## 职责边界

- `PackCommand*` 解析命令、初始化变量、加载打包项并选择包类型。
- `Package*` 保存跨格式包模型和格式特定元数据；`Generator*` 负责编码具体容器、头部和载荷。
- `Scriptor.Systemd` 解析/生成服务单元与安装卸载脚本。
- `Normalizer`、`Variables` 和 `Utility` 负责变量、路径、Runtime Identifier 与 Unix 权限等共享语义。

## 高风险契约

- tar、deb、rpm 必须对相同输入保持一致的目标路径、根路径别名、排除、权限和生命周期意图，同时尊重各格式的元数据规范。
- 安装、升级、覆盖安装和最终卸载的脚本阶段不同；修改卸载保护时保留 Debian 动作参数和 RPM 剩余实例语义。
- 根路径条目、安装目录、符号链接和 systemd 服务可写系统位置或删除文件；生成测试只检查隔离产物，不执行安装脚本。
- 二进制格式中的长度、偏移、对齐、校验和、字节序、cpio/tar 路径与 RPM 标签属于精确契约，避免无关重构。
- 公开选项或行为变化应同步 `README.md`、`README.zh-Hans.md`、`docs/implementation.md`、资源文本和测试。

## 验证

- 最小自动化验证：`dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0`。
- 构建：`dotnet build Zongsoft.Tools.Packager.slnx -f net10.0`；兼容性变化再覆盖 net8.0、net9.0、net10.0。
- 使用临时发布目录生成包，检查清单、权限、元数据、脚本和归档路径；格式工具只做只读检查。
- 未经明确要求，不执行生成的 `install.sh`/`uninstall.sh`，不调用 `dpkg -i`、`rpm -U`、`systemctl`、`sudo`，不运行 Cake `pack`。
