# 统一绑定描述与交付元数据方案

> 本文是绑定重构的当前设计依据，替换此前草案；不记录方案演进过程。
> 已确认的规则是实现依据；标为“建议”或“推荐”的内容是实施方向，不表示已经定稿。剩余决策集中在第 12 节，开始相关实现前逐项确认。
> 已确定：XML 处理指令名为 `binding`，配置路径使用 `/`，包级结果名称为 `.bindings`。
> binding 的 path 使用精确路径选择单项，使用末尾 `/*` 选择直接子项。
> 字段引用采用普通字段名或 `字段名[设置键]`；后者使用 Core Settings 解析指定字段，再读取其中的键。
> 字段引用以 `/` 开头时从本次配置根读取，否则相对于当前候选项读取；protocols 引用与 filter 使用同一规则，范围限定在本次文件及其显式 Import 结果内。template 的配置视图见第 4.5 节。
> `template` 统一按 Core 模板求值；option 表示当前候选项，`${option['/Network/Address']}` 显式从本次配置根读取；固定 Settings 键采用字符串索引，如 `${option.Settings['port']}`。
> `protocols` 是可选属性，补充 template 地址未表达的协议能力；默认使用常量，读取配置时仅使用 `@` 前缀显式标记引用；去除 `@` 后沿用字段引用规则。
> 协议列表采用逗号分隔，HTTP 类名称使用 `http`、`http1`、`http2`、`http3`，支持多项；http 表示未声明具体版本的 HTTP，与具体版本同时出现时移除泛称 http，见第 4.6 节。
> 显式 HTTP/1、HTTP/2 能力对应 TCP，HTTP/3 对应 UDP；混合声明保留两种传输需求及完整协议集合，不增加 transport 属性，不自动发布宿主端口。
> 首版支持 http、https、tcp、udp、opc.tcp 网络监听；Unix domain socket 及未识别的 scheme 在 binding 提取时明确报错，不静默跳过或猜测为 TCP。
> URL scheme 与协议集合存在已知冲突时，整条绑定声明报错，不自动修改 URL 或丢弃冲突协议；required 不豁免此类错误。
> 多地址仅在显式指定 `separators` 时展开；它是模板求值结果的分隔字符集合，省略时按单个完整 URL 处理。展开后的地址使用当前候选项的同一组 protocols，分别校验。
> PI 位置及收集规则沿用 Core；binding 的 path 始终从本次读取结果的配置根定位。
> 每个绑定以匹配项的完整配置路径作为默认别名；name 可选，只增加简短别名：精确 path 增加 name，末尾 `/*` 的子项增加 name 和 `name/子项键`。按内容去重后合并全部别名。server 直接填写名称或别名，无需 `~` 前缀；省略与空值等效，最终无有效后端配置时自动选择唯一合适目标。
> 筛选属性名称为 `filter`，采用 deployer 的过滤语法；Settings 仅用于已声明的设置字段取值，不作为筛选表达式语法。
> 启用条件统一由 `filter` 表达，不设独立 `enabled` 参数，也不隐式识别名为 Enabled 的配置字段。
> `required` 默认为 `false`；设为 `true` 时，该声明必须在全局去重前提取出至少一个有效绑定。允许零结果不豁免解析或校验错误。

## 阅读导航

- [目标与已确认约定](#goals)
- [现有能力与复用边界](#reuse)
- [入包文件范围与逐文件提取](#configuration)
- [绑定声明与提取](#instructions)
- [统一结果模型](#model)
- [Web 代理与应用绑定](#web)
- [包格式与元数据承载](#packages)
- [Containerizer 消费与发布](#containers)
- [实际服务器的配置适配](#servers)
- [实施顺序与改动位置](#implementation)
- [验收与开发约束](#acceptance)
- [待确认问题与讨论顺序](#decisions)

## 续接实施说明

本文可以独立用于下一台电脑的设计续接，状态如下：

| 状态 | 范围 |
| --- | --- |
| 已完成的前置工作 | 工具改为只读取 source 直属 `.env` 及显式导入；Core Settings 解析修复已在 framework 仓库完成 |
| 已确认的业务规则 | 逐文件提取、按内容去重、声明参数、模板配置视图、协议范围、默认路径别名及 server 三种选择规则，见第 1～6 节中已确认的约定 |
| 仅完成可行性验证 | template 配置视图经过临时隔离原型验证；原型不是仓库内的正式实现或回归测试，换机后应按第 4.5、11 节建立正式测试 |
| 尚未实施 | 绑定提取器、统一结果模型、三格式读写、Web/Containerizer 消费、旧 Listen 链移除及宿主 binding 声明迁移 |
| 尚待确认 | 第 12 节列出的代理适配、服务器配置适配、结果规范化与承载、发布及探测、Import 文件范围 |

续接时先同步 tools，并按所需工作同步 framework、hosting 和 guidelines；本地 Debug 引用的相邻目录布局以项目文件为准，Automao 宿主另从其所属仓库同步。本文跨仓库引用使用 GitHub 链接，不要求保持当前电脑的盘符。工具和 Core 依赖版本从项目及 Directory.Packages.props 读取，不能仅复制当前电脑的编译输出作为实现前提。

下一步从第 12 节第 1 项继续确认代理连接目标，已确认的 server 写法和别名规则不再重新设计。随后按第 10 节分阶段实现；涉及尚待确认的行为，先补齐该项契约，不把建议默认为已批准的规则，也不扩大为工具对应用运行时的自动推断。

<a id="goals"></a>

## 1. 目标与已确认约定

目标是解析最终进入安装包的所有 `*.option` 文件，按其中的 binding 声明提取绑定信息，将结果汇总去重，生成一份供工具消费的包级元数据。应用的配置选择由部署工程师负责，工具保持通用性。

```text
.env 与显式工具变量
        │
        ▼
deployer 展开 *.option.template
        │
        ▼
Packager 确定最终入包的全部 *.option
        │
        ▼
逐文件由 Core 读取配置、Import 与 binding
        │
        ▼
按声明提取 → 汇总结果并去重 → 应用绑定
        │                         │
        │                         ▼
        │                  web.profile 选择后端
        │                         │
        │                         ▼
        │                  最终 Nginx 配置与绑定
        └────────────┬────────────┘
                     ▼
              统一包级绑定元数据
                     │
                     ▼
          Containerizer 规划代理、发布和探测
```

以下为已确认的总体边界：

- 实际地址、端口和启用条件只维护在运行代码确实消费的配置中。binding 引用这些字段，不重复保存一份监听值，也不猜测服务器或第三方库默认值。
- Kestrel 等服务器自行根据应用配置建立监听；工具提取 binding 只生成交付元数据，不替应用打开、关闭或重新配置监听。重构后不再通过 --listen 或生成的 --urls 向应用注入监听地址。
- `*.option` 是运行配置；`.env` 是工具变量；`.bindings` 是生成的交付元数据。应用运行时不读取 `.bindings`。
- 源端不要求手写 `.bindings` 或 `.bindings.template`。不恢复已撤回的 `port!` 临时语法，不建立旧格式或旧选项兼容层。
- `web.profile` 保留，负责代理入口、域名、路径、证书和代理行为。应用监听与代理入口分别描述。
- 一个安装包提供一份逻辑绑定结果，包含应用和代理等不同执行目标；不按业务插件拆成多份结果。
- `.bindings` 不进入应用安装目录或应用运行镜像。PI 可保留在 `.option` 中，运行时默认不解释 binding 的业务语义。
- 绑定功能只依赖 Zongsoft.Core 和工具内部共享代码，不依赖 Plugins 或其它应用框架库，不装载应用程序集、不解析插件树、不推导应用的配置加载顺序。
- Core 已有的实现规范直接遵循，包括 PI 位置及属性解析、XML 配置映射、Import、Settings 词法及比较规则。工具只定义 binding 的业务语义；仅在确认 Core 存在问题或能力不足时，定位具体实现与案例，再提出修复或扩展。
- 部署工程师通过 `.deploy` 确定交付内容，Packager 按最终入包条目确定解析范围；不要求额外的配置入口文件。
- 每个入包 option 都参与解析；声明哪些 binding 就处理哪些，全部绑定结果去重后形成应用绑定集。不从文件名、端口惯例或插件类型猜测未声明的绑定。
- 按绑定内容去重，匹配项的完整配置路径及可选名称作为别名保留。内容相同的结果合并为一项并合并全部别名，别名和声明来源不参与绑定内容的等价比较。
- 方案、环境和网络由本次部署选择确定，不固化客户目录树或额外优先级。

`.env` 的前置调整已经完成：工具只加载工作源目录直属的 `.env`，其它变量文件通过 `#@import` 显式组织。有 `--source` 时使用最终 source，否则使用调用目录；不搜索祖先目录。`--source` 自身相对于调用目录解析，其管理的相对输入输出基于最终 source，配置内部引用仍基于声明文件。模板采用完整扩展名 `*.template`。

当前绑定重构尚未实现。Packager 仍使用 `Listen` 与 `.web/nginx/.bindings`，Containerizer 仍消费这两套信息。下文说明替换它们的目标和实施位置，不表示新命令或新格式已经可用。

<a id="reuse"></a>

## 2. 现有能力与复用边界

### 2.1 Core 已经提供的能力

| 工作 | 直接复用 | 工具保留的职责 |
| --- | --- | --- |
| XML 配置键、命名集合和 option 路径映射 | `AddOptionFile`、`XmlConfigurationProvider` | 遍历最终入包的 option 条目 |
| 递归导入、路径基准、循环与深度检查 | Core XML Import | 提供与交付路径一致的文件访问视图 |
| 有效指令及来源收集 | 文件/流提供程序的 `Instructions` | 在该文件及其导入完成后处理 binding |
| PI 属性词法、实体解码、重复属性 | `XmlConfigurationInstruction.GetAttributes()` | 参数白名单、必需项和组合校验 |
| Settings 文本 | `Settings.Parse()` 及原始字典访问 | 取出声明引用的显式字段并转换 |
| 工具变量、模板和 Profile 导入 | `Variables`、`TemplateEvaluator`、`Profile`、共享 Utility | 按现有工具边界组织输入 |
| 配置查询 | `IConfiguration` 及子节访问 | 在本次文件读取的结果中定位声明引用的字段 |

详细契约见 [XML 配置](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/docs/xml-configuration.zh-Hans.md)、[Settings](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/docs/settings.zh-Hans.md)、[Profile](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/docs/profiles.zh-Hans.md)和[模板](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/docs/expressions.zh-Hans.md)。本文不复制这些解析器的完整规范。

已经由 Core 定义的行为不再作为工具方案的独立待确认项，也不另造一套大小写、转义、容错或节点位置规则。新增 binding 参数、集合选择与结果汇总等业务规则不属于 Core 现有解析契约，应与基础库能力区分说明。

### 2.2 binding 不需要注册 Core 指令处理器

对每个入包 option 分别使用 Core 提供程序读取。在该文件及其显式 Import 完成后，遍历提供程序的 `Instructions`，解析 binding，并从本次读取的配置结果取值。随后汇总所有文件的提取结果。

指令执行回调的 `Configuration` 只含当前文件已经读到的内容，不包含后续配置和导入，因此不在遇到 PI 时立即提取值。binding 不写入配置，也不需要改变 Core 的全局处理器注册表。不同入包文件之间不构建全局合并配置。

`XmlStreamConfigurationProvider.Read(Stream, ...)` 只解析当前流，不执行或跟随 Import。需要支持显式导入时使用会执行指令的文件或流提供程序。单个提供程序已经提供有效导入链的指令及来源快照；工具负责绑定语义与结果汇总，不复制导入解析器。

`XmlConfigurationInstruction.Path` 是实际 XML 父元素路径，不是映射后的配置路径。binding 的 `path` 参数必须显式给出，不通过“紧跟某个节点”推断含义。

### 2.3 Settings 的当前边界

普通设置与连接设置共用 Core 解析器。工具提取字符串中的参数时直接使用普通 `Settings`，通过 `TryGetValue`、成员判断和枚举读取显式内容，不从驱动描述器补默认值。

以等号开头的值须加引号，例如 `password='=abc=='`。引号内使用反斜杠转义；未闭合引号、未完成转义及末尾非法字符现在会正确失败。索引器会删除空值并裁剪非空值，生成设置文本时需要保留显式空值或首尾空白则使用字典 `Add`。

Containerizer 的 [ServiceSettings](containerizer/src/ServiceSettings.cs) 已统一复用 Core 的读写语法。绑定提取不再引入正则拆分或 ADO.NET 引号规则。

模板插值、Settings 取值与条件判断是不同职责。现有 `TemplateEvaluator` 不承担过滤表达式的逻辑组合；绑定筛选沿用 deployer 的过滤语法，Settings 继续负责 `字段名[设置键]` 的值解析，见第 4 节。

<a id="configuration"></a>

## 3. 入包文件范围与逐文件提取

### 3.1 已确认的职责边界

部署工程师通过 `.deploy` 定义应用交付内容。Packager 在完成输入选择、排除及目标路径映射后，以最终入包的全部 `*.option` 为解析范围，不另行要求配置入口、入口清单或 Plugins 提供离线装配接口。

工具不关心应用运行时加载哪些 option、插件是否被装载、host/site/环境后缀的含义及运行时覆盖顺序，也不为绑定重构要求迁移宿主 Program 中的参数。进入打包范围的 option 有 binding 就按声明处理，没有 binding 就不自行推导监听。

部署工程师负责让声明结果符合应用的部署用途。应用通过配置以外的代码、环境变量或启动参数调整监听时，由部署方案保持配置与声明一致；工具不模拟应用启动，也不尝试证明声明覆盖了运行时所有动态行为。

### 3.2 已确认的逐文件求值规则

对每个入包 option 独立执行：用 Core 加载该文件及其显式 Import，取得本次读取的配置与指令；处理有效 binding 声明，得到该文件贡献的绑定结果。不同文件的结果最后合并去重，文件枚举顺序不决定覆盖优先级。

例如两个入包文件分别声明同一路径的绑定，一个读出 `http://*:8069`，另一个读出 `http://*:8070`，两项都进入结果集；工具不根据 `production`、`default` 等文件名猜测谁覆盖谁。若第二个文件没有 binding，也没有导入带 binding 的配置，就不会借用第一个文件的声明来生成或修改结果。

显式 Import 内部仍遵循 Core 的声明位置和覆盖规则。例如 B 导入 A，并将 A 中的端口改为 8070，B 的提取应读取 8070。若 A 自身也是入包 option，且带有能够独立读出 8069 的 binding，A 的独立提取仍会贡献 8069；8069 与 8070 不会通过去重相互抵消。若只希望导出最终值，可把 binding 放在最终消费配置的文件，共享基础文件只提供配置值。

这不要求增加统一入口文件。每个入包 option 都是自然的解析单元，Import 仅表达文件主动声明的局部依赖。逐文件求值规则已确认，导入指令作用域及重复导入沿用 Core；实现时为上述覆盖与独立入包场景补充回归，不重新定义导入顺序。

### 3.3 已确认的结果去重与别名规则

不能先按 PI 文本、规则名或来源文件去重。同一条 `name="kestrel" path="Kestrel/Endpoints/*"` 声明，在不同文件中可能提取出不同端点；同一个被导入的声明也可能因引用文件的配置不同而产生不同值。

按规范化后的绑定内容判断等价：内容相同的结果合并为一项，将它们的默认路径别名和可选名称别名合并为该绑定的别名集合；重复别名不重复保存。别名与声明来源不参与内容等价比较，来源仅用于诊断。端口号相同不足以证明重复，还需区分执行目标、监听地址、传输协议，以及应用协议、HTTP 能力、URL 路径等消费者需要的属性；这些属性不同的结果不能作为重复项删除。

例如以下三项属于同一个应用执行目标，未列出的属性均相同：

| 来源 | 名称 | 监听地址 | HTTP 能力 |
| --- | --- | --- | --- |
| A.option | web | `http://*:8069` | Http1 |
| B.option | api | `http://*:8069` | Http1 |
| C.option | management | `http://*:8070` | Http1 |

最终生成两个绑定：除各自的默认路径别名外，8069 对应的绑定保留 web、api 两个名称别名；8070 对应的绑定保留 management 名称别名。若 A、B 的地址和端口相同，但 HTTP 能力或有意义的路径不同，则不能按此例合并，也不能自行拼成一种新的协议能力。

不同文件使用相同名称不直接构成错误，也不建立覆盖关系；绑定内容不同时仍分别保留。同一别名指向多个绑定时，引用选择按第 6.2 节处理，筛选后仍有歧义则报错；地址和别名的精确规范化规则随结果格式讨论确定。重复结果的合并和无效声明的报错分开处理，不能用去重掩盖解析失败。

### 3.4 使用最终交付路径

推荐以 `Package.Entries` 建立只读文件视图：按最终入包路径判断哪些条目为 option，导入也按交付后的逻辑路径访问，避免读取已排除内容或按重命名前路径解析引用。`*.option.template` 先由 deployer 展开为 option，源模板不作为 option 解析。

Core 已支持虚拟 `IFileProvider`，工具只需提供流、目录查询及来源诊断，制作时不启用文件监听。虚拟文件不能向 Core 暴露制作机的物理路径作为导入基准。

建议首版导入限定在最终交付文件视图中，沿用 Core 对虚拟相对路径、循环和越界的检查；缺失的可选导入按 Core 规则处理。包外配置及绝对路径导入是否支持另行确认，不静默读取制作机的同名文件，也不把此问题扩展成宿主运行环境推导。

<a id="instructions"></a>

## 4. 绑定声明与提取

本节为最小语法方案。已采用指令名 `binding`、配置路径分隔符 `/`、精确路径选择单项及末尾 `/*` 选择直接子项、普通字段引用与 `字段名[设置键]` 取值语法。字段引用以 `/` 开头时从配置根读取，否则相对于当前候选项读取。PI 位置与收集遵循 Core，path 始终从配置根定位。筛选属性为 `filter`，语法采用 deployer 的过滤条件。监听地址通过 template 统一求值，option 成员导航及字符串路径/设置键索引规则见第 4.5 节；尚待确定的协议来源适配及结果规范化细节见第 12 节，不再预留另一套 binding 参数。

### 4.1 已确认的参数

| 参数 | 示例 | 含义 |
| --- | --- | --- |
| `name` | `kestrel` | 可选，只增加简短别名；精确 path 增加 name，集合子项增加 name 及 `name/子项键`，均保留完整配置路径默认别名 |
| `path` | `Kestrel/Endpoints/http` 或 `Kestrel/Endpoints/*` | 必填，从本次文件及导入读取结果的配置根选择单项或直接子项，非 XPath |
| `template` | `${option.Url}`、`${option.Settings['urls']}` | 必填，统一以 Core 模板生成完整监听地址，见第 4.5 节；允许在明确声明分隔符后展开多值 |
| `protocols` | `http1,http2` 或 `@Protocols` | 可选的协议能力补充，列表以逗号分隔，默认常量，以 `@` 前缀显式引用配置字段；省略不补入服务器默认协议 |
| `filter` | `Driver:prometheus` | 可选，按 deployer 过滤语法选择候选项；省略保留全部候选项 |
| `separators` | `,;\|` | 可选的地址分隔字符集合，仅切分 template 求值后的结果；省略时按单个完整 URL 处理，不切分外层 Settings；协议列表固定使用逗号 |
| `required` | `false`（默认）或 `true` | 默认允许零结果；true 要求该声明在全局去重前提取出至少一个有效绑定，不豁免解析或校验错误 |

protocols 默认按常量解释，例如 `protocols="http1,http2"`。读取配置时仅采用 `@` 前缀显式标记引用，例如 `protocols="@Protocols"`。去除前缀后，引用内容按已确定的相对路径、根路径及 Settings 取键规则处理，再解析取得的协议列表。协议值无法识别时不自动改为配置查找。监听地址统一由 template 模板生成，成员导航规则见第 4.5 节，filter 保持既有过滤语法。

| protocols 的写法 | 含义 |
| --- | --- |
| `http1` | 单个协议名称 |
| `http1,http2` | 同时声明 HTTP/1 和 HTTP/2 |
| `@Protocols` | 当前候选项的 Protocols 字段 |
| `@/Server/Protocols` | 本次配置根下的 Server/Protocols 字段 |
| `@Settings[protocols]` | 当前候选项的 Settings 字段，经 Core Settings 解析后读取 protocols 键 |
| `@/Server/Settings[protocols]` | 本次配置根下的 Server/Settings 字段，经 Core Settings 解析后读取 protocols 键 |

Core 的 PI 属性解析只提供属性文本；TemplateEvaluator 的变量引用采用 `${...}`，不会把 `@...` 自动解释为上述配置引用。`@` 引用标记属于 binding 的业务语法，识别后继续复用 Core 的配置路径转换、查询与 Settings 解析。template 的求值复用 Core 模板，见第 4.5 节，不自建插值解析器或引入脚本求值。

path 的末尾 `/*` 表示枚举前面路径的直接子项。去掉该后缀的精确路径只选择自身，不因节点具有子项而自动枚举。字段引用逐候选项求值，引用内容以 `/` 开头时从本次配置根读取，否则相对于该候选项读取；protocols 为引用时先去除 `@` 前缀。

| path | 选择范围 |
| --- | --- |
| `Kestrel/Endpoints/http` | 只选择 http 项 |
| `Kestrel/Endpoints/*` | 分别选择 http、telemetry 等直接子项 |
| `Kestrel/Endpoints` | 只选择 Endpoints 节点自身，不自动深入各端点 |

末尾 `/*` 不表示递归搜索或部分名称匹配。首版只支持精确路径与这一后缀，不引入 `**`、`http*` 或中间路径段的通配规则。Core 的 `ConfigurationUtility.GetConfigurationPath` 负责路径分隔符转换，不负责展开 `*`；工具识别末尾后缀后，用 `GetSection` 定位父节点并调用 `GetChildren()`，继续复用既有配置查询能力。

首版支持单对象、直接子项、普通字段和一次 Settings 取键，筛选采用 deployer 已有的存在判断、候选值匹配、否定及逻辑组合语法。不增加任意代码、方法调用、XPath 或另一种条件表达式语言。

字段引用内容的语法已确认：以 `/` 开头时从本次文件及其显式 Import 形成的配置根读取，否则相对于当前候选项读取。这套规则用于 protocols 显式引用中去除标记后的内容，以及 filter 中的字段引用。template 的成员与索引语法遵循第 4.5 节的已确认规则，两者保持相同的配置读取边界。protocols 的常量不参与配置查询。binding 的 path 始终从配置根定位，不受字段引用的相对起点规则影响。

| 引用 | 取值步骤 |
| --- | --- |
| `Url` | 直接读取当前项的 Url 字段 |
| `Settings[urls]` | 读取当前项的 Settings 字段，用 Core Settings 解析，再取 urls 键 |
| `Connection[port]` | 读取当前项的 Connection 字段，用 Core Settings 解析，再取 port 键 |
| `/Server/Protocols` | 从本次配置根读取 Server/Protocols 字段 |
| `/Server/Settings[urls]` | 从本次配置根读取 Server/Settings 字段，用 Core Settings 解析，再取 urls 键 |

对 protocols 和 filter 的字段引用，方括号前是配置字段路径，不限定字段名为 Settings；方括号内是该字段所含设置文本的键。先按前导 `/` 确定引用起点并读取字段，再用 Core Settings 解析该字段并取键。这类引用仅在采用 `字段名[设置键]` 时解析 Settings；template 则通过第 4.5 节的显式索引取设置键。两者都不对普通字段自动尝试解析，也不在外层配置与设置文本之间隐式回退。提取的是 Core 解析后的实际值，再按 URL 或协议等目标语义转换。

实现时先按参数语义区分常量和字段引用；protocols 为显式引用时先去除 `@` 前缀。随后识别引用内容的前导 `/`，选择本次配置根或当前候选项作为查询对象，再复用 Core `ConfigurationUtility.GetConfigurationPath` 转换配置字段路径并读取。该 API 会去除前后 `/` 并转换分隔符，因此不能先转换再判断引用起点。根引用不会读取其它独立入包文件，相对引用缺失时也不自动向上级或配置根回退。

例如以下声明提取 `http://*:9090/`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<?binding name="metrics" path="Metrics/Listener" template="${option.Settings['urls']}"?>
<configuration>
	<option path="Metrics">
		<listener settings="urls=http://*:9090/;path=/metrics" />
	</option>
</configuration>
```

Settings 文本的引号、转义、空值及语法错误均沿用 Core；读取声明所引用的 Settings 时解析失败，不回退为普通字段或把整串当作地址。字段或键缺失时与其它必需字段缺失采用同一错误契约，见第 4.4 节。配置键和 Settings 键的已有规则直接复用；若方括号引用与合法键名存在无法表达的情况，再用具体案例提出语法补充，读取范围保持在本次文件及导入结果内。

例如，假设本次读取结果包含以下配置：

```text
Server/Enabled = true
Server/Protocols = Http1
Server/Listeners/public/Url = http://*:8080/
Server/Listeners/admin/Url = http://127.0.0.1:8081/
```

两个监听项共用上层的启用条件和协议字段，可声明为：

```xml
<?binding name="server"
	path="Server/Listeners/*"
	filter="/Server/Enabled:true"
	template="${option.Url}"
	protocols="@/Server/Protocols"?>
```

逐项读取各自的 Url，并从本次配置根读取共同的 Enabled 和 Protocols。Enabled 为 true 时提取两个绑定，为 false 时产生零结果；省略 required，按默认规则允许零结果。

筛选属性名称确定为 `filter`，语法参考 [deployer 的过滤条件](deployer/README.zh-Hans.md#过滤)，实际行为以 [Utility.Requisition](deployer/src/Utility.cs) 为依据。

属性值直接填写过滤表达式，不携带 `.deploy` 条目外围的 `<...>`。字段引用与提取字段共用起点规则：以 `/` 开头时从本次配置根读取，否则相对于当前候选项读取，均支持普通字段及 `字段名[设置键]` 引用。不自动查询制作进程的环境变量、命令选项或其它文件。

| 表达式 | 含义 |
| --- | --- |
| `Driver` | Driver 字段存在，不按值的真假判断 |
| `!Driver` | Driver 字段不存在 |
| `Driver:prometheus` | Driver 值为 prometheus，忽略大小写 |
| `Driver:prometheus,custom` | Driver 值为候选列表中的任意一个，忽略大小写 |
| `/Server/Enabled:true` | 本次配置根下的 Server/Enabled 值为 true，忽略大小写 |
| `!Driver:telemetry` | Driver 不匹配 telemetry，字段缺失时也满足否定 |
| `Driver:prometheus & Scope:public` | 两个条件都满足 |
| `Driver:prometheus \| Scope:public` | 任一条件满足 |

先筛选再读取绑定字段，未选中项无需包含被 template 引用的地址字段。存在判断必须区分缺失与已存在的空值；例如 `Enabled` 只判断字段存在，要按内容筛选应写 `Enabled:true`。Settings 内部键的存在性通过 Core `TryGetValue` 判断，不能只检查返回值是否为 null。

启用条件统一通过 `filter` 表达，不提供独立的 `enabled` 参数。需要显式启用时写 `filter="Enabled:true"`；如果对应服务约定字段缺失时默认启用，写 `filter="!Enabled | Enabled:true"`。两者均沿用字符串匹配规则，忽略大小写并处理首尾空白，不额外进行布尔类型校验；空字符串或其它不匹配的文本不选中。省略 filter 时，保留 path 选中的全部候选项，不因存在名为 Enabled 的配置字段而自动筛选。字段缺失时是否启用由声明表达，工具不猜测服务默认值。

字符串比较沿用 deployer 的忽略大小写匹配及首尾空白处理，不再采用 Settings 等值比较作为筛选语义。`字段名[设置键]` 的取值过程仍遵循 Core Settings；得到的值再交给过滤比较。否定作用于整个单项匹配结果，包括字段缺失的情况。

deployer 当前实现将 `&` 和 `|` 从左到右组合，没有单独的运算符优先级，也不将括号解释为分组。因此 `A | B & C` 相当于先计算 A 或 B，再与 C 组合。共享实现应保留这一规则，不只在 binding 中改变优先级；若以后需要括号或优先级，另行作为统一语法扩展讨论。值中包含过滤分隔符的转义能力不能照搬 Settings 语法，出现需求时按实际案例确定。

PI 属性通过 Core `GetAttributes()` 解析，表达式中的 `&` 写为 `&amp;`，由 Core 单次解码后交给过滤器。例如 `filter="Driver:prometheus &amp; !Disabled:true"`。不得再手工执行第二次实体解码。

实现时建议把 deployer 的通用过滤解析、逻辑组合及字符串匹配抽到 `tools/.shared/`，通过调用方提供字段或变量查询，共用于 deployer 和 Packager。deployer 的 `framework:net10.0^` 属于目标框架专用比较，依赖 NuGet 的部分留在 deployer，由其适配，不引入绑定工具或改变普通同名配置字段的含义。不让 Packager 引用 deployer 可执行项目，也不复制一套独立解析器。

绑定声明以 template 生成完整监听地址，由 protocols 补充协议及能力说明，不另设 transport 参数。结果模型需要区分地址、端口、传输协议与应用协议，不意味着声明语法也要逐项增加同名参数。URL 的 scheme 已能表达的信息不要求在 protocols 重复填写；已知的 scheme 与 protocols 冲突直接报错，见第 4.6 节。首版 scheme 范围见第 4.7 节，未知协议不能默认为 TCP；非 HTTP 的 protocols 扩展名称尚未定义，不能将支持某个 scheme 等同于新增同名的 protocols 值。

独立 address/port 参数不列入当前最小方案。对于地址和端口分开存储的运行配置，使用 template 与 option 配置成员导航，由 Core 模板组合现有字段。具体规则及求值阶段见第 4.5 节，方案已确认并完成隔离原型验证，尚未接入正式工具；不增加一份与运行配置独立维护的监听地址。

### 4.2 Kestrel 示例

开发人员在应用配置中定义 Kestrel 端点。例如本机 `hosting/web/default/web.default.option` 已有下列配置；该文件是本地部署产物，不在 Git 中，换机时可从仓库内的 [web.option.template](https://github.com/Zongsoft/hosting/blob/main/.deploy/.shared/web.option.template) 查阅其配置结构：

```xml
<endpoint endpoint.name="http" url="http://*:8069" protocols="Http1" />
```

运行时由 Kestrel 根据加载后的配置监听该地址，无需打包工具再指定 --listen。为入包配置补充 binding 后，Packager 仅读取同一配置中的地址和协议并输出元数据；binding 不产生运行时监听，也不改变服务器是否启用。该示例不赋予 web.default.option 特殊的工具入口或加载优先级，仍遵循全部入包 option 逐文件提取的规则。

不指定 name 也能生成可引用的绑定。例如在该文件中声明：

```xml
<?binding path="kestrel/endpoints/*" template="${option.Url}"?>
```

匹配到 http 项后，默认别名为 `kestrel/endpoints/http`，web.profile 可以直接引用：

```ini
server=kestrel/endpoints/http
```

按第 4.5 节的已确认规则，拟在 [web.option.template](https://github.com/Zongsoft/hosting/blob/main/.deploy/.shared/web.option.template) 的 XML 声明之后加入：

```xml
<?binding name="kestrel"
          path="Kestrel/Endpoints/*"
          template="\${option.Url}"
          protocols="@Protocols"?>
```

现有运行配置模板仍提供实际字段：

```xml
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

deployer 只展开模板。Packager 在 web.option 中提取该文件的 http 端点。[Diagnostics OTLP endpoint](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Diagnostics/protocols/server/src/Zongsoft.Diagnostics.Protocols.Server.option) 若入包，也应带有相应 binding 声明，从其自身配置提取 telemetry 端点。两份文件可使用相同的 Kestrel 集合声明，分别求值后汇总，不能因 PI 文本相同而跳过其中一份。

上面的 `protocols="@Protocols"` 以来源字段符合绑定协议列表为前提；单项 Http1/Http2 的大小写处理见第 4.6 节。若实际配置为 Http1AndHttp2 等 Kestrel 组合枚举，不能直接套用该声明；其来源适配属于第 12 节第 2 项，不能擅自把运行配置改成 Kestrel 不接受的表示。

如果模板已生成端口 8070，或该文件的显式 Import 最终得到 8070，则该文件导出 8070。独立的其它入包文件不覆盖它；这些文件有声明时分别贡献结果，没有声明时不追加绑定。

### 4.3 Prometheus 与混合字段示例

拟在 [Zongsoft.Diagnostics.option](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Diagnostics/src/Zongsoft.Diagnostics.option) 中声明：

```xml
<?binding name="prometheus"
          path="Diagnostics/Diagnostor/Meters/Exporters/*"
          filter="Driver:prometheus"
          template="${option.Settings['urls']}"
          separators=",;|"?>
```

先按 deployer 过滤语法筛选 exporter，再用 Core 解析其 Settings，最后分割 urls 字段。分号若属于 urls 的内容，必须先按 Settings 语法在外层正确加引号。

**已确认的多地址展开：**只有显式指定 separators 时才切分 template 的求值结果；省略时将结果作为一个完整 URL 校验，不自动猜测分隔符。separators 是分隔字符集合，例如 `separators=",;|"` 表示逗号、分号和竖线中的任意一个都可分隔地址，不表示三字符组成的连续分隔串。该步骤发生在模板求值之后，不切分原始模板或外层 Settings。

例如 Settings 为 `urls='http://127.0.0.1:9464/;http://127.0.0.1:9465/';path=/metrics`，模板先取得 urls 的完整值，使用 `separators=";"` 后得到两个监听地址。不指定 separators 时，URL 路径中的逗号或分号保持原义。展开后每个地址使用当前候选项求得的同一组 protocols，不按地址和协议的位置配对；需要不同协议能力时使用独立候选项或声明。各地址仍分别校验，任一地址无效或与协议冲突即整条声明失败。

此处的 Driver 筛选只决定声明选取哪些配置项。部署工程师负责让声明对应本次交付需要的监听；若需要启用条件，应由 filter 引用本次文件及导入结果可读的配置。工具不检查插件是否装载或诊断服务是否启动。

[PrometheusExporterLauncher](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Diagnostics/src/Telemetry/PrometheusExporterLauncher.cs) 创建独立 HttpListener：先裁掉每个 urls 地址末尾的 `/`，再追加首尾补齐 `/` 的 path，得到实际监听前缀。例如 `urls=http://127.0.0.1:9464/;path=/metrics` 对应 `http://127.0.0.1:9464/metrics/`。本节声明只演示 Settings 取值和多地址展开，尚未导出拼接后的路径，不能直接作为完整的 Prometheus 迁移方案；实际声明及探测输入如何保留该路径，纳入第 12 节第 2、4 项确认。同文件中的 OTLP `server` 是客户端目的地址，不能因都是 HTTP 字符串就一起导出。

[Garnet 配置示例](https://github.com/Zongsoft/framework/blob/main/externals/garnet/README.zh-Hans.md#安装与配置) 将 bind 和 port 分开写在设置文本中，例如 `bind=127.0.0.1;port=6379`，并没有完整的监听 URL。[GarnetServer](https://github.com/Zongsoft/framework/blob/main/externals/garnet/src/GarnetServer.cs) 把各设置项转换为服务器参数。第 4.5 节给出从其 Value 字段组合 URL 的具体模板，不把读取单个 bind 或 port 等同于取得完整监听地址。

### 4.4 提取顺序与错误

推荐顺序：确定全部入包 option → 逐文件完成 Core 读取及显式 Import → 校验 binding 语法 → 按 filter 筛选候选项 → 通过 template 求值及 protocols 字段引用取值（按显式索引解析 Settings）→ 多值展开与规范化校验 → 按声明检查 required → 汇总去重及必要的身份校验。

各文件的 binding 先分别求值，不按规则名或声明文本提前合并，不因跨文件同名而直接报错。独立文件之间没有规则覆盖或禁用关系。候选项按 filter 筛选；相同绑定内容合并，名称合入别名集合，见第 3.3 节。引用身份及消歧方式见第 5 节。

required 默认为 false，省略该参数等同于 `required="false"`。它只约束当前声明是否必须产生绑定，不改变配置读取、筛选和字段解析规则。

| 情况 | 省略 required 或 `required="false"` | `required="true"` |
| --- | --- | --- |
| path 不存在 | 不产生绑定 | 报错 |
| path 末尾为 `/*`，但没有直接子项 | 不产生绑定 | 报错 |
| 全部候选项被 filter 排除 | 不产生绑定 | 报错 |
| 选中项缺少必需字段，或地址、Settings 等解析或校验失败 | 报错 | 报错 |

`required="true"` 要求当前声明在全局去重前提取出至少一个有效绑定；结果与其它声明重复仍满足要求，其它声明产生的绑定不能代替本声明满足要求。所有选中项仍须通过校验，不能因其中一个项有效就忽略其它项的错误。

例如，可选的 Prometheus 声明省略 required，没有匹配 exporter 时正常产生零条绑定；必须提供监听的 Kestrel 声明设置 `required="true"`，路径拼错、集合为空或全部被筛选掉时均报错。默认允许零结果也意味着未设置 required 的声明无法仅凭空结果识别路径拼写错误，必须存在的监听由声明者显式标记。

筛选后选中项的缺失字段、非法值、未展开模板、随机端口、空列表项及协议不明均导致提取失败。required 不豁免 XML、binding 声明、filter 或所引用 Settings 的解析错误，不能把解析失败降级为空结果。

PI 属性只使用 Core 的解析规则：带引号、单次实体解码、拒绝重复属性和尾部杂文。工具另外检查必需参数、拒绝未知参数并校验参数语义，诊断包含声明文件、行列和所引用的配置路径，不输出整段 Settings 或凭据。

声明位置与 path 作用域已确认：沿用 Core 对合法 XML 处理指令位置的支持，在文件级或元素内部均可放置 binding；path 始终显式填写并从本次读取结果的配置根定位，不随 XML 嵌套位置改变基准。按路径 Import 时，仍由 Core 的有效范围筛选决定哪些声明会被收集；这与声明被收集后如何解释 path 是两件事。工具不额外限制 binding 只能位于文件头或 option 的直接子级。

<a id="url-templates"></a>

### 4.5 template 与配置成员导航（已确认）

绑定声明使用 template 属性，所有值统一交给 Core TemplateEvaluator 求值，不再按是否含 `${` 区分字段引用和模板。template 仍专门生成完整监听地址，不表示任意输出格式；运行配置自身的 Url 字段不改名。不保留 url 的兼容别名，也不增加 address、port、transport 等 PI 属性。本节的声明语法、配置视图及求值边界已确认，正式实现仍按验收要求推进。

| template 写法 | 含义 |
| --- | --- |
| `${option.Url}` | 读取当前候选项的完整地址 |
| `${option.Settings['urls']}` | 解析当前项的 Settings 字段，读取 urls |
| `tcp://${option.Address}:${option.Settings['port']}` | 组合当前项普通字段与 Settings 键 |
| `tcp://${option.Value['bind']}:${option.Value['port']}` | 从当前项 Value 设置文本读取两个键后组合 |
| `tcp://${option['/Network/Address']}:${option.Settings['port']}` | 从本次配置根读取公共地址，组合当前项的端口 |
| `${option['/Network/Settings']['port']}` | 先从根读取 Settings 字段，再解析设置文本并取 port 键；此处仅演示字段取值 |

单字段引用也写成模板表达式。`${option.Url}` 读取当前候选项的 Url；`${url}` 表示名为 url 的模板变量，不作为当前配置字段的简写，工具不自动把配置字段注册成同名变量。`template="Url"` 的结果是字面文本 Url，不查询字段，随后不能通过完整监听地址校验。纯文本合法地址仍由 Core 原样输出；实际运行参数优先从配置读取，避免维护两份监听值。

**Core 原生语法与配置适配必须分开说明：**

- `${option.Address}` 和 `${option.Network.Address}` 是已有的成员导航语法。原生求值通过 Reflector 读取真实 CLR 公共属性或字段，不会把 IConfigurationSection 的任意配置子项、字典键或 ExpandoObject 键自动视为同名属性。
- `${option.Settings['port']}` 是已有的成员导航加字符串索引语法。如果 Settings 属性返回 Core Settings 对象，原生求值即可读取；实际配置中的 Settings 或 Value 通常仍是字符串，必须由工具在明确的设置键索引处调用 Core Settings 解析，不能直接对原字符串使用字符串键索引。
- `${option.Settings[port]}` 同样是合法模板，但 port 是另一个变量的引用。它不是字符串键 port 的简写；变量不存在时失败，变量值为 alternative 时读取的是 alternative 键。固定设置键应采用 `['port']`，不改变 Core 对动态索引的含义。

例如，把声明加入已有 Garnet 配置，地址和端口只维护在运行程序消费的 Value 字段中：

```xml
<?xml version="1.0" encoding="utf-8"?>
<?binding name="garnet"
	path="Externals/Garnet/default"
	template="tcp://${option.Value['bind']}:${option.Value['port']}"?>
<options>
	<option path="/Externals/Garnet">
		<server server.name="default" value="bind=127.0.0.1;port=6379" />
	</option>
</options>
```

提取结果为 `tcp://127.0.0.1:6379`。tcp 由声明者明确指定；工具不根据 Garnet 类型或默认参数猜测。Settings 取键不限定配置字段必须叫 Settings。

**配置起点必须显式区分：**只提供 option 视图。点号成员相对于 path 选中的当前候选项；直接作用于 option 的字符串索引表示配置路径，以 `/` 开头时从当前文件及其显式 Import 完成后的配置根读取，否则仍相对于当前候选项。选定字段之后的字符串索引表示读取该字段设置文本中的键。配置路径索引复用 Core ConfigurationUtility.GetConfigurationPath，先判断前导 `/` 再转换路径。不存在相对路径向根自动回退，也不访问其它独立入包文件。

假设 path 选中 Server/Listener，当前项的 Network/Address 为 `127.0.0.2`，配置根的 Network/Address 为 `10.0.0.1`：

- `${option.Network.Address}` 读取 `Server/Listener/Network/Address`，得到 `127.0.0.2`。
- `${option['/Network/Address']}` 读取 `Network/Address`，得到 `10.0.0.1`。
- `${option['Network/Address']}` 与点号写法一样读取当前项下的路径，得到 `127.0.0.2`。

`${option['/Network/Address']}` 的字符串索引是 Core 现有语法，但 `/` 定位配置根是工具配置视图的语义，不是 TemplateEvaluator 自带的路径规则。直接把 IConfigurationSection 注册为 option 不会自动获得这种语义。该方式保持单一 option 名称，并可用字符串路径表达含连字符等不适合点号成员语法的配置键。

**实现复用边界：**

1. Core 完成当前文件及 Import 加载，再选择候选项并执行 filter；未选中项不读取模板字段。
2. 使用独立的 TemplateEvaluator，设置 `CultureInfo.InvariantCulture`、`Recursive=false`、`Fallback=false`，不注册环境变量或工具命令的变量来源。
3. 通过现有 Resolving 事件适配 option 完整引用。工具负责配置成员导航、显式路径定位及 Settings 取键，成功后设置 Value 和 Handled；Core 继续负责外层模板解析、文本拼接、转义、格式化和诊断。无需应用专用配置类型、Plugins 依赖或新增 Core 公共 API。
4. 原型复用 Core `MemberExpression.Parse` 取得成员和索引节点，没有在工具中自建模板解析器。该解析器与 TemplateEvaluator 内部解析器是不同实现，原型验证不意味着所有语法已等价；接入时应覆盖合法特殊键、转义、动态索引及错误位置。Resolving 接管完整引用会跳过默认动态索引求值，适配必须保持变量索引的原义，不能将裸标识符偷换成字符串键。
5. 设置文本按实际索引惰性解析并在本次读取范围内缓存；用 Settings.TryGetValue 区分缺键与存在的空值。缺字段、缺设置键及非法设置文本报告错误。已存在的 null 或空字符串沿用 Core 的空文本插值。
6. 求值结果继续进行多值处理、完整监听地址和协议校验，再按声明检查 required 并汇总去重。

[Core 模板文档](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/docs/expressions.zh-Hans.md)、[TemplateEvaluator.Resolve](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Text/Templating/TemplateEvaluator.cs)、[Reflector](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Reflection/Reflector.cs) 和 [MemberExpression](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Reflection/Expressions/MemberExpression.cs) 是上述语法与扩展点的依据。这里需要补充的是通用配置取值适配，不能把简单更换变量名等同于已经支持任意配置成员导航；目前没有发现这些示例所需的模板解析错误。

部署与绑定提取继续区分求值阶段：普通 `.option` 使用 `${option...}`；由 deployer 展开的 `.option.template` 使用 `\${option...}`，先生成保留占位符的 `.option`，再在打包阶段读取最终配置求值。例如：

```xml
<?binding name="garnet"
	path="Externals/Garnet/default"
	template="tcp://\${option.Value['bind']}:\${option.Value['port']}"?>
```

不为 deployer 增加跳过 binding 或保留未知变量的特殊处理。模板不自动完成 URL 编码、IPv6 方括号或协议推导；已知字段保存裸 IPv6 时可明确写 `tcp://[${option.Address}]:${option.Settings['port']}`，其它格式化需求优先复用 Core Formatting 扩展点并按实际案例确定。

已使用现有 Core 程序集完成 32 项隔离验证：真实属性及 Settings 对象的原生导航、裸索引变量含义、原始配置和字典点号读取的限制、原生字符串索引没有配置根语义、适配后的绝对与相对路径索引、根路径与本地成员混合、路径后继续成员导航或 Settings 取键、连字符键、任意设置字段、直接字段模板、字面文本、空值、关闭递归、大小写、动态索引、缺字段/缺键、非法 Settings 和两阶段转义。原型证明本节列举的表达式可通过已有扩展点实现，尚未接入 Packager；完整配置视图和模板契约仍需按上述边界验收。

### 4.6 可选协议列表与能力补充

**已确认的表示格式：**协议名称采用 `http`、`http1`、`http2`、`http3`，多个名称用逗号分隔，例如 `http1,http2`、`http1,http2,http3`。这些名称是 HTTP 类协议的表示，不表示其它协议被排除。协议列表不使用 Http1AndHttp2 一类组合枚举名称。列表格式与协议字段的来源分开处理。

建议按不区分大小写比较各名称、裁剪分隔项两端空白，并规范化为不重复的协议集合；空项和未知名称报告错误。协议分隔符固定为逗号，不受地址多值 separators 影响。原有配置若使用 Http1AndHttp2，直接引用该字段不会自动变成新格式；配置适配时须明确来源表示，不能假定 TemplateEvaluator 会转换枚举组合名称。

**已确认的 http 含义及规范化：**http 表示 HTTP 协议，但未声明具体版本，不等于 http1，也不展开为全部版本。与一个或多个具体 HTTP 版本同时出现时，移除重复的泛称 http，保留全部已声明的具体版本。此规则仅规范化同一项的协议列表，不把不同声明的协议集合合并。

| 输入 | 规范化结果 | 含义 |
| --- | --- | --- |
| `http` | `http` | HTTP，具体版本未声明 |
| `http1` | `http1` | 明确支持 HTTP/1 |
| `http1,http2` | `http1,http2` | 明确支持 HTTP/1 和 HTTP/2 |
| `http,http2` | `http2` | 泛称不增加其它版本能力 |
| `http,http1,http3` | `http1,http3` | 保留 HTTP/1 和 HTTP/3，不补入 HTTP/2 |

只有 http 时不能据此确认 HTTP/2 或 HTTP/3 能力；下游需要确定版本时应要求明确声明。规范化后的协议集合参与绑定内容比较，不能把版本未声明的 http 与明确的 http1 当作相同能力。

**已确认：保留可选的 protocols 属性。** template 生成完整监听 URL，protocols 补充地址未表达的协议能力。Core TemplateEvaluator.Evaluate 返回文本，不会因模板引用了某配置项就自动取得该项其它协议字段。相同的 `https://*:8443` 可以对应不同 HTTP 版本集合，单看 URL 无法区分以下三种声明：

```xml
<?binding name="web" path="Server/Listener"
	template="${option.Url}" protocols="http1,http2"?>
```

将上例的 protocols 分别改为 `http3` 或 `http1,http2,http3`，地址保持相同，协议能力却不同。[Kestrel 的 HTTP/3 文档](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/http3?view=aspnetcore-10.0) 说明 HTTPS 端点可以同时提供多个 HTTP 版本；HTTP/3 使用的 QUIC 基于 UDP，见 [RFC 9000](https://www.rfc-editor.org/rfc/rfc9000.html)。提取结果需要保留这类差异，供代理选择及 TCP/UDP 发布规划使用，不能把 https 固定等同于一种 HTTP 版本或传输方式。

如果地址自身已经包含制作流程所需的协议信息，例如 `tcp://127.0.0.1:6379`，可省略 protocols。省略只表示未补充协议能力，不能推导为支持全部版本或套用服务器默认值；下游操作若需要明确版本而信息不足，应报告不足。

template 的输出保持 URL，不在其结果中嵌入另一层 Settings 绑定描述，也不把协议列表塞入自定义 URL 附加语法。protocols 继续采用已确认的常量及 `@` 引用规则；template 的表达式支持不自动改变其它参数的取值规则。

**已确认的 scheme 与协议集合冲突处理：**对已知的矛盾直接报告错误，整条绑定声明失败，不自动改写地址，也不丢弃冲突的协议项。required 只约束零结果，不豁免此类错误；提取失败时终止制包，不发布部分绑定结果。

| 模板求值得到的 URL | protocols | 一致性检查结果 |
| --- | --- | --- |
| `http://*:8080` | `http1,http2` | 通过本项检查 |
| `http://*:8080` | `http3` | 报错：HTTP/3 要求 HTTPS |
| `http://*:8080` | `http1,http3` | 整条声明报错，不仅保留 http1 |
| `https://*:8443` | `http1,http3` | 通过本项检查 |

错误诊断指出声明来源文件、位置、配置路径、可选的绑定名称、求值后的地址及冲突协议，便于定位。这是工具的绑定语义校验，Core 继续负责配置解析与模板求值。通过本项检查只表示没有这一已知矛盾，实际监听及运行环境支持仍由部署验收保证。首版 scheme 支持范围见第 4.7 节。

**已确认的 HTTP 版本到传输需求映射：**对通过一致性检查且显式声明具体 HTTP 版本的每个地址，保留完整协议集合，并记录对应的 TCP/UDP 传输需求。HTTP/1 和 HTTP/2 对应 TCP，HTTP/3 对应基于 UDP 的 QUIC；同一地址同时声明这些版本时需要保留两种传输，不能只按端口整数去重。不增加 PI transport 参数，具体结果字段在第 5 节的序列化结构讨论中确定。

| 同一地址 `https://*:8443` 的 protocols | 对应的传输需求 |
| --- | --- |
| `http1,http2` | `8443/tcp`，保留两种 HTTP 能力 |
| `http3` | `8443/udp`，不额外补入 TCP |
| `http1,http2,http3` | `8443/tcp` 和 `8443/udp`，保留三种 HTTP 能力 |

上述映射描述声明所需的监听，不自动发布宿主端口，也不保证任意代理或探测器支持这些协议。省略 protocols 或仅声明 http 时，继续保留版本信息不足的边界，不通过本项映射补入未声明的版本。首版 scheme 范围及 Unix socket 边界见第 4.7 节。

### 4.7 首版监听范围（已确认）

首版覆盖 IP 网络监听，提供 http、https、tcp、udp 和 opc.tcp 的地址解析与语义映射；不认识的 scheme 明确报错，不猜测传输。http/https 按前述能力约定处理，tcp/udp 分别表达其传输，opc.tcp 对应 TCP 并保留 OPC 应用协议及 URL 路径。协议专用行为通过通用工具内部的映射实现，不引用对应应用框架库，也不按端口号猜测应用协议。

| 地址示例 | 首版处理 |
| --- | --- |
| `http://*:8080`、`https://*:8443` | 按已确认的 HTTP 能力规则处理 |
| `tcp://*:6379` | TCP 监听 |
| `udp://*:9000` | UDP 监听 |
| `opc.tcp://localhost:4840/server` | TCP 监听，保留 OPC 协议及路径 |
| `unix:///run/example/app.sock` | 明确报告不支持 |
| 未识别的 scheme | 明确报告不支持，不回退为 TCP |

Unix domain socket 暂不纳入首版：例如 `unix:///run/example/app.sock` 被 binding 选中时明确报告不支持，不能静默跳过或转换成网络端口。应用仍可使用此类监听；此边界只限制工具的绑定元数据处理。后续支持时需一并明确路径身份、容器挂载、代理连接和探测方式。本项确认功能范围，具体序列化字段不在这里定稿。

<a id="model"></a>

## 5. 统一结果模型

推荐一个带格式版本的结果对象，分为绑定、Web 入口和后端引用三部分。字段名与序列化结构需要定稿，以下先固定各部分必须表达的语义。

| 部分 | 所需内容 |
| --- | --- |
| 绑定 | 稳定身份、别名集合、执行目标、一个或多个实际监听端点 |
| 监听端点 | 地址、端口、传输协议、应用协议，以及有意义的 HTTP 协议能力和 URL 路径 |
| Web 入口 | 站点身份、域名、所引用的代理监听、显式默认站点关系 |
| 后端引用 | 代理路由引用的应用绑定，区分本应用后端与普通远程地址 |
| 结果状态 | 格式版本；能够区分有效空结果、未生成元数据和无效结果 |

执行目标至少区分应用进程和 nginx。多个业务插件共用应用进程时无需按插件拆文件；插件和声明来源用于制作时诊断，不作为容器端口归属。

每个绑定有完整配置路径默认别名，可通过 name 增加简短别名。第 6.2 节的别名生成及 server 引用规则已确认，可用于调试定位和后端选择。不能预设同一配置路径或名称在多个文件之间唯一。结果等价比较不包含别名与来源，不依赖文件枚举顺序，不用数组序号作为长期身份。

结果去重至少考虑执行目标、地址及地址族、传输和端口，并保留应用协议、HTTP 能力和 URL 路径等语义。同一 socket 对应的不同能力或路径不能直接丢弃，也不能未经约定自动拼成新的监听描述。工具不模拟多个服务能否实际共享 socket；宿主发布冲突由发布规划另行校验。

三类地址必须分开处理：

```text
实际监听：http://*:8069
代理连接：同机 http://127.0.0.1:8069；跨容器 http://应用服务名:8069
宿主发布：宿主 8080 → 应用容器 8069/tcp
```

- 通配地址不是代理连接目标；构造连接目标不能反过来改写实际监听。
- 回环、IPv4、IPv6 与 IPv6 zone 保留实际含义；不能假定 IPv6 通配自动覆盖 IPv4。
- HTTP/2 不自动等于 gRPC。具体 HTTP 版本的 TCP/UDP 映射按第 4.6 节执行；普通 UDP 地址、Unix socket 及未知协议按第 4.7 节处理，不能统一回退为 TCP。
- OPC URL 路径和 Prometheus 抓取路径不增加 socket，但需要构造请求或引用端点时不能丢失。
- 元数据不含证书私钥、Settings 全文、无关配置或仍需求值的表达式。
- 输出顺序稳定，失败不发布部分结果或保留可被误读的旧文件。没有 binding 的文件不贡献结果；合法空集合表示本次交付未提取出绑定，可能没有声明，也可能声明允许且得到零结果，不要求工具证明应用没有动态监听。

推荐保留 option 中的 PI，无需额外解析并改写运行文件以剥离它们。生成结果只保留确定值，Containerizer 不重新求值声明。

<a id="web"></a>

## 6. Web 代理与应用绑定

### 6.1 保留现有配置器

[Definition.Resolver](packager/src/Web/Definition.Resolver.cs) 继续负责 `web.profile` 的覆盖、站点、路径和后端策略；应用绑定只替代它依赖单个 Listen 地址的部分。

[Configurator.Nginx.Bindings](packager/src/Web/Configurator.Nginx.Bindings.cs) 继续从生成后的 Nginx 结构读取 listen、server_name 和 default_server。改为返回统一模型中的对象，再与应用绑定合并，不先生成 `.web/nginx/.bindings` 文本供另一个流程读回。

原生 Nginx 指令可能覆盖 profile 的入口值，导出器必须继续反映最终结构。不能安全表示的 include、动态监听或复杂匹配应给出定位明确的失败；推荐在统一绑定生成模式中终止制包，不把部分入口当成完整结果。

`.web/nginx/<包名>.conf`、配置模板和证书资源仍按现有职责保留。只替换独立的旧绑定文件，不删除整个 `.web` 目录。

### 6.2 应用后端选择

server 是可选的代理后端选择项。应用自行监听，工具从 binding 结果取得可用后端，托管配置器据此生成转发配置。

**已确认的别名生成：**每个匹配项以其完整配置路径作为默认别名，取 Core 映射后的实际配置路径，以 `/` 连接各级且不带前导 `/`。例如 path 为 `kestrel/endpoints/*`，匹配到 http 项的默认别名为 `kestrel/endpoints/http`；使用精确 path 选中同一项时，默认别名相同。默认别名不包含通配符或文件名，不使用枚举序号。

name 是 `<?binding ... ?>` 的可选属性，只增加简短别名，不替换默认路径别名。精确 path 增加 name；末尾 `/*` 的每个子项增加 name 和 `name/子项键`，子项键取 Core 配置节 Key。例如同一 http 项加上 `name="kestrel"` 后，可用的别名为 `kestrel/endpoints/http`、`kestrel`、`kestrel/http`。

内容去重后合并全部别名。跨文件同路径或同名不覆盖结果；别名查找后仍有歧义则报错。别名可用于调试定位及显式引用，无需在 web.profile 中重复列出全部绑定。

**已确认的 server 规则只有三种：**

| 有效写法 | 含义 |
| --- | --- |
| 未指定或 `server=` | 自动选择唯一合适的应用绑定 |
| `server=kestrel/endpoints/http`、`server=kestrel` 或 `server=kestrel/public` | 按默认路径别名或 name 生成的简短别名选择应用绑定 |
| `server=http://backend.example.test:8080` | 使用指定的完整后端 URL |

自动选择和名称引用都必须得到唯一合适的后端及可连接目标；零个或多个候选报错，不取第一条，也不自动组成后端池。候选是否合适由当前代理方式和绑定协议能力判断。

例如 kestrel 下有 public、admin 两个合适端点，自动选择或 `server=kestrel` 都有歧义，填写 `server=kestrel/public` 即可明确目标。

配置合并时，空值及求值后的纯空白按未声明处理，与省略采用相同的继承规则；合并后的有效值按上表解析，求值错误仍报错。带 `scheme://` 的值按 URL 校验，非法 URL 不回退为名称查询。取消 `~` 特殊值及前缀，不保留旧写法兼容层。

托管配置器使用选中的后端生成转发配置。当前 [Nginx Writer](packager/src/Web/Configurator.Nginx.Writer.cs) 和 [WebIngress.Files](containerizer/src/WebIngress.Files.cs) 的本应用标记可扩展为绑定引用，用于构造同机或跨容器的连接地址；显式远程 URL 保持原义。TLS、HTTP/gRPC 转发及多地址连接适配属于配置器实现，支持范围需单独验收，不增加 server 写法。

<a id="packages"></a>

## 7. 包格式与元数据承载

### 7.1 推荐的序列化方式

推荐 `.bindings` 使用带格式版本的 JSON，由公共模型和 `System.Text.Json` 读写。它是机器生成的交付结果，绑定的地址集合、站点关系和后端引用适合直接序列化，不需要再维护一套 Profile 到对象的映射规则。

Packager 与 Containerizer 共用少量模型、序列化和必要校验源码，放在 `tools/.shared/` 按实际引用关系链接；不互相引用可执行项目。Core 继续处理用户输入和配置，包级业务模型不放入 Core。现场执行器继续消费已有交付协议，不为解析 `.bindings` 增加 Core 依赖。

明确格式版本、必需字段、未知字段、未知协议、重复身份和内容长度的处理。版本不支持或内容损坏应失败，不回退到旧 Listen 或旧 Nginx 文件。空数组表示已生成的空结果时，仍须有合法的顶层结构；缺少元数据不能自动等价为空结果。

### 7.2 推荐的三格式存放位置

统一逻辑内容，物理承载遵循包格式：

| 格式 | 推荐承载 | 必须同步的消费边界 |
| --- | --- | --- |
| tar.gz | 归档根目录 `.bindings` | 安装脚本及应用镜像提取排除它；PackageReader 在普通安装路径映射前识别 |
| deb | `control.tar.*` 内 `control` 的专用字段，名称可采用 `Zongsoft-Bindings` | 存放序列化结果，不加入 `data.tar.*` 安装载荷 |
| rpm | Header 的专用应用字符串标签 | 不加入 payload 文件清单；标签号与读写类型统一定义 |

deb/rpm 可沿用现有 Listen 元数据的写入、读取位置，替换为统一结果；字段及标签不沿用 Listen 的含义。三种格式使用同一序列化器，只有外层承载适配不同。deb/rpm 中的 `.bindings` 表示同一逻辑文档，不承诺存在字面意义的根目录文件。

Debian 允许[自定义 control 字段](https://www.debian.org/doc/debian-policy/ch-controlfields.html#user-defined-fields)，RPM Header 提供[字符串标签](https://rpm.org/docs/4.20.x/manual/tags.html)。上述承载是方案建议，具体字段、标签号、大小限制和原生读回结果必须在实施时验证。

生成结果作为包元数据直接交给编码器，不通过普通 `Package.Entries` 安装条目添加。tar 使用独立的归档条目；deb/rpm 使用各自元数据结构。避免“先安装再删除”的处理，也避免 Containerizer 把 tar 根文件归入应用 InstallPath。

### 7.3 发布完整性

先完成应用和 Nginx 绑定提取及校验，再序列化和发布安装包。复用现有成组/原子发布能力，不在源目录写一个可被下次误用的中间 `.bindings`。

绑定提取失败时终止制包。所有入包 option 均无 binding 时，应用绑定集自然为空；Web 代理部分仍按其配置生成。空结果如何序列化、是否总是生成元数据在格式讨论时确定，不增加运行时监听推断。

<a id="containers"></a>

## 8. Containerizer 消费与发布

### 8.1 分开规划职责

[PackageReader](containerizer/src/PackageReader.cs) 负责三格式元数据读取，得到同一绑定模型；它不解析各插件的 option，也不装载插件或重新展开模板。

[ApplicationPlanner](containerizer/src/ApplicationPlanner.cs) 负责入口与工作目录。[ApplicationHealth](containerizer/src/ApplicationHealth.cs) 当前还会根据 Listen 改写 `--urls`、转换监听地址、在没有 Web 资产时发布端口并选择首个 HTTP 端点；这些行为需按职责调整：

- 应用入口保留实际启动参数，不根据绑定元数据追加或改写 `--urls`。
- 绑定模型只描述真实监听；发布规划产生宿主映射，不改变 option 中的值。
- [WebIngress](containerizer/src/WebIngress.cs) 按绑定引用规划代理关系，逐项处理应用端点，不能因为存在 nginx 就忽略其它 HTTP/TCP 监听。
- 健康检查独立选择端点和探测方式；没有可靠探测信息时保留进程检查，不把端口可连接等同于业务健康。

### 8.2 发布策略留在交付配置

监听不自动等于宿主公开。推荐在 `.container` 中通过稳定绑定身份声明直接发布、仅内部访问及宿主 IP/端口；精确语法和默认策略待定，不恢复 port! 方案。

代理后端、Prometheus、OTLP 和业务 TCP 端口分别决策。宿主发布冲突按绑定 IP、地址族、传输及宿主端口校验；不能仅对整数端口做 Distinct。回环监听无法通过增加宿主映射变成跨容器可访问。

ZeroMQ、OPC、Garnet 的发现或通告地址还需与部署网络一致。NAT 映射不是这些协议的通告地址配置。

### 8.3 整体替换 Listen 链

需要同步移除或替换：

- Packager 的 `--listen`、PackageOptions/ApplicationHost/Package 的相应属性与验证。
- 自动生成 systemd 服务时追加的 `--urls`。
- tar PAX、Debian control 和 RPM Header 的旧 Listen 元数据。
- 依赖单一 Listen 的后端选择，以及 `~` 特殊值和名称引用前缀；改为缺失/空值自动选择及直接名称引用。
- Containerizer 的 ListenerAddresses、旧绑定读取、监听地址重写和启动参数拼接。
- hosting 脚本中的相关提示或传参，以及双语帮助、资源、文档和测试。

已有自定义 `.service` 的 ExecStart 不应机械删除参数。它与绑定声明是否一致由部署工程师负责，工具不解析任意启动参数来推导或反向覆盖绑定。普通业务变量名 `listen` 若仍用于生成实际 `web:url`，可以继续存在，不因 CLI 选项移除而误删。

<a id="servers"></a>

## 9. 实际服务器的配置适配

以下为声明编写者适配实际服务器时的参考，不是工具内置的类型识别或启动检查逻辑。工具只处理入包配置中的 binding；部署工程师负责选择配置文件、声明及显式条件，保证它们表达本次交付需要的监听。

| 组件 | 当前事实 | 本次适配重点 |
| --- | --- | --- |
| Web / Diagnostics OTLP | 运行时可共同贡献 Kestrel 端点 | 各入包文件声明并提取自身端点，再汇总去重，保留 Http1/Http2 等能力 |
| Prometheus | 独立 HttpListener，读取 Settings 中 urls 和 path | 声明筛选所需 exporter 及显式配置条件，保留回环语义，不选取 OTLP 客户端地址 |
| ZeroMQ daemon | 发现端口来自服务器属性；数据/控制端口来自具名或默认选项，也可被启动参数覆盖 | 明确实际选中实例、发现端口、控制通道启用条件和固定端口 |
| Garnet | 从选中的 Settings 生成服务参数，包含 bind/port、TLS、Unix socket 和通告项 | 只提取被选中的网络监听；不把全部预设和通告项当作端口 |
| OPC | BaseAddresses 为实际服务地址，Discovery 为注册目的地址 | 保留 URL 路径与协议，只提取服务监听 |
| MQTT / 通用 TcpServer | 业务宿主实际创建并启动服务器才监听 | 在真实启动链支持显式配置后添加声明，不依据客户端插件推断 |

ZeroMQ 推荐先完善服务器配置，再添加 binding。[ServerOptions](https://github.com/Zongsoft/framework/blob/main/messaging/zero/src/Configuration/ServerOptions.cs) 当前端口串有位置语义：两项为输入/输出，三项为控制/输入/输出；[ZeroQueueServer](https://github.com/Zongsoft/framework/blob/main/messaging/zero/src/ZeroQueueServer.cs) 还单独维护发现端口，并允许随机绑定；[控制通道](https://github.com/Zongsoft/framework/blob/main/messaging/zero/src/ZeroQueueServer.Control.cs) 在没有存储时不监听。

建议把发现、输入、输出、控制端口及其启用条件明确接到运行时配置。复用现有选项和插件属性注入职责，避免为了端口串而把位置索引、补位和任意条件运算加入通用 binding 语法。具体配置对象与迁移方式需要单独确认；部署工程师保持声明与启动配置一致，工具不推导启动参数优先级。适配不仅修改 [daemon option 模板](https://github.com/Zongsoft/hosting/blob/main/.deploy/.shared/Zongsoft.Messaging.ZeroMQ-daemon.option.template)。

Hangfire Dashboard、Controller、SignalR、普通 gRPC 服务映射等复用宿主监听，无需重复声明。数据库、Redis、Etcd、S3、MQTT 客户端等出站连接不生成应用端口或基础服务容器。

服务器实现参考：[Prometheus](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Diagnostics/src/Telemetry/PrometheusExporterLauncher.cs)、[Garnet](https://github.com/Zongsoft/framework/blob/main/externals/garnet/src/GarnetServer.cs)、[OPC](https://github.com/Zongsoft/framework/blob/main/externals/opc/src/OpcServerOptions.cs)、[MQTT](https://github.com/Zongsoft/framework/blob/main/messaging/mqtt/src/MqttQueueServer.cs)、[TcpServer](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Net/src/TcpServer.cs)。

<a id="implementation"></a>

## 10. 实施顺序与改动位置

| 阶段 | 工作与完成条件 | 主要位置 |
| --- | --- | --- |
| 1 | 基于最终包条目遍历全部 option；由 Core 逐文件读取配置与 PI，明确 Import 与求值边界 | Packager 的 PackCommand、Package 与交付文件视图；Core 原有 XML API |
| 2 | 确认剩余取值边界及结果规范化细节，复用 deployer 通用过滤，接入已确认的 template 与 option 配置视图，落实按内容去重和别名合并，完成 Kestrel/Prometheus 提取 | Packager 新增绑定提取实现、`tools/.shared/` 过滤与必要模型；Core Settings、TemplateEvaluator 及成员表达式 API |
| 3 | 接入 Web 后端引用与选择；Nginx 导出对象并合入统一结果 | Web/Definition.Resolver、Configurator.Nginx.Bindings、Nginx Writer |
| 4 | 三格式写入并统一读回，排除运行载荷，处理失败发布 | [Generator.Tar](packager/src/Generator.Tar.cs)、[Generator.Deb](packager/src/Generator.Deb.cs)、[Generator.Rpm](packager/src/Generator.Rpm.cs)、PackageReader 对应实现 |
| 5 | 按绑定规划代理、发布和健康检查，整体替换旧 Listen 链 | Containerizer 的 ApplicationPlanner、ApplicationHealth、WebPackage、WebIngress；Packager 的宿主与服务生成 |
| 6 | 在实际交付的配置中加入绑定；有必要时完善 ZeroMQ 等服务器的显式配置消费 | Framework 对应服务器配置、Zongsoft/Automao hosting 模板；不改造 Plugins 配置装配 |
| 7 | 同步脚本、帮助、双语文档，完成隔离集成及获授权的运行验收 | 两套 hosting、受影响工具的测试与文档 |

阶段可以分别验证，但不能把只覆盖 Kestrel 的阶段结果宣称为整个统一绑定重构完成。JSON 模型及文件位置确定后，生成端和读取端成对变更，不留下双轨兼容代码。

deployer 继续只负责模板展开并保留合法 PI；migrator 不增加监听解析职责。两者参与最终交付链验证，不因绑定功能扩大业务范围。

建议的工具内部结构只包含必要职责：交付文件遍历及读取、声明提取与校验、结果汇总去重及序列化；过滤复用从 deployer 抽取的共享实现。不增加插件发现、宿主配置装配、全局配置入口、XML/Settings 解析器或通用规则引擎。

<a id="acceptance"></a>

## 11. 验收与开发约束

### 11.1 配置与提取

- 只按最终入包范围遍历 option；无需 Plugins、插件清单、应用程序集或额外配置入口即可提取。
- 基础、环境及 host/site 命名的文件按相同规则处理；文件枚举顺序不产生覆盖关系。
- 排除、重命名、可选导入、按 option 路径导入、循环和越界均遵循 Core 与交付文件视图的实际规则。
- binding 位于 Core 允许的不同 PI 位置时，path 的配置根基准不变；按路径 Import 对声明的保留或过滤沿用 Core，工具不增加位置限制。
- 每个文件及其显式导入完成后再求值；被导入文件独立入包时仍单独提取，按结果去重，不按声明来源提前跳过。
- 精确 path 仅选择指定项；末尾 `/*` 仅枚举直接子项；不把无后缀的集合节点自动展开或递归遍历更深层级。
- protocols 默认采用常量，配置引用仅使用 `@` 前缀；去除前缀后覆盖普通字段、根引用和 Settings 取键。常量不触发配置查找，未知值不回退为字段引用。
- protocols 可省略；省略不补入服务器默认协议、不宣称支持全部版本，下游需要明确协议能力而信息不足时报告不足。template 的结果保持 URL。
- 协议列表覆盖逗号分隔的 http/http1/http2/http3 多项输入，不按 Http1AndHttp2 组合枚举名解析；来源取值和列表解析分开，地址 separators 不影响协议分隔。
- http 单独出现表示版本未声明，不转换为 http1 或全部版本；与具体版本同时出现时移除 http，保留具体版本。覆盖 http,http2 与 http2 的规范化等价，以及 http 与 http1 的内容不等价，不跨声明拼合协议集合。
- scheme 与协议集合的已知冲突使整条声明失败，不修改 URL、不丢弃冲突协议，也不受 required 豁免。覆盖 http 搭配 http3、http1,http3 的失败，以及 http 搭配 http1,http2、https 搭配 http1,http3 通过本项检查；诊断包含来源、配置路径、可选名称、求值后的地址及冲突协议，失败不发布部分结果。
- 显式 http1/http2 映射 TCP，http3 映射 UDP，混合版本保留两种传输及完整协议集合；同端口的 TCP/UDP 不合并，只有 http3 时不补 TCP。不增加 transport 参数，也不因得到传输需求自动发布宿主端口。
- 首版覆盖 http/https、tcp、udp 和 opc.tcp；opc.tcp 保留应用协议和路径。被声明选中的 Unix socket 或未知 scheme 明确失败，不静默跳过、不回退 TCP，且不引入应用框架库依赖。
- protocols 引用与 filter 共用引用起点规则：引用内容无前导 `/` 时相对于当前候选项，有前导 `/` 时从本次配置根读取；普通字段及 Settings 取键均覆盖。候选项与根有同名字段时分别读取正确值，相对引用缺失时不自动向上级或根回退。
- 根引用覆盖本次显式 Import 的最终读取结果，不读取其它独立入包文件；binding 的 path 始终从配置根定位。
- Kestrel 多端点、Settings 引号/转义/空值、普通字段与 Settings 混合、有限筛选及启用条件均有行为测试。
- 筛选覆盖字段存在、否定、逗号候选值、忽略大小写、缺失与空值、从左到右的混合逻辑组合；PI 中的 `&amp;` 经 Core 单次解码。deployer 变量查询和 binding 配置字段查询使用同一通用过滤规则，框架专用比较保持在 deployer。
- 启用条件仅由 filter 表达；覆盖 `Enabled:true`、`!Enabled | Enabled:true` 以及省略 filter 的情况，不自动识别 Enabled 字段，不额外进行布尔校验。独立 enabled 参数作为未知参数拒绝。
- 普通字段直接读取，`字段名[设置键]` 只解析指定字段；覆盖 Settings 以外的字段名、内层键缺失及非法设置文本，不自动尝试其它来源或回退为原始字符串。
- template 验收覆盖 option 的当前项成员、相对与绝对路径索引、Settings 取键及 Import 最终值；单字段也使用模板表达式，`${url}` 不隐式转换为 `${option.Url}`，字面文本不隐式查字段，未选中项不读取其模板字段。
- 多地址仅在显式指定 separators 时展开，分隔字符集合只作用于模板求值后的地址文本，不作用于原始模板或外层 Settings；省略时保留 URL 路径中的逗号、分号。覆盖多个地址共用当前候选项的 protocols，以及其中任一地址无效或与协议冲突时整条声明失败，不按位置配对地址和协议。
- URL 模板分别验证普通 option、部署模板中的 `\${...}` 保留及打包阶段求值；不注册环境变量来源，关闭递归及回退，字段缺失或 Settings/模板错误不降级为空结果。
- 同一声明文本在不同文件读出不同值时全部保留；同名声明不隐式覆盖。内容相同、名称不同的结果合并为一项，保留全部别名并去除重复别名；改变文件枚举顺序不改变绑定和别名集合。
- 未指定 name 时仍生成匹配项的完整配置路径默认别名；精确 path 与通配 path 选中同一项时默认别名相同，不保留通配符。提供 name 时保留默认别名，并按精确项/集合项增加 name、name/子项键；别名不受枚举顺序影响，内容去重后合并全部默认及简短别名。
- 同一默认路径别名在多个文件对应不同绑定时不覆盖、不提前合并；引用筛选后仍有歧义则失败。省略 name 的诊断以来源及配置路径定位。
- 地址、端口、传输协议、应用协议、HTTP 能力或有意义的路径不同，不能只因端口或名称相同就合并；声明来源不同不阻止相同内容去重。
- required 省略及 false 时，路径缺失、没有直接子项或全部被 filter 排除均允许零结果；true 时上述情况报错。约束逐声明在全局去重前检查，重复结果仍满足非空要求，其它声明有结果不能掩盖本声明无结果。
- 缺失必需字段、解析及校验错误不受 required 豁免；一个有效项不能掩盖其它选中项的错误。无声明的文件不贡献绑定。
- 不从客户端地址或服务器默认值推测绑定；随机端口及残留模板变量失败。输入声明与实际服务的一致性在部署验收中验证。

### 11.2 代理、发布与包内容

- server 直接引用名称或别名，完整 URL 继续作为显式后端；未知别名、无合适候选及多个候选均失败，远程同端口后端不被误改。不接受旧 ~ 特殊值及前缀。
- server 缺失、显式空值及求值后的纯空白按相同继承规则处理，最终无有效配置时自动选择；求值错误仍报错。其它配置字段保持各自的空值语义。
- Nginx 原生指令影响最终监听时正确导出；不支持的结构不产生部分成功结果。
- 回环、通配、IPv4/IPv6、传输协议、不同执行目标和多地址分别验证。
- 有 nginx 的应用仍保留其它端点；内部端口不因存在就全部公开。发布策略与健康检查不修改运行监听。
- tar/deb/rpm 都能写入并读回同一逻辑结果；验证未知版本、损坏、缺失、合法空结果及大小限制。
- 安装目录与应用镜像没有工具元数据、`.env` 或源模板；实际 option、代理配置及证书按原职责交付。
- 同输入生成稳定结果，失败不会发布旧或半成品元数据；包级结果不含凭据、私钥和 Settings 全文。
- 旧 Listen、启动参数补写与独立 Nginx 绑定文件链全部清理，保留业务变量和自定义服务的正确语义。

### 11.3 整体交付验收

最终验证同时覆盖容器和本地 Debug 两条路径，不能只检查生成的端口表。

容器路径：以 [Zongsoft web/default](https://github.com/Zongsoft/hosting/tree/main/web/default) 为代表，从 hosting 根目录走部署、打包、升迁、容器化、安装及启动；在授权测试环境使用所需基础服务和独立 ZeroMQ daemon，验证登录、Security 常用 API、代理转发、日志、事件交换与发现后的实际通道可达性。

本地路径：使用宿主 Debug 便捷部署入口，连接同一组基础服务及独立 daemon，执行相同业务验证。保持便捷脚本已有的当前宿主、Debug、Windows/x64 和覆盖策略，不为绑定重构无关改写。Automao 宿主按其实际站点配置增加代表性覆盖。

Security 输入参考 [HTTP 示例](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Security/docs/http)和[数据库脚本](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Security/database)。账户和凭据从已授权测试配置取得，不写入本文或生成元数据。

本文不是启动服务或执行安装的授权。执行前遵守用户当时的服务状态要求；归档检查、编译及交叉编译不能替代对应平台的安装和运行验证。

### 11.4 开发要求

遵循仓库 [AGENTS.md](AGENTS.md)、目标工具的 SKILL 和 [guidelines](https://github.com/Zongsoft/Guidelines/blob/main/AGENTS.md)。保留用户修改，不为测试扩大成员可见性；文本 CRLF、代码 Tab，Shell 和第三方文件保持各自格式约定。

代码改动按受影响项目执行全部目标框架的严格构建、IDE0049 验证和相关回归。文件系统行为使用临时目录及本地输入；文档改动只检查内容、链接、换行和差异。API、命令或资源变化同步对应双语文档及生成资源流程。

Core 和工具依赖版本以项目及 [Directory.Packages.props](Directory.Packages.props) 为准。Debug 本地程序集与 Release 包引用都必须包含所需的新能力，不能以本地 Debug 通过代替包依赖验收。

各工具保留独立解决方案和发布流程。Cake pack 会推送 NuGet，不能用作本地验证。需要实际工具安装验收时，按届时用户授权的范围执行；版本从项目读取，安装操作与普通源码测试分开，不把本文当作另一台电脑的全局卸载或安装授权。

<a id="decisions"></a>

## 12. 待确认问题与讨论顺序

下面是影响实施的决策点。推荐方向已写入对应章节；尚未确认的细节不应被实现者自行视为最终契约。

| 顺序 | 需要确认的问题 | 推荐方向与边界 |
| --- | --- | --- |
| 1 | 代理连接目标与方式 | 默认路径别名、可选 name 简短别名、server 直接引用、缺失与空值等效及唯一候选自动选择已确认；明确通配/回环/IPv4/IPv6 多地址的可连接目标构造、协议能力不足时的候选判断，以及 HTTP/2、TLS 与代理方式的适配。不改动 server 的三种写法 |
| 2 | 实际服务器适配 | 首版监听范围已确认；明确 Kestrel 组合枚举与绑定协议列表的适配、Prometheus urls 与 path 的组合、必要的非 HTTP 协议能力，有必要时完善 ZeroMQ 的显式配置。部署方案保证与运行一致，通用提取器不内置服务器类型判断 |
| 3 | 结果规范化、JSON 结构与三格式承载 | 确认下述规范化边界、JSON 版本/身份/别名/引用/空结果结构、deb 字段、rpm 标签及共同长度限制；元数据不进入运行载荷 |
| 4 | `.container` 发布与健康检查输入 | 通过绑定身份选择内部访问、直接发布、宿主 IP/端口和探测方式；确定默认策略及 Prometheus 等端点的请求路径来源，不恢复 port! |
| 5 | Import 的文件访问范围 | 建议按最终交付路径访问入包内容；确认包外依赖与绝对导入是否支持，不增加运行时配置发现职责 |

第 3 项还需明确以下边界，避免序列化前各消费者已经采用不同的等价规则：

- 一次多地址展开是分别形成绑定，还是形成含多个端点的绑定；例如同一候选项得到 A、B，另一声明只得到 B 时，如何去重、合并别名并保留唯一后端选择语义。
- URL 的缺省端口、尾斜杠、大小写、IPv4/IPv6 与通配形式怎样比较；保留有意义的路径，不能直接把 URL 字符串相等或端口相同当成等价。
- 协议列表采用第 4.6 节建议的大小写、空白及重复项规范化；为可选参数显式空值、空的分隔字符集合、根节点候选的默认别名等边界给出一致的有效性规则。未知协议及展开后的空地址项报错这一约束不变。
- 别名查找与去重沿用 Core 配置键比较规则的范围、输出大小写和稳定排序；身份生成不依赖来源顺序或数组位置。

后续按顺序逐个讨论，给出实际文件、配置片段和设计理由，不一次要求选择全部事项。已确认后直接更新对应章节并移除该待确认项，保持本文只描述当前方案。
