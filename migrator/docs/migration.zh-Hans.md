# 升迁输入与执行计划

命令、产物命名和执行方法见 [README](../README.zh-Hans.md)；项目职责与原生构建见[实现说明](implementation.zh-Hans.md)。

## 版本来源与变量

生成时先解析 `--version`，再展开升迁输入和输出路径。版本号、文件/目录路径及省略选项的 hosting 用法见 [README](../README.zh-Hans.md#版本选择)。

- 未指定、空串或全空白值只读取 `<当前工作目录>/.version`，不查找父目录或子目录，也不使用环境变量 `version` 代替。
- 能由 `System.Version` 解析的值直接作为版本号，支持两段、三段和四段数字；全零版本被拒绝。此时即使 `.version` 存在但已损坏，也不会打开它。
- 其余值展开后作为文件路径，支持绝对路径，相对路径基于当前目录。现有目录追加直属 `.version`；文件不限扩展名，类似数字的文件名用 `./1.0.0` 指定。文件缺失、不可读或损坏时失败，诊断包含实际查找的完整路径。
- 由 Core `ApplicationVersion` 加载文件。空 Edition 使用顶层版本或唯一具名 Edition；多个具名 Edition 必须明确选择。指定 Edition 忽略大小写匹配段落，单版本文件不允许指定具名 Edition；保留选中段落的拼写。
- `--name` 仍必填，不从文件推导，也不要求与文件中的应用名称一致。成功或失败均不创建、保存或改写版本文件。

其他变量保持显式选项优先于环境变量的规则。版本路径使用同一变量展开机制，未知或循环引用报错。选定版本前最终 `$(version)` 尚不可用，不能用它定位自身的来源文件。选定后，最终版本与 Edition 的规范拼写回填生成变量，确保输入/输出路径、计划身份和两份产物名称一致。选择当前目录之外的版本文件不会改变升迁输入或输出路径的相对基准。版本解析失败不会改动已有输出，即使指定了 `--overwrite`。

## 升迁器与执行顺序

段落名称不区分大小写：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`/`postgresql`、`tdengine`、`amazon.s3`。空段落不创建任务；只要找到了 INI，全部输入合计必须产生至少一个任务。未知段落即使为空也会报错。

按命令行文件顺序、文件内段落顺序、段落内条目顺序解析，通配符匹配按相对路径 Ordinal 排序并在当前参数位置展开，不对全部输入重新排序。不同任务之间不承诺执行顺序，不应依赖先后关系；每个数据库任务内部按脚本列表顺序执行。SQL 路径同样支持路径段 `*`、`?` 和独立段 `**`；每个模式匹配的文件按相对路径进行 Ordinal 排序，同一来源段落内重叠匹配只执行一次。建议文件名使用补零序号。每次安装和重试均执行全部 SQL，由脚本作者保证可重复执行，包括部分失败后的重试。不对跨脚本、跨资源操作自动回滚，详见下文重试规则。

数据库条目只写 SQL 路径，不写值。多数驱动接收完整文件；SQL Server、TDengine 需要分批，MySQL 的客户端 `DELIMITER` 指令在提交前适配。运行器不模拟交互式数据库客户端。

网络数据库先连接引导库、检查并创建目标库，再连接目标库执行 SQL。SQL Server 引导库默认 `master`，PostgreSQL 默认 `postgres`，MySQL/TDengine 默认不指定库，可通过 `BootstrapDatabase` 覆盖。连接账号需要相应建库、查询及 DDL 权限。SQLite/DuckDB 自动创建父目录和数据库文件；`Database` 必须是目标系统的绝对路径（Linux 为 `/` 开头；Windows 为完整盘符或 UNC 路径）。自定义服务用户需要拥有访问该数据库文件的权限。

## SQL 脚本如何提交给驱动

[`MigrationLoader.Database`](../src/MigrationLoader.Database.Batches.cs) 由升迁制作工具调用，处理客户端分隔符并生成可以直接提交给驱动的批次。每个批次写入 `<解压目录>/.migration/.artifacts/<升迁器名称>/<四位序号>.sql`，顺序和 SHA-256 校验和记录在计划中。运行器按计划逐文件读取并提交预处理后的批次。SQL 语法、结构变更和业务含义仍由脚本作者负责，SQL 错误由执行时的驱动或数据库报告。

| 升迁器 | 提交方式 |
| --- | --- |
| PostgreSQL、DuckDB、SQLite | 整个文件原样提交为一个命令，由驱动处理语句边界、函数及触发器体 |
| MySQL | 整个文件作为一个命令提交；移除实际的 `DELIMITER` 指令行，将引用/注释之外的自定义结束符替换为 `;`，保留存储过程体 |
| SQL Server | 按独立行 `GO` 分批，允许其后有 `--` 注释；保留批次内分号和变量作用域，不支持 `GO 2` |
| TDengine | 按引用/注释之外的分号分批提交 |

MySQL 连接始终开启 `AllowUserVariables=true`，支持同一会话里的 `SET @变量`、`PREPARE`、`EXECUTE` 和 `DEALLOCATE PREPARE`。同一任务的文件共用一个连接。重试会创建新连接并从头执行任务中的文件，脚本应初始化所需的会话变量和临时对象。

批次以 UTF-8 无 BOM 编码写入归档，校验和针对生成后的字节计算；保留批次内的换行，不修复 SQL 语法。完整文件提交的升迁器仍生成一个批次，避免破坏函数、触发器、存储过程和会话变量的作用域。MySQL 分隔符扫描遵循常规反斜杠转义；脚本切换到 `NO_BACKSLASH_ESCAPES` 模式时，应避免在客户端分隔符附近使用含义不明确的转义引用。不实现 `\i`、`SOURCE`、`.read`、`:r`、`:setvar` 等厂商客户端命令；其他 SQL 文件通过 INI 条目指定。`CommandTimeout` 作用于每次提交的命令，支持批量提交的驱动对应整个文件。失败会停止后续命令和文件，但哪些变更已提交由数据库决定。

## TDengine WebSocket 执行

[`Migrator.Database.TDengine`](../executor/src/Migrator.Database.TDengine.cs) 使用 .NET 自带的 WebSocket 与 JSON API，不依赖 `TDengine.Connector`，也不随包携带 TDengine 客户端库。目标数据库需要提供 taosAdapter WebSocket 服务，`Server` 填主机名或 IP，`Port` 默认 `6041`。

运行器发送 `conn` 完成账号认证，然后依次发送 `query` 执行 `CREATE DATABASE IF NOT EXISTS`、`USE` 和各 SQL 批次。同一任务共用一个 WebSocket 会话。每个请求具有递增的 `req_id`，接收分片时组装完整 JSON，并核对响应动作和请求编号。对返回结果集的 SQL，发送 `free_result` 释放结果，不读取行数据，也不等待此动作的响应。协议见 [taosAdapter 查询接口源码](https://github.com/taosdata/taosadapter/blob/main/controller/ws/query/ws.go)。

`Timeout` 覆盖 WebSocket 握手与登录；`CommandTimeout` 分别限制每条 SQL 的发送、响应和结果释放。认证错误、SQL 错误、异常响应、断连、超时或取消都会终止本次升迁。错误提示不回显服务端返回的 SQL 或凭据；状态记录异常类型，详细 SQL 原因从服务端日志核查。

## 导入配置文件

升迁 INI 和 `.env` 均支持 Core 的 `#@import` 指令。例如：

```ini
# migration/main.migration
#@import shared/schema.migration
[sqlite]
./main.sql
```

```ini
# migration/shared/schema.migration
[sqlite]
./schema.sql
```

`schema.sql` 相对于 `migration/shared/`，它的参数从该目录的 `schema.env`、`sqlite.env` 开始逐级查找；`main.sql` 则从 `migration/` 查找 `main.env`、`sqlite.env`。两者形成各自持有连接参数的任务。S3 条目也从其声明所属 INI 查找参数。

参数文件可以显式导入公共配置后覆盖值：

```ini
# migration/sqlite.env
#@import common.env
Database=/var/lib/example/application.db
```

`common.env` 可声明根参数，也可声明完整的升迁器段落；沿用参数选择规则，根参数不会补入已选中的段落。只有选中的候选文件及其显式导入参与合并，缺少必需参数时仍报错，不从其他候选文件自动补齐。

- 导入路径相对于包含该指令的文件，也可为绝对路径。多个路径用空格、Tab 或 `|` 分隔，不支持引号转义、通配符或变量展开。指令合并完整 Profile，即使位于段落内也不会把子文件根条目移入当前段落。
- 缺失的导入文件按 Core 可选导入规则跳过。循环导入或超过 64 层（含根文件）时失败；允许菱形及重复导入，每次重新读取。配置链接保留逻辑来源，导入、SQL 和旁侧参数以链接位置为基准。
- 每个文件单独检查重复段落、重复键及语法。升迁 INI 的根条目、未知段落和同一有效配置中的升迁器别名冲突仍报错，导入文件也不例外。
- 不同文件的同段同名条目按读取顺序覆盖，名称不区分大小写；有效集合中仍使用该键第一次出现的位置。被覆盖的 SQL/Bucket 条目不会生成任务。需要独立执行同名条目时，应使用多个命令行 INI 输入。
- 一个有效段落按连续的声明来源拆成任务，保留有效条目顺序；同一来源在该段落内的 SQL 重叠选择跨任务去重，不对不同来源或独立输入去重。各来源保留自己的参数。跨任务执行顺序仍不作为依赖契约。
- 导入文件解析和参数展开错误报告实际来源，参数错误不输出敏感值。所有解析在打包阶段完成，不因导入而执行 SQL 或访问 S3。

## `.env` 参数查找

不提供参数文件命令选项。针对每个升迁器，从条目声明所属 INI 的目录逐级向父目录查找，直至文件系统根目录。**每一级目录**均按以下顺序查找：

1. 与升迁文件同名、扩展名改为 `.env` 的文件，读取对应段落。
2. `<升迁器>.env`；PostgreSQL 先 `postgres.env` 后 `postgresql.env`。对应段落优先于根条目；升迁器命名的文件允许省略段落。

没有适用段落的文件继续查找，升迁器命名且有根条目的文件除外。一旦找到适用配置（包括其显式导入），必须完整有效；不与其他候选文件或父目录自动合并，也不因缺少必填项而回退。多个升迁器共享参数文件时必须分段。最终未找到配置则打包失败，提示查找过的路径。文件名大小写遵循打包机文件系统，建议统一使用上述小写文件名。

| 升迁器 | 必填参数 | 可选参数 |
| --- | --- | --- |
| `mssql`、`mysql`、`postgres`、`tdengine` | `Server`、`Database`、`UserName` | `Port`、`Password`、`BootstrapDatabase`、`Timeout`、`CommandTimeout`、`Secured` |
| 仅 `mssql` | — | `TrustServerCertificate`，默认 `false` |
| `sqlite`、`duckdb` | `Database` | `CommandTimeout`；接受 `Timeout`，但文件数据库没有网络连接超时 |
| `amazon.s3` | `Server`、`Region`、`AccessKey`、`SecretKey` | `Timeout` |

参数名不区分大小写。`Timeout` 默认 30 秒，`CommandTimeout` 默认 300 秒；支持正整数及 `s`、`m` 后缀，上限一天。`Port` 范围为 1–65535；布尔值使用 `true`/`false`；省略密码表示空密码。SQL Server 默认启用加密。MySQL/PostgreSQL 未指定 `Secured` 时使用驱动默认值。TDengine 通过 .NET `ClientWebSocket` 直连 taosAdapter 的 `/rest/ws`，端口默认 `6041`，`Secured=true` 使用 `wss`，否则使用 `ws`。`TrustServerCertificate` 仅 SQL Server 可用。

S3 的 `Server` 为完整的 `http://` 或 `https://` 端点，不能嵌入账号密码。客户端使用 path-style 请求，适用于 RustFS。Bucket 条目支持以下选项，以逗号或 `|` 分隔：

| 选项 | 含义 |
| --- | --- |
| `private` / `public` | 互斥，默认 private；public 通过 Bucket Policy 授予匿名 `s3:GetObject`，不使用 ACL，不开放匿名写入或列表 |
| `encryption:sse-s3` | 默认使用 S3 管理密钥的 AES-256 服务端加密 |
| `encryption:sse-kms` | 默认使用 KMS 服务端加密；可同时指定 `encryption.key:<密钥标识>`，省略密钥标识时采用服务端默认 KMS 密钥 |
| `versioning:enabled` / `versioning:suspended` | 启用或暂停版本控制；暂停不删除已有历史版本 |
| `tag.<名称>:<值>` | 桶标签，可指定多个；名称区分大小写，允许空值；最多50个，名称和值分别不超过128、256个UTF-16代码单元，名称不得为空或以 aws: 开头 |

选项名称和模式值不区分大小写；标签名称、标签值和密钥标识保留大小写。值以引号外的第一个冒号分隔，因此 KMS ARN 中的冒号不会被再次分段；选项首尾空白忽略。标签名称包含冒号时也须用引号包围（例如 `tag."team:owner":zongsoft`）。单引号或双引号包围的文本可包含逗号和 `|`，同种引号连续写两次表示一个引号。访问权限、加密模式、密钥和版本控制不得重复，同名标签也不得重复。未知选项、缺少值、无效模式及未闭合引号均使打包失败；`encryption.key` 只能与 `encryption:sse-kms` 一起使用。

以下展示 hosting 现有桶名的可选配置写法，不表示当前 hosting 文件已启用这些配置：

```ini
[amazon.s3]
attachments=private,encryption:sse-s3,versioning:enabled,tag.application:zongsoft.web,tag.environment:$(environment)
learning=private,versioning:enabled
```

省略加密、版本控制或标签时，不调用对应配置接口，沿用服务端默认。`Encryption` 默认是 null，整个对象不写入 JSON；显式提供对象时 `Mode` 必须为 `sse-s3` 或 `sse-kms`，空对象或空 Mode 无效。加密配置作用于后续上传对象，不重加密已有对象；Amazon S3 的基础服务端加密不能关闭，因此不提供 encryption:false。SSE-C 涉及每次对象请求的客户密钥，不作为桶默认加密选项；本功能不创建或管理 KMS 密钥。容量配额、生命周期、CORS、日志、对象锁和 `internal` 不在支持范围。

已有 Bucket 直接跳过，不改变其权限或其他配置。新桶按版本控制、加密、标签、公共读取策略的顺序初始化，全部成功后才清除本地 pending 记录。若建桶后配置失败，保留记录以便下次重试重新应用所指定的配置，不跳过部分配置操作。认证、权限错误不视为“桶不存在”；服务端不支持已指定的配置或拒绝请求时安装失败并阻止启动，不静默忽略。S3 服务端的公共访问限制也可能拒绝公开策略。

## migration.json 字段说明

`migration.json` 是升迁制作工具生成、migrator 读取的 UTF-8 JSON 对象。它保存已解析的执行输入，字段名按下表输出；运行器读取属性名时忽略大小写。

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `FormatVersion` | 整数 | 中间文件格式版本，当前为 `1`，与应用版本和升迁制作工具版本无关。 |
| `Package` | 字符串 | 规范升迁名称，包含自动补全的升迁后缀及 Edition（如有），例如 `zongsoft-migrate` 或 `zongsoft-migrate-enterprise`。 |
| `Version` | 字符串 | 制作升迁产物时指定的版本。 |
| `Runtime` | string | 目标 RID：`linux-x64`、`linux-arm64` 或 `win-x64`，参与指纹并在执行前校验 |
| `Title` / `Summary` / `Description` | string? | 可选展示文本；未设置时不输出，参与指纹 |
| `Tasks` | 数组 | 按输入解析顺序生成的独立任务；数组位置不代表任务依赖，不保证跨任务执行顺序。 |
| `Tasks[].Id` | 字符串 | 计划内唯一任务标识，例如 `0002-mysql`，用于日志和状态，不决定 SQL 目录。 |
| `Tasks[].Provider` | 字符串 | 规范升迁器名称：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`、`tdengine`、`amazon.s3`。 |
| `Tasks[].Parameters` | 字符串键值对象 | 从 `.env` 读取并展开变量后的连接参数；参数名忽略大小写，值仍是字符串，具体规则见上文参数说明。 |
| `Tasks[].Scripts` | 数组 | 数据库任务的有序批次列表；S3 任务为 `[]`。 |
| `Tasks[].Scripts[].Path` | 字符串 | 相对于升迁归档解压根目录的批次路径，例如 `.migration/.artifacts/mysql/0001.sql`。同类任务共享目录和连续编号。 |
| `Tasks[].Scripts[].Checksum` | 字符串 | 包内批次实际 UTF-8 字节的 SHA-256，使用大写十六进制；执行前校验内容完整性。 |
| `Tasks[].Buckets` | 数组 | S3 任务的桶描述列表；数据库任务为 `[]`。 |
| `Tasks[].Buckets[].Name` | 字符串 | Bucket 名称，例如 hosting 的 `attachments`。 |
| `Tasks[].Buckets[].Public` | 布尔值 | `true` 表示配置公共读取策略；`false` 表示私有桶。 |
| `Tasks[].Buckets[].Encryption` | 对象，可省略 | 默认加密配置，包含 `Mode` 和可选 `Key`。 |
| `Tasks[].Buckets[].Encryption.Mode` | 字符串 | `sse-s3` 或 `sse-kms`。 |
| `Tasks[].Buckets[].Encryption.Key` | 字符串，可省略 | SSE-KMS 使用的已有密钥标识；仅适用于 `sse-kms`。 |
| `Tasks[].Buckets[].Versioning` | 字符串，可省略 | `enabled` 或 `suspended`。 |
| `Tasks[].Buckets[].Tags` | 字符串键值对象，可省略 | 桶标签，键名区分大小写；不是 INI 中带 `tag.` 前缀的原始选项名。 |

省略的桶配置不发送对应配置请求。`Script.Source` 和 `Script.Content` 只供升迁生成端使用，不写入 JSON；原始 INI/ENV 路径和本机 SQL 源路径也不作为协议字段保存。连接参数可能包含密码或密钥，文件权限为 `0600`。

计划指纹不保存在 JSON 内：升迁制作工具对模型的无缩进 JSON UTF-8 字节计算 SHA-256，将大写十六进制结果写入 `.migration/id`；运行器全部执行成功后写入状态目录的 `ready`。它不是对带缩进的 `migration.json` 文件直接计算摘要。参数、脚本路径、校验和、桶配置以及数组顺序均参与指纹；缩进和文件换行不参与。指纹用于当前安装计划的完成判断，不是签名或逐脚本执行历史。

## 脚本幂等性与失败重试

首次安装、升级、覆盖安装以及每次 `apply` 均执行全部配置的 SQL 文件，顺序保证限定在各数据库任务内部。不维护逐文件成功历史，也不根据先前执行结果跳过文件。结构变更和数据操作的可重复执行性由脚本作者保证：新增、更名、删除前检查对象存在性及预期状态，数据插入与更新避免重复写入或重复累加。遇到不符合预期的状态应明确报错，不应直接跳过必要变更。

同一数据库任务内文件 A 成功、B 失败后，重试仍从该任务的首个文件开始，包括重新执行 A。B 也可能已有部分语句提交，脚本应处理这些中间状态，或由操作者在重试前修复。升迁制作工具不自动回滚 SQL，事务由脚本在数据库支持的范围内控制。

SQL 校验和只用于在外部资源操作之前验证包内文件与当前计划一致，不是执行历史，也不阻止重新生成的包执行修订后的 SQL。文件锁、状态及 ready 标记控制安装是否完成，不用于在 `apply` 时跳过文件。
