# Web 托管配置指南

[English](web.md) | [简体中文](web.zh-Hans.md)

本指南说明 `web.profile` 的完整配置语法、Nginx 映射、文件交付及安装行为。打包命令的通用参数见 [README](../README.zh-Hans.md)，内部类型及包格式适配见 [实现说明](implementation.zh-Hans.md)。

## 目录

- [能力与快速开始](#overview)
- [命令选项与输入文件](#command)
- [Profile 语法、作用域与导入](#profile)
- [站点、主机名与绑定](#bindings)
- [入口 HTTPS 与证书](#certificates)
- [路径与匹配规则](#routes)
- [后端地址、应用地址与权重](#servers)
- [后端替换与继承实例](#inheritance)
- [后端 HTTPS 与证书验证](#tls)
- [调度、故障、重试与会话保持](#policies)
- [主动健康检查](#health)
- [请求头与 WebSocket](#headers)
- [托管器原始设置](#native)
- [变量与转义](#variables)
- [完整配置示例](#example)
- [载荷选择与输出布局](#delivery)
- [裸机、容器构建与生命周期](#installation)
- [故障排查与能力边界](#troubleshooting)
- [IIS 扩展边界](#iis)

<a id="overview"></a>
## 能力与快速开始

应用监听来自最终入包 .option 文件中的 zongsoft.tools.binding 处理指令；Web 路由和 Nginx 监听与它们共同写入安装根的 [.bindings JSON 文档](implementation.zh-Hans.md#binding-document)。server 未填写或为空时先继承，再自动选择适用的应用绑定。

`--web` 把抽象的站点、绑定、路径和后端定义转换成所选 Web 托管器的配置。当前实现 Nginx；`iis` 是已识别但尚未实现的名称，选择它会报错。

一个应用包生成一份 `.web/nginx/<PackageName>.conf`，其中可包含多个站点、路径、应用专属后端池及健康判定块。它是供现有 Nginx 主配置的 `http` 上下文包含的片段，不是完整 `nginx.conf`；不含外层 `http { ... }`。不支持 `worker_processes`、`events` 等实例配置，也不允许任意原始 `http` 指令或块。

例如，应用和 Nginx 在同一台机器上，在源目录准备：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://127.0.0.1:8069
```

假设 `publish` 中已有 `Example.Web.dll` 及运行所需文件，以下单行命令可从 Windows 或 Linux 终端执行；`1.0.0` 为示例应用版本：

```shell
dotnet-pack deb --name:Example.Web --version:1.0.0 --platform:linux --architecture:x64 --framework:net10.0 --source:publish --output:../packages --daemon:example.web --web:nginx --exclude:*.profile
```

应用运行配置应监听 http://127.0.0.1:8069；server 显式引用该连接地址。Nginx 的 IPv4/IPv6 HTTP 入口监听 80，bind 仅控制代理入口。

容器场景使用部署网络中的固定应用服务名；该显式地址不会自动改写：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://app:8069
```

应用运行配置应监听其他容器可访问的接口，如 http://0.0.0.0:8069。部署系统提供 app 的解析和网络；容器构建时关闭 Web 激活，见[安装流程](#installation)。

打包器生成配置，不在构建机启动 Nginx、探测后端、签发证书或维护负载均衡运行状态。静态文件服务、通用重写语言、证书安装和 IIS/MSI 不属于当前公共配置能力。

<a id="command"></a>
## 命令选项与输入文件

完整形式为 `--web:<hoster[:filepath]>`。托管器名忽略大小写，启用必须来自显式命令选项；源目录中仅有 `web.profile` 或环境中仅有 `web=nginx` 都不会自动启用。

| 选项值 | 行为 |
| --- | --- |
| 未指定、空值、空字符串、全空白、`none`、`none:` | 关闭；不查找或解析 Web 输入。 |
| `nginx`、`nginx:` | 读取最终 source 直属的 `web.profile`。 |
| `nginx:config/web.profile` | 相对最终 source 定位。 |
| `nginx:../shared/web.production.profile` | 允许输入位于 source 外，但不自动向父目录搜索。 |
| `nginx:D:/config/web.profile` | 支持构建平台的绝对路径；只拆第一个冒号，保留盘符。 |
| `:web.profile`、`none:web.profile` | 非空路径缺少有效托管器，报错。 |
| `iis`、`iis:...` | 报告未实现，不回退 Nginx。 |
| 其他托管器名称 | 报未知托管器。 |

默认文件名只有 `web.profile`，显式路径必须是一个文件，不能是目录、通配符或文件列表。顶层路径含空格时可用终端引号包住整个参数：

```shell
dotnet-pack deb "--web:nginx:config files/web.profile"
```

整个选项值先按本次命令的最终变量求值，再去掉首尾空白、拆分并校验。相对位置基于最终 source，不基于命令启动目录。文件不存在、不可读、导入缺失、解析或生成失败均终止打包，不发布这次的半成品，也不因这次失败保存源 `.version`。

下文省略身份参数的短命令假设源 `.version` 或其他命令参数已提供必要身份；`--web` 不替代常规打包参数。

<a id="profile"></a>
## Profile 语法、作用域与导入

通过 Core Profile 读取 UTF-8 文本。根默认值位于首个段落之前；[api] 定义站点，[api reports] 定义该站点的路径作用域。最多两级，名称中的点号不推导 URL，路径单独填写 path。字段名与段落名忽略大小写。

~~~ini
forwarded = true
#@import shared/common.profile

[api]
host = api.example.test
bind!plain = http://*

[api reports]
path = /reports
server = http://reports.internal:8080/reports
~~~

import 相对于声明文件，按读取顺序合并，必须存在；循环和超深导入失败。导入参数使用 Core 模板求值，不自动展开通配符、去引号或搜索父目录。单文件同段落重复字段、未知字段、错误层级都失败。行首 # 或 ; 是注释，行尾注释属于值。Profile 不自动去除值的引号；首个等号之后的内容保留。

host、bind!名称、certificate、certificate-key 属于站点；path、match 属于路径；server、策略、header!名称、forwarded、websocket 按根→站点→路径继承。nginx: 原始设置只用于站点或路径。显式空值的含义由字段决定；server 空值继续继承，header 空值则覆盖原值。

<a id="bindings"></a>
## 站点、主机名与绑定

host 是请求域名，bind 是本地入口监听，不决定应用监听。host 支持具体 DNS/IP、*.example.test、.example.test、example.*；省略表示不增加名称约束，端口站点应省略 host，不能以 _ 充当域名。

~~~ini
[api]
host = api.example.test,www.example.test
bind!plain = http://*,http://[::]
bind!secure = https://*:443,https://[::]:443
~~~

plain/secure 是用户自定组名。每个逗号或分号分隔项必须是完整 URI；HTTP/HTTPS 默认端口分别为 80/443。* 只代表 IPv4，IPv6 通配需要 [::]。入口仅接受 IP，不能包含路径、查询或片段。端口范围 1–65535，空项失败。

同名 bind 组整体替换，异名组合；同站点重复端点去重。相同 socket 与域名的跨站点冲突失败，不同域名可以共享监听。实际默认关系仅记录显式 default_server，不伪造隐式默认站点。

<a id="certificates"></a>
## 入口 HTTPS 与证书

HTTPS 入口默认引用安装根 .certificates/<PackageName>.pem 中的合并证书及私钥。它是目标路径，不会在制作机自动生成。

~~~ini
[api]
bind!secure = https://*:443
certificate = file:/etc/tls/api/fullchain.pem
certificate-key = file:/etc/tls/api/private.key
~~~

file: 后面是目标 Linux 绝对路径。省略 certificate-key 时使用 certificate 的同一 PEM 文件；显式空值清除继承的单独私钥。Nginx 原始证书设置必须同时提供 ssl_certificate 和 ssl_certificate_key，整对覆盖公共设置。入口证书与后端 CA 信任用途不同。

<a id="routes"></a>
## 路径与请求类别

每个站点自动组合应用的 HTTP 承载绑定。普通 HTTP 与原生 gRPC 分开选择；application/grpc Content-Type（含 +后缀和参数）归入 gRPC，grpc-web 不归入。每个类别独立选择最长字面基路径。

前面的 /reports 配置只覆盖普通 HTTP 的该路径范围，其余路径继续自动组合。例如 /REPORTS/2026?id=7 忽略大小写匹配并原样转发，/reports-old 不命中。查询不参与选路，仍随请求转发。

path 只接受 ASCII 字母数字以及 / - . _ ~；不接受空路径段、点段、百分号转义、查询、片段或正则语法。尾斜杠可选，规范基路径转为小写。match 仅支持 prefix，exact/regex 无法表达绑定契约，明确失败。

生成器以 http 上下文的 map 读取原始 $request_uri，再跳转到命名 location，保留方法、原始 URI 和查询。HTTP 未命中返回 404；gRPC 未命中返回 HTTP 200、grpc-status 12、grpc-message 与 application/grpc，两类请求互不兜底。

例如应用分别声明 / 的 http1、/metrics 的 http1 和 /opentelemetry 的 grpc+http2，会产生三条独立映射。/metrics 优先于 /，未知 gRPC 路径不会落入普通 Web 根路由。同类别、同规范路径的不同逻辑绑定冲突时失败。

<a id="servers"></a>
## 后端选择与远程池

| server 值 | 含义 |
| --- | --- |
| 省略、空值或求值后空白 | 继承有效父级目标；最终无目标时自动组合应用绑定。 |
| web | 按应用别名 web 筛选，再按路径和请求类别确定目标。 |
| http://remote:8080/api | /api 范围的显式普通 HTTP/1.1 远程目标。 |
| https://remote:8443/api | 同上，连接使用 TLS。 |
| grpc://remote:4317/opentelemetry | 显式原生 gRPC HTTP/2，必须写端口。 |
| grpcs://remote:4317/opentelemetry | 同上，连接使用 TLS，必须写端口。 |

别名必须存在，每项具体映射必须唯一。站点选择限制整个站点，路径选择只覆盖对应范围。远程 URI 的路径限制服务范围，不是替换前缀；禁止用户信息、查询、片段、zone 与通配远程主机。旧 ~ 和前缀式选择器已移除。

同一 binding 的多个端点表示同服务的替代地址，不作为负载均衡实例。稳定选择顺序为明确 IPv4、明确 IPv6、通配、DNS，再按主机、端口、身份排序。本机安装时通配连接同地址族回环，DNS 保留原义；不探测、不修改应用监听配置。

~~~ini
[api]
bind!plain = http://*
server!east = http://east.internal:8080/api weight=3
server!west = http://west.internal:8080/api
~~~

east/west 是两个独立远程实例，权重为 3:1。显式池至少两个成员，方案、协议、基路径必须相同，端口可以不同；规范化后重复地址失败。权重必须是目标实现可表达的正整数。替换池需声明完整的新成员集合。

上游版本只能从声明能力中选择；同服务普通语义取共同 HTTP 版本，原生 gRPC 使用 HTTP/2。Nginx 适配支持普通 HTTP/1.1 和 HTTP/2；HTTP/2 上游语法最低要求 Nginx 1.29.4 及 HTTP/2 模块，该版本是语法最低要求。仅 HTTP/3 上游会报告实现能力限制。WebSocket 要求可用的 HTTP/1 上游。

入口版本与上游版本独立。gRPC 路由自动开启入口 http2；显式矛盾设置失败。原始 QUIC 监听导出 UDP/http3 能力，需要 TLS、目标 Nginx QUIC 模块以及 Nginx 1.29.3 或更高版本。QUIC 站点的默认上游 Host 优先使用 $http_host；为空时通过 $host$is_request_port$request_port 从 HTTP/3 :authority 恢复主机及显式端口。回退时主机名大小写被规范化；显式 header!Host 保持原值。[request_port 与 is_request_port 变量](https://nginx.org/en/docs/http/ngx_http_core_module.html#var_request_port)要求上述最低版本。配置生成不验证或安装目标模块。

<a id="inheritance"></a>
## 后端替换与继承实例

**后端目标整组替换，策略逐字段继承。** 导入者声明一个单值或任意数量的本地池成员，就替换该层从共享文件取得的整个目标组，不按成员名称增量合并。根 → 站点 → 路径也遵循这个规则。

### 例一：单后端替换共享池

`shared/site.profile`：

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
server-balance = least-connections
```

`web.profile`：

```ini
#@import shared/site.profile

[api]
server = http://single:8069
```

最终只有 single；app1、app2 均不保留。server-balance 仍继承 least-connections，因为策略不是目标组的一部分。

### 例二：本地池替换共享单后端

`shared/site.profile`：

```ini
[api]
bind!legacy = http://*
server = web
```

`web.profile`：

```ini
#@import shared/site.profile

[api]
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
```

最终只有 app1/app2 池。共享 web 别名已被替换，不再解析。

### 例三：本地池替换共享池

`shared/site.profile`：

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
```

`web.profile`：

```ini
#@import shared/site.profile

[api]
server!app3 = http://app3:8069
server!app4 = http://app4:8069
```

最终只有 app3/app4，不是四个成员。替换后的池仍必须至少包含两个成员。若要修改 app1 权重并保留 app2，必须在本地重写整个池：

```ini
#@import shared/site.profile

[api]
server!app1 = http://app1:8069 weight=5
server!app2 = http://app2:8069
```

被替换的地址、权重或变量不再求值；但文件语法、未知字段、错误层级、同文件混用 server 两种形式、缺失 import 等结构错误仍然报错。

### 例四：根、站点和路径

```ini
server = http://shared:8069
server-retry-count = 2

[api]
bind!legacy = http://*
server = http://api:8070

[api root]

[api reports]
path = /reports
server = http://reports:8080

[admin]
bind!legacy = http://*:8081
```

api 根路径使用 api:8070，reports 使用 reports:8080，admin 自动根路径使用 shared:8069。三个路径都继承 retry-count=2。

池的层级替换同样整体进行：

```ini
server!shared1 = http://shared1:8069
server!shared2 = http://shared2:8069

[api]
bind!legacy = http://*
server!app3 = http://app3:8069
server!app4 = http://app4:8069

[api root]

[api reports]
path = /reports
server = http://reports:8080

[admin]
bind!legacy = http://*:8081
```

api 根路径只用 app3/app4；reports 只用 reports；admin 继承 shared1/shared2。不混入其他层级成员。


<a id="tls"></a>
## 后端 HTTPS 与证书验证

TLS 字段独立继承，仅用于 HTTPS。server-tls-verify 默认 true，false 明确关闭验证；server-tls-name 决定 SNI 与证书身份，独立于连接地址和 HTTP Host/:authority。

~~~ini
[api]
bind!plain = http://*
server = https://192.0.2.20:8443/api
server-tls-name = api.internal
server-tls-trust = file:/etc/tls/internal-ca.pem
~~~

实际连接 192.0.2.20:8443，证书需受 internal-ca.pem 信任并匹配 api.internal。除非 header!Host 显式覆盖，原请求 Host 保持不变；/api 前缀仍在请求中。

名称省略或为空时，仅当全部有效目标具有相同 DNS 名称才自动推导。IP 及通配转回环连接需要显式验证名称。清空继承名称恢复推导；清空 trust 删除继承的显式文件，验证仍保持开启。

未指定 trust 时使用系统 CA：tar/deb 为 /etc/ssl/certs/ca-certificates.crt，rpm 为 /etc/pki/tls/certs/ca-bundle.crt。目标必须提供该文件，其他布局请明确指定信任文件。系统路径不作为可重定位包资源；容器制作按 Nginx 镜像调整默认位置。

生成时不读取目标证书、不执行握手。原始 proxy_ssl_*/grpc_ssl_* 设置可能与记录策略矛盾，因此拒绝；请使用上述结构化字段。

<a id="policies"></a>
## 调度、故障、重试与会话保持

所有策略支持根 → 站点 → 路径逐字段继承。更换 server 不清空策略；列表值整体替换，不和父级取并集。枚举值忽略大小写；除明确允许清空的字段外，空值和未知值都报错。

| 字段 | 默认值 | 约束及 Nginx 行为 |
| --- | --- | --- |
| `server-balance` | round-robin | 加权轮询；least-connections 为最少连接；least-requests 无准确映射，报能力错误。 |
| `server-failure-count` | 3 | 每成员非负整数；0 关闭被动失败统计与隔离；对应 max_fails。 |
| `server-failure-timeout` | 30s | 正时长；对应 fail_timeout 的统计窗口及隔离时段。 |
| `server-retry` | error,timeout | 重试条件集合，或单独 off。 |
| `server-retry-count` | 1 | 非负整数，表示首次之外的额外尝试次数；0 关闭。 |
| `server-affinity` | none | none 或 cookie。 |
| `server-affinity-cookie` | 自动命名 | 可选合法 Cookie 名称；空值恢复自动命名，不自行开启 affinity。 |

### 时长语法

时长按 Core `TimeSpanUtility.Parse/TryParse` 支持的语法解析，再检查字段的正数要求和目标精度；不直接把原始文本交给 Nginx。

| 输入 | 含义/结果 |
| --- | --- |
| `30s`、`30S` | 30 秒。 |
| `500ms`、`500MS` | 500 毫秒。 |
| `1.5s` | 1.5 秒。 |
| `1.5m` | 90 秒。 |
| `2h`、`1d` | 2 小时、1 天。 |
| `00:01:30` | 标准 TimeSpan，90 秒。 |
| `1.02:03:04` | 1 天 2 小时 3 分 4 秒。 |
| `1m30s` | 不支持的复合单位写法，报错。 |
| `30` | 按标准 TimeSpan 解释为 30 天，不能当作 30 秒。 |
| 空值、0、负数 | 即使 Core 能解析，也不满足这些时长字段的正数约束。 |

建议使用带单位的值。Nginx 的 failure-timeout 要求精确整秒；健康检查周期及连接/发送/读取超时要求精确整毫秒。因此 `server-failure-timeout=1.5s` 报错，而 `server-health-interval=1.5s` 可表达为 1500ms。不取整、不截断，超出目标数值范围也报错。

### 调度与被动失败

`round-robin` 和 `least-connections` 都保留成员权重；后者按活动连接数调度，不等于最少请求数，不能把 least-requests 静默替换为 least-connections。

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = least-connections
server-failure-count = 3
server-failure-timeout = 30s
```

失败统计针对每个成员，不是整个池的累计。达到统计窗口中的失败条件后，由 Nginx 暂时隔离成员；这与主动检查的“连续失败次数”不同。单成员 upstream 的 max_fails/fail_timeout 受 Nginx 原生限制，不能承诺它会被被动隔离。`failure-count=0` 不关闭主动健康检查。参见 [Nginx upstream 成员](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#server)。

### 重试

支持的条件为 `error`、`timeout`、`invalid_header`、`http_500`、`http_502`、`http_503`、`http_504`、`http_403`、`http_404`、`http_429`，以逗号分隔；`off` 必须单独使用。

```ini
server-retry = error,timeout,http_502,http_503
server-retry-count = 2
```

最多为首次加两次额外尝试，映射为 Nginx 总尝试数 3。实际是否能重试还受目标托管器条件影响；不自动加入 non_idempotent 等放宽条件，不承诺已发出的非幂等请求会再次发送。

`server-retry=off` 或 `server-retry-count=0` 任一成立即关闭重试；0 不映射成 Nginx 的“无限制次数”。关闭一个字段不清除另一个字段，例如父级 off、子级仅 count=2 仍关闭；子级还需把 retry 改成有效条件才能开启。`off,error`、负数、小数或空值均非法，不能用另一个关闭字段掩盖非法值。参见 [Nginx proxy_next_upstream](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_next_upstream)。

### Cookie 会话保持

```ini
[api]
host = api.example.com
bind!secure = https://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
server-affinity = cookie
server-affinity-cookie = API_ROUTE
```

省略或清空名称时自动生成 `HOSTER_ROUTE_<标识>`；名称不直接包含应用版本。同一有效池复用名称，不同池隔离；显式名称在可静态确定的同一主机范围内冲突时报错。

Cookie 使用 `Path=/`、`HttpOnly`、`SameSite=Lax`，不指定 Domain 或持久有效期。站点所有入口都是 HTTPS 时加 Secure；包含 HTTP 入口时不加。它是路由亲和性，不复制应用会话数据，也不等同于客户端 IP 哈希。

目标需要支持 [sticky cookie](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#sticky)：开源 Nginx 最低 1.29.6，或具备该功能的 Nginx Plus。构建机不检测目标版本，部署环境负责满足模块要求。

<a id="health"></a>
## 主动健康检查

### 开启、关闭与继承

**只有有效的 `server-health` 非空路径才启用检查。** 没有 `server-health-enabled` 字段。

| server-health 状态 | 行为 |
| --- | --- |
| 本层未声明 | 继承父层；所有层都缺失则关闭。 |
| `/` | 显式启用，探测根路径。 |
| `/health?ready=true` | 显式启用，使用该路径和查询。 |
| `off`、`OFF` 等 | 关闭，可覆盖父层。 |
| `server-health=` 或无等号 | 关闭，不回退为 `/`，不重新继承。 |

最小设置及局部关闭：

```ini
[api]
bind!legacy = http://*
server!a = http://app1:8069
server!b = http://app2:8069
server-health = /health

[api root]

[api reports]
path = /reports
server-health =
```

root 启用检查，reports 关闭。把空值改成 off 等价；更具体层可再次用路径启用。单独设置 interval、status、请求头或超时不会启用检查，但其有效声明仍须合法。

### 全部探测字段

| 字段 | 默认值 | 规则 |
| --- | --- | --- |
| `server-health` | 关闭 | `/` 开头的目标相对 URI；可含查询，不允许完整 URL、`//` 开头、片段或空白。 |
| `server-health-interval` | 10s | 正时长，探测周期。 |
| `server-health-connect-timeout` | 5s | 正时长，连接超时。 |
| `server-health-send-timeout` | 5s | 正时长，发送超时。 |
| `server-health-read-timeout` | 5s | 正时长，读取超时。 |
| `server-health-failure-count` | 3 | 正整数，连续失败多少次判为不健康。 |
| `server-health-recovery-count` | 2 | 正整数，连续成功多少次恢复。 |
| `server-health-status` | 2xx | 非空健康响应状态集合。 |
| `server-health-header!名称` | 未声明 | 固定探测头，按名称合并；空值不发送。 |

完整策略例子：

```ini
[api]
host = api.example.com
bind!legacy = http://*
server!a = https://192.0.2.20:8443
server!b = https://192.0.2.21:8443
server-tls-name = api.internal
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
server-health = /health?ready=true
server-health-interval = 10s
server-health-connect-timeout = 2s
server-health-send-timeout = 5s
server-health-read-timeout = 5s
server-health-failure-count = 3
server-health-recovery-count = 2
server-health-status = 200-299,3xx
server-health-header!Host = probe.internal
server-health-header!X-Probe = readiness
```

每个成员使用自己的协议、地址、端口，以 GET 探测，不附加业务路由前缀或客户端查询。HTTPS 复用有效 server-tls 设置；HTTP Host 与 TLS 名称独立，例如上例 Host 是 probe.internal，SNI 是 api.internal。

### 状态码集合

| 写法 | 接受范围 |
| --- | --- |
| `200` | 仅 200。 |
| `200,204` | 仅 200 和 204。 |
| `200-299`、`2xx` | 200–299。 |
| `200-299,3xx` | 200–399。 |
| `2xx,301,302` | 200–299，再加 301、302。 |

单码及区间端点只允许 100–599；类别允许 1xx–5xx，忽略 x 的大小写。逗号取并集，重复和重叠归一化；空值、空元素、逆序区间、99、600、6xx 均报错。

`200-299,3xx` 表示收到 3xx 本身也健康，**不跟随 Location 重定向**；网络、超时或 TLS 失败不会因此被当成成功。默认 2xx 生成对应 200–299 判定。

状态集合整体覆盖。例如根级为 2xx，路径级为 200,204，该路径仅接受两个状态码；不会保留整个 2xx。探测路径、周期等其他字段继续各自继承。

### 计数、超时与请求头隔离

默认阈值示意：

| 连续探测结果 | 判定 |
| --- | --- |
| 失败、失败、失败 | 达到 failure-count=3，判为不健康。 |
| 失败、失败、成功、失败 | 成功打断失败序列，最后只有连续失败 1 次。 |
| 不健康后：成功、成功 | 达到 recovery-count=2，恢复。 |
| 不健康后：成功、失败、成功 | 失败打断恢复序列，尚未达到连续成功 2 次。 |

计数是每个成员的主动探测结果，不是业务请求失败数、重试次数或全池累计。启动、重载及原生运行状态仍由托管器处理；打包器不增加探测进程或维护状态数据库。

连接、发送、读取是三个独立阶段的超时，不是探测总耗时。例如连接耗时 2 秒、读取阶段耗时 4 秒，不因合计超过 5 秒就按某个“总超时”判失败；发送/读取也受 Nginx 连续等待语义影响。

三个字段都可省略。只填 `server-health-read-timeout=2s`，另外两个仍分别继承或默认 5s。业务 `nginx:proxy_read_timeout=60s` 不把探测读取默认改成 60s，探测 2s 也不改写业务超时。

探测头是独立集合，不继承业务 header、原始 proxy_set_header、forwarded 或 WebSocket 自动头。探测头自身按根 → 站点 → 路径、名称忽略大小写合并。未声明 Host 时使用托管器原生代理默认；显式空 Host 表示不发送，不回退默认。

### Nginx 模块与生成结构

主动检查需要 [Nginx 主动健康检查模块](https://nginx.org/en/docs/http/ngx_http_upstream_hc_module.html)（Nginx Plus）。普通开源 Nginx 的被动失败统计不等于这个模块；工具不以被动检查模拟主动检查。

配置器生成应用专属 upstream、共享区、状态 match，以及专用内部命名 location。该 location 隔离探测头和超时，不添加用户可访问的业务路由。相同站点内，相同有效后端及相关策略共用池和探测配置；探测开关、路径、头、TLS 或其他相关策略不同则分别生成，跨站点不共享。池名是生成细节，不应由用户原始指令猜测和引用。

<a id="headers"></a>
## 请求头与 WebSocket

### 自动转发头

`forwarded` 默认 true，Nginx 映射为：

| 发往后端的头 | 值 |
| --- | --- |
| Host | <code>&#36;http_host</code>，保留收到的 Host 端口。 |
| X-Real-IP | <code>&#36;remote_addr</code>。 |
| X-Forwarded-For | <code>&#36;proxy_add_x_forwarded_for</code>。 |
| X-Forwarded-Proto | <code>&#36;scheme</code>，表示客户端入口协议。 |

例如入口 HTTPS、后端 HTTP 时，自动 Proto 仍为 https。`forwarded=false` 仅停止自动生成这些设置，不代表删除客户端或外层配置中的同名头；需要明确不发送时设置对应空 header。

应用端还需按部署拓扑处理转发头及可信代理；生成代理头不完成应用侧配置。

### 公共固定头和优先级

```ini
header!X-Application = example-api

[api]
bind!legacy = http://*
server = http://app:8069
header!X-Service = api

[api root]

[api hub]
path = /hub
header!X-Service = hub
header!X-Application =
```

root 发送 X-Application=example-api、X-Service=api；hub 发送 X-Service=hub，不发送 X-Application。未声明才继承，空值显式抑制；头名不区分大小写。

公共 header 的值是变量求值后的固定文本，不是 Nginx 表达式。header 和 server-health-header 的名称必须是合法 HTTP 字段名，不能含空格、冒号等分隔符。不加引号表达字面字符串。动态头请用原始项：

```ini
[api hub]
nginx:proxy_set_header!X-Remote = $remote_addr
```

同名头的优先级从低到高为：

```text
自动头 → 根公共头 → 站点公共头 → 站点原始头 → 路径公共头 → 路径原始头
```

因此路径公共头可以覆盖站点原始头，同层原始头覆盖公共头。生成器为每条路径输出完整有效头集合，避免 Nginx 的整组继承导致只改一个头就丢掉其他头。覆盖自动转发或 WebSocket 头时也遵循上述规则，并提示覆盖来源。

### WebSocket

`websocket` 默认 false，和 forwarded 一样支持 true/false、1/0，文本忽略大小写，空值非法；根 → 站点 → 路径继承，子层 false 可覆盖 true。

```ini
[api]
bind!legacy = http://*
bind!secure = https://*
server = http://app:8069

[api root]

[api devices]
path = /hub/devices
websocket = true
```

通过 `ws://.../hub/devices` 或 `wss://.../hub/devices` 使用同一路径；该路径也允许普通 HTTP 请求。Nginx 生成 HTTP/1.1 代理及 Upgrade/Connection 设置，不创建全局 map。自动设置可以被显式头覆盖，覆盖后效果由该设置负责。参见 [Nginx WebSocket 代理](https://nginx.org/en/docs/http/websocket.html)。

<a id="native"></a>
## 托管器原始设置

站点或路径使用 nginx:指令=值、nginx:指令!首参数=其余参数。不要包含末尾分号或行尾注释；引号和运行时表达式遵循 Nginx 语法。禁止任意块、嵌套指令与 worker_processes 等实例配置。

~~~ini
[api]
bind!plain = http://*
nginx:listen!8080
nginx:server_name = api.example.test
nginx:client_max_body_size = 16m
nginx:proxy_set_header!X-Request-Id = $request_id
~~~

最终监听 8080，整个公共 bind 集合被替换；最终 server_name 覆盖 host。导出使用最终值并保留显式 default_server。原始证书设置同样整对覆盖公共证书与私钥。

proxy_pass、grpc_pass、include、rewrite、try_files、return、error_page、recursive_error_pages、proxy_http_version、proxy_method、proxy_set_body、上游 TLS/重试指令及 grpc_set_header 不得覆盖结构化映射，发布前即失败。请使用 server、server-tls-*、server-retry* 与 header!Host。动态或空的上游 Host 无法表示；支持字面 Host 以及默认 $http_host。

字面 IP 监听可使用 ssl、default_server、bind、ipv6only=on、http2、quic、reuseport。TCP/UDP 同号端口是不同 socket。DNS/Unix 监听、正则或动态名称、不支持的选项、矛盾 TLS/QUIC 与重复默认关系都会失败，不生成缺项的绑定文档。

~~~ini
[secure]
host = api.example.test
nginx:listen!443 = ssl
nginx:listen!0.0.0.0:443 = quic reuseport
nginx:http2 = on
~~~

两个 listen 的 ! 后均是实际首参数，不是描述性名称。该例为同号 TCP/UDP 443，配套填写普通证书/私钥设置。http2 on 与 http3 on/off 必须是字面开关，元数据反映最终能力。

<a id="variables"></a>
## 变量与转义

字段值支持 <code>&#36;(name)</code> 和 `${name}`，使用本次命令固定后的变量视图；来源依次包括默认值、环境、从根到最终 source 的 `.env` 以及显式选项。源目录和包身份遵循通用打包规则，不能用 Web 输入反向改变。

```ini
[api]
host = ${Environment}.api.example.com
bind!legacy = http://*
server = http://${BackendHost}:8069
header!X-Environment = ${Environment}
```

只有有效值需要求值。缺失的有效变量报错；被替换的无效后端不再求值。已知但未选择的 `iis:...` 值不求值；拼错前缀如 `ngnix:...` 则报错。

段落名、公共字段名和成员标识不求值。导入参数在 `Directives.Processing` 中评估模板；原始 `!` 后缀按实际参数求值。

| 写法 | 行为 |
| --- | --- |
| `${name}`、`${namespace:name}` | 打包变量。 |
| `\${name}` | 保留字面的 `${name}`，递归评估也不再次展开。 |
| `$host`、`$remote_addr` | 保留给 Nginx 的运行时变量。`${host}` 形式需写为 `\${host}`。 |

Profile 引号不阻止模板评估。变量转义不替代目标编码：公共固定头含 `$` 时，Nginx 输出器无法保证字面语义，会报错；运行时变量应放在原始 Nginx 指令中。

Bash/PowerShell 使用单引号保护命令参数中的模板，Windows cmd 可用双引号：

```shell
dotnet-pack deb '--web:nginx:web.${Environment}.profile'
```

共享文件可以用 `#@import web.${Environment}.profile` 选择环境。每条指令导入一个文件；内部空格属于文件名，不用引号包裹路径。构建期 `#@import` 读取 Profile；运行期 `nginx:include` 只输出 Nginx 引用，不复制文件。

<a id="example"></a>
## 完整配置示例

共享文件 `shared/web.profile`：

```ini
forwarded = true
websocket = false
header!X-Application = example-api
```

宿主 `web.profile`（app 为部署网络中的应用服务名）：

```ini
#@import shared/web.profile

[api]
host = api.example.com
bind!legacy = http://*,http://[::]
bind!secure = https://*,https://[::]
certificate = file:/etc/tls/api/fullchain.pem
certificate-key = file:/etc/tls/api/private.key
server = http://app:8069

[api root]

[api hub.devices]
path = /hub/devices
websocket = true
nginx:proxy_read_timeout = 300s
```

root 提供根前缀兜底；hub.devices 支持 WS/WSS 并增加业务读取超时。协议从入口决定，无需重复声明两套路由。证书文件由目标环境提供。需要加权池时，把 site 的单行 server 换成：

```ini
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = least-connections
```

需要主动探测时，再显式增加 `server-health=/health`，并确保目标具有所需模块。所有站点、有效池及判定块仍写在一个应用配置文件中。

<a id="delivery"></a>
## 载荷选择与输出布局

### 输入是否入包由普通参数决定

`--web` 只读取输入并生成配置，不决定哪些 Profile 文件入包。位置参数 arguments 与 `--exclude` 继续决定普通载荷。

以下命令的 source 均为当前目录，未给位置参数，因此默认递归收集该目录：

```shell
dotnet-pack deb --web:nginx
```

读取 web.profile 及导入，生成 Nginx 配置；web.profile 同时按普通收集规则入包。去掉 --web 后，它仍会入包，只是不再生成配置。

```shell
dotnet-pack deb --web:nginx --exclude:web.profile
```

仍读取和转换 web.profile，但该原始文件不入包。生成的配置仍入包。

```shell
dotnet-pack deb --web:nginx --exclude:*.profile
```

读取输入及递归导入，并按普通排除规则排除 Profile 载荷。**排除载荷不等于禁止构建期读取。**

显式位置参数只收录所选文件，参与导入不使 source 外的共享文件自动入包；证书、Nginx include 目标也不因引用自动复制。显式启用生成的配置不会被普通 exclude 静默排除。

### 文件、名称与冲突

```text
<安装根>/.web/nginx/zongsoft.web.conf
/etc/nginx/conf.d/zongsoft.web.conf
    -> <安装根>/.web/nginx/zongsoft.web.conf
```

上面第一行是真实生成文件，第二行是启用激活时在目标机创建的加载链接。容器工具直接读取真实文件，不依赖链接，不需要 `.hoster` 元数据。`<hoster>` 表示托管器，例如 nginx、未来的 iis。

- 每个包生成一份 Nginx 配置，多站点合写。文件名采用最终 PackageName，包含 Edition、不含版本；`zongsoft.web-prod` 对应 `zongsoft.web-prod.conf`。
- 同一包名升级时文件名稳定。文件标识拒绝路径字符等非法输入，不靠静默替换字符解决重名。
- 配置以安装根相对条目入包，权限 0644，目录 0755；UTF-8 无 BOM、CRLF、Tab。生成脚本使用 LF。
- 不在 source 创建临时 .conf，不写回 Profile；不复制私钥内容进入生成配置。相同有效输入和安装路径生成确定内容，不附加时间戳或构建机路径。
- 普通载荷或目标别名与生成配置占用同一规范化目标时，打包报错并列出双方来源，不按加入顺序覆盖。

例如手工 manual.conf 被映射到 `.web/nginx/zongsoft.web.conf`，同时 --web 也生成该文件，则冲突。使用生成配置时应排除/移动该手工载荷；使用自备配置时关闭 --web。这与安装时覆盖系统加载入口是两种不同规则。

<a id="installation"></a>
## 裸机、容器构建与生命周期

### 激活开关

`HOSTER_WEB_ACTIVATION` 在**安装或卸载进程**中读取，不把构建机当前值固化到包中：

| 值 | 安装/升级的行为 |
| --- | --- |
| 未设置、`1`、`true` | 创建/更新加载链接，校验 Nginx，运行中则重载。 |
| `0`、`false` | 正常交付配置，跳过新链接创建/更新及全部 Nginx 操作。 |
| 显式空值、其他值 | 报错。 |

true/false 忽略大小写；不自动识别容器、PID 1 或 Nginx 是否存在来猜测开关。关闭时不为 Web 激活要求 Nginx/systemd，但该开关不取消应用 daemon、升迁或用户生命周期钩子。

容器构建的安装命令示例（以实际包名替换 application）：

```dockerfile
RUN HOSTER_WEB_ACTIVATION=0 dpkg -i /tmp/application.deb
```

RPM 或 Tar 对应：

```sh
HOSTER_WEB_ACTIVATION=false rpm -Uvh /tmp/application.rpm
HOSTER_WEB_ACTIVATION=0 sh /tmp/application.sh
```

containerizer 在构建镜像前读取包内 .bindings 和 .conf.template。make 适配当前应用后端及已知文件引用、收集资源，并将站点、发布关系和探测身份写入 containerizer.json；run 使用该中间结果。声明托管器但交接不完整时失败，不从启动参数推导入口。普通 .conf 继续用于主机安装。

### 裸机自动激活条件

| 项目 | 固定约定 |
| --- | --- |
| Nginx 服务 | systemd 的 `nginx.service`。 |
| 主配置 | `/etc/nginx/nginx.conf`。 |
| 加载入口 | `/etc/nginx/conf.d/<PackageName>.conf`。 |
| 校验 | `nginx -t -c /etc/nginx/nginx.conf`。 |
| 加载确认 | 用 `nginx -T` 确认当前包入口被包含。 |
| 已运行 | 校验成功后 `systemctl reload nginx.service`。 |
| 明确停止（inactive/failed） | 仅校验并保留停止，不自动启动。 |

主配置的 http 上下文需包含这些片段，例如已有 `include /etc/nginx/conf.d/*.conf;`。打包器不改写主配置。不能把站点片段单独作为 `nginx -t -c` 的主配置，也不能把“不相关配置校验成功”当成本包已加载的证明。

找不到 nginx/systemctl、服务未加载、状态无法确定、加载关系缺失、校验失败或重载失败，都会使安装/升级失败。校验失败不发重载；工具不安装 Nginx、不注入发行版依赖、不自动启动或重启它。状态为 activating 等不能当成明确停止。

非 systemd、自定义主配置、多实例或自定义服务名使用 `HOSTER_WEB_ACTIVATION=0`，并由现有用户钩子管理自己的链接、校验、重载及卸载清理。默认实例不会根据用户启动脚本自动猜测其他参数。

`nginx -t` 会访问证书等依赖文件；镜像构建阶段不具备依赖时关闭激活，可延后到目标环境验证。重载成功不等于应用健康检查通过，也不代表包管理器提供跨格式的文件事务回滚。参见 [Nginx 命令参数](https://nginx.org/en/docs/switches.html)及[重载机制](https://nginx.org/en/docs/control.html)。

### 系统加载位置的处理

启用激活时：

| 加载位置现状 | 行为 |
| --- | --- |
| 不存在 | 创建指向本包真实配置的符号链接。 |
| 已正确指向本包配置 | 复用。 |
| 普通文件 | 直接替换为本包链接。 |
| 指向其他目标的链接，包括悬空链接、指向目录的链接 | 仅替换链接本身，原目标不受影响。 |
| 实际目录 | 报冲突，不递归删除。 |

链接由目标机安装动作创建，不作为无条件符号链接载荷交付，也不要求 Windows 构建机创建实体链接。

关闭激活只跳过**本次**激活动作，不撤销过去的加载状态。例如旧安装已有链接，新包继续交付同名配置，activation=0 的升级会更新真实文件并保留旧链接。管理员以后重载 Nginx 时仍可能加载新内容；这不等于永久停用站点。原来没有链接时则不会新建。

### 安装及升级顺序

安装前仍执行 preinstalling → installing → postinstalling，随后交付载荷。文件交付阶段完成默认路径定位和弃用配置清理，再执行：

| 顺序 | 动作 |
| --- | --- |
| 1 | preinstalled 用户钩子，可准备证书等依赖。 |
| 2 | 升迁准备及升迁，未选择时跳过。 |
| 3 | installed 主钩子；默认执行应用服务安装/启动，自定义主钩子替换默认动作。 |
| 4 | Web 激活：按开关处理链接、校验和条件重载。 |
| 5 | postinstalled 用户钩子，可做业务验证。 |

前置步骤失败后不继续激活或执行后置钩子。工具只根据步骤返回值推进，不另行等待应用健康。自定义主钩子不删除独立的 Web 步骤，daemon 禁用也不禁用 Web。

Web 激活失败保留诊断并返回失败，不额外执行自定义回滚。载荷此时可能已经安装，不能把错误理解为目标机没有改变；各包格式按自身的安装状态与重试规则处理。

### 更新、弃用与卸载

.web 是包拥有的生成目录。升级直接覆盖同名生成配置，包括安装后的手工修改；不备份、不使用 DEB conffile/RPM config 保留策略。长期变更应修改构建端 Profile 后重新打包。

新包不再交付的旧配置会清理，仍指向该旧配置的本包加载链接也清理，**不受激活开关控制**。例如新版本取消 --web，旧 .conf 和匹配链接仍被删除；关闭激活仅阻止 Nginx 操作。新版本仍交付同名文件时则更新文件，关闭激活时保留旧链接。

普通卸载顺序：

| 顺序 | 动作 |
| --- | --- |
| 1 | preuninstalling 用户钩子。 |
| 2 | 删除仍指向本包预期配置的加载链接；按开关校验并对运行中的 Nginx 重载。 |
| 3 | uninstalling 主钩子；默认停止/禁用应用服务。 |
| 4 | postuninstalling 用户钩子。 |
| 5 | 包格式适配器移除应用载荷。 |
| 6 | preuninstalled → uninstalled 主钩子 → 清理残留整个 .web。 |
| 7 | postuninstalled 用户钩子。 |

关闭激活卸载同样删除本包链接和 .web，包括手工修改，不遗留悬空链接。此时不调用 Nginx，不承诺运行中的站点立即停用。源目录文件不受目标机卸载影响。

卸载只删除目标仍匹配的本包链接；同名普通文件、实际目录或指向其他目标的链接保留。此所有权规则与安装时直接覆盖普通文件/其他链接的规则不同。

| 普通卸载时的失败 | 处理 |
| --- | --- |
| Nginx 命令缺失、校验失败、状态未知、重载失败 | 保留具体诊断，警告并继续清理，不声称站点成功停用。 |
| 配置校验失败 | 不发重载，继续文件清理。 |
| 删除本包链接或文件失败 | 报卸载错误，不纳入警告继续规则。 |
| 激活变量非法 | 报错。 |

例如其他站点配置错误导致卸载时 nginx -t 失败，本包文件仍清理；运行中的旧配置后续由管理员处理。安装/升级依然使用严格失败规则，不套用卸载的警告策略。

### 包格式和重定位

| 格式/场景 | Web 行为 |
| --- | --- |
| DEB | 安装步骤只在 postinst 的 configure 阶段执行；保留 Debian 升级/卸载阶段语义。 |
| RPM | 新包 post 更新；最终移除时清理，旧版本升级卸载不清理新版本配置。 |
| Tar 正常安装 | 同样执行交付、生命周期与激活。 |
| Tar `INSTALL_PATH` | 默认证书引用和链接目标随实际安装根定位，显式绝对引用不变。 |
| Tar `DESTDIR` 暂存 | 仅交付暂存载荷和完成路径定位；跳过生命周期，不操作真实系统加载目录。 |

DESTDIR 前缀不写入默认引用；关闭激活或跳过生命周期仍必须完成交付路径定位。升级清理由新包的交付集合决定，不依赖旧版本普通卸载。既无当前 Web 产物，也无本包旧配置需清理时，不执行托管器生效操作。

<a id="troubleshooting"></a>
## 故障排查与能力边界

| 现象 | 检查与处理 |
| --- | --- |
| 找不到 web.profile | 默认只查最终 source 直属文件；使用显式路径或纠正 source。 |
| import 缺失/路径异常 | 按声明文件的目录解析评估后的完整路径；检查变量来源和目标文件，不要给路径添加引号。 |
| 同文件 server 混用 | 选择单后端或具名池；跨文件覆盖时才允许更换形式。 |
| 绑定无法解析 | 检查最终 .option PI、别名、请求类别与字面基路径。 |
| 路径未命中 | 检查请求类别、基路径段边界、别名选择及应用声明。 |
| 输入 Profile 被打进包 | 这是普通载荷收集行为；用 --exclude:*.profile 排除。 |
| 生成目标冲突 | 检查源载荷/目标别名是否占用 .web 下同一路径。 |
| least-requests 报能力错误 | Nginx 没有当前公共语义的准确映射，选择受支持算法。 |
| sticky/health_check 未知 | 检查目标 Nginx 的版本及模块；配置生成不安装模块。 |
| HTTPS 加载失败 | 区分入口证书/私钥与后端 CA/名称；检查目标路径和文件内容。 |
| 开启后端验证仍不能工作 | 验证不是“无条件信任”；需要有效 CA 信任和匹配名称。 |
| 公共头含 &#36; 报错 | 公共头是固定文本；动态 Nginx 表达式放原始 proxy_set_header。 |
| 原始指令解析错误 | 去掉末尾分号/行尾注释，检查引号和作用域；不允许块或点号嵌套。 |
| 链接存在但激活失败 | 主配置须实际包含该入口；检查 nginx -T 及 nginx.service。 |
| activation=0 后站点仍存在 | 关闭只跳过本次激活；仍交付的旧链接和已加载运行配置不会因此撤销。 |
| 升级后手工修改丢失 | 生成目录由包管理；把长期修改移到构建端 Profile。 |

构建错误尽量提供来源文件、段落、条目、行号和相关来源；原始覆盖也有来源提示。生成器验证模型、格式、作用域、静态冲突及已知能力，不在构建机检查证书内容、DNS、后端可达性或目标模块。

部署前可检查归档内容与生成配置，再在具备实际依赖的 Nginx 环境执行主配置校验。检查包内容不等于完成安装验证；运行状态、证书续期、DNS 变化、应用会话和业务健康由部署系统、托管器及应用负责。

<a id="iis"></a>
## IIS 扩展边界

当前选择 `--web:iis` 明确报未实现；以下仅说明公共语义的扩展边界，不是已经交付的功能：

- 站点、绑定、应用池及证书关联属于 IIS 服务器级配置，不能只靠一个应用 web.config 创建。参见 [IIS bindings](https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/sites/site/bindings/binding)。
- 本应用托管可由 ANCM 处理；显式远程后端和加权池需要 ARR/URL Rewrite 及服务器群，不能默认当作 ANCM。ARR 支持成员权重，但各调度、重试、会话和健康字段仍须逐项验证语义，不做近似替换。参见 [ARR 服务器群](https://learn.microsoft.com/en-us/iis/extensions/configuring-application-request-routing-arr/define-and-configure-an-application-request-routing-server-farm)。
- 正则路径需要适配 URL Rewrite 的大小写、顺序和相对路径输入；任意 Nginx 原始项不具备自动跨托管器转换能力。
- `.web/<hoster>/` 是交付和发现约定，不改变目标运行时的要求。ANCM 的 web.config 仍需部署到应用内容根，不能仅放在 .web/iis/ 就认为已配置 IIS。参见 [IIS web.config 位置](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#webconfig-file-location)。
- Windows 证书存储、IIS 资源所有权、安装事务及 MSI/WiX 适配需由相应实现处理，不能机械套用 Linux PEM 路径或 Nginx 生命周期。

## 容器交接文件

每份 .conf 都有来自同一最终指令模型的 .conf.template。应用连接 authority 使用 {{zongsoft:binding:BINDING_ID:ENDPOINT_ID}}，两个身份均引用根 JSON。例如普通配置 http://127.0.0.1:8069，在模板中为 http://{{zongsoft:binding:B:E}}；B/E 代表记录中的完整 SHA-256 身份，消费端填入所选连接 authority（包含原端口）。显式远程目标保持原义。

引号中的 {{zongsoft:file:BASE64}} 标记已知证书/信任文件，BASE64 是路径的 UTF-8 编码；./ 相对应用安装根，绝对路径保持原义。用户文本不得含保留标记。

安装根 .bindings 描述应用与 Nginx 的监听、站点入口、请求类别路由、引用与策略。tar、deb、rpm 均以相同的普通 UTF-8 JSON 载荷携带，不写入包头。配置、模板、证书及生命周期资产共同随包交付。

字段示例和来源见[文件规范](implementation.zh-Hans.md#binding-document)，拓扑和发布见 [Containerizer 契约](../../containerizer/README.zh-Hans.md#web-交接与端口发布)。生成阶段校验引用和可表达性，不代表现场 DNS、TLS、Nginx 语法能力或网络已验证。


原生 gRPC 当前不映射 cookie 会话保持和主动 HTTP 健康检查，遇到这些组合明确报告能力限制，不为 RPC 端点伪造 HTTP 探测。
