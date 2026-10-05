[English](templates.md) | [简体中文](templates.zh-Hans.md)

# 基础设施模板

模板采用 Core Profile，声明 `version=1`，仅用于基础设施/入口的制作输入，不是可执行钩子。内置定义见 [templates](../templates)；文件存在不代表对应镜像版本/平台组合已经验收。

根字段：`image`（默认仓库）、`kind`（`infrastructure`、`ingress`）、`platforms`（分号分隔的 `x64`/`arm64`）、JSON 字符串数组 `entrypoint`、`command`、`health`，以及 `workdir`、`user`、数字 `data-owner`（`UID:GID`）、`restart`、`stop-signal`、`health-timeout`（秒）、`dependencies`（分号分隔的本节点服务 ID）。`version` 是模板协议版本。

| 段落 | 契约 |
| --- | --- |
| `[environment]` | 默认环境值 |
| `[ports]` | 命名的 `[IPv4:]宿主端口:容器端口[/tcp|udp]` |
| `[data]` | 稳定目录 ID → 容器绝对路径；只有一个数据挂载时使用 `/var/lib/containerizer/data/<name>/<service>/`，多个数据挂载分别使用 `<mount>/` 子目录 |
| `[configuration]` | 容器文件绝对路径 → 相对于模板的文件；仅基础设施使用 |
| `[settings 参数]` | `environment` 和/或 `argument` 映射；可选 `variable`、`default`、`required=true`、分号分隔的 `choices` |

未知字段/参数报错。显式 settings 覆盖公共及模板默认值；单项显式空值阻止回退。`environment!NAME` 可以覆盖未由参数管理的环境项；同时指定参数及其环境映射且值冲突时报错。参数未指定时，可用显式环境项满足它的映射。工具派生的环境项不写入清单，避免重制时覆盖用户编辑后的 settings。命令数组保留参数边界，不通过宿主 Shell 展开。

`variable` 声明参数缺省时使用的共享变量；make 在不存在该变量时使用 default，存在但为空仍为空。MySQL root-password 绑定 mysql_root_password，RustFS 两个凭据分别绑定 rustfs_access_key、rustfs_secret_key。plan 保留引用，缺少必填值以紫红色告警并保存草稿；make 在镜像操作之前校验。其它变量需显式绑定或引用，不整份导出。

模板的值通过制作端共享求值器支持 `$(变量)` 和 `%变量%`，使用清单最终 source 对应的 `.env` 祖先链及 CLI 变量，并提供最终根字段变量。段名与键名保持字面含义；同名变量不会自动替换字面值。JSON 参数数组先解析，再逐项展开字符串，变量值中的引号及反斜杠不会破坏参数边界。缺失或循环引用报错；`$$(变量)` 和 `%%变量%%` 保留字面引用。配置文件内容原样复制，只展开声明的输入路径，路径仍相对于模板目录。现场执行器不再次求值。

固定模板元数据及默认值保持字面值；不同构建间变化的值才使用变量，例如在 `[settings maxmemory]` 中写 `default=$(redis_limit)` 或 `default=%redis_limit%`。既有内置参数映射也允许在清单的 settings 值中引用变量，无需修改模板。

非 root 镜像的数据归属从锁定镜像的用户元数据及停止状态检查容器中的账号文件解析为数字 UID/GID，也可用 data-owner 显式指定。配置在宿主受保护并只读绑定，可写路径记录归属。RHEL 系列绑定请求私有 SELinux 标签。

应用不支持模板、配套模板查找或模板 settings。前台启动命令、工作目录和服务环境来自安装包；packager 从 hosting/.deploy/<scheme>/ 选定的应用配置保留在应用镜像内，不按扩展名提取或生成应用配置挂载。显式 `environment!NAME` 可覆盖服务环境。

有效 `Listen` 元数据来自 tar PAX、Debian control 或 RPM 标签 `1000001`。元数据缺失或为空时采用 `["CMD-SHELL","kill -0 1"]`；非空值须是分号分隔的 HTTP/HTTPS URL，不含凭据、非根路径、查询或片段。无效值报错，基础设施检查保持原规则。

制作端将回环、通配或 DNS 监听转换为容器全部接口绑定，保留协议和端口；每个不同端口以同端口号发布到宿主 `127.0.0.1`。选择第一个 HTTP 监听，没有 HTTP 时选择第一个 HTTPS；在容器内经回环地址执行 GET `/`，收到任何 HTTP 响应（包括 404）即视为监听存活，不跟随重定向。连接、超时及 TLS 校验失败仍不健康；这不代表业务就绪。

HTTPS 使用 `Listen` 声明的 DNS 身份、SNI 与镜像系统信任库；仅有 HTTPS IP/通配监听无法提供 DNS 身份，因而报错。应用模板不能提供覆盖值。自包含宿主也安装 curl/CA 依赖；检查使用 10 秒间隔、5 秒超时、12 次重试和 30 秒启动宽限。

启动或运行时信息存在歧义的安装包须在上游修正。运行时推断读取入口 DLL 的 runtimeconfig，显式 runtime-* 依赖须与其一致。

Ubuntu 22.04 的 .NET 9 及以后应用镜像按[官方安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)使用 Ubuntu `ppa:dotnet/backports` 源。APT/DNF 从配置的软件源安装所需主、次版本的当前可用补丁，不固定可能已从仓库移除的旧包修订号。工具会验证实际安装版本与所需主、次版本一致且不低于安装包要求的最低补丁版本，交付锁记录构建镜像实际报告的补丁版本。

原始 Pod/Compose YAML 输入、来源前缀及服务选择器已移除。自定义运行描述使用 `template`，仓库覆盖使用 `repository`；生成的 Compose 资产仍随交付提供。模板的 `image` 和协议 `version` 保持其含义，与 `.container` 字段分开；镜像软件标签及 ENV 版本不用于选择或改写 tag。

所有声明的端口提供参数入口：`default` 对应 `port`，其它命名端口对应 `名称-port` 且默认不发布。有 `[data]` 的模板统一提供 `storage=persistent|temporary`；临时存储使用匿名卷，普通停止/启动保留，由工具卸载容器时清理。Redis/Valkey 共用 persistence/password 转换，无需重复维护两套行为。完整参数及默认值见 [README](../README.zh-Hans.md#内置基础服务参数参考)。本轮不新增模板继承、通用 config 输入或 `.settings template`；自定义模板详细设计延期。
