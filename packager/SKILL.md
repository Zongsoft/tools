---
name: zongsoft-tools-packager
description: 修改或审查 Zongsoft tools/packager 的 dotnet-pack 命令、变量规范化、打包项与排除规则、Unix 权限、systemd 服务、安装卸载脚本、tar.gz/deb/rpm 二进制生成、包元数据或格式测试时使用。
---

# Zongsoft Tools Packager

先阅读 [AGENTS.md](AGENTS.md)、[README.zh-Hans.md](README.zh-Hans.md) 和 [实现说明](docs/implementation.md)。

## 定位变更

- 命令选项、默认值、输出路径：`PackCommand.cs` 及 `PackCommand.Tar.cs`、`.Deb.cs`、`.Rpm.cs`。
- 变量来源、`$(name)`/`%name%`、路径规范化：`Variables.cs`、`Normalizer.cs`。
- 打包项、别名、通配、排除、冲突和文件模式：`Package.cs`、`Utility.cs`。
- 服务发现、systemd 单元和生命周期脚本：`Scriptor.Systemd.cs`。
- tar.gz：`Package.Tar.cs`、`Generator.Tar.cs`。
- deb：`Package.Deb.cs`、`Generator.Deb.cs`。
- rpm：`Package.Rpm.cs`、`Generator.Rpm.cs`。
- 升级/卸载回归：`test/PackageLifecycleTests.cs`。

## 实现顺序

1. 明确变化属于共享包模型还是单一格式；共享语义先在 `Package`/命令层定义，再由各生成器编码。
2. 对路径先区分普通载荷与以 `/` 或 `\` 开头的根路径别名，检查规范化后路径不能意外逃逸预期包根。
3. 修改服务脚本时分别推演首次安装、升级、同版本覆盖、最终移除和显式 tar 卸载。
4. 修改 deb/rpm 编码时依据 [实现说明](docs/implementation.md) 核对字段、架构映射、偏移、对齐和载荷清单，并增加针对性断言。
5. 公开行为变化同步双语 README、实现说明、资源和测试；不要把格式知识只留在代码注释中。

## 跨格式检查

- 文件选择、目标路径、排除与重复冲突在三种格式中保持同一输入语义。
- Unix 主机保留源权限；Windows 推断的可执行/普通文件模式保持文档化规则。
- `/etc` 等根路径条目在 tar 的 `.root`、Debian `conffiles` 和 RPM 配置文件标记中各自正确表达。
- systemd 禁用值、服务文件优先级、宿主定位、环境变量和 `--urls` 生成规则保持兼容。
- Debian 升级动作和 RPM 非最终实例卸载不得执行最终删除逻辑；tar 卸载器只删除解析后的目标。

## 安全验证

优先运行测试项目，再从临时源目录生成代表性小包。可以使用 `tar -tf`、`dpkg-deb --info/--contents`、`rpm -qip/-qlp/--scripts` 等只读命令检查产物；工具不可用时记录未验证项。

不要安装生成包、执行其生命周期脚本、写入 `/opt` 或 `/etc`、管理 systemd，也不要使用真实应用产物中的密钥。发布 NuGet 工具包仅在用户明确要求时进行。
