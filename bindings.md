# 统一绑定描述与 XML 处理指令设计

> 文档路径：`D:\Zongsoft\tools\bindings.md`
> 用途：为后续新聊天提供完整的设计上下文、源码依据、已确认约定、待定问题和实施目标。
> 状态：设计交接稿。XML 绑定提取、统一包元数据及相应工具改造尚未实现，示例中标为“拟议”的语法不能当作现有工具能力。
> 指令名称已经确定为 `binding`；指令配置路径使用 `/`，例如 `path="Kestrel/Endpoints"`。结果文件名称仍为 `.bindings`。

## 阅读导航

- [背景与问题](#background)
- [已达成的共识](#agreements)
- [当前代码的真实行为](#current)
- [Framework 项目调研结论](#research)
- [目标处理链与职责](#pipeline)
- [处理指令规范：确定项和待定项](#instruction)
- [配置与指令示例](#examples)
- [有效配置、条件和覆盖](#configuration)
- [统一绑定模型与容器化](#model)
- [合并结果与包格式](#package)
- [实现影响、待定事项和实施顺序](#implementation)
- [验收目标](#acceptance)
- [本地源码与外部参考](#references)
- [附录：已核对的 Framework 项目索引](#projects)

<a id="background"></a>

## 1. 背景与要解决的问题

### 1.1 部署配置的来源

本地工作区包含：

| 目录 | 职责 |
| --- | --- |
| `D:\Zongsoft\hosting` | daemon、terminal、web/default 等宿主及部署、打包、升迁、容器化脚本。 |
| `D:\Zongsoft\tools\deployer` | 根据 `.deploy` 文件部署文件、插件、配置和 NuGet 资产，支持模板处理。 |
| `D:\Zongsoft\tools\packager` | 收集部署结果，制作 tar.gz、deb、rpm，生成服务文件、安装脚本及 Nginx 配置。 |
| `D:\Zongsoft\tools\containerizer` | 消费安装包，制作应用及基础服务的容器交付物。 |
| `D:\Zongsoft\tools\migrator` | 独立制作升迁产物，涉及数据库和 S3 等初始化；本次绑定功能不应侵入其业务职责。 |
| `D:\Zongsoft\framework` | Core、Plugins、Web、各服务器和外部系统适配插件。 |
| `D:\Zongsoft\guidelines` | 必须遵循的开发规范。 |

`hosting/.deploy/<scheme>/` 用于隔离部署产品、项目、客户或云平台的配置、证书和插件组合。共享内容放在 `hosting/.deploy/.shared/`。这些目录由 `.deploy` 文件通过 `${scheme}` 引用，工具不应固化客户、环境和网络的目录组合。

一次部署生成一个已经确定的配置组合；输出位置由命令选项指定，不要求工具推导一套固定目录结构。deployer 使用 `--source`、`--output`。按仓库契约，`--source` 本身相对于调用目录，受其管理的本地相对输入、输出相对于最终 source；配置文件内部相对引用仍遵循对应文件的既有规则。

`.env` 用于工具变量，不是运行配置，也不部署到输出位置。目录祖先链与显式导入共同组织变量，不应由工具写死 scheme、宿主、项目各层的特殊优先顺序。例如：

```ini
# 路径示意：D:\Zongsoft\hosting\daemon\.env
#@import ../.deploy/${scheme}/.env

name=Zongsoft.Hosting.Daemon
title=Zongsoft Daemon
```

项目也可以有自己的 `.env`，与方案目录的 `.env` 并不互斥。附加变量文件以 `.env` 结尾；当前已有 `network.internal.env`、`network.external.env`、`environment.development.env`、`environment.test.env`、`environment.production.env` 等名称。

模板使用完整扩展名 `*.template`。Core 的 `Variables`、`TemplateEvaluator`、`Profile` 以及 `tools/.shared/` 已经承担相关通用能力，后续实现应复用。

### 1.2 为什么需要统一的绑定描述

原先容器化主要围绕 Web：Packager 从 `web.profile` 生成 Nginx 配置及 `.web/nginx/.bindings`，Containerizer 读取这些信息处理 Web 入口。另外，Packager 的 `--listen` 会进入生成服务的 `--urls` 和包头监听元数据。

实际应用还可能包含：

- Web 宿主本身的 Kestrel HTTP/HTTPS。
- Diagnostics OTLP 的 HTTP/2/gRPC 端点。
- 独立 Prometheus HttpListener。
- ZeroMQ、MQTT、Garnet、OPC 或业务 TCP 服务器。

仅依靠 Nginx 文件和一个 Web 的 listen 值，不能覆盖整个应用的监听。

此前曾临时增加 `port!名称=[绑定IP:]宿主端口:容器端口` 声明。用户已要求撤回该功能及其文档，避免影响统一设计。它不应被恢复为插件绑定来源，也不应在后续实施中顺带撤回其他既有改动。

### 1.3 方案的演进结论

最初考虑各插件维护固定名称 `.bindings`，Packager 合并为一份。后来发现这样会与 `*.option` 重复维护监听参数，即使两个模板引用相同 `.env` 变量，也仍有两份描述。

现在的方向是：

1. 监听参数在实际运行配置中显式给出。
2. 在 XML 格式的 `*.option` 或 `*.option.template` 中，用 `<?binding ...?>` 声明参数的提取位置。
3. Packager 按实际有效配置提取，生成统一绑定元数据。
4. Containerizer 消费生成结果，处理端口映射。
5. `web.profile` 保留，负责 Web 代理入口、域名、路由、证书和代理行为。

来源端不再要求手写 `.bindings` 或 `.bindings.template`；包中的统一结果是工具生成物。

<a id="agreements"></a>

## 2. 已达成的共识

以下内容是继续讨论的基础，不应在新聊天中重新混淆。

| 编号 | 已确认约定 |
| --- | --- |
| C01 | XML 处理指令名为单数 `binding`，例如 `<?binding ...?>`。 |
| C02 | 指令中配置路径使用 `/` 分隔，例如 `Kestrel/Endpoints`；不是 XML XPath，也不使用冒号作为对外示例的路径分隔符。 |
| C03 | `*.option` 是运行配置。处理指令仅供工具读取，不启动服务器、不修改监听行为。 |
| C04 | `.env` 和 `.bindings` 都服务于工具；不得将 `.bindings` 当作应用启动时读取的配置。 |
| C05 | 包内绑定结果属于交付元数据，不应复制到应用安装目录或容器的应用运行目录。处理指令本身可随 `.option` 留存并被运行时忽略；是否在生成包时剥离，尚未决定。 |
| C06 | 实际地址和端口只维护在运行配置中，处理指令引用配置字段，不再维护一份重复的值。 |
| C07 | `settings` 是有明确键值语义的字符串，使用 Core 的解析能力，不在工具中另写连接字符串解析器。 |
| C08 | `ISettings` 继承 `IDictionary<string, string>`，`IConnectionSettings` 继承 `ISettings`，`ConnectionSettingsBase` 继承 `Settings` 并共用解析器；工具通过 Core 解析设置文本。 |
| C09 | 需要自动导出的监听必须显式配置。打包时应能确定所需监听参数，不能依赖工具猜测代码或第三方库的默认值。 |
| C10 | 显式配置必须由运行代码实际消费，不能只增加供工具读取的假配置。 |
| C11 | 共用宿主监听的插件无须重复声明端口。Hangfire Dashboard、控制器等应复用宿主的绑定描述。 |
| C12 | `web.profile` 及其处理机制继续保留。实际监听由运行配置提供，代理入口由 `web.profile` 提供。 |
| C13 | 最终希望合并成一份包级绑定元数据，原来的 `.web/nginx/.bindings` 信息纳入其中。不同原生包格式的具体存放位置仍需定稿。 |
| C14 | 不需要按业务插件归属生成多个绑定文件，但不同执行进程/容器的监听仍须能区分。 |
| C15 | 工具内部使用，不设计旧格式、旧选项的兼容层。按最终行为同步代码、文档、帮助和注释。 |
| C16 | 优先复用 Core 和 `tools/.shared/`。发现既有框架能力有缺陷或需要改造，应准确指出来源，不在工具中绕过根因。 |

已经确认的事实：在当前 Web 宿主正常加载显式 Kestrel Endpoints 且没有启用 `PreferHostingUrls` 的情况下，生成服务中的 `--urls` 可以省略，实际监听不变。

建议的改造目标：移除 Packager 的重复监听输入及生成的 `--urls`，以配置提取结果替代它对代理和包元数据的作用。彻底删除 `--listen` 的具体影响及 `server=~` 的替代选择规则，仍须在实施前定清楚；不能只删一段服务命令就认为改造完成。

<a id="current"></a>

## 3. 当前代码的真实行为

### 3.1 当前宿主配置和模板

实际文件：

- [hosting/web/.env](../hosting/web/.env)
- [hosting/web/default/.deploy](../hosting/web/default/.deploy)
- [hosting/.deploy/.shared/web.option.template](../hosting/.deploy/.shared/web.option.template)
- [hosting/web/default/web.default.option](../hosting/web/default/web.default.option)

当前变量示例：

```ini
# D:\Zongsoft\hosting\web\.env
listen=8069

[web]
url=http://*:${listen}
protocols=Http1
```

当前模板：

```xml
<!-- D:\Zongsoft\hosting\.deploy\.shared\web.option.template -->
<configuration>
	<option path="kestrel">
		<endpoints>
			<endpoint endpoint.name="http"
			          url="${web:url#xml}"
			          protocols="${web:protocols#xml}" />
		</endpoints>
	</option>
</configuration>
```

当前 `.deploy` 中的相关引用：

```ini
# D:\Zongsoft\hosting\web\default\.deploy
template:../../.deploy/.shared/web.option.template = web.${site}.option
```

站点为 default 时，部署结果包含：

```xml
<!-- D:\Zongsoft\hosting\web\default\web.default.option -->
<configuration>
	<option path="kestrel">
		<endpoints>
			<endpoint endpoint.name="http" url="http://*:8069" protocols="Http1" />
		</endpoints>
	</option>
</configuration>
```

删除工具的 `--listen` 选项，不等于必须机械删除所有名为 listen 的业务变量。上面的变量仍可作为生成 `web:url` 的普通输入；需核对引用后决定最终命名。

### 3.2 当前 Packager 的 listen 链路

主要源码为 [ApplicationHost.cs](packager/src/ApplicationHost.cs)、[PackageOptions.cs](packager/src/PackageOptions.cs)、[Scriptor.Systemd.cs](packager/src/Scriptor.Systemd.cs)、[PackCommand.cs](packager/src/PackCommand.cs) 和 [Definition.Resolver.cs](packager/src/Web/Definition.Resolver.cs)。

当前行为：

1. listen 可由显式命令选项或既有变量来源取得；脚本没有显式传 `--listen`，不代表最终值一定为空。
2. 纯数字端口会被规范为 `http://127.0.0.1:端口`。
3. 自动生成宿主服务时可写入 `ExecStart=dotnet ... --urls ...`；已有服务文件与自动生成服务的处理不同。
4. `web.profile` 的 `server=~` 使用该应用地址。
5. 自动生成宿主的有效 listen 写入包元数据：tar PAX 的 Listen、Debian control 的 Listen、RPM 应用字符串标签。
6. 当前 Containerizer 的 [ApplicationHealth.cs](containerizer/src/ApplicationHealth.cs) 和 [ApplicationPlanner.cs](containerizer/src/ApplicationPlanner.cs) 还会利用这些值改写或添加 `--urls`、产生端口和健康检查。

新方案必须一起审视这些消费者，避免移除服务参数后仍依赖旧 Listen 元数据，或把独立 HttpListener 的地址错误地塞给 Kestrel。

### 3.3 为什么当前 --urls 是重复配置

[Application.cs](../framework/Zongsoft.Plugins.Web/src/Application.cs) 调用 `LoadConfiguration("web")`；[WebApplicationBuilder.cs](../framework/Zongsoft.Plugins.Web/src/WebApplicationBuilder.cs) 将插件配置加入应用配置管理器。

Kestrel 默认优先采用显式 Endpoints，而不是再叠加顶层 URLs。当前例子中，即使服务命令写的是：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

正常加载上述 `.option` 后，实际仍按 `http://*:8069` 监听。可以简化为：

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

这个判断有条件：对应配置确实加载，且没有 `PreferHostingUrls` 或其他实际生效的覆盖。不能推广成“所有 Web 程序的 --urls 永远无效”。官方依据见文末参考。

### 3.4 Hangfire Dashboard 与端口的关系

本仓库 [Hangfire Web Initializer.cs](../framework/externals/hangfire/web/Initializer.cs) 调用 `UseHangfireDashboard()`。上游实现把默认路径 `/hangfire` 注册为现有 ASP.NET Core 应用的请求处理分支，并放入 Dashboard 中间件；它不创建新的网络监听。

请求流程：

```text
http://localhost:8069/hangfire/
          │          │
          │          └─ 请求到达应用后，匹配 Dashboard 路径
          └─ 连接 Web 宿主 Kestrel 的 8069 端口
```

因此：

- Dashboard 的默认路由不是默认独立端口。
- 提取宿主 Kestrel 的绑定已经覆盖 Dashboard 所使用的网络入口。
- 无须为 Hangfire 补一份端口配置。
- 如果通过 Nginx 转发，入口和路径转发规则继续在 `web.profile` 中配置。
- Hangfire 后台作业处理器的“Server”名称，也不能据此认定存在独立 TCP/HTTP 监听。
- 授权、JobStorage 和 Dashboard 的可用性是运行验证的另一个层面，端口已映射不证明 Dashboard 能成功访问。

<a id="research"></a>

## 4. Framework 项目调研结论

### 4.1 调研范围与证据强度

本轮讨论已逐项核对 Framework 的 113 个 `.csproj`，查看项目定义、引用和就近文档，对归属这些项目的 2654 个源码/配置文件进行检索，并对监听相关命中追踪配置与实现。附录保留项目路径，后续聊天不需要依赖本机临时审计文件。

这不是“每行代码都经过人工审计”，也不是 113 个项目全部经过运行验证。库中存在服务器类型，不代表部署该库后服务器就会启动；测试和 samples 中的监听也不能自动当作生产绑定。Framework 的第三方 proto 子模块不作为本次 .NET 项目绑定设计的修改对象。

后续实现应重新核对受影响文件的最新内容，尤其是工具引用的 Core API、字典视图和设置文本语义是否符合下述契约。

### 4.2 实际监听和配置形态

| 项目/组件 | 已确认行为 | 对设计的影响 |
| --- | --- | --- |
| Web 宿主 / Plugins.Web | 宿主及插件可共同向 Kestrel Endpoints 提供配置。 | 在最终有效配置上统一提取，不能仅检查一个固定文件。 |
| Diagnostics.Protocols.Server | `Zongsoft.Diagnostics.Protocols.Server.option` 配置 `http://*:4317`、`Http2`。 | HTTP/2 端点；代理 gRPC 的方式不能仅由 HTTP/2 字样推断。 |
| Diagnostics / Prometheus | `PrometheusExporterLauncher` 使用独立 `AddPrometheusHttpListener`；从连接设置的 urls 读取地址。 | 也是 HTTP，但不由 Kestrel 的 `--urls` 控制；必须纳入通用提取。 |
| ZeroMQ daemon | 发现端口由服务器 Port 属性提供；控制/输入/输出端口来自配置或启动参数。 | 显式配置、实例选择、位置端口及启动参数覆盖都要核对。 |
| MQTT | 内嵌 MqttQueueServer 以 Port 属性设置端口；现有主插件主要提供客户端能力，并不自动启动服务器。 | 不因 DLL 或客户端配置存在就导出 MQTT 监听。 |
| Garnet | 服务器从选定 Settings 配置生成底层启动参数；插件有启动注册。 | 需提取被选中的设置，而不是把所有预设都当作运行实例。 |
| OPC | OpcServerOptions.Urls 对应 ServerConfiguration.BaseAddresses；Discovery 用于注册发现。 | 实际监听与发现目的地址应区分；证书和安全策略不等于新增端口。 |
| Zongsoft.Net | 提供通用 TcpServer；存在样例与默认地址/端口行为。 | 业务宿主实际创建并启用服务器后，才应提供相应配置提取声明。 |

关键源码见第 14 节引用索引。

### 4.3 ZeroMQ 的具体边界

当前 [ZeroQueueServer.cs](../framework/messaging/zero/src/ZeroQueueServer.cs) 中：

- 发现端口的初始值为 7969，Port 属性在停止状态下可设置。
- 控制、输入、输出端口可由启动参数提供；否则读取 `/Messaging/ZeroMQ/Servers`。
- 可按服务器名称选择配置，或使用集合默认配置。
- 数据通道未给定固定端口时，底层可能执行 `BindRandomPort`。
- [ZeroQueueServer.Control.cs](../framework/messaging/zero/src/ZeroQueueServer.Control.cs) 在消息存储为空时不创建控制监听。
- [ServerOptions.cs](../framework/messaging/zero/src/Configuration/ServerOptions.cs) 的端口字符串有位置语义：两个值表示输入/输出，三个值表示控制/输入/输出；不是任意端口集合。
- 发现响应会发布后续通道端口。宿主端口与容器端口不一致时，单独做 NAT 映射不保证发现结果可用；还需处理通告地址/端口语义。

当前 [ZeroMQ daemon 插件](../framework/messaging/zero/src/Zongsoft.Messaging.ZeroMQ-daemon.plugin) 创建服务器并加入启动列表，但没有在该文件中将发现端口明确连接到一个 `.option` 参数。已有 [daemon option 模板](../hosting/.deploy/.shared/Zongsoft.Messaging.ZeroMQ-daemon.option.template) 则提供控制、输入、输出端口。

因此，支持新方案前，需要让“实际服务器属性”与“可提取配置”建立真实连接。不能只在 XML 中添加一个工具专用的 7969 值。具体由配置对象还是插件属性注入完成，尚待实现设计。

原始库的 [Zongsoft.Messaging.ZeroMQ.deploy](../framework/messaging/zero/src/Zongsoft.Messaging.ZeroMQ.deploy) 会按 site 选择插件变体。客户端和 daemon 变体不应获得相同的服务器绑定声明。

### 4.4 Prometheus 的具体边界

源码：[PrometheusExporterLauncher.cs](../framework/Zongsoft.Diagnostics/src/Telemetry/PrometheusExporterLauncher.cs)、[Zongsoft.Diagnostics.option](../framework/Zongsoft.Diagnostics/src/Zongsoft.Diagnostics.option)。

- Prometheus 启动器当前通过 `ConnectionSettings` 读取 settings。
- urls 可包含多个地址；运行代码按逗号、分号、竖线拆分取得的 urls 值。外层连接字符串的分隔语义仍先由 Core 决定，不能混淆两层分隔。
- 抓取路径由 path 提供，常见值为 `/metrics`；路径不是新的端口。
- 当前配置中的回环地址不能被工具偷偷换成通配地址以便容器访问。
- 同文件中 OTLP exporter 的 `server=http://localhost:4317` 是客户端目的地址，不是本进程监听。
- settings 的存在及某条 exporter 的存在，也要结合实际启用的诊断组件判断。

### 4.5 OPC、MQTT、Garnet 与通用 TCP

- [OpcServerOptions.cs](../framework/externals/opc/src/OpcServerOptions.cs) 的默认 URL 为 `opc.tcp://localhost:4840`；多个安全策略可共用同一个端点。不要导出 Discovery 注册地址，也不要因安全策略数量增加端口。某些 OPC 地址可能带路径，不能先把通用 URL 规范定成“一律不许路径”再忽略此场景。
- [MqttQueueServer.cs](../framework/messaging/mqtt/src/MqttQueueServer.cs) 当前明确设置默认 TCP endpoint 的端口。尚未从本仓库实现确认内嵌服务器暴露 TLS/WebSocket 的配置能力，不能凭第三方库能力假定已经支持。
- [GarnetServer.cs](../framework/externals/garnet/src/GarnetServer.cs) 使用 Core Settings，支持多种底层参数映射，其中包括 bind、port、TLS、UnixSocketPath 和集群通告字段。Unix socket 不属于宿主 TCP/UDP 端口映射，通告字段也不是另一个监听。
- [TcpServer.cs](../framework/Zongsoft.Net/src/TcpServer.cs) 提供可配置或由启动参数改变的地址/端口。显式约定要覆盖真正参与启动的值，不能导出一个随后被参数覆盖的旧值。

### 4.6 不新增独立监听的 Web 功能

下列功能主要注册路由、中间件或 Web 服务，复用已有 Web 宿主监听：

- Hangfire Dashboard。
- Security Web API、Captcha。
- OpenAPI、SignalR。
- Zongsoft.Web.Grpc 的服务映射。
- Intelligences Web 的流式响应。
- Grapecity Web 报表/设计器。
- Aliyun、Wechat 的 Web/gateway 控制器。
- Upgrading Web API。

其中 Diagnostics OTLP 插件额外提供 Kestrel endpoint 配置，需与“仅映射一个 gRPC 服务”的行为区分。同一个 Kestrel endpoint 也可能服务多种路由，不能设计成每个端口只能属于 http 或 grpc 的互斥类型。

### 4.7 客户端、基础设施及样例

Data 及 ClickHouse、DuckDB、Influx、MsSql、MySql、PostgreSql、SQLite、TDengine 驱动中的连接配置，是应用连接数据库的设置；不会因此在应用容器新增数据库服务端口。

Kafka、RabbitMQ 客户端，以及 Redis、Etcd、Amazon S3、Aliyun、Wechat 等外部连接同理。基础服务容器的端口仍由它们自己的容器交付定义负责，不能从应用的连接地址反推一个同名基础服务应该被创建。

Core、Commands、Reporting、Hardwares、通用插件设施及各类文档/脚本适配库，不因名称或依赖项存在就认定有生产监听。服务器 samples 证明能力用法，不是部署启用证明。

本次生产源码检索没有发现必须立即覆盖的独立 UDP/HTTP3 默认监听；这不构成永远没有 UDP 的保证。UDP、HTTP3、Unix socket 等支持范围需要在正式规范中明示，不能把未知协议默认当 TCP。

<a id="pipeline"></a>

## 5. 目标处理链与职责

```text
.env / 显式工具变量
        │
        ▼
deployer：展开 *.option.template
        │
        ▼
实际 *.option，包含 <?binding ...?> 提取声明
        │
        ▼
packager：确定文件及有效配置 → 收集指令 → 提取、校验、合并
        │                                      │
        │                                      └─ 为 Web 代理提供应用端点信息
        ▼
web.profile → Nginx 最终配置 → 导出 Nginx 监听信息
        │
        ▼
一份包级绑定元数据
        │
        ▼
containerizer：确定执行目标、网络、代理和端口发布
```

### 5.1 deployer

沿用现有模板机制，输出实际运行配置。模板中的变量由 Core 求值，XML 值使用适当的 XML 格式化。

新功能不要求 deployer 理解 Kestrel、Prometheus 或 ZeroMQ 的监听结构。它也不负责启动应用进行探测。不应仅为绑定提取另造一套 `.env` 导入和覆盖机制。

### 5.2 packager

建议承担：

1. 按 source、打包输入及排除条件确定应用配置范围。
2. 建立与本次运行组合相对应的有效配置视图。
3. 收集启用配置中的 binding 指令。
4. 复用 Core 读取配置字段和 Settings，执行有限的筛选及展开。
5. 验证必需值、协议、端口、重复和矛盾。
6. 让 Web 配置器引用明确的应用端点。
7. 从最终 Nginx 配置导出其监听，合并为包元数据。
8. 保留文件、指令行号和配置路径用于诊断，但不要求在交付元数据中保留业务插件归属。

不要将客户方案目录、包缓存、Framework 源码中所有 `.option` 无差别扫描后合并。提取应针对真正交付并参与配置的文件，不是全仓搜索。

### 5.3 containerizer

读取统一结果，处理执行目标和端口发布。它不应再次实现各插件的 `.option` 解析，也不应通过反射启动插件查找监听。

需要区分：

- 应用容器实际监听。
- Nginx 等代理容器监听。
- 应用间内部连接。
- 宿主机对外发布。
- 健康检查所使用的协议和地址。

已有“出现 Nginx 就不再单独处理其他 HTTP 监听”等概括性判断，需要改成逐个端点判断。

### 5.4 运行时

运行程序继续使用 `.option` 等本来就会读取的配置。Core 当前会忽略 XML 处理指令；无需让 Web 宿主、MQTT、ZeroMQ、OPC 等读取包内 `.bindings`。

生成元数据不能改变配置含义，也不能代替运行时配置。监听变化应重新制包及生成容器交付物。

<a id="instruction"></a>

## 6. XML 处理指令规范：确定项和待定项

### 6.1 名称与路径已经确定

```xml
<?binding path="Kestrel/Endpoints" url="Url" protocols="Protocols" ?>
```

- 指令目标名为 `binding`。
- path 表示配置路径，使用 `/` 分隔。工具内部可转换为 Core 配置 API 的键分隔形式。
- path 不是 XML XPath；不能在没有明确支持的情况下夹入 XPath 谓词。
- 真实 XML 的根可能为 configuration 或 options，集合项也可能采用不同元素名称。应复用 Core 将它们映射为配置键的行为。
- 路径大小写、前导斜杠、尾部斜杠和非法片段的规范化需正式定义；不应擅自改变真实配置键。

### 6.2 参数示例尚不是最终语法

以下名称只是讨论中的候选，不代表已经实现或全部定案。

| 参数/能力 | 示例 | 仍需决定的语义 |
| --- | --- | --- |
| path | `Kestrel/Endpoints` | 指向当前项还是默认枚举子项，如何明确区分。 |
| url | `Url` | 配置字段引用；是否另设 urls 以区分多值。 |
| protocols | `Protocols` | HTTP 协议能力；实际字段缺失时不能凭空复制运行时默认值。 |
| settings | `Settings` | 引用外层字符串后进入 Settings；需要解决同条规则混合内外字段的情况。 |
| match | `Driver=prometheus` | 筛选先于读取；比较类型、大小写、缺失值规则及有限逻辑组合。 |
| separators | `,\|` | 分隔字符集合还是完整分隔串；只作用于提取出的字段值。 |
| address / port | 字段引用 | 独立地址加端口；与完整 URL 形式互斥。 |
| transport / scheme | `tcp` 等 | 哪些是字面常量，哪些是字段引用；应用协议与传输协议不能混淆。 |
| 标识、禁用、覆盖 | 尚未定名 | 多文件的重复指令、规则替换、显式禁用与作用域。 |
| 单项/集合选择 | 尚未定名 | 自身字段、直接子项、列表或位置端口。 |
| 条件启用 | 尚未定名 | 运行配置的布尔条件与实例选择，不能仅以字段存在为启用。 |

建议不要设计任意代码执行能力。规则应能通过少量明确的配置选择、取值、转换和展开操作表达，并产生可定位的错误。

### 6.3 普通设置（Settings）与连接设置（ConnectionSettings）的使用约定

普通设置（`Settings`）与连接设置（`ConnectionSettings`）共用 Core 解析器，构造、`Value` 赋值和 `Settings.Parse/TryParse` 按相同语法处理文本。重复键按原文顺序后值覆盖，驱动别名参与同一逻辑参数的顺序。

- `ISettings` 是 `IDictionary<string, string>`；原始索引器、非泛型 `TryGetValue`、成员判断及枚举仅呈现显式主参数。
- 类型化读取使用驱动描述器及其默认值；工具判断字段是否存在时使用原始字典视图。
- 带 `TOptions` 的连接设置（`ConnectionSettings`）按描述器划分主参数和扩展属性集（`Properties`）；有效复合路径属于主参数，未知文本项属于扩展参数。
- 完整 `Value` 包含显式主参数及扩展项，代码修改同步到文本；工具解析完整文本时直接使用普通设置（`Settings`）。

工具引用的 Core 程序集须满足上述契约。详细行为见[设置契约](../framework/Zongsoft.Core/docs/settings.zh-Hans.md)。

### 6.4 XML PI 的词法规则必须写清

正式术语是 XML 处理指令（Processing Instruction，PI）。它用于嵌入应用指令，并不自动执行预处理。

XML 读取器提供目标名称和指令内容；其中 `path="..."` 形式属于本功能约定的参数语法，不会自动成为 XElement.Attributes。建议正式规范采用属性式写法，并规定：

- 单/双引号、换行、空白和转义规则。
- 未知参数、重复参数和格式错误应如何报告。
- PI 内容中的 `?>` 是终止标记，不能作为普通内容直接出现。
- 如果工具再按 XML 属性式语法解析内容，明确解码的层次，避免重复解码。
- 不用正则扫描整个文件替代 XML 解析。
- 不给运行时增加带命名空间的配置属性来模拟指令；现有 Core XML 读取器对该类属性有自己的限制，PI 已提供独立通道。

### 6.5 指令位置与作用范围

建议将声明放在文档头部，或规范允许的配置根级位置；是否允许放在任意节点附近，需要定稿。不要靠“紧跟某节点”推断配置语义，因为覆盖文件可能只修改属性而不重复指令。

必须明确：某条指令访问的是本文件、对应配置文件组，还是本次应用最终有效配置。前面“一个 Kestrel 规则可覆盖宿主与插件共同提供的端点”的讨论，要求有应用级有效配置视图，不能仅以单文件解析实现。

指令生效文件、规则标识范围、覆盖以及条件控制应一起确定。相同声明可去重；冲突不能取决于目录遍历顺序。

<a id="examples"></a>

## 7. 配置与指令示例

本节所有新增 binding 指令都是拟议语法。已确定的是指令名、路径分隔和职责；第 6 节列出的参数组合仍需定稿。示例省略不相关配置，不代表可以直接替换原文件的全部内容。

### 7.1 Web 宿主：只维护实际运行配置

拟修改现有文件 `D:\Zongsoft\hosting\.deploy\.shared\web.option.template`：

```xml
<?xml version="1.0" encoding="utf-8" ?>

<?binding path="Kestrel/Endpoints"
          url="Url"
          protocols="Protocols" ?>

<configuration>
	<option path="kestrel">
		<endpoints>
			<endpoint endpoint.name="http"
			          url="${web:url#xml}"
			          protocols="${web:protocols#xml}" />
		</endpoints>
	</option>
</configuration>
```

由现有模板部署规则生成 `web.default.option`。指令引用 Url 和 Protocols，不重复引用一份地址变量，因此之后修改 `web:url` 时，工具读取的就是部署出来的实际值。

本例按“枚举 Kestrel/Endpoints 的直接子项”理解；正式语法仍需明确单项/集合模式。

### 7.2 Diagnostics OTLP：来自插件的 Kestrel 端点

当前文件：

```xml
<!-- D:\Zongsoft\framework\Zongsoft.Diagnostics\protocols\server\src\Zongsoft.Diagnostics.Protocols.Server.option -->
<options>
	<option path="kestrel">
		<endpoints>
			<telemetry url="http://*:4317" protocols="Http2" />
		</endpoints>
	</option>
</options>
```

只要这个插件及其配置在本次应用中实际启用，应用级 Kestrel 提取规则应能取得该端点，不必额外维护一个 4317 的 `.bindings` 文件。

如果选择让插件也自带同样的 Kestrel 提取规则，必须有稳定的相同规则去重机制。不能因为两处声明就产生重复绑定，更不能把同一端点误判成两个服务器占用端口。

### 7.3 Prometheus：从 Settings 取 urls

拟修改现有文件 `D:\Zongsoft\framework\Zongsoft.Diagnostics\src\Zongsoft.Diagnostics.option`，核心片段示意：

```xml
<?xml version="1.0" encoding="utf-8" ?>

<?binding path="Diagnostics/Diagnostor/Meters/Exporters"
          match="Driver=prometheus"
          settings="Settings"
          url="urls"
          separators=",|" ?>

<options>
	<option path="/Diagnostics">
		<diagnostor>
			<meters>
				<exporters>
					<exporter exporter.name="prometheus"
					          driver="prometheus"
					          settings="urls=http://127.0.0.1:9464,http://localhost:9464;path=/metrics;" />
				</exporters>
			</meters>
		</diagnostor>
	</option>
</options>
```

处理意图：

1. 选择 meters 的 exporter 子项。
2. 先匹配外层 Driver，避免处理 OTLP 客户端 exporter。
3. 读取外层 Settings，使用 Core 普通设置（`Settings`）解析。
4. 取 urls 的值。
5. 按明确的字段内分隔符展开 URL。
6. 记录实际回环监听，不将其改成通配监听。

`match` 只说明如何筛选 exporter，并不自动证明诊断服务或该 exporter 已启用；最终规则还要与实际启动条件一致。

### 7.4 ZeroMQ：有位置语义的多端口

现有模板中的配置片段：

```xml
<!-- D:\Zongsoft\hosting\.deploy\.shared\Zongsoft.Messaging.ZeroMQ-daemon.option.template -->
<option path="/Messaging/ZeroMQ">
	<servers port="${messaging.zeromq:control},${messaging.zeromq:incoming},${messaging.zeromq:outgoing}" />
</option>
```

部署后的示意值：

```xml
<servers port="32100,32101,32102" />
```

提取结果需要表达：

| 位置 | 运行语义 | 提取要求 |
| --- | --- | --- |
| 第一个 | 控制端口 | 还要满足控制通道实际启用条件。 |
| 第二个 | 输入端口 | 必须是打包时已确定的有效端口。 |
| 第三个 | 输出端口 | 必须是打包时已确定的有效端口。 |

发现端口需要先补齐实际配置来源。本节故意不提供一个貌似完整的 binding 指令，以免把“任意分割全部端口”误写成已经成立的方案。

需要决定：有限的位置选择能否满足该结构，还是应同步完善服务器选项，使各个监听参数具有更清楚的配置字段。涉及 Framework 的改造应先说明具体契约及复用方式。

### 7.5 普通字段与 Settings 混合的输入

以下是语法设计用的假想配置，不是仓库中已经存在的服务器文件：

```xml
<!-- 假想文件：D:\Zongsoft\hosting\.deploy\.shared\Example.Server.option.template -->
<configuration>
	<option path="/Example">
		<server address="${example:address#xml}"
		        settings="port=${example:port#xml};" />
	</option>
</configuration>
```

这里 Address 来自普通配置，Port 来自解析后的 Settings。前面采用一个全局 `settings="Settings"` 切换所有字段来源的草案，不足以表达这种情况。

正式规范必须选定一种清楚的字段引用方式，例如分别声明来源或采用受限字段访问语法；不能继续靠“这个参数有时读外层、有时读内层”的隐式规则。具体拼写尚未决定，不应直接按聊天中的临时示例实现。

### 7.6 显式配置覆盖的验证例

以下数值仅用于设计和测试，不是在修改当前宿主端口。

```xml
<!-- 测试输入：临时 source/web.option -->
<configuration>
	<option path="kestrel">
		<endpoints>
			<endpoint endpoint.name="http" url="http://*:8069" protocols="Http1" />
		</endpoints>
	</option>
</configuration>
```

```xml
<!-- 测试输入：同一临时 source/web.default.option -->
<configuration>
	<option path="kestrel">
		<endpoints>
			<endpoint endpoint.name="http" url="http://*:8070" />
		</endpoints>
	</option>
</configuration>
```

如果 default 是本次实际站点，按对应运行配置加载规则，最终应导出 8070，并保留从有效配置继承的协议设置。8069 不能作为额外端点残留。若 default 文件未被本次运行选择，不能仅因它位于目录中就生效。

### 7.7 Web 代理入口与应用监听

保留 `D:\Zongsoft\hosting\web\default\web.profile` 的作用。下面使用示例域名，展示语义而非当前完整配置：

```ini
# web.profile 的现有机制示意；server=~ 的新端点选择规则仍待定。
server=~

[api]
host=api.example.test
bind=http://*:8080
```

这里的 8080 是代理入口；应用自身仍可能监听 8069。不能把 profile 的入口端口改写进 Kestrel。

若没有代理，容器发布也可以是“宿主机 8080 → 应用容器 8069/tcp”。这只是映射语义示意，不是在恢复已经撤回的 port! 配置语法。

### 7.8 应报告错误的输入

- 已启用声明引用不存在的 Url/Port 字段。
- 端口是空值、非数值、越界值，或运行时随机端口标记。
- 声明中的配置路径拼错却没有任何可解释的禁用条件。
- 多地址字段存在空项或不符合约定的分隔方式。
- 完整 URL 与独立 address/port 同时指定。
- 一条规则要求显式协议，却读取不到该协议字段。
- 提取结果还包含未展开变量。
- 多个 HTTP 后端都能匹配 `server=~`，没有明确选择。
- 只生成了部分必要绑定，却准备把元数据作为完整结果发布。

“集合本来可以为空”与“路径拼错”的区别需要通过规则的基数/必需性约定解决，不能一律空集合成功或一律失败。

<a id="configuration"></a>

## 8. 有效配置、条件和覆盖

### 8.1 不可用目录扫描代替配置加载

当前宿主配置加载考虑：

- 基础宿主 option。
- 环境对应文件。
- host 和 site 对应文件及相关环境配置。

插件配置还考虑：

- 与插件文件对应的基础 option。
- 环境 option。
- 环境附属 option。
- host/site 专用文件。
- 插件本身是否被装配。

这些规则分布在 Plugins 的配置提供者和宿主构建器中。若多个插件对同一全局配置键有贡献，还需核实真实的优先关系；不能自行规定字母序或最后扫描的文件获胜。

建议在实现前找到可离线复用的配置选择/组合职责。如果既有 API 与插件树或运行服务耦合，需要先告知用户并讨论必要重构，而不是在工具中复制一套近似加载器。

### 8.2 指令收集与值求取分开

推荐顺序：

1. 确定本次真正使用的配置文件。
2. 使用 Core 形成最终配置视图，同时记录提取声明的来源。
3. 按规则标识和作用域处理重复、覆盖、禁用。
4. 判断条目和监听的启用条件。
5. 从最终视图读取字段。
6. 解析 Settings、转换和展开。
7. 校验并生成绑定。

这样基准文件可以只声明一次提取方式，覆盖文件只修改实际值。

规则标识是否全局唯一、是否按配置文件组隔离，仍需定稿。业务插件归属可作为制作时诊断上下文，不必进入最终端口描述。

### 8.3 不能只看“配置存在”

需要覆盖：

- 包中有客户端插件，但没有服务器启动插件。
- 配置集合包含多个预设，运行对象只选中一个。
- 服务器由开关禁用。
- ZeroMQ 控制通道因没有存储而不启动。
- 应用参数实际覆盖了配置。
- 业务代码在运行时动态决定是否创建服务器。

最后一种若不能由部署配置确定，应明确不在自动静态提取的保证范围内。不要为此启动应用探测，也不要仅靠扫描 DLL 名称决定端口。

### 8.4 确定配置组合，而非固定目录组合

环境、站点、scheme 和网络选择必须与本次交付一致，但不要求工具强制使用某种目录树，也不要求创建所有组合的输出文件。

包元数据是制作时快照。部署后修改监听参数、改变启用条件或以启动参数覆盖监听，都可能使快照失效；相应交付物需要重新生成。

<a id="model"></a>

## 9. 统一绑定模型与容器化

本节是语义需求，不是已经批准的字段名或类定义。

### 9.1 模型至少要表达什么

| 信息 | 用途与约束 |
| --- | --- |
| 监听地址与端口 | 反映实际配置；保留回环、通配、IPv4/IPv6 语义。 |
| 传输协议 | TCP 与 UDP 分开；未知协议不得默认为 TCP。 |
| 应用协议 | HTTP、HTTPS、opc.tcp 等，不能仅以一个通用 tcp 字样丢掉代理需要的信息。 |
| HTTP 协议能力 | 例如 Http1、Http2；不等于路由一定采用 gRPC。 |
| 执行目标 | 应用与 Nginx 等不同进程/容器需要区分，不必细分每个业务插件。 |
| 可引用身份 | 如 Web Profile 需要选择某个应用入口，必须有稳定引用方式；命名待定。 |
| 必要的代理入口关系 | 保留已有 Nginx 域名、默认站点等信息，不因合并丢失现有消费能力。 |
| 来源位置 | 制作时诊断使用，是否写入包不作为必要要求。 |

同端口可能出现在不同地址、不同传输或不同执行目标上。相同声明的去重与真正端口冲突也不相同，不能仅按整数端口做 Distinct。

应分别校验应用内部监听、生成的代理监听，以及宿主机发布冲突。不能把系统是否允许复用端口的复杂语义简化成“同端口必然冲突”。

### 9.2 三种地址不能混淆

```text
实际监听：http://*:8069
代理连接：http://127.0.0.1:8069 或 http://应用服务名:8069
宿主发布：8080 → 容器 8069/tcp
```

- `*`、`0.0.0.0`、`[::]` 不是可直接交给代理连接的业务后端主机名。
- 回环地址不能自动改成通配地址；普通独立容器中的回环监听不能仅通过增加宿主发布就保证可访问。
- 同机安装与跨容器代理需要不同的连接目标构造。
- IP、DNS、localhost、通配符及 IPv6 zone 的支持和规范化需要逐项定义。不同服务器对非 IP 主机名的解释未必相同。
- OPC 路径、Prometheus 抓取路径、Web 业务路径不应参与 TCP 端口去重，但也不能在还需要构造代理请求时丢失必要语义。
- ZeroMQ、OPC、Garnet 等对外通告/发现地址的处理，不能冒充普通 NAT 映射已经解决。

### 9.3 HTTP、gRPC、HTTPS 与代理选择

- HTTP/2 不自动等于 gRPC；同一 Kestrel endpoint 可以承载多个服务和路由。
- 独立 Prometheus HttpListener 不能受 Kestrel `--urls` 管理。
- TLS 终止可能发生在应用，也可能发生在 Nginx；绑定元数据不携带私钥，也不代替运行证书配置。
- 代理采用普通 HTTP、gRPC 等哪种转发方式，继续由 `web.profile` 及配置器能力确定。
- 当前自动生成的普通代理配置不能直接被宣称已经支持所有 grpc_pass 场景；应核对配置器并单独验收。
- HTTP3 若纳入支持，需要明确其传输语义；不能把所有 http/https 端点无条件只生成 TCP 映射。

### 9.4 server=~ 的新语义必须定稿

取消重复 listen 后，应用可能有 8069、4317、9464 等多个 HTTP 地址，不能随便选第一个作为主站点后端。

讨论中的建议是：唯一合适候选可以自动确定；多个候选必须明确选择。需要进一步决定：

- 在 binding 声明中标记主入口，还是让 `web.profile` 显式引用绑定身份。
- 如何保留应用后端与普通远程后端的区别。
- 通配监听如何转换成当前安装形态下的可连接地址。
- 如何在容器化时重定位本应用后端，而不误改一个恰好同端口的远程服务器。

这是未定事项，不能在实现中悄悄选择“排序第一个”。

### 9.5 端口发布与健康检查

从应用监听得知“容器内有哪些端口”，不等于“所有端口都必须发布到宿主机”。内部端口、直接发布端口、代理后端端口需要由交付策略区分；宿主绑定 IP 和宿主端口也属于部署策略。具体配置入口仍待定，不能恢复临时 port! 语法充当最终方案。

健康检查也不能简单使用“第一个 HTTP URL”：

- 纯 HTTP/2 gRPC 服务未必能用普通 HTTP/1 curl 探测。
- 路由、授权、TLS 和必要 Host 请求头可能影响结果。
- TCP 连通与应用业务健康不同。
- 缺少可靠 HTTP 探测信息时，不应编造一个必然通过的检查。

应审视现有 ApplicationHealth 行为，明确可推导部分、显式配置部分和合理的降级方式。


<a id="package"></a>

## 10. 合并结果与安装包规范

### 10.1 一个逻辑结果，包含应用与代理

已确认的目标是：同一安装包只提供一份统一的绑定结果。原有 `.web/nginx/.bindings` 的信息并入其中，不再要求 Containerizer 分别寻找两套端口描述。

需要保留的并不是“这些端口属于哪个业务插件”，而是“哪个执行目标实际监听这些端口”。应用和 Nginx 可能运行在不同容器中，同一端口的含义不同；合并文件仍必须表达这个区别。

合并不能影响 `.web/nginx/` 中真正的配置文件及其模板用途，也不能把整个目录删除。

建议的结果约束：

1. 写入已经展开、转换、校验的确定值，不再包含变量、配置路径、提取表达式或指令。
2. 结果由本次最终应用配置和最终代理配置共同生成，不靠手工追加。
3. 顺序稳定；重复声明按明确规则归并；冲突报告指向原始配置。
4. 不携带 Settings 全文、连接凭据、证书私钥或不相关配置。
5. 文件为空、文件缺失、提取失败是不同状态，不能混同为“没有任何监听”。
6. 下游能识别不支持的格式或协议，并明确失败；格式版本机制是否必要、怎样表达，需要在正式格式中确定。
7. 此文件服务于工具，不作为应用运行时配置，不要求应用或插件读取它。

### 10.2 合并文件示意：尚未冻结的语法

下面仅说明需要表达的信息；章节名称、字段名、身份编码及值的规范化均是草案。

示意位置：tar.gz 安装包的根目录 `/.bindings`，**不是**安装后的 `/opt/zongsoft/web/.bindings`。

```ini
# 拟议的统一元数据；不是现有工具已经接受的格式。
# application 表示应用进程，nginx 表示代理执行目标。
# 数字编号仅为展示，不作为稳定绑定身份方案。

[application 1]
url=http://*:8069
transport=tcp
protocols=Http1

[application 2]
url=http://*:4317
transport=tcp
protocols=Http2

[application 3]
url=tcp://*:7969
transport=tcp

[nginx api]
host=api.example.test
bind=http://0.0.0.0:8080,http://[::]:8080
```

这个示例没有声称：

- 7969 是所有 TCP 插件的固定端口。
- 三条 application 绑定都必须发布到宿主机。
- `Http2` 就足以决定使用 gRPC 代理。
- 只要声明了一个 IPv6 监听，就一定同时覆盖 IPv4。
- 所有安装格式都能原样在同一物理根目录存放此文件。

如果 Web Profile 已有默认站点、多个域名、特殊监听参数等，合并模型必须完整保留当前消费所需的信息。上例为简化展示，并非完整 Nginx 元数据定义。

### 10.3 Nginx 绑定必须来自最终配置

现有 `D:\Zongsoft\tools\packager\src\Web\Configurator.Nginx.Bindings.cs` 的价值在于从配置器处理后的 Nginx 结构导出入口，而不是简单复制 `web.profile` 原始的 bind 字段。

重构必须保留这个原则：用户的原生 Nginx 指令可能改变最终监听；最终元数据应反映配置器实际输出。

当前导出器对不能安全表示的结构会拒绝导出并报告信息。统一流程还需明确：

- 不支持的 include、动态或复杂匹配等结构，如何说明无法完整提取。
- 未生成 Nginx 元数据时，是否允许继续制作仅供传统安装的包。
- Containerizer 如何拒绝把一个不完整结果当作完整端口映射。
- 如何避免失败后保留上一次生成的旧 `.bindings`。

不能仅为凑齐一个文件而忽略无法解析的入口。

### 10.4 “安装包根目录”在不同格式中的含义

这里存在必须继续确认的物理存储问题。

| 包格式 | 现有结构与约束 | 建议方向，尚未定稿 |
| --- | --- | --- |
| tar.gz | 可以有归档根目录元数据，载荷由安装脚本处理。 | 根目录 `.bindings`，安装与容器载荷提取必须排除它。 |
| deb | 包外层是 ar，内部区分 `control.tar.*` 与 `data.tar.*`；数据归档条目会成为安装载荷。 | 研究控制归档或其他合法包元数据位置；验证制作工具、读取器和实际安装器行为。不能把工具元数据混入 data 载荷。 |
| rpm | 元数据在 RPM Header，载荷有文件清单及安装语义。当前已存在应用监听等自定义元数据读取链。 | 研究承载统一序列化结果的 Header 元数据，或明确其他合法存放机制；需验证字段类型、大小及读写库支持。 |

建议统一的是一份逻辑模型和一套读取接口；物理承载遵循包格式。若用户坚持三种包都必须是字面意义上的根目录文件，应先把各格式的实际结构展示清楚，再作决定。

不能用“安装后再删掉 `.bindings`”掩盖包文件归属错误，也不能宣称 tar.gz 的根目录方案已经自动解决 deb/rpm。

### 10.5 源端手写 .bindings 的地位

早期讨论确认过“每个目录使用固定名称 `.bindings`，打包后合并”，而不是为每个插件创建 `PluginName.bindings`。

后续选择 XML 指令，就是为了尽量省掉这些手写源文件，改由真实 `*.option` 提取。当前设计主线应围绕指令实现。

是否仍为非 XML 配置提供手写 `.bindings` 入口，尚未确认。不要自动保留为兼容机制，也不要同时实现两套可以互相覆盖而没有优先规则的输入。

<a id="implementation"></a>

## 11. 最终改造目标与影响范围

本节列出工具实现所需的依赖契约及实施位置；Core 提供现有设置解析能力，其余工具能力以各项说明为准。

### 11.1 按职责修改

| 位置 | 目标 |
| --- | --- |
| `D:\Zongsoft\framework\Zongsoft.Core\src\Configuration\Settings*.cs`、`ConnectionSettings*.cs` | Core 提供共用设置解析器；工具直接调用普通设置（`Settings`），引用的程序集须满足第 6.3 节契约。 |
| `D:\Zongsoft\framework\Zongsoft.Core\src\Configuration\Xml\XmlStreamConfigurationProvider.cs` | 核实现有 XML 到配置的映射及 PI 保留/忽略行为；若缺少提取需要的复用入口，先说明问题再讨论改造。 |
| `D:\Zongsoft\framework\Zongsoft.Plugins\src\Hosting\ApplicationBuilder.cs`、`Configuration\PluginConfigurationProvider.cs` | 确定可以离线复用的配置选择和合并能力，避免 Packager 重写不一致的加载顺序。 |
| `D:\Zongsoft\tools\deployer\src\TemplateResolver.cs` | 模板展开后保留合法 XML 指令，正确报告模板错误；不承担插件监听业务。 |
| `D:\Zongsoft\tools\packager\src\PackageOptions.cs`、`PackCommand.cs`、`ApplicationHost.cs` | 调整重复 listen 概念及所有引用；建立提取过程需要的确定宿主/环境上下文。选项移除方式需与正式设计一致。 |
| `D:\Zongsoft\tools\packager\src\Scriptor.Systemd.cs` | 根据最终方案停止生成重复或误导性的 `--urls`；保留其他启动参数和既有服务文件处理规则。 |
| `D:\Zongsoft\tools\packager\src\Web\Definition.Resolver.cs` | 明确 `server=~` 如何指向真实应用端点，处理多个候选及通配地址。 |
| `D:\Zongsoft\tools\packager\src\Web\Configurator.Nginx.Bindings.cs` 等 | 保留最终 Nginx 配置导出，将其合并到统一结果；不能降低原有验证能力。 |
| `D:\Zongsoft\tools\packager\src\Generator.Tar.cs`、`Generator.Deb.cs`、`Generator.Rpm.cs` 及公共生成流程 | 写入各包格式的统一绑定元数据，清除被替代的监听元数据与孤立代码；保证工具元数据不进入运行载荷。 |
| `D:\Zongsoft\tools\containerizer\src\PackageReader.cs` 及 `PackageReader.Tar.cs`、`PackageReader.Deb.cs`、`PackageReader.Rpm.cs` | 统一读取模型；不再分别依赖旧 Listen 与旧 Nginx 绑定文件，也不解析应用 option。 |
| `D:\Zongsoft\tools\containerizer\src\ApplicationPlanner.cs`、`ApplicationHealth.cs` | 生成正确的端口、启动与健康检查计划，取消从旧单一 listen 推导全部行为的假设。 |
| `D:\Zongsoft\tools\containerizer\src\WebIngress.cs`、`WebIngress.Files.cs` | 保留代理重定位功能，以明确的后端关系取代含糊的端口匹配；支持应用和代理分别消费绑定。 |
| `D:\Zongsoft\tools\.shared` | 仅在多个工具确实共享模型、序列化或诊断职责时复用；不要各自复制同一套代码，也不要让工具互相引用可执行项目以求复用。 |
| `D:\Zongsoft\hosting\.deploy\.shared\*.option.template`、具体插件的 `*.option` | 添加声明与必要的显式配置；核实字段确实被运行对象读取。 |
| `D:\Zongsoft\hosting\.shared.ps1`、根目录 `pack.cmd`、`containerize.cmd` 等 | 仅同步确实受影响的参数和提示，不引入新的 scheme/env/network 固定组合。 |
| `D:\Zongsoft\tools\migrator` | 参与整体交付验收，但不因统一绑定而增加不属于它的监听解析职责。 |

以上是已知职责位置，不是最终的类和文件结构。实现前应按最新源码重新定位所有引用，不能只改表中列出的几个文件。

### 11.2 必须整体处理 --listen

若最终决定取消 Packager 的 `--listen`，应一并处理：

- 命令选项、资源消息、变量/属性/字段、验证逻辑。
- Systemd 服务生成和已有服务文件的处理边界。
- tar/deb/rpm 的旧监听元数据读写。
- `server=~` 的解析与 Web Profile 示例。
- Containerizer 的启动参数修改、端口推导、代理上游重定位和健康检查。
- hosting 的脚本提示、传参、说明和测试。
- 双语 README、实现文档和相关 SKILL。

不能只删命令参数，却仍由 Containerizer 拼接 `--urls`。也不要因为命令选项叫 listen，就误删模板中用于生成实际运行配置的普通同名变量。

### 11.3 Framework 调整的边界

用户要求复用 Core 和共享代码；若它们存在问题或不足，先说明具体源码、输入和影响，再由用户决定如何重构。此前用户针对特定 Core 问题、ZeroMQ 插件问题作出的修复授权，不等于本次可以无条件改造所有框架接口。

普通设置（`Settings`）与连接设置（`ConnectionSettings`）共用 Core 解析器。工具使用第 6.3 节契约读取完整文本和显式参数，依赖以项目实际引用的程序集为准。

同理，如果某个服务器的配置对象没有端口属性，或者虽然写了 option 但启动代码并不读取它，应在该服务器的真实配置链上解决。不要让 XML 指令输出一个实际并未监听的端口来绕过问题。

### 11.4 开发约束

后续实施必须继续遵守工作区的最新 AGENTS、SKILL 和 `D:\Zongsoft\guidelines`，尤其是：

- 不考虑兼容旧的临时方案；代码、文档、注释直接描述最终效果，不编写重构过程流水账。
- 保留与本功能无关的未提交修改，不用整文件回退清除局部功能。
- 文本使用 CRLF；手写代码使用 Tab；按阶段适当留空行；第三方文件保持上游格式；shell 脚本按 Git 属性保留 LF。
- XML 文档注释应遵循项目规范，尽量使用语义化的XML元素(`<c>...</c>`, `<see ...></see>`)；对同类公共成员保持完整一致，不只给其中几个成员加注释。
- 不为方便测试扩大生产代码的访问范围。
- 命令和资源变更同步双语文档及资源来源，生成文件通过既有生成流程更新。
- 四个工具的通用输出继续复用 `tools/.shared/`：警告暗黄色配高亮参数，错误暗红色配紫色参数；不重新引入各工具重复的 Output 实现。
- 依赖和工具版本以项目及仓库版本配置为准，文档不把某次讨论的版本号变成长期固定要求。
- 仓库 Cake 的发布任务可能向 NuGet 推送；不要把发布任务当作普通本地验证。

## 12. 尚未定稿的事项与后续讨论顺序

### 12.1 待定事项

下面的“建议方向”用于延续讨论，并不代表用户已经批准其精确语法。

| 优先级 | 必须定稿的问题 | 建议方向 |
| --- | --- | --- |
| 1 | Packager 怎样获取与运行时一致的有效配置和插件选择？ | 找到可离线复用的 Framework API；不要自行按文件名排序合并。 |
| 1 | 指令针对单对象还是集合，怎样引用字段、Settings 内字段及混合来源？ | 先用 Kestrel、Prometheus、ZeroMQ 三种真实输入推导最小语法，再验证普通字段与 Settings 混合示例。 |
| 1 | 声明怎样表达启用条件、选择中的实例和配置缺失？ | 明确可静态判断的范围、必需性和空集合语义；动态业务启动不伪装为已知。 |
| 1 | 指令作用域、规则标识、覆盖/禁用以及重复规则如何处理？ | 从最终配置求值，允许基础文件声明一次；标识作用域必须清楚。 |
| 1 | `server=~` 如何选择主 HTTP 入口？ | 唯一候选可推导；多个候选要求明确引用，不按扫描顺序取第一个。 |
| 2 | 多 URL、多端口、位置语义、地址默认值与协议来源怎样表达？ | 采用受限、可验证的声明，不设计任意脚本语言，也不在工具写死插件类名。 |
| 2 | 哪些端口只供内部使用，哪些通过代理，哪些直接发布？ | 与实际监听分层，明确容器发布策略及宿主端口配置入口；不恢复 port! 临时语法。 |
| 2 | 通配、回环、IPv6、同端口多协议及发现/通告地址如何处理？ | 分清监听、连接和发布，针对每种服务器验证；NAT 不能代替服务通告配置。 |
| 2 | 最终 `.bindings` 的字段、身份及格式版本？ | 一个结果表达应用和代理；保留当前 Nginx 消费所需语义，不记录不必要的插件归属。 |
| 2 | deb/rpm 的物理存放方式？ | 使用合法的包元数据结构，并验证安装后不进入运行目录。 |
| 2 | 无法完整提取时制作包和制作容器分别是否允许继续？ | 不允许下游把部分结果当成完整结果；诊断精确到文件、指令和配置路径。 |
| 3 | 是否保留非 XML 的源端手写描述入口？ | 先完成真实 option 场景；有具体需求再定，不为兼容旧草案增加双轨输入。 |
| 3 | 是否移除运行产物中的 PI、是否持久化来源诊断？ | 不影响运行读取；工具结果不携带敏感配置；按实际成本和需要决定。 |
| 3 | 健康检查如何支持 HTTP/2、TLS、授权和非 HTTP？ | 不从端口表凭空推导完整业务探测，保留必要的显式配置。 |

继续讨论时遵循用户要求：**问题逐个询问，给出真实文件路径、配置片段和设计理由。** 不要一次把整张表抛给用户选择，不使用没有具体文件指代的“清单”等含糊叫法。

### 12.2 推荐的实施顺序

1. 重新核实当前分支和未提交修改；确认 Core Settings 与配置加载能力的最新状态。
2. 用真实配置冻结 XML 指令最小语法、有效配置上下文和错误规则。
3. 冻结统一结果模型、包内存放方式及 `web.profile` 选择应用端点的约定。
4. 必要时先完成已确认的 Framework 配置能力调整，覆盖真实服务器的显式配置消费。
5. 在 Packager 实现指令发现、最终配置求值和模型校验；先用临时目录与本地输入验证。
6. 合并 Nginx 最终配置导出，处理三种包格式及工具元数据排除。
7. 改造 Containerizer 的读取、端口规划、代理和健康检查，整体处理旧 listen 链。
8. 同步 hosting 示例模板、脚本和工具文档，删除被替代且确实无用的代码。
9. 执行分层测试，再在明确的测试环境完成安装、容器和本地 Debug 验收。
10. 文档只描述最终使用效果；本设计交接稿的待定项在确认后及时更新，避免形成互相矛盾的规则。

本次请求仅整理文档。此前用户已要求停止容器和本地 Web；新会话不能仅因看到后面的验收清单就擅自恢复运行服务。

<a id="acceptance"></a>

## 13. 实现验收目标

以下均是新功能完成后需要验证的目标，不能把本文中的设计示例当作已通过的测试结果。

### 13.1 解析与配置语义

| 场景 | 期望结果 |
| --- | --- |
| `<?binding ...?>` 及 `path="Kestrel/Endpoints"` | 正确识别单数指令和斜杠路径；普通 XML 注释、其他 PI 不冒充声明。 |
| 引号、实体、空白、重复参数、未知参数、终止符 | 严格遵循正式词法规范；错误包含文件位置，不用整文件正则猜测。 |
| Kestrel 单端点与多端点 | 正确取得最终地址、端口和协议能力。 |
| 基础配置被实际选中的站点或环境覆盖 | 只导出最终生效值；不残留被覆盖的端口。 |
| 未选择的环境文件、未装配的插件、源目录中的模板 | 不因扫描到文件就纳入监听。 |
| 同一全局配置路径由多个实际插件贡献 | 与运行配置提供者的真实合并行为一致。 |
| 指令重复、覆盖、禁用及跨文件标识 | 遵循已定作用域，无扫描顺序依赖。 |
| Settings 中大小写、空白、引用、转义和分隔符 | 与所引用 Core 的实际解析一致；没有工具私有的第二套解析规则。 |
| 外层 Address 与 Settings 内 Port 混合 | 能明确解析来源，不使用模糊的隐式回退。 |
| 未启用条目、可为空集合、缺失必需字段 | 三类情况有清楚且可验证的不同结果。 |
| 多 URL 与有位置语义的端口列表 | 不把 ZeroMQ 的控制/输入/输出位置丢掉。 |
| 动态端口、越界端口、残留模板变量 | 不生成看似有效的容器绑定；明确报告无法静态确定。 |
| 入站监听与出站连接混在同一 option | 只按声明和真实启用状态提取监听，不扫描所有 server/port 字段。 |
| 配置包含凭据 | 只输出必要绑定字段；错误定位不要求转储整份连接设置。 |

### 13.2 归并、代理与容器行为

- 同一监听的重复声明正确归并；不同执行目标、地址族和传输不被错误合并。
- 检查 IPv4、IPv6、通配、回环、同端口多地址及 TCP/UDP 的区别；尚不支持的形态明确拒绝。
- `server=~` 在唯一候选、多候选、无候选时行为确定。
- HTTP/2、gRPC、TLS、普通 HTTP 的代理和健康检查不互相冒充。
- 本应用代理上游可以按安装形态重定位；普通远程后端即使同端口也不被误改。
- Nginx 原生指令改变监听时，元数据来自最终配置。
- 不完整或不支持的 Nginx 配置不得悄悄变成“没有入口”。
- 回环监听不因增加容器发布规则而被宣称可从外部访问。
- 内部端口不自动全部公开；直接发布与代理入口的宿主冲突能够发现。
- ZeroMQ 独立 daemon 的发现、实际启用的数据/控制通道及跨容器/本地客户端的发现结果一致。
- Web 与 daemon 共用插件库时，不给 Web 误导出 daemon 服务端端口。
- 不再残留已废弃 listen 的属性、包头写入、启动参数拼接或旧 Nginx 绑定读取路径。
- 相同输入重复制作得到稳定的绑定内容；失败不会留下可被下游误读的旧结果。

### 13.3 包格式和运行载荷

对 tar.gz、deb、rpm 分别检查：

1. 元数据能够写入并由 Containerizer 正确读取。
2. 所有应用绑定与 Nginx 入口都进入同一逻辑结果。
3. 格式不支持、内容损坏和缺失完整信息能被识别。
4. 安装目录与应用运行镜像内没有工具专用的 `.env`、源模板和 `.bindings`。
5. 实际 option、证书及代理配置仍按原职责正确交付。
6. 安装脚本、服务文件及卸载行为正确，不出现“先安装工具元数据再手工删掉”的补丁。
7. 在对应平台验证包元数据和安装行为。仅检查归档内容，不等于已经验证安装成功。

### 13.4 用户要求的整体流程

用户此前提出的验收目标仍是有价值的最终验证场景；本次设计文档不表示已完成这套新功能的验收。

**容器流程：**

1. 以 `D:\Zongsoft\hosting\web\default` 为宿主，从 hosting 根目录分别走 `deploy.cmd`、`pack.cmd`、`migrate.cmd`、`containerize.cmd`。
2. 验证部署、打包、数据库等升迁、容器交付、安装和启动的完整链路。
3. 按选定配置运行需要的 Redis、MySQL、RustFS、Etcd 等基础服务，ZeroMQ 作为独立 daemon 部署。
4. Web 启动后实际登录，再执行 Security 常用 API，不能只检查 HTTP 端口开放。
5. 检查服务日志、代理转发、事件交换器和发现通道，确认没有重复订阅或实际端口缺失。

**本地 Debug 流程：**

1. 保持验收所需的基础服务和独立 daemon 运行。
2. 执行 `D:\Zongsoft\hosting\web\default\deploy.cmd`。
3. 该便捷脚本已有用户要求的固定默认项：当前宿主、Debug 开、Windows、x64、默认 framework、始终覆盖；其他项目保持既有默认和提示。
4. 在本机启动 Web，连接同一组服务，登录并执行相同的常用 API。
5. 同时核对本地访问 ZeroMQ 发现结果是否真的可达，不能只验证发现端口。

Security 的参考输入在：

- [HTTP 示例目录](../framework/Zongsoft.Security/docs/http)
- [数据库脚本目录](../framework/Zongsoft.Security/database)

此前使用的本地测试账号为 `Popeye`，密码不写入仓库文档；执行前从用户授权的测试配置取得，必要时向用户确认，不以猜测值代替。

这些步骤必须在明确的测试环境执行。当前用户曾要求停止全部容器和本地 Web，本次整理文档不撤销该要求。后续会话开展有副作用的运行验收前，应核实当前指令和环境；运行结束后的服务状态也应服从用户当时的要求。

### 13.5 开发验证与本地工具安装

遵循对应工具的独立解决方案和测试流程，不为单工具变动无条件构建整个仓库。

受影响 C# 项目需要严格规范构建及 IDE0049 检查，例如以 Packager 为例：

```powershell
# 工作目录：D:\Zongsoft\tools
dotnet build .\packager\Zongsoft.Tools.Packager.slnx -p:ZongsoftCodeStyleStrict=true
dotnet format style .\packager\Zongsoft.Tools.Packager.slnx --no-restore --verify-no-changes --diagnostics IDE0049
```

多目标项目按当前仓库要求覆盖全部目标框架；测试命令和平台以各项目实际配置为准。Containerizer 的解决方案是 `D:\Zongsoft\tools\containerizer\Containerizer.slnx`，不要凭命名规律虚构路径。

文件与解析测试使用临时 source/output 和本地缓存；无需真实启动服务器的测试不要引入服务依赖。实际安装、服务启停和容器验证单独记录，不能用构建成功代替。

用户此前明确要求实际工具验证采用本机全局工具：**先卸载，再安装本次本地生成的工具包**，防止同版本号被旧安装遮蔽。执行时从项目读取真实包标识和版本，不把安装示例写成过期固定版本，也不使用 NuGet 推送流程来制作本地验证包。

本文档整理本身只需要核对内容、链接、CRLF 和差异，不需要构建工具或安装运行服务。

<a id="references"></a>

## 14. 参考资料与源码索引

本地链接均相对于本文 `D:\Zongsoft\tools\bindings.md`。表格标出目录基准，链接文字为实际文件名；复制到其他环境时，应使用对应仓库的同路径文件。

源码是当前实现的依据；README 可辅助理解，但 hosting 的旧文档可能未同步设计。若说明与实现冲突，先定位真实行为，再更新文档，不把旧说明当作已生效契约。

### 14.1 规范和工具说明

目录基准：`D:\Zongsoft\tools`。

- [仓库 AGENTS.md](AGENTS.md)、[仓库 SKILL.md](SKILL.md)。
- [deployer/README.zh-Hans.md](deployer/README.zh-Hans.md)、[deployer/SKILL.md](deployer/SKILL.md)。
- [packager/README.zh-Hans.md](packager/README.zh-Hans.md)、[packager/SKILL.md](packager/SKILL.md)。
- [containerizer/README.zh-Hans.md](containerizer/README.zh-Hans.md)、[containerizer/SKILL.md](containerizer/SKILL.md)。
- [migrator/README.zh-Hans.md](migrator/README.zh-Hans.md)。
- [guidelines/README.md](../guidelines/README.md)、[guidelines/AGENTS.md](../guidelines/AGENTS.md)。

### 14.2 Core 变量、配置和解析

目录基准：`D:\Zongsoft\framework\Zongsoft.Core\src`。

| 文件 | 核对内容 |
| --- | --- |
| [Common/Variables.cs](../framework/Zongsoft.Core/src/Common/Variables.cs) | 变量与命名空间能力，不重新实现。 |
| [Text/Templating/TemplateEvaluator.cs](../framework/Zongsoft.Core/src/Text/Templating/TemplateEvaluator.cs) | 模板展开与表达式语义。 |
| [Configuration/Settings.cs](../framework/Zongsoft.Core/src/Configuration/Settings.cs)、[Settings.Parser.cs](../framework/Zongsoft.Core/src/Configuration/Settings.Parser.cs) | 通用键值设置与共用解析器；`ISettings` 继承 `IDictionary<string, string>`。 |
| [Configuration/ConnectionSettings.cs](../framework/Zongsoft.Core/src/Configuration/ConnectionSettings.cs)、[ConnectionSettingsBase.cs](../framework/Zongsoft.Core/src/Configuration/ConnectionSettingsBase.cs) | 连接设置基类（`ConnectionSettingsBase`）继承普通设置（`Settings`），提供驱动约束、类型化读取及扩展属性集（`Properties`）。 |
| [Configuration/Xml/XmlStreamConfigurationProvider.cs](../framework/Zongsoft.Core/src/Configuration/Xml/XmlStreamConfigurationProvider.cs) | option、命名属性、路径映射、XML 节点处理。 |
| [Configuration/Xml/XmlConfigurationExtension.cs](../framework/Zongsoft.Core/src/Configuration/Xml/XmlConfigurationExtension.cs) | 配置来源接入方式。 |

### 14.3 宿主与插件配置

目录基准：`D:\Zongsoft\framework`。

- [Zongsoft.Plugins/src/Hosting/ApplicationBuilder.cs](../framework/Zongsoft.Plugins/src/Hosting/ApplicationBuilder.cs)
- [Zongsoft.Plugins/src/Configuration/PluginConfigurationProvider.cs](../framework/Zongsoft.Plugins/src/Configuration/PluginConfigurationProvider.cs)
- [Zongsoft.Plugins.Web/src/Application.cs](../framework/Zongsoft.Plugins.Web/src/Application.cs)
- [Zongsoft.Plugins.Web/src/WebApplicationBuilder.cs](../framework/Zongsoft.Plugins.Web/src/WebApplicationBuilder.cs)

重点不是只知道路径分隔符，而是确认哪些文件实际加载、先后顺序、环境/站点选择和插件贡献怎样进入同一配置。

### 14.4 服务器和 Web 组件

目录基准：`D:\Zongsoft\framework`。

| 组件 | 实际文件 |
| --- | --- |
| OTLP/Kestrel | [Zongsoft.Diagnostics.Protocols.Server.option](../framework/Zongsoft.Diagnostics/protocols/server/src/Zongsoft.Diagnostics.Protocols.Server.option) |
| Prometheus | [PrometheusExporterLauncher.cs](../framework/Zongsoft.Diagnostics/src/Telemetry/PrometheusExporterLauncher.cs)、[Zongsoft.Diagnostics.option](../framework/Zongsoft.Diagnostics/src/Zongsoft.Diagnostics.option) |
| ZeroMQ | [ZeroQueueServer.cs](../framework/messaging/zero/src/ZeroQueueServer.cs)、[ZeroQueueServer.Control.cs](../framework/messaging/zero/src/ZeroQueueServer.Control.cs)、[ServerOptions.cs](../framework/messaging/zero/src/Configuration/ServerOptions.cs)、[daemon.plugin](../framework/messaging/zero/src/Zongsoft.Messaging.ZeroMQ-daemon.plugin)、[ZeroMQ.deploy](../framework/messaging/zero/src/Zongsoft.Messaging.ZeroMQ.deploy) |
| MQTT | [MqttQueueServer.cs](../framework/messaging/mqtt/src/MqttQueueServer.cs) |
| Garnet | [GarnetServer.cs](../framework/externals/garnet/src/GarnetServer.cs) |
| OPC | [OpcServer.cs](../framework/externals/opc/src/OpcServer.cs)、[OpcServerOptions.cs](../framework/externals/opc/src/OpcServerOptions.cs) |
| 通用 TCP | [TcpServer.cs](../framework/Zongsoft.Net/src/TcpServer.cs) |
| Hangfire Dashboard | [Initializer.cs](../framework/externals/hangfire/web/Initializer.cs)、[README.zh-Hans.md](../framework/externals/hangfire/web/README.zh-Hans.md) |
| gRPC 路由 | [GrpcInitializer.cs](../framework/Zongsoft.Web/grpc/GrpcInitializer.cs) |
| Grapecity 路由 | [WebApplicationInitializer.cs](../framework/externals/grapecity/api/Reporting/WebApplicationInitializer.cs) |

### 14.5 工具的现有实现入口

目录基准：`D:\Zongsoft\tools`。

- 部署模板：[deployer/src/TemplateResolver.cs](deployer/src/TemplateResolver.cs)。
- 共享能力：[.shared/Utility.cs](.shared/Utility.cs)、[.shared/Utility.Output.cs](.shared/Utility.Output.cs)。
- 打包参数与宿主：[PackageOptions.cs](packager/src/PackageOptions.cs)、[PackCommand.cs](packager/src/PackCommand.cs)、[ApplicationHost.cs](packager/src/ApplicationHost.cs)。
- 服务文件：[Scriptor.Systemd.cs](packager/src/Scriptor.Systemd.cs)。
- 包写入：[Generator.Tar.cs](packager/src/Generator.Tar.cs)、[Generator.Deb.cs](packager/src/Generator.Deb.cs)、[Generator.Rpm.cs](packager/src/Generator.Rpm.cs)。
- Web 上游：[Definition.Resolver.cs](packager/src/Web/Definition.Resolver.cs)。
- 最终 Nginx 绑定：[Configurator.Nginx.Bindings.cs](packager/src/Web/Configurator.Nginx.Bindings.cs)。
- 包读取：[PackageReader.cs](containerizer/src/PackageReader.cs)、[PackageReader.Tar.cs](containerizer/src/PackageReader.Tar.cs)、[PackageReader.Deb.cs](containerizer/src/PackageReader.Deb.cs)、[PackageReader.Rpm.cs](containerizer/src/PackageReader.Rpm.cs)。
- 容器计划与健康：[ApplicationPlanner.cs](containerizer/src/ApplicationPlanner.cs)、[ApplicationHealth.cs](containerizer/src/ApplicationHealth.cs)。
- 代理入口：[WebIngress.cs](containerizer/src/WebIngress.cs)、[WebIngress.Files.cs](containerizer/src/WebIngress.Files.cs)。

### 14.6 Hosting 示例来源

目录基准：`D:\Zongsoft\hosting`。

- [web/.env](../hosting/web/.env)：宿主变量及导入。
- [web/default/.deploy](../hosting/web/default/.deploy)：具体部署引用。
- [.deploy/.shared/web.option.template](../hosting/.deploy/.shared/web.option.template)：Web 运行配置模板。
- [.deploy/.shared/app.option.template](../hosting/.deploy/.shared/app.option.template)：应用共享运行配置模板。
- [web/default/web.default.option](../hosting/web/default/web.default.option)：已有实际宿主配置示例。
- [web/default/web.profile](../hosting/web/default/web.profile)：继续保留的 Web 代理定义。
- [.deploy/.shared/Zongsoft.Messaging.ZeroMQ.option.template](../hosting/.deploy/.shared/Zongsoft.Messaging.ZeroMQ.option.template)：客户端连接配置，不能当作服务端监听。
- [.deploy/.shared/Zongsoft.Messaging.ZeroMQ-daemon.option.template](../hosting/.deploy/.shared/Zongsoft.Messaging.ZeroMQ-daemon.option.template)：daemon 配置。
- [.shared.ps1](../hosting/.shared.ps1)：共享脚本逻辑。
- [deploy.cmd](../hosting/deploy.cmd)、[pack.cmd](../hosting/pack.cmd)、[migrate.cmd](../hosting/migrate.cmd)、[containerize.cmd](../hosting/containerize.cmd)：完整流程入口。

`D:\Zongsoft\hosting\.deploy\default\.env` 及对应 `network.*.env`、`environment.*.env` 是本地方案变量来源。本文不复制其中的真实连接凭据和地址。

### 14.7 外部的一手参考

这些引用解释框架底层行为，不代替核查本地实际依赖版本。

- [W3C XML：Processing Instructions](https://www.w3.org/TR/xml/#sec-pi)：准确术语是 XML“处理指令”；它本身不自动执行预处理，工具需要定义目标名和内容语义。
- [ASP.NET Core：Kestrel endpoints](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0)：端点配置、地址配置、协议与优先规则。
- [ASP.NET Core AddressBinder.cs](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Servers/Kestrel/Core/src/Internal/AddressBinder.cs)：实际配置端点与 server addresses、PreferHostingUrls 的选择。此固定源码链接是证据快照，不是要求将项目依赖固定为该版本。
- [HangfireApplicationBuilderExtensions.cs](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.AspNetCore/HangfireApplicationBuilderExtensions.cs)：UseHangfireDashboard 构造并映射 Dashboard 请求管线，不自行向 Kestrel 增加端口。
- [AspNetCoreDashboardMiddleware.cs](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.AspNetCore/Dashboard/AspNetCoreDashboardMiddleware.cs)：处理已经进入 ASP.NET Core 管线的请求。
- [ASP.NET Core MapExtensions.cs](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Http.Abstractions/src/Extensions/MapExtensions.cs)、[MapMiddleware.cs](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Http.Abstractions/src/Extensions/MapMiddleware.cs)：按请求路径分支，不创建新的网络监听。

<a id="projects"></a>

## 15. 附录：Framework 项目索引

下表逐项保留本轮核对的 113 个项目，目录基准为 `D:\Zongsoft\framework`。它用于新会话定位范围，不表示每个项目都需要添加 binding 指令，也不表示每个项目都已完成运行测试。

“示例/测试”按项目所在路径分类；其余列为“库/应用能力”，不据此断定是否创建监听。具体生产监听及无需额外监听的结论见第 4 节。

| 序号 | 项目文件 | 分类 |
| --- | --- | --- |
| 1 | [externals/aliyun/gateway/Zongsoft.Externals.Aliyun.Gateway.csproj](../framework/externals/aliyun/gateway/Zongsoft.Externals.Aliyun.Gateway.csproj) | 库/应用能力 |
| 2 | [externals/aliyun/src/Zongsoft.Externals.Aliyun.csproj](../framework/externals/aliyun/src/Zongsoft.Externals.Aliyun.csproj) | 库/应用能力 |
| 3 | [externals/amazon/src/Zongsoft.Externals.Amazon.csproj](../framework/externals/amazon/src/Zongsoft.Externals.Amazon.csproj) | 库/应用能力 |
| 4 | [externals/amazon/test/Zongsoft.Externals.Amazon.Tests.csproj](../framework/externals/amazon/test/Zongsoft.Externals.Amazon.Tests.csproj) | 测试/基准 |
| 5 | [externals/closedxml/samples/Zongsoft.Externals.ClosedXml.Samples.csproj](../framework/externals/closedxml/samples/Zongsoft.Externals.ClosedXml.Samples.csproj) | 示例 |
| 6 | [externals/closedxml/src/Zongsoft.Externals.ClosedXml.csproj](../framework/externals/closedxml/src/Zongsoft.Externals.ClosedXml.csproj) | 库/应用能力 |
| 7 | [externals/closedxml/test/Zongsoft.Externals.ClosedXml.Tests.csproj](../framework/externals/closedxml/test/Zongsoft.Externals.ClosedXml.Tests.csproj) | 测试/基准 |
| 8 | [externals/etcd/samples/distributedlock/master/Zongsoft.Externals.Etcd.DistributedLock.Master.csproj](../framework/externals/etcd/samples/distributedlock/master/Zongsoft.Externals.Etcd.DistributedLock.Master.csproj) | 示例 |
| 9 | [externals/etcd/samples/distributedlock/slaver/Zongsoft.Externals.Etcd.DistributedLock.Slaver.csproj](../framework/externals/etcd/samples/distributedlock/slaver/Zongsoft.Externals.Etcd.DistributedLock.Slaver.csproj) | 示例 |
| 10 | [externals/etcd/samples/sequence/Zongsoft.Externals.Etcd.Sequence.Samples.csproj](../framework/externals/etcd/samples/sequence/Zongsoft.Externals.Etcd.Sequence.Samples.csproj) | 示例 |
| 11 | [externals/etcd/src/Zongsoft.Externals.Etcd.csproj](../framework/externals/etcd/src/Zongsoft.Externals.Etcd.csproj) | 库/应用能力 |
| 12 | [externals/etcd/test/Zongsoft.Externals.Etcd.Tests.csproj](../framework/externals/etcd/test/Zongsoft.Externals.Etcd.Tests.csproj) | 测试/基准 |
| 13 | [externals/garnet/src/Zongsoft.Externals.Garnet.csproj](../framework/externals/garnet/src/Zongsoft.Externals.Garnet.csproj) | 库/应用能力 |
| 14 | [externals/garnet/test/Zongsoft.Externals.Garnet.Tests.csproj](../framework/externals/garnet/test/Zongsoft.Externals.Garnet.Tests.csproj) | 测试/基准 |
| 15 | [externals/grapecity/api/Zongsoft.Externals.Grapecity.Web.csproj](../framework/externals/grapecity/api/Zongsoft.Externals.Grapecity.Web.csproj) | 库/应用能力 |
| 16 | [externals/grapecity/src/Zongsoft.Externals.Grapecity.csproj](../framework/externals/grapecity/src/Zongsoft.Externals.Grapecity.csproj) | 库/应用能力 |
| 17 | [externals/hangfire/samples/Zongsoft.Externals.Hangfire.Samples.csproj](../framework/externals/hangfire/samples/Zongsoft.Externals.Hangfire.Samples.csproj) | 示例 |
| 18 | [externals/hangfire/src/Zongsoft.Externals.Hangfire.csproj](../framework/externals/hangfire/src/Zongsoft.Externals.Hangfire.csproj) | 库/应用能力 |
| 19 | [externals/hangfire/storages/redis/Zongsoft.Externals.Hangfire.Storages.Redis.csproj](../framework/externals/hangfire/storages/redis/Zongsoft.Externals.Hangfire.Storages.Redis.csproj) | 库/应用能力 |
| 20 | [externals/hangfire/test/Zongsoft.Externals.Hangfire.Tests.csproj](../framework/externals/hangfire/test/Zongsoft.Externals.Hangfire.Tests.csproj) | 测试/基准 |
| 21 | [externals/hangfire/web/Zongsoft.Externals.Hangfire.Web.csproj](../framework/externals/hangfire/web/Zongsoft.Externals.Hangfire.Web.csproj) | 库/应用能力 |
| 22 | [externals/lua/src/Zongsoft.Externals.Lua.csproj](../framework/externals/lua/src/Zongsoft.Externals.Lua.csproj) | 库/应用能力 |
| 23 | [externals/lua/test/Zongsoft.Externals.Lua.Tests.csproj](../framework/externals/lua/test/Zongsoft.Externals.Lua.Tests.csproj) | 测试/基准 |
| 24 | [externals/opc/samples/client/Zongsoft.Externals.Opc.Samples.Client.csproj](../framework/externals/opc/samples/client/Zongsoft.Externals.Opc.Samples.Client.csproj) | 示例 |
| 25 | [externals/opc/samples/server/Zongsoft.Externals.Opc.Samples.Server.csproj](../framework/externals/opc/samples/server/Zongsoft.Externals.Opc.Samples.Server.csproj) | 示例 |
| 26 | [externals/opc/src/Zongsoft.Externals.Opc.csproj](../framework/externals/opc/src/Zongsoft.Externals.Opc.csproj) | 库/应用能力 |
| 27 | [externals/opc/test/Zongsoft.Externals.Opc.Tests.csproj](../framework/externals/opc/test/Zongsoft.Externals.Opc.Tests.csproj) | 测试/基准 |
| 28 | [externals/openxml/src/Zongsoft.Externals.OpenXml.csproj](../framework/externals/openxml/src/Zongsoft.Externals.OpenXml.csproj) | 库/应用能力 |
| 29 | [externals/openxml/test/Zongsoft.Externals.OpenXml.Tests.csproj](../framework/externals/openxml/test/Zongsoft.Externals.OpenXml.Tests.csproj) | 测试/基准 |
| 30 | [externals/polly/samples/Zongsoft.Externals.Polly.Samples.csproj](../framework/externals/polly/samples/Zongsoft.Externals.Polly.Samples.csproj) | 示例 |
| 31 | [externals/polly/src/Zongsoft.Externals.Polly.csproj](../framework/externals/polly/src/Zongsoft.Externals.Polly.csproj) | 库/应用能力 |
| 32 | [externals/polly/test/Zongsoft.Externals.Polly.Tests.csproj](../framework/externals/polly/test/Zongsoft.Externals.Polly.Tests.csproj) | 测试/基准 |
| 33 | [externals/python/src/Zongsoft.Externals.Python.csproj](../framework/externals/python/src/Zongsoft.Externals.Python.csproj) | 库/应用能力 |
| 34 | [externals/python/test/Zongsoft.Externals.Python.Tests.csproj](../framework/externals/python/test/Zongsoft.Externals.Python.Tests.csproj) | 测试/基准 |
| 35 | [externals/redis/samples/distributedcache/Zongsoft.Externals.Redis.DistributedCache.Samples.csproj](../framework/externals/redis/samples/distributedcache/Zongsoft.Externals.Redis.DistributedCache.Samples.csproj) | 示例 |
| 36 | [externals/redis/samples/distributedlock/master/Zongsoft.Externals.Redis.DistributedLock.Master.csproj](../framework/externals/redis/samples/distributedlock/master/Zongsoft.Externals.Redis.DistributedLock.Master.csproj) | 示例 |
| 37 | [externals/redis/samples/distributedlock/slaver/Zongsoft.Externals.Redis.DistributedLock.Slaver.csproj](../framework/externals/redis/samples/distributedlock/slaver/Zongsoft.Externals.Redis.DistributedLock.Slaver.csproj) | 示例 |
| 38 | [externals/redis/samples/messaging/Zongsoft.Externals.Redis.Messaging.Samples.csproj](../framework/externals/redis/samples/messaging/Zongsoft.Externals.Redis.Messaging.Samples.csproj) | 示例 |
| 39 | [externals/redis/src/Zongsoft.Externals.Redis.csproj](../framework/externals/redis/src/Zongsoft.Externals.Redis.csproj) | 库/应用能力 |
| 40 | [externals/redis/test/Zongsoft.Externals.Redis.Tests.csproj](../framework/externals/redis/test/Zongsoft.Externals.Redis.Tests.csproj) | 测试/基准 |
| 41 | [externals/scriban/src/Zongsoft.Externals.Scriban.csproj](../framework/externals/scriban/src/Zongsoft.Externals.Scriban.csproj) | 库/应用能力 |
| 42 | [externals/wechat/api/Zongsoft.Externals.Wechat.Web.csproj](../framework/externals/wechat/api/Zongsoft.Externals.Wechat.Web.csproj) | 库/应用能力 |
| 43 | [externals/wechat/gateway/Zongsoft.Externals.Wechat.Gateway.csproj](../framework/externals/wechat/gateway/Zongsoft.Externals.Wechat.Gateway.csproj) | 库/应用能力 |
| 44 | [externals/wechat/src/Zongsoft.Externals.Wechat.csproj](../framework/externals/wechat/src/Zongsoft.Externals.Wechat.csproj) | 库/应用能力 |
| 45 | [externals/wechat/test/Zongsoft.Externals.Wechat.Tests.csproj](../framework/externals/wechat/test/Zongsoft.Externals.Wechat.Tests.csproj) | 测试/基准 |
| 46 | [messaging/kafka/samples/Zongsoft.Messaging.Kafka.Samples.csproj](../framework/messaging/kafka/samples/Zongsoft.Messaging.Kafka.Samples.csproj) | 示例 |
| 47 | [messaging/kafka/src/Zongsoft.Messaging.Kafka.csproj](../framework/messaging/kafka/src/Zongsoft.Messaging.Kafka.csproj) | 库/应用能力 |
| 48 | [messaging/kafka/test/Zongsoft.Messaging.Kafka.Tests.csproj](../framework/messaging/kafka/test/Zongsoft.Messaging.Kafka.Tests.csproj) | 测试/基准 |
| 49 | [messaging/mqtt/samples/client/Zongsoft.Messaging.Mqtt.Samples.Client.csproj](../framework/messaging/mqtt/samples/client/Zongsoft.Messaging.Mqtt.Samples.Client.csproj) | 示例 |
| 50 | [messaging/mqtt/samples/server/Zongsoft.Messaging.Mqtt.Samples.Server.csproj](../framework/messaging/mqtt/samples/server/Zongsoft.Messaging.Mqtt.Samples.Server.csproj) | 示例 |
| 51 | [messaging/mqtt/src/Zongsoft.Messaging.Mqtt.csproj](../framework/messaging/mqtt/src/Zongsoft.Messaging.Mqtt.csproj) | 库/应用能力 |
| 52 | [messaging/mqtt/test/Zongsoft.Messaging.Mqtt.Tests.csproj](../framework/messaging/mqtt/test/Zongsoft.Messaging.Mqtt.Tests.csproj) | 测试/基准 |
| 53 | [messaging/rabbit/samples/Zongsoft.Messaging.RabbitMQ.Samples.csproj](../framework/messaging/rabbit/samples/Zongsoft.Messaging.RabbitMQ.Samples.csproj) | 示例 |
| 54 | [messaging/rabbit/src/Zongsoft.Messaging.RabbitMQ.csproj](../framework/messaging/rabbit/src/Zongsoft.Messaging.RabbitMQ.csproj) | 库/应用能力 |
| 55 | [messaging/rabbit/test/Zongsoft.Messaging.RabbitMQ.Tests.csproj](../framework/messaging/rabbit/test/Zongsoft.Messaging.RabbitMQ.Tests.csproj) | 测试/基准 |
| 56 | [messaging/zero/samples/client/Zongsoft.Messaging.ZeroMQ.Samples.Client.csproj](../framework/messaging/zero/samples/client/Zongsoft.Messaging.ZeroMQ.Samples.Client.csproj) | 示例 |
| 57 | [messaging/zero/samples/server/Zongsoft.Messaging.ZeroMQ.Samples.Server.csproj](../framework/messaging/zero/samples/server/Zongsoft.Messaging.ZeroMQ.Samples.Server.csproj) | 示例 |
| 58 | [messaging/zero/src/Zongsoft.Messaging.ZeroMQ.csproj](../framework/messaging/zero/src/Zongsoft.Messaging.ZeroMQ.csproj) | 库/应用能力 |
| 59 | [messaging/zero/test/Zongsoft.Messaging.ZeroMQ.Tests.csproj](../framework/messaging/zero/test/Zongsoft.Messaging.ZeroMQ.Tests.csproj) | 测试/基准 |
| 60 | [upgrading/deployer/Zongsoft.Upgrading.Deployer.csproj](../framework/upgrading/deployer/Zongsoft.Upgrading.Deployer.csproj) | 库/应用能力 |
| 61 | [upgrading/tool/Zongsoft.Tools.Upgrader.csproj](../framework/upgrading/tool/Zongsoft.Tools.Upgrader.csproj) | 库/应用能力 |
| 62 | [upgrading/upgrader/Zongsoft.Upgrading.Upgrader.csproj](../framework/upgrading/upgrader/Zongsoft.Upgrading.Upgrader.csproj) | 库/应用能力 |
| 63 | [upgrading/web/Zongsoft.Upgrading.Web.csproj](../framework/upgrading/web/Zongsoft.Upgrading.Web.csproj) | 库/应用能力 |
| 64 | [Zongsoft.Commands/src/Zongsoft.Commands.csproj](../framework/Zongsoft.Commands/src/Zongsoft.Commands.csproj) | 库/应用能力 |
| 65 | [Zongsoft.Core/benchmark/Zongsoft.Core.Benchmarks.csproj](../framework/Zongsoft.Core/benchmark/Zongsoft.Core.Benchmarks.csproj) | 测试/基准 |
| 66 | [Zongsoft.Core/samples/eventexchanger/Zongsoft.Samples.EventExchanger.csproj](../framework/Zongsoft.Core/samples/eventexchanger/Zongsoft.Samples.EventExchanger.csproj) | 示例 |
| 67 | [Zongsoft.Core/samples/memorycache/Zongsoft.Samples.MemoryCache.csproj](../framework/Zongsoft.Core/samples/memorycache/Zongsoft.Samples.MemoryCache.csproj) | 示例 |
| 68 | [Zongsoft.Core/samples/spooler/Zongsoft.Samples.Spooler.csproj](../framework/Zongsoft.Core/samples/spooler/Zongsoft.Samples.Spooler.csproj) | 示例 |
| 69 | [Zongsoft.Core/samples/superviser/Zongsoft.Samples.Superviser.csproj](../framework/Zongsoft.Core/samples/superviser/Zongsoft.Samples.Superviser.csproj) | 示例 |
| 70 | [Zongsoft.Core/src/Zongsoft.Core.csproj](../framework/Zongsoft.Core/src/Zongsoft.Core.csproj) | 库/应用能力 |
| 71 | [Zongsoft.Core/test/Zongsoft.Core.Tests.csproj](../framework/Zongsoft.Core/test/Zongsoft.Core.Tests.csproj) | 测试/基准 |
| 72 | [Zongsoft.Data/drivers/clickhouse/src/Zongsoft.Data.ClickHouse.csproj](../framework/Zongsoft.Data/drivers/clickhouse/src/Zongsoft.Data.ClickHouse.csproj) | 库/应用能力 |
| 73 | [Zongsoft.Data/drivers/clickhouse/test/Zongsoft.Data.ClickHouse.Tests.csproj](../framework/Zongsoft.Data/drivers/clickhouse/test/Zongsoft.Data.ClickHouse.Tests.csproj) | 测试/基准 |
| 74 | [Zongsoft.Data/drivers/duckdb/src/Zongsoft.Data.DuckDB.csproj](../framework/Zongsoft.Data/drivers/duckdb/src/Zongsoft.Data.DuckDB.csproj) | 库/应用能力 |
| 75 | [Zongsoft.Data/drivers/duckdb/test/Zongsoft.Data.DuckDB.Tests.csproj](../framework/Zongsoft.Data/drivers/duckdb/test/Zongsoft.Data.DuckDB.Tests.csproj) | 测试/基准 |
| 76 | [Zongsoft.Data/drivers/influx/src/Zongsoft.Data.Influx.csproj](../framework/Zongsoft.Data/drivers/influx/src/Zongsoft.Data.Influx.csproj) | 库/应用能力 |
| 77 | [Zongsoft.Data/drivers/influx/test/Zongsoft.Data.Influx.Tests.csproj](../framework/Zongsoft.Data/drivers/influx/test/Zongsoft.Data.Influx.Tests.csproj) | 测试/基准 |
| 78 | [Zongsoft.Data/drivers/mssql/src/Zongsoft.Data.MsSql.csproj](../framework/Zongsoft.Data/drivers/mssql/src/Zongsoft.Data.MsSql.csproj) | 库/应用能力 |
| 79 | [Zongsoft.Data/drivers/mssql/test/Zongsoft.Data.MsSql.Tests.csproj](../framework/Zongsoft.Data/drivers/mssql/test/Zongsoft.Data.MsSql.Tests.csproj) | 测试/基准 |
| 80 | [Zongsoft.Data/drivers/mysql/src/Zongsoft.Data.MySql.csproj](../framework/Zongsoft.Data/drivers/mysql/src/Zongsoft.Data.MySql.csproj) | 库/应用能力 |
| 81 | [Zongsoft.Data/drivers/mysql/test/Zongsoft.Data.MySql.Tests.csproj](../framework/Zongsoft.Data/drivers/mysql/test/Zongsoft.Data.MySql.Tests.csproj) | 测试/基准 |
| 82 | [Zongsoft.Data/drivers/postgres/src/Zongsoft.Data.PostgreSql.csproj](../framework/Zongsoft.Data/drivers/postgres/src/Zongsoft.Data.PostgreSql.csproj) | 库/应用能力 |
| 83 | [Zongsoft.Data/drivers/postgres/test/Zongsoft.Data.PostgreSql.Tests.csproj](../framework/Zongsoft.Data/drivers/postgres/test/Zongsoft.Data.PostgreSql.Tests.csproj) | 测试/基准 |
| 84 | [Zongsoft.Data/drivers/sqlite/src/Zongsoft.Data.SQLite.csproj](../framework/Zongsoft.Data/drivers/sqlite/src/Zongsoft.Data.SQLite.csproj) | 库/应用能力 |
| 85 | [Zongsoft.Data/drivers/sqlite/test/Zongsoft.Data.SQLite.Tests.csproj](../framework/Zongsoft.Data/drivers/sqlite/test/Zongsoft.Data.SQLite.Tests.csproj) | 测试/基准 |
| 86 | [Zongsoft.Data/drivers/tdengine/src/Zongsoft.Data.TDengine.csproj](../framework/Zongsoft.Data/drivers/tdengine/src/Zongsoft.Data.TDengine.csproj) | 库/应用能力 |
| 87 | [Zongsoft.Data/drivers/tdengine/test/Zongsoft.Data.TDengine.Tests.csproj](../framework/Zongsoft.Data/drivers/tdengine/test/Zongsoft.Data.TDengine.Tests.csproj) | 测试/基准 |
| 88 | [Zongsoft.Data/src/Zongsoft.Data.csproj](../framework/Zongsoft.Data/src/Zongsoft.Data.csproj) | 库/应用能力 |
| 89 | [Zongsoft.Data/test/Zongsoft.Data.Tests.csproj](../framework/Zongsoft.Data/test/Zongsoft.Data.Tests.csproj) | 测试/基准 |
| 90 | [Zongsoft.Diagnostics/protocols/client/src/Zongsoft.Diagnostics.Protocols.Client.csproj](../framework/Zongsoft.Diagnostics/protocols/client/src/Zongsoft.Diagnostics.Protocols.Client.csproj) | 库/应用能力 |
| 91 | [Zongsoft.Diagnostics/protocols/server/samples/Zongsoft.Diagnostics.Protocols.Server.Samples.csproj](../framework/Zongsoft.Diagnostics/protocols/server/samples/Zongsoft.Diagnostics.Protocols.Server.Samples.csproj) | 示例 |
| 92 | [Zongsoft.Diagnostics/protocols/server/src/Zongsoft.Diagnostics.Protocols.Server.csproj](../framework/Zongsoft.Diagnostics/protocols/server/src/Zongsoft.Diagnostics.Protocols.Server.csproj) | 库/应用能力 |
| 93 | [Zongsoft.Diagnostics/src/Zongsoft.Diagnostics.csproj](../framework/Zongsoft.Diagnostics/src/Zongsoft.Diagnostics.csproj) | 库/应用能力 |
| 94 | [Zongsoft.Hardwares/samples/Zongsoft.Hardwares.Samples.csproj](../framework/Zongsoft.Hardwares/samples/Zongsoft.Hardwares.Samples.csproj) | 示例 |
| 95 | [Zongsoft.Hardwares/src/Zongsoft.Hardwares.csproj](../framework/Zongsoft.Hardwares/src/Zongsoft.Hardwares.csproj) | 库/应用能力 |
| 96 | [Zongsoft.Hardwares/test/Zongsoft.Hardwares.Tests.csproj](../framework/Zongsoft.Hardwares/test/Zongsoft.Hardwares.Tests.csproj) | 测试/基准 |
| 97 | [Zongsoft.Intelligences/api/Zongsoft.Intelligences.Web.csproj](../framework/Zongsoft.Intelligences/api/Zongsoft.Intelligences.Web.csproj) | 库/应用能力 |
| 98 | [Zongsoft.Intelligences/src/Zongsoft.Intelligences.csproj](../framework/Zongsoft.Intelligences/src/Zongsoft.Intelligences.csproj) | 库/应用能力 |
| 99 | [Zongsoft.Net/samples/client/Zongsoft.Net.Samples.Client.csproj](../framework/Zongsoft.Net/samples/client/Zongsoft.Net.Samples.Client.csproj) | 示例 |
| 100 | [Zongsoft.Net/samples/server/Zongsoft.Net.Samples.Server.csproj](../framework/Zongsoft.Net/samples/server/Zongsoft.Net.Samples.Server.csproj) | 示例 |
| 101 | [Zongsoft.Net/src/Zongsoft.Net.csproj](../framework/Zongsoft.Net/src/Zongsoft.Net.csproj) | 库/应用能力 |
| 102 | [Zongsoft.Net/test/Zongsoft.Net.Tests.csproj](../framework/Zongsoft.Net/test/Zongsoft.Net.Tests.csproj) | 测试/基准 |
| 103 | [Zongsoft.Plugins.Web/src/Zongsoft.Plugins.Web.csproj](../framework/Zongsoft.Plugins.Web/src/Zongsoft.Plugins.Web.csproj) | 库/应用能力 |
| 104 | [Zongsoft.Plugins/src/Zongsoft.Plugins.csproj](../framework/Zongsoft.Plugins/src/Zongsoft.Plugins.csproj) | 库/应用能力 |
| 105 | [Zongsoft.Plugins/test/Zongsoft.Plugins.Tests.csproj](../framework/Zongsoft.Plugins/test/Zongsoft.Plugins.Tests.csproj) | 测试/基准 |
| 106 | [Zongsoft.Reporting/src/Zongsoft.Reporting.csproj](../framework/Zongsoft.Reporting/src/Zongsoft.Reporting.csproj) | 库/应用能力 |
| 107 | [Zongsoft.Security/api/Zongsoft.Security.Web.csproj](../framework/Zongsoft.Security/api/Zongsoft.Security.Web.csproj) | 库/应用能力 |
| 108 | [Zongsoft.Security/captcha/Zongsoft.Security.Captcha.csproj](../framework/Zongsoft.Security/captcha/Zongsoft.Security.Captcha.csproj) | 库/应用能力 |
| 109 | [Zongsoft.Security/src/Zongsoft.Security.csproj](../framework/Zongsoft.Security/src/Zongsoft.Security.csproj) | 库/应用能力 |
| 110 | [Zongsoft.Web/grpc/Zongsoft.Web.Grpc.csproj](../framework/Zongsoft.Web/grpc/Zongsoft.Web.Grpc.csproj) | 库/应用能力 |
| 111 | [Zongsoft.Web/openapi/Zongsoft.Web.OpenApi.csproj](../framework/Zongsoft.Web/openapi/Zongsoft.Web.OpenApi.csproj) | 库/应用能力 |
| 112 | [Zongsoft.Web/src/Zongsoft.Web.csproj](../framework/Zongsoft.Web/src/Zongsoft.Web.csproj) | 库/应用能力 |
| 113 | [Zongsoft.Web/test/Zongsoft.Web.Tests.csproj](../framework/Zongsoft.Web/test/Zongsoft.Web.Tests.csproj) | 测试/基准 |
