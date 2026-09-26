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

`--web` 把抽象的站点、绑定、路径和后端定义转换成所选 Web 托管器的配置。当前实现 Nginx；`iis` 是已识别但尚未实现的名称，选择它会报错。

一个应用包生成一份 `.web/nginx/<PackageName>.conf`，其中可包含多个站点、路径、应用专属后端池及健康判定块。它是供现有 Nginx 主配置的 `http` 上下文包含的片段，不是完整 `nginx.conf`；不含外层 `http { ... }`。不支持 `worker_processes`、`events` 等实例配置，也不允许任意原始 `http` 指令或块。

例如，应用和 Nginx 在同一台机器上，在源目录准备：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = ~
```

假设 `publish` 中已有 `Example.Web.dll` 及运行所需文件，以下单行命令可从 Windows 或 Linux 终端执行；`1.0.0` 为示例应用版本：

```shell
dotnet-pack deb --name:Example.Web --version:1.0.0 --platform:linux --architecture:x64 --framework:net10.0 --source:publish --output:../packages --daemon:example.web --listen:8069 --web:nginx --exclude:*.profile
```

生成的应用服务监听 `http://127.0.0.1:8069`，`server=~` 复用该地址。Nginx 的 IPv4/IPv6 HTTP 入口监听 80 端口。`bind` 控制 Nginx 入口，`--listen` 控制应用进程，二者互不替代。

如果应用和 Nginx 分属不同容器，改为使用部署网络中的应用服务名：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://app:8069
```

应用自身需监听其他容器可访问的接口，例如生成应用服务时使用 `--listen:http://0.0.0.0:8069`。`app` 的解析和容器网络由部署系统提供。容器构建时关闭 Web 激活，并从已知安装根读取配置，详见[安装流程](#installation)。

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
| `nginx:D:\config\web.profile` | 支持构建平台的绝对路径；只拆第一个冒号，保留盘符。 |
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

### 文本和段落

输入由 Zongsoft.Core 的 `Profile` 加载。建议使用 UTF-8 文本：

```ini
# 根级公共默认值
forwarded = true
server = http://app:8069

[api]
host = api.example.com
bind!legacy = http://*

[api hub.devices]
path = /hub/devices
websocket = true
```

- 第一部分 `api` 是站点标识，第二部分 `hub.devices` 是路径规则标识，以空格或 Tab 分开。点号是标识的一部分，不表示嵌套，也不推导 `/hub/devices`。
- 最多两个层级，`[api hub devices]` 非法。段落名不允许 `/`、`:`、`!`，不能写 `[api /hub/devices]`；路径写在 `path` 字段中。站点标识不自动成为域名、安装目录或服务名。
- 字段名、段落标识和具名条目的标识忽略大小写；值仍按相应字段解释，路径和原始参数不会统一转为小写。
- 根级公共字段位于第一个段落之前。根级默认值不创建站点，至少需要一个有效站点。
- `#`、`;` 开头的整行是注释；`#@import` 是导入指令。不要在值后添加行尾注释，它会成为值的一部分。
- 条目以第一个 `=` 分开名称和值；后续 `=` 保留，例如 `server!a=http://app weight=3`。
- Profile 不剥除值外的引号，不提供多行值或通用反斜线转义。公共字段通常不加引号；`header!X-Name='hello'` 的值包含两个单引号。
- `key` 和 `key=` 都表示显式声明空值；它们不等于未声明。是否允许空值由字段决定。
- 同一文件、同一段落内重复完整键会报错；跨导入的覆盖按读取顺序处理。后端单值与池的限制见[后端继承](#inheritance)。

### 字段位置速查

“根 → 站点 → 路径”表示子层缺失时继承，子层声明时按该字段的覆盖规则处理。

| 字段 | 可用层级 | 缺省或主要规则 |
| --- | --- | --- |
| `host` | 站点 | 可省略；逗号分隔请求主机名或 IP。 |
| `bind!名称` | 站点 | 至少有公共绑定或当前托管器原始监听；不隐式补端口入口。 |
| `certificate`、`certificate-key` | 站点 | HTTPS 的证书/私钥引用。 |
| `path` | 路径 | prefix/exact 缺失或空为 `/`；regex 必填。 |
| `match` | 路径 | `prefix`，也可 `exact`、`regex`；显式空值非法。 |
| `server` 或 `server!名称` | 根 → 站点 → 路径 | 目标整组继承/替换，全部缺失时为 `~`。 |
| `server-balance`、`server-failure-*`、`server-retry*`、`server-affinity*` | 根 → 站点 → 路径 | 策略逐字段继承，见[策略表](#policies)。 |
| `server-tls-*` | 根 → 站点 → 路径 | 仅由 HTTPS 后端消费，见[TLS 表](#tls)。 |
| `server-health`、`server-health-*` | 根 → 站点 → 路径 | 仅有效的非空探测路径启用检查，见[健康检查](#health)。 |
| `forwarded`、`websocket` | 根 → 站点 → 路径 | 分别默认 true、false。 |
| `header!名称`、`server-health-header!名称` | 根 → 站点 → 路径 | 两个独立的头集合，各自按名称合并。 |
| `nginx:...`、`iis:...` | 站点或路径 | 当前托管器原始设置，不能写在根级。 |

未知公共字段、错误层级、缺失具名后缀（如 `bind=...`、`header!=...`）均报错，不静默忽略。

### 导入顺序与路径

共享文件 `shared/common.profile`：

```ini
forwarded = true
header!X-Application = example-api

[api]
bind!legacy = http://*
server = http://shared:8069
```

宿主 `web.profile`：

```ini
#@import shared/common.profile

[api]
host = api.example.com
server = http://local:8069
```

最终使用 local 后端，保留共享的绑定和请求头。导入在指令出现处执行，通常把导入放在前面，把本地覆盖放在后面；本地写在导入前面时也可能被后续导入覆盖。

导入路径相对**声明该指令的文件**解析。`shared/common.profile` 再导入 `backend.profile` 时，目标为 `shared/backend.profile`。导入始终把目标文件合并到 Profile 根，不以当前段落作为目标前缀，也不自动重命名目标站点。

`#@import` 可用空白、Tab 或 `|` 分隔多个目标：

```ini
#@import shared/common.profile|shared/sites.profile
```

导入路径不执行打包变量展开，不支持引号包裹、通配符或含空格文件名；可改用没有空格的共享路径。顶层 `--web` 的终端引号能力不改变该规则。跨平台共享文件建议使用 `/` 分隔路径。

所有直接和递归导入目标都必须存在且可读。循环导入失败；默认最大深度为 64（包含顶层文件）。一个已完成的文件可再次导入，不等于递归循环。错误保留来源文件、段落、条目、行号及底层详情。

<a id="bindings"></a>
## 站点、主机名与绑定

`host` 表示请求中的主机名或 IP，`bind` 表示 Nginx 监听的本地地址：

```ini
[api]
host = api.example.com,192.0.2.10
bind!legacy = http://*:80
bind!secure = https://*:443
server = http://app:8069
```

`legacy`、`secure` 是可自定义的绑定组名称。HTTP 80、HTTPS 443 可省略：

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
bind!secure = https://*,https://[::]
server = http://app:8069
```

| 写法 | 含义 |
| --- | --- |
| `http://*`、`http://0.0.0.0:80` | IPv4 任意地址的 HTTP 80。 |
| `http://[::]` | IPv6 任意地址的 HTTP 80。 |
| `https://*`、`https://[::]:443` | 分别为 IPv4、IPv6 HTTPS 443。 |
| `http://127.0.0.1:8080` | 仅 IPv4 回环地址 8080。 |
| `https://[2001:db8::10]:8443` | 指定 IPv6 地址及非默认端口。 |

每个逗号分隔项都是完整地址，不能写 `http://*,[::]`。逗号两侧空白可省略，空元素（如尾随逗号）非法。端口范围为 1–65535。IPv6 必须有方括号；绑定不接受域名、路径、查询或片段。`*` 只表示 IPv4，不把 IPv6 绑定当成跨平台的双栈捷径。

绑定协议仅支持 http/https。WebSocket 使用路径上的 `websocket=true`，不写 `bind!x=ws://...` 或 `wss://...`。

`host` 不是网卡地址，`host=192.0.2.10` 不会使 Nginx 只监听该 IP。省略时不附加公共主机名限制，不根据站点名补出域名；仍受同一监听入口的 Nginx 虚拟主机选择规则影响。

host 的逗号两侧空白会去掉，同名值忽略大小写去重；显式空值、列表空元素和含空白的主机名报错。它只填写主机名或 IP，不带协议、路径和端口；监听端口放在 bind 中。更复杂的 Nginx server_name 表达式使用原始设置。

同名绑定组在导入后整体替换。例如共享文件的 `bind!legacy=http://*,http://[::]` 被本地 `bind!legacy=http://127.0.0.1:8080` 替换后，legacy 只剩一个监听。不同组展开后合并；同站点规范化后重复绑定只输出一次。

站点至少需要一个公共绑定或所选托管器的原始监听。`iis:...` 不满足 Nginx 的监听要求。跨站点同入口、同 Host 的静态冲突报错，不同 Host 可共享入口。系统中其他配置文件的冲突由目标环境的 Nginx 校验发现。

<a id="certificates"></a>
## 入口 HTTPS 与证书

这些字段配置客户端连接 Nginx 时使用的证书，与[后端 TLS](#tls)无关。每个站点的一组证书供该站点全部 HTTPS 绑定使用；HTTP 绑定不消费它们。

| 字段 | 未声明 | 显式空值 |
| --- | --- | --- |
| `certificate` | 使用实际安装根下 `.certificates/<PackageName>.pem`。 | 报错。 |
| `certificate-key` | 使用最终有效的 certificate 文件。 | 清除继承的独立私钥，改用最终 certificate 文件。 |

### 默认合并 PEM

```ini
[api]
host = api.example.com
bind!secure = https://*
server = http://app:8069
```

假设包名为 `zongsoft.web`，安装根为 `/opt/zongsoft/web`，生成的证书指令如下（站点配置摘录）：

```nginx
ssl_certificate "/opt/zongsoft/web/.certificates/zongsoft.web.pem";
ssl_certificate_key "/opt/zongsoft/web/.certificates/zongsoft.web.pem";
```

同一个 PEM 文件需包含证书链和相应私钥。工具不生成该文件、不转换 PFX，也不安装系统证书。多个 HTTPS 站点都省略字段时，共用这个默认文件；其证书需覆盖实际域名。

### 自定义合并文件与分离文件

自定义合并 PEM：

```ini
[api]
host = api.example.com
bind!secure = https://*
certificate = file:/etc/tls/api/combined.pem
server = http://app:8069
```

证书和私钥分离：

```ini
[api]
host = api.example.com
bind!secure = https://*
certificate = file:/etc/tls/api/fullchain.pem
certificate-key = file:/etc/tls/api/private.key
server = http://app:8069
```

也可只填写 `certificate-key`，使默认证书文件配合独立私钥使用。公共字段不要求成对填写；原始 Nginx 证书指令另有[成组覆盖规则](#native)。

### 导入后清除独立私钥

共享 `shared/site.profile`：

```ini
[api]
bind!secure = https://*
certificate = file:/etc/tls/shared/fullchain.pem
certificate-key = file:/etc/tls/shared/private.key
server = http://app:8069
```

宿主 `web.profile`：

```ini
#@import shared/site.profile

[api]
certificate = file:/etc/tls/api/combined.pem
certificate-key =
```

最终两个指令都指向 combined.pem。如果只改 certificate 而省略 certificate-key，共享的 private.key 仍会继承，不会自动清除。

`file:` 后是 Linux **目标环境绝对路径**，不按 Windows 构建机路径转换。构建时不读取、不要求存在、不自动加入载荷；需通过普通打包参数、安装钩子或部署挂载提供。缺文件、缺私钥、密钥不匹配等由启用激活时的 `nginx -t` 校验发现。

Tar 的默认证书路径随实际 `INSTALL_PATH` 重定位；`DESTDIR` 暂存前缀不会写入配置。显式绝对引用保持原样。关闭 Web 激活仍完成默认路径定位，跨容器复制配置时须使引用的证书路径在 Nginx 容器中成立。

<a id="routes"></a>
## 路径与匹配规则

### 默认路径与显式兜底

站点没有任何路径段落时，自动生成 `/` 前缀路由，使用站点的有效后端。只要声明了路径段落，就仅生成声明的路径，不额外生成兜底。

```ini
[api]
bind!legacy = http://*
server = http://app:8069

[api root]

[api devices]
path = /hub/devices
websocket = true
```

`[api root]` 的 path 和 match 都可省略，它明确保留根路径兜底。下面三种写法在 prefix/exact 模式下等价，分别选用一项即可：

```ini
[api root]
```

```ini
[api root]
path =
```

```ini
[api root]
path = /
```

规则标识 `root` 不具有特殊语义。不同标识默认成相同 match/path 时仍会报重复；改名字不能创建两个相同路由。

prefix/exact 的非空 path 必须以 `/` 开头，不能含查询字符串、片段或控制字符；查询参数属于请求，不写进路径匹配字段。工具不自动补斜杠或把段落名中的点号转换为路径。

### 三种匹配模式

`match` 值忽略大小写，缺失为 prefix，显式空值或未知值报错。

| match | 含义 | Nginx 结构 |
| --- | --- | --- |
| `prefix` | 区分大小写的前缀匹配。 | `location /api { ... }` |
| `exact` | 区分大小写的精确匹配。 | `location = /health { ... }` |
| `regex` | 忽略大小写的正则匹配。 | <code>location ~* "^/orders/[0-9]+&#36;" { ... }</code> |

前缀示例：

```ini
[api products]
path = /api
```

匹配 `/api`、`/api/orders`、`/apix`，不匹配 `/API`。如需路径段边界，可用 `/api/`，或使用下文的正则。尾随 `/` 有意义；Nginx 对某些以 `/` 结束的代理路径会重定向省略尾斜杠的请求，具体以其 [location 规则](https://nginx.org/en/docs/http/ngx_http_core_module.html#location)为准。

精确示例：

```ini
[api health]
path = /health
match = exact
```

匹配 `/health` 和 `/health?full=true`，不匹配 `/health/`、`/health/live` 或 `/Health`。查询字符串不参与路径匹配。

正则示例：

```ini
[api orders]
path = ^/orders/[0-9]+$
match = regex

[api segment]
path = ^/api(/|$)
match = regex

[api json]
path = ^/files/.*\.json$
match = regex
```

分别匹配数字订单号、api 路径段边界和 JSON 文件路径；`/Orders/123`、`/FILES/a.JSON` 也匹配。正则模式必须显式提供非空 path；不会自动增加 `^`、<code>&#36;</code>，也不会从 path 的外观猜测 match。`\.` 在 Profile 中直接写一个反斜线，不写 Markdown 转义 `\_`，不在 path 前加 Nginx 的 `~*`。后缀模式如 <code>\.json&#36;</code> 也合法，不强制以 `/` 开头。

### 优先级与重叠示例

在本工具生成的平级路由中，精确匹配优先；否则 Nginx 记住最长前缀，按声明顺序检查正则，采用第一个命中的正则；没有正则命中时才使用记住的前缀。

```ini
[api]
bind!legacy = http://*
server = http://app:8069

[api root]

[api resources]
path = /resources/

[api json]
path = \.json$
match = regex

[api health]
path = /health
match = exact
```

| 请求 | 选中的规则 |
| --- | --- |
| `/health?full=true` | 精确 health。 |
| `/resources/a.json` | 正则 json，优先于 resources 前缀。 |
| `/resources/a.txt` | 最长前缀 resources。 |
| `/other` | 根前缀 root。 |

多个正则重叠时，较窄规则应放在较宽规则前面：

```ini
[api order-number]
match = regex
path = ^/orders/[0-9]+$

[api order-any]
match = regex
path = ^/orders/.*$
```

若交换顺序，较宽规则会先匹配数字订单。导入保留段落首次出现顺序：共享文件为 A、B，本地覆盖 A 并新增 C，最终顺序仍为 A、B、C；覆盖 A 不会把它移到末尾。

相同 match/path 报错；工具不尝试证明两个不同正则是否等价或是否重叠。匹配由目标引擎执行，Nginx 的 URI 规范化也适用，不能把配置中保留的表达式理解为逐字节匹配原始请求行。工具不额外解码、拼接或改写用户表达式。

### 转发路径与可移植边界

公共 server 生成不带替换 URI 的代理目标：

```text
客户端：/api/orders/123?detail=true
后端：  http://app:8069/api/orders/123?detail=true
```

匹配前缀不会被删除；查询参数保留。正则匹配也不隐式做捕获替换或业务路径重写。此约定不承诺绕过托管器自身的 URI 处理。

公共配置不提供 `^~`、用户命名 location、嵌套 location 或捕获变量重写语法。正则不是文件通配符。Nginx 使用 PCRE/PCRE2；未来 IIS URL Rewrite 需适配其 ECMAScript 正则和相对路径输入，不能用 .NET Regex 校验代替目标引擎，也不能保证任意表达式跨引擎完全等价。详见 [IIS URL Rewrite](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/url-rewrite-module-configuration-reference)。

<a id="servers"></a>
## 后端地址、应用地址与权重

### 单后端与 URI

`server` 支持 HTTP/HTTPS 的协议、主机和可选端口。仅有一个尾随 `/` 时去掉它，不把它当作业务路径：

| 值 | 结果 |
| --- | --- |
| `http://app`、`http://app:80/` | 同一 HTTP 80 后端。 |
| `https://api.internal` | HTTPS 443。 |
| `http://192.0.2.20:8069` | 指定 IPv4 和端口。 |
| `https://[2001:db8::20]:8443` | 指定 IPv6 和端口。 |
| `http://app/api` | 非法：不允许后端业务路径。 |
| `http://app?q=1`、`http://app#part` | 非法：不允许查询和片段。 |
| `http://user:password@app` | 非法：不允许用户信息。 |
| `http://*:8069`、`http://0.0.0.0:8069`、`http://[::]:8069` | 非法：通配监听地址不是连接目标。 |

域名、IP、默认端口按有效地址规范化；不在构建机检测后端可达性。`server=` 非法，省略才继承，所有层都省略才使用 `~`。

### `~` 的推导条件

`~` 是“本次生成的应用服务的连接地址”，不是任意名称占位符：

| 本次应用服务情况 | `server=~` |
| --- | --- |
| 生成 systemd 服务，`--listen:8069` | `http://127.0.0.1:8069`。 |
| 生成服务，listen 为单个具体 HTTP/HTTPS 连接地址 | 使用该地址。 |
| 使用已有 `.service` 文件 | 报错；不解析其中 ExecStart 来猜测。 |
| `--daemon:none`，或未找到宿主而没有生成服务 | 报错。 |
| listen 缺失、多地址、通配地址 | 报错；不能唯一确定连接目标。 |

这里的服务文件是 systemd 的 `.service`，其中可以含 `ExecStart=dotnet ...`。用户已有服务文件可能采用脚本、环境变量或其他启动方式，因此它不提供自动推导依据。此时显式写 `server=http://127.0.0.1:8069` 即可；不是要求删除服务文件。

不从站点 bind 推导应用端口；不从应用默认端口猜测。某一路径被原始 `nginx:proxy_pass` 覆盖后不再消费其公共 server，但其他仍消费 `~` 的路径继续要求满足推导条件。

### 后端池与权重

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = round-robin
```

app1/app2 是成员标识，省略 weight 为 1。上例相对权重为 3:1；它不是每四个请求必然严格按固定顺序分配的承诺，运行状态和算法也会影响选择。

- 单成员池合法；成员写具体地址，不在 `server!名称` 中使用 `~`。
- weight 必须为正整数，0、负数、小数非法。公共模型不设随意的固定上限；超出目标托管器整数范围时报错，不截断。
- 同一池协议必须一致，端口可以不同；不能混合 HTTP 与 HTTPS。
- 规范化后相同的成员地址报错，即使成员名称不同；例如 `http://app` 与 `http://APP:80/`。
- 同一输入文件、同一层级不能混用 `server` 与 `server!名称`。跨导入文件的替换允许，见下一节。
- 只为有效引用生成应用专属 upstream，避免跨包名称冲突；它与 server 块同处现有 http include 中，不开放任意原始 upstream。

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
server = ~
```

`web.profile`：

```ini
#@import shared/site.profile

[api]
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
```

最终只有 app1/app2 池。共享 `~` 已被替换，不再要求 --listen 或本次生成应用服务。共享文件写具体单后端地址时也是同样行为。

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

最终只有 app3/app4，不是四个成员。即使本地只写 app3，也只剩 app3。若要修改 app1 权重并保留 app2，必须在本地重写整个池：

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

`certificate` 用于客户端 → Nginx；`server-tls-*` 用于 Nginx → HTTPS 后端。两段连接独立，入口 HTTPS 可以代理 HTTP 后端，入口 HTTP 也可以代理 HTTPS 后端。

| 字段 | 默认值 | 规则 |
| --- | --- | --- |
| `server-tls-verify` | false | true/false、1/0，文本忽略大小写；空值非法。 |
| `server-tls-trust` | 未指定 | 可选 `file:/目标绝对路径`，引用 PEM CA 信任证书；空值清除 Profile 继承值。 |
| `server-tls-name` | 自动推导 | 可选 TLS SNI/验证名称；空值清除继承名称并重新推导。 |

这些设置逐字段继承，对有效单后端或池内全部成员统一生效。HTTP 后端不消费 TLS 字段。

### 验证开关和信任来源

缺省 false 会显式生成 `proxy_ssl_verify off`，不被外层的开启设置意外改变。HTTPS 加密连接不等于验证了后端身份；设置 true 后，目标环境须提供可用的 CA 信任和证书名称。

```ini
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem

[api]
bind!legacy = http://*
server = https://api.internal:8443

[api root]

[api health]
path = /health
match = exact
server-tls-verify = false
```

api 根路径验证后端证书，health 路径显式关闭。internal-ca.pem 是信任 CA，不是入口私钥。

`server-tls-trust` 可不填，表示由目标 Nginx 的有效外层配置提供信任来源，不表示“信任所有证书”，也不保证自动采用系统 CA。若开启验证但最终没有可用的 `proxy_ssl_trusted_certificate`，Nginx 校验会失败。打包器不在构建机寻找 CA 文件。参见 [Nginx 后端证书验证](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_ssl_verify)。

例如根级使用内部 CA，而某一路径需清除该引用：

```ini
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem

[api]
bind!legacy = http://*
server = https://api.internal:8443

[api root]

[api external]
path = /external
server = https://external.example.com
server-tls-trust =
```

external 不再输出内部 CA 引用，仍保持验证开启，需外层环境提供适用信任。空值不会重新继承 Profile 的内部 CA，也不会关闭验证。

### SNI 与验证名称

自动推导只依据最终后端 DNS 名称，不使用站点 host、站点/成员标识或生成的 upstream 名称：

| 有效后端 | 未填写或清空 server-tls-name |
| --- | --- |
| 单个 DNS 后端，或只有一个 DNS 成员的池 | 使用该 DNS 名称。 |
| 池内全部成员是同一个 DNS 名称，端口不同 | 使用共同 DNS 名称。 |
| IP 地址后端，或不同 DNS 名称的池 | 不自动选名称；验证关闭时不发送 SNI，验证开启时报错并要求显式名称。 |

同域名不同端口可直接推导：

```ini
[api]
bind!legacy = http://*
server!a = https://api.internal:8443
server!b = https://api.internal:9443
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
```

使用两个 IP 连接但共同验证 api.internal：

```ini
[api]
bind!legacy = http://*
server!a = https://192.0.2.20:8443
server!b = https://192.0.2.21:8443
server-tls-name = api.internal
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
```

显式名称启用 SNI；当 verify=true 时也用于验证，但不会自行开启验证、改变连接地址或改写 HTTP Host。各后端证书需满足这个共同名称。若后端不需要 SNI 且不要求验证，IP 池可以省略全部 TLS 字段。

server-tls-name 填一个主机名或 IP 字面量，不填写完整 URL、端口或路径；显式非法名称报错。更换后端不会自动清除继承的名称，需要恢复推导时显式留空。

覆盖继承名称时，空值可恢复新后端的推导：

```ini
server-tls-name = api.internal

[api]
bind!legacy = http://*
server = https://192.0.2.20:8443

[api root]

[api external]
path = /external
server = https://external.example.com
server-tls-name =
```

根路径使用 api.internal，external 使用 external.example.com，不继续发送继承的 api.internal。

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

### 语法、作用域和空值

`hoster:指令=参数` 表示原始叶子指令；`!` 后的内容是该指令的第一个参数：

```ini
[api]
nginx:listen!80
nginx:listen![::]:80
nginx:server_name = api.example.com
server = http://app:8069

[api root]
nginx:proxy_set_header!Upgrade = $http_upgrade
nginx:proxy_set_header!Connection = 'upgrade'
nginx:proxy_set_header!Host = $host
nginx:proxy_set_header!X-Real-IP = $remote_addr
nginx:proxy_set_header!X-Forwarded-For = $proxy_add_x_forwarded_for
nginx:proxy_set_header!X-Forwarded-Proto = $scheme
nginx:proxy_read_timeout = 300s
```

`nginx:listen!80` 和 `nginx:listen!80=` 等价，生成 `listen 80;`。不要在同一段落同时写两行等价键。`nginx:listen!443=ssl` 生成 `listen 443 ssl;`；如需自动完整 WebSocket 配置，优先使用 websocket 字段。

公共 `bind!legacy`、`server!app1` 的后缀是标识；原始 `nginx:listen!80` 的后缀则是实际参数，二者语义不同。`nginx:proxy_set_header!X-Optional` 或同项空值生成 `proxy_set_header X-Optional "";`，表示不发送该头，不会生成缺参数指令。

站点原始项进入 server 上下文，路径原始项进入 location 上下文。不把任意站点原始项复制到每个路径，只有请求头等明确规则由公共模型处理。不能在根级写原始项；不支持 `nginx:http.server.listen` 这类点号层级。

工具限制已知指令的上下文、单值/重复参数及结构。未知叶子指令可以交给目标 Nginx/模块校验，并不保证它在目标环境可用。禁止 worker_processes、events 等实例配置，也禁止用户定义 http、server、location、upstream、match、map、if、limit_except 等块注入；站点和路径结构由配置器生成。

例如 listen/server_name 只能放在站点，proxy_pass 只能放在路径；proxy_pass、ssl_certificate 等已知单值指令不能借不同的 `!` 后缀重复声明或添加多余参数。普通原始指令的空值表示没有额外参数，不像公共布尔值那样自动取 true；是否允许零参数仍由该指令的语法决定。

原始参数可保留 Nginx 引号和表达式，但**不写末尾分号**。未闭合引号、换行、NUL、未引用的分号、注释符或块符号报错。Profile 行尾注释也不会被剥除。不要把完整 Nginx 配置粘贴到字段值中。

### 原始设置覆盖公共设置

| 原始项 | 覆盖范围 |
| --- | --- |
| 站点任意 `nginx:listen`（含具名项） | 替换全部 bind 组生成的监听集合。 |
| 站点 `nginx:server_name` | 替换完整 host 列表。 |
| 路径 `nginx:proxy_pass` | 替换有效单后端/后端池目标。 |
| 站点 `nginx:ssl_certificate` 和 `nginx:ssl_certificate_key` | 两项成组替换公共证书及私钥，必须完整提供。 |

监听替换例子：

```ini
[api]
host = api.example.com
bind!legacy = http://*
bind!secure = https://*
nginx:listen!8080
server = http://app:8069
```

最终仅监听 8080，不再生成 80/443。原始 server_name 同样不与公共 host 叠加。

原始代理替换默认应用地址：

```ini
[api]
bind!legacy = http://*
server = ~

[api root]
nginx:proxy_pass = http://remote:8069
```

此路径不再推导 ~，不要求 --listen；如果另加未覆盖的路径，则那个路径仍需推导。原始 proxy_pass 的 URI 改写属于 Nginx 语义；正则路由不允许静态 proxy_pass 带 URI 部分，公共 server 本身始终禁止 URI 替换。

原始证书成组替换：

```ini
[api]
bind!secure = https://*
certificate = file:/unused/public.pem
nginx:ssl_certificate = /etc/tls/native/fullchain.pem
nginx:ssl_certificate_key = /etc/tls/native/private.key
server = http://app:8069
```

最终只用原始两项。仅写其中一项会报错，即使公共字段可提供另一半也不补全；配套完整性在导入合并后判定。

被完全覆盖且无其他使用处的公共值不展开变量、不检查相关依赖、不产生默认引用；报告覆盖来源。结构错误仍然检查。原始指令可改变公共语义，使用者需确认生成结果；不是绕过字段结构检查的方式。

<a id="variables"></a>
## 变量与转义

字段值支持 <code>&#36;(name)</code> 和 `%name%`，使用本次命令固定后的变量视图；来源依次包括默认值、环境、从根到最终 source 的 `.env` 以及显式选项。源目录和包身份遵循通用打包规则，不能用 Web 输入反向改变。

```ini
[api]
host = $(Environment).api.example.com
bind!legacy = http://*
server = http://%BackendHost%:8069
header!X-Environment = $(Environment)
```

只有有效值需要求值。缺失的有效变量报错；被替换的无效后端不再求值。已知但未选择的 `iis:...` 值不求值；拼错前缀如 `ngnix:...` 则报错。

段落名、公共字段名、成员标识及 import 路径不展开。原始 `!` 后缀代表实际参数，会按原始参数求值；不要据此在公共键名中使用变量。

| 写法 | 行为 |
| --- | --- |
| <code>&#36;(name)</code>、`%name%` | 打包变量。 |
| <code>&#36;&#36;(name)</code>、`%%name%%` | 转义为字面的 <code>&#36;(name)</code>、`%name%`，递归展开也不再求值。 |
| <code>&#36;host</code>、<code>&#36;{host}</code>、<code>&#36;remote_addr</code> | 不是这两类打包变量；在原始设置中保留给 Nginx。 |

Profile 引号不会保护变量免于展开。变量转义只解决求值，不保证目标配置能表示该字面值：例如公共固定头含 <code>&#36;</code> 时，当前 Nginx 输出器无法安全保持字面语义，会明确报错；不能把 <code>header!X-Template=&#36;&#36;(name)</code> 当作有效 Nginx 固定头示例。`header!X-Template=%%Environment%%` 则可表达字面 `%Environment%`。

按环境选择顶层文件可在命令参数中展开。Bash/PowerShell 中用单引号避免终端先解释 <code>&#36;()</code>：

```shell
dotnet-pack deb '--web:nginx:web.$(Environment).profile'
```

Windows cmd 可用双引号。被选文件再用固定的相对 import 共享内容；不写 <code>#@import web.&#36;(Environment).profile</code>。

构建期 `#@import` 与运行期 `nginx:include` 不同：前者读取 Profile，后者只是输出 Nginx 引用，不自动复制其目标文件。

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

安装完成后，容器化工具在已知安装根的 `.web/nginx/*.conf` 查找直接子文件，读取并交付到 Nginx 镜像或挂载位置。不要依赖 /etc/nginx/conf.d 的链接存在；部署端负责地址、证书及 include 引用在 Nginx 容器中可用。不要在卸载后再收集，因为卸载会删除 .web。

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
| import 缺失/路径异常 | 按声明文件的目录解析；不能给 import 加变量、引号或空格路径。 |
| 同文件 server 混用 | 选择单后端或具名池；跨文件覆盖时才允许更换形式。 |
| ~ 无法解析 | 检查是否本次生成服务、listen 是否唯一可连接；也可显式填写 server。 |
| 路径未命中 | 检查 match、大小写、尾斜杠、正则顺序及是否显式保留根兜底。 |
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
