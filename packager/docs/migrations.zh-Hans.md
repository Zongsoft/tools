# 安装升迁

[English](migrations.md) | [简体中文](migrations.zh-Hans.md)

`--migration` 为 tar、Debian 和 RPM 安装包增加数据库建库、建表以及 S3 存储桶初始化。生成的中间文件名为 **`migration.json`**，安装位置为 `<安装目录>/.migration/migration.json`。用 `;` 或 `|` 分隔多个 `.ini` 路径。请引用整个选项，避免 Shell 解释分隔符和变量表达式。

```text
--migration:../../.deploy/$(scheme)/migration/$(version)/*.ini
```

命令行路径相对于 `--source`；INI 内的 SQL 路径相对于该 INI 文件。升迁文件及 `.env` 参数文件均使用部署工具所采用的 Zongsoft.Core `Profile` 解析器，支持整行 `#`/`;` 注释和无值条目。`Profile` 的条目名称不区分大小写，重复键报错。同一个 INI 中重复升迁器段落（包括 PostgreSQL 两个别名并存）也会报错；不同文件和重复指定同一文件仍独立解析。

升迁文件路径、SQL 路径、Bucket 名称/选项及参数值支持既有 `$(name)`、`%name%` 变量，在打包时展开。SQL 内容不进行变量展开，客户端批次分隔符在打包时处理。未定义变量、无匹配 SQL、未知升迁器/选项、缺少参数以及生成路径冲突均使打包以非零退出码失败。参数文件是 INI，不执行 Shell 表达式。

升迁 INI 路径支持文件名中的 `*` 和 `?`，例如 `../../.deploy/$(scheme)/migration/$(version)/*.ini`。每个模式按文件名 Ordinal 顺序展开，保留命令参数顺序；指定 INI 文件不存在或通配符无匹配时，输出警告并跳过，继续处理其余文件。若全部文件均未找到，仍生成普通安装包，不附带升迁文件、运行器及升迁启动门禁；已存在但无效的 INI、缺少 `.env` 参数以及缺少 SQL 脚本仍使打包失败。有效空 INI 不增加任务；若找到了 INI，但全部解析后没有任何非空段落，则报无升迁任务错误。

SQL 批次按规范升迁器名称组织，例如 `.migration/.artifacts/mysql/0001.sql` 和 `.migration/.artifacts/postgres/0001.sql`。每次加载计划时各升迁器从 0001 独立计数，同类任务共享连续编号；PostgreSQL 别名统一归入 postgres。每个非空段落仍是独立任务，保留自己的连接参数及脚本列表；任务 Id 用于日志和状态，不作为目录名。同段落内 SQL 重叠匹配去重，跨段落、跨文件和重复指定 INI 不合并或去重。S3 配置直接保存在计划中，不生成空中间目录。

## 升迁器与执行顺序

段落名称不区分大小写：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`/`postgresql`、`tdengine`、`amazon.s3`。空段落不创建任务；只要找到了 INI，全部输入合计必须产生至少一个任务。未知段落即使为空也会报错。

按命令行文件顺序、文件内段落顺序、段落内条目顺序解析，通配符匹配按文件名 Ordinal 排序并在当前参数位置展开，不对全部输入重新排序。不同任务之间不承诺执行顺序，不应依赖先后关系；每个数据库任务内部按脚本列表顺序执行。SQL 通配符 `*`、`?` 只允许出现在文件名中；每个模式匹配的文件按相对路径进行 Ordinal 排序，同一段落内重叠匹配只执行一次。建议文件名使用补零序号。每次安装和重试均执行全部 SQL，由脚本作者保证可重复执行，包括部分失败后的重试。不对跨脚本、跨资源操作自动回滚，详见下文重试规则。

数据库条目只写 SQL 路径，不写值。多数驱动接收完整文件；SQL Server、TDengine 需要分批，MySQL 的客户端 `DELIMITER` 指令在提交前适配。运行器不模拟交互式数据库客户端。

网络数据库先连接引导库、检查并创建目标库，再连接目标库执行 SQL。SQL Server 引导库默认 `master`，PostgreSQL 默认 `postgres`，MySQL/TDengine 默认不指定库，可通过 `BootstrapDatabase` 覆盖。连接账号需要相应建库、查询及 DDL 权限。SQLite/DuckDB 自动创建父目录和数据库文件；`Database` 必须是目标 Linux 的绝对路径。自定义服务用户需要拥有访问该数据库文件的权限。

## SQL 脚本如何提交给驱动

[`MigrationLoader.Database`](../src/MigrationLoader.Database.Batches.cs) 由打包器调用，处理客户端分隔符并生成可以直接提交给驱动的批次。每个批次写入 `<安装目录>/.migration/.artifacts/<升迁器名称>/<四位序号>.sql`，顺序和 SHA-256 校验和记录在计划中。运行器按计划逐文件读取并执行，不再分段。SQL 语法、结构变更和业务含义仍由脚本作者负责，SQL 错误由安装时的驱动或数据库报告。

| 升迁器 | 提交方式 |
| --- | --- |
| PostgreSQL、DuckDB、SQLite | 整个文件原样提交为一个命令，由驱动处理语句边界、函数及触发器体 |
| MySQL | 整个文件作为一个命令提交；移除实际的 `DELIMITER` 指令行，将引用/注释之外的自定义结束符替换为 `;`，保留存储过程体 |
| SQL Server | 按独立行 `GO` 分批，允许其后有 `--` 注释；保留批次内分号和变量作用域，不支持 `GO 2` |
| TDengine | 按引用/注释之外的分号分批提交 |

MySQL 连接始终开启 `AllowUserVariables=true`，支持同一会话里的 `SET @变量`、`PREPARE`、`EXECUTE` 和 `DEALLOCATE PREPARE`。同一任务的文件共用一个连接。重试会创建新连接并从头执行任务中的文件，脚本应初始化所需的会话变量和临时对象。

批次以 UTF-8 无 BOM 编码写入归档，校验和针对生成后的字节计算；保留批次内的换行，不修复 SQL 语法。完整文件提交的升迁器仍生成一个批次，避免破坏函数、触发器、存储过程和会话变量的作用域。MySQL 分隔符扫描遵循常规反斜杠转义；脚本切换到 `NO_BACKSLASH_ESCAPES` 模式时，应避免在客户端分隔符附近使用含义不明确的转义引用。不实现 `\i`、`SOURCE`、`.read`、`:r`、`:setvar` 等厂商客户端命令；其他 SQL 文件通过 INI 条目指定。`CommandTimeout` 作用于每次提交的命令，支持批量提交的驱动对应整个文件。失败会停止后续命令和文件，但哪些变更已提交由数据库决定。

## TDengine WebSocket 执行

[`Migrator.Database.TDengine`](../migrator/src/Migrator.Database.TDengine.cs) 使用 .NET 自带的 WebSocket 与 JSON API，不依赖 `TDengine.Connector`，也不随包携带 TDengine 客户端库。目标数据库需要提供 taosAdapter WebSocket 服务，`Server` 填主机名或 IP，`Port` 默认 `6041`。

运行器发送 `conn` 完成账号认证，然后依次发送 `query` 执行 `CREATE DATABASE IF NOT EXISTS`、`USE` 和各 SQL 批次。同一任务共用一个 WebSocket 会话。每个请求具有递增的 `req_id`，接收分片时组装完整 JSON，并核对响应动作和请求编号。对返回结果集的 SQL，发送 `free_result` 释放结果，不读取行数据，也不等待此动作的响应。协议见 [taosAdapter 查询接口源码](https://github.com/taosdata/taosadapter/blob/main/controller/ws/query/ws.go)。

`Timeout` 覆盖 WebSocket 握手与登录；`CommandTimeout` 分别限制每条 SQL 的发送、响应和结果释放。认证错误、SQL 错误、异常响应、断连、超时或取消都会终止本次升迁。错误提示不回显服务端返回的 SQL 或凭据；状态记录异常类型，详细 SQL 原因从服务端日志核查。

## `.env` 参数查找

不提供参数文件命令选项。针对每个升迁器，从 INI 所在目录逐级向父目录查找，直至文件系统根目录。**每一级目录**均按以下顺序查找：

1. 与升迁文件同名、扩展名改为 `.env` 的文件，读取对应段落。
2. `<升迁器>.env`；PostgreSQL 先 `postgres.env` 后 `postgresql.env`。对应段落优先于根条目；升迁器命名的文件允许省略段落。

没有适用段落的文件继续查找，升迁器命名且有根条目的文件除外。一旦找到适用配置，必须完整有效；不与其他文件或父目录合并，也不因缺少必填项而回退。多个升迁器共享参数文件时必须分段。最终未找到配置则打包失败，提示查找过的路径。文件名大小写遵循打包机文件系统，建议统一使用上述小写文件名。

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

## Zongsoft hosting 范例

当前本机 `D:/Zongsoft/hosting` 已有以下升迁配置，Web 的 `web/default/pack.cmd` 和 `deploy.cmd` 通过版本目录通配符选择 INI：

```text
.deploy/default/migration/
├── mysql.env
├── amazon.s3.env
└── 1.0.0/
    ├── mysql.ini
    └── amazon.s3.ini
```

`1.0.0/mysql.ini` 的前几个条目如下；实际文件还包含 Administratives 的建表和省市区、街道数据脚本，应保留原文件中的完整列表和顺序：

```ini
[mysql]
../../../../../framework/Zongsoft.Security/database/Zongsoft.Security-mysql.sql
../../../../../framework/upgrading/database/zongsoft.upgrading-mysql.sql
../../../../../discussions/database/Zongsoft.Discussions-mysql.sql
```

这些路径相对于 `1.0.0/mysql.ini` 所在目录，指向本机相邻仓库的实际 SQL。`1.0.0/amazon.s3.ini` 当前内容为：

```ini
[amazon.s3]
learning
temporary
upgrading
attachments
```

无值桶条目使用私有默认值，未指定加密、版本控制或标签，因此 JSON 省略这些可选属性，运行器不调用对应配置接口。共享 `mysql.env` 和 `amazon.s3.env` 位于父目录，可省略升迁器段落；连接参数按目标 MySQL、RustFS 配置填写，不在本文复制真实凭据。文件部署位置和数据库名称应与宿主插件配置一致。

从 `hosting/web/default` 打包时，现有命令的选项为（`%scheme%` 先由 CMD 展开，`$(version)` 由打包器使用最终应用版本展开）：

```text
--migration:"../../.deploy/%scheme%/migration/$(version)/*.ini;"
```

末尾空路径会被忽略。选择 `scheme=default`、版本 `1.0.0` 时，按文件名 Ordinal 顺序解析 `amazon.s3.ini`、`mysql.ini`，这不构成跨任务执行先后的承诺。若从 `hosting/daemon` 为源目录复用同一配置，增加以下选项；daemon 当前的 pack.cmd 本身没有配置升迁：

```text
"--migration:../.deploy/$(scheme)/migration/$(version)/*.ini"
```

此时需另外提供 `--scheme:default` 或同名环境变量。以 README 的 `hosting/publish` 暂存目录为源时也使用 `../.deploy/` 前缀。安装版本没有对应目录时只会警告并跳过，需确认本次版本目录包含所需初始化脚本。

保留 Web 宿主已有的载荷条目和 Nginx 钩子。Web 入口是 `Zongsoft.Hosting.Web.dll`，命令使用 `--name:Zongsoft.Hosting.Web --title:Zongsoft.Web --daemon:zongsoft.web --daemon-bind:8069`；daemon 命令使用 `--name:zongsoft.daemon`，其入口由既有宿主定位规则确定。上述双引号用于 CMD；PowerShell/Bash 使用单引号引用整个打包器表达式选项，避免 Shell 展开 `$()`，并将 `%scheme%` 换成打包器的 `$(scheme)` 或 Shell 已展开的实际值。

## migration.json 字段说明

`migration.json` 是打包器生成、migrator 读取的 UTF-8 JSON 对象。它保存已解析的执行输入，字段名按下表输出；运行器读取属性名时忽略大小写。

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `FormatVersion` | 整数 | 中间文件格式版本，当前为 `1`，与应用版本和打包器版本无关。 |
| `Package` | 字符串 | 最终安装包名称，例如 hosting Web 的 `zongsoft.web`，包含已选择的 Edition 后缀（如有）。 |
| `Version` | 字符串 | 本次打包的应用版本。 |
| `Tasks` | 数组 | 按输入解析顺序生成的独立任务；数组位置不代表任务依赖，不保证跨任务执行顺序。 |
| `Tasks[].Id` | 字符串 | 计划内唯一任务标识，例如 `0002-mysql`，用于日志和状态，不决定 SQL 目录。 |
| `Tasks[].Provider` | 字符串 | 规范升迁器名称：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`、`tdengine`、`amazon.s3`。 |
| `Tasks[].Parameters` | 字符串键值对象 | 从 `.env` 读取并展开变量后的连接参数；参数名忽略大小写，值仍是字符串，具体规则见上文参数说明。 |
| `Tasks[].Scripts` | 数组 | 数据库任务的有序批次列表；S3 任务为 `[]`。 |
| `Tasks[].Scripts[].Path` | 字符串 | 相对于安装根的批次路径，例如 `.migration/.artifacts/mysql/0001.sql`。同类任务共享目录和连续编号。 |
| `Tasks[].Scripts[].Checksum` | 字符串 | 包内批次实际 UTF-8 字节的 SHA-256，使用大写十六进制；执行前校验内容完整性。 |
| `Tasks[].Buckets` | 数组 | S3 任务的桶描述列表；数据库任务为 `[]`。 |
| `Tasks[].Buckets[].Name` | 字符串 | Bucket 名称，例如 hosting 的 `attachments`。 |
| `Tasks[].Buckets[].Public` | 布尔值 | `true` 表示配置公共读取策略；`false` 表示私有桶。 |
| `Tasks[].Buckets[].Encryption` | 对象，可省略 | 默认加密配置，包含 `Mode` 和可选 `Key`。 |
| `Tasks[].Buckets[].Encryption.Mode` | 字符串 | `sse-s3` 或 `sse-kms`。 |
| `Tasks[].Buckets[].Encryption.Key` | 字符串，可省略 | SSE-KMS 使用的已有密钥标识；仅适用于 `sse-kms`。 |
| `Tasks[].Buckets[].Versioning` | 字符串，可省略 | `enabled` 或 `suspended`。 |
| `Tasks[].Buckets[].Tags` | 字符串键值对象，可省略 | 桶标签，键名区分大小写；不是 INI 中带 `tag.` 前缀的原始选项名。 |

省略的桶配置不发送对应配置请求。`Script.Source` 和 `Script.Content` 只供打包端使用，不写入 JSON；原始 INI/ENV 路径和本机 SQL 源路径也不作为协议字段保存。连接参数可能包含密码或密钥，文件权限为 `0600`。

计划指纹不保存在 JSON 内：打包器对模型的无缩进 JSON UTF-8 字节计算 SHA-256，将大写十六进制结果写入 `.migration/id`；运行器全部执行成功后写入状态目录的 `ready`。它不是对带缩进的 `migration.json` 文件直接计算摘要。参数、脚本路径、校验和、桶配置以及数组顺序均参与指纹；缩进和文件换行不参与。指纹用于当前安装计划的完成判断，不是签名或逐脚本执行历史。

## 打包器与 migrator 的协作

两个程序通过安装包中的 `migration.json` 和 SQL 文件交接，参照 Zongsoft upgrading 的 upgrader/deployer 设计：协议源码分别编译到两端，运行时不加载对方的程序集。

| 组成部分 | 产物及职责 |
| --- | --- |
| [`src`](../src/Zongsoft.Tools.Packager.csproj) | `dotnet-pack` 工具：使用 Core Profile 读取 INI、查找 `.env`、展开变量及 SQL 路径、预处理 SQL 批次、解析 Bucket 选项、生成计划并收集安装载荷 |
| [`.shared`](../.shared/MigrationPlan.cs) | 计划模型、JSON 序列化/指纹、升迁器名称与参数校验及其本地化资源，分别链接编译到两端，不产生共享 DLL |
| [`migrator`](../migrator/src/Zongsoft.Tools.Packager.Migrator.csproj) | `Zongsoft.Tools.Packager.Migrator`：独立 Linux 命令行程序，负责数据库/S3 执行、文件完整性检查、锁、状态和完成标记 |

`MigrationPlan` 采用 partial 类，将 `Step`、`Script`、`Bucket` 描述组织为嵌套类型。打包侧 `Script.Source`（源路径）和 `Script.Content`（批次文本）只在主项目 partial 扩展中定义，不进入 JSON；运行器使用包内脚本路径和校验和。计划包含 `FormatVersion=1`、`Package`、`Version`、有序 `Tasks`；脚本记录包内 `Path` 和 SHA-256 `Checksum`。计划指纹是无缩进 JSON 的 UTF-8 SHA-256。命名空间统一为 `Zongsoft.Tools.Packager.Migration`，`MigrationProvider` 通过嵌套 Database/AmazonS3 描述管理规范名称、别名和参数规则，`MigrationUtility` 仅提供参数读取和超时解析，计划结构由 `MigrationPlan.Validate` 校验。

`MigrationLoader` 处理文件/段落遍历、变量、参数查找及错误定位。私有嵌套 `Database` 负责 SQL 选择、排序、去重、分段和校验和，`AmazonS3` 负责 Bucket 文本与重名检查。运行端 `Migrator.Database` 是抽象 partial 基类，共用 ADO.NET 执行辅助方法；嵌套 `MsSql`、`MySql`、`Postgres`、`Sqlite`、`DuckDB` 分别封装连接与建库规则，`TDengine` 封装 WebSocket 会话。`Migrator.Create` 显式分派，`MigrationExecutor` 负责校验、锁、调度和完成状态。驱动及 AWS SDK 只属于 migrator；计划使用源码生成 JSON，TDengine 请求、S3 策略、状态使用 `Utf8JsonWriter`。

主项目不引用或构建 migrator。`.shared/Migration.props` 将协议源码及资源分别编译到两端。与 upgrading 一样，打包器通过约定目录消费独立准备的可执行程序。migrator 目标为 **net10.0**，启用 `PublishAot` 和 `IsAotCompatible`；每个原生发布目录包含 `Zongsoft.Tools.Packager.Migrator` 及必要原生库。打包器将 `src/.migrator/<RID>/` 作为普通 Content 收入产物。宿主框架不决定原生升迁器的依赖。

1. **构建**：显式 Cake `migrator` 任务将 glibc `linux-x64`、`linux-arm64` 产物分别准备到 `src/.migrator/` 下；符号单独保存。完整工具包流程要求两个发布目录均成功准备，普通 `dotnet build/test` 不启动容器。
2. **打包**：`MigrationLoader` 生成计划，`MigrationBundle` 检查目标 RID 的 ELF 入口，收集整个目录，加入 SQL 批次、`migration.json`、`id` 和 `migrate.sh`，不分析或裁剪程序集依赖。
3. **安装**：`migrate.sh apply` 直接执行原生 migrator，成功退出后继续启动宿主；宿主不引用升迁程序集。

打包期间不连接数据库或 S3。两端协同按以下文件完成：

| 文件 | 写入方 → 读取方 | 内容 |
| --- | --- | --- |
| `.migration/migration.json` | 打包器 → migrator | 已展开的连接参数、有序任务、SQL 路径与校验和、Bucket 描述；权限 `0600` |
| `.migration/.artifacts/<升迁器名称>/<四位序号>.sql` | 打包器 → migrator | 可直接执行的 SQL 批次；执行前统一验证校验和，权限 `0644` |
| `.migration/id` | 打包器 → `migrate.sh check` | 当前计划指纹；权限 `0644` |
| `/var/lib/<包名>/packager/status.json` | migrator → `status` | 最近一次执行完成或失败时记录的任务、结果及异常类型，并非实时进度 |
| `/var/lib/<包名>/packager/ready` | migrator → `migrate.sh check` | 全部任务成功后写入的计划指纹；权限 `0644` |

例如 Web 宿主的计划路径是 `/opt/zongsoft/web/.migration/migration.json`，完成标记为 `/var/lib/zongsoft.web/packager/ready`。Shell 以退出码把升迁结果传回安装流程：成功为 `0`，执行失败为 `1`，独立运行器参数用法错误为 `2`。安装流程只有收到成功结果才继续启动宿主。


### 原生构建环境

[`migrator/build/packager.linux-x64.yaml`](../migrator/build/packager.linux-x64.yaml) 定义独立 Rocky Linux 9 Pod 和持久化工具链缓存；工作区默认经 `/mnt/d/` 挂载 `D:\Zongsoft\tools\packager`，其他检出位置需要调整挂载路径。参考用的 framework Pod 不受影响。显式执行 Cake `migrator` 任务前需要启动 Podman。

`setup.sh` 安装 .NET 10 SDK、clang/lld，并从同一 Rocky 基线提取 ARM64 RPM 到目标 sysroot；`publish.sh` 原生编译 x64，使用 sysroot 和交叉链接器编译 ARM64。完整发布包含必要 `.so` 和语言资源；符号、完整警告和 ELF 检查记录位于 `migrator/src/bin/aot/<RID>/`。项目中的依赖版本保持固定。原生执行验证与编译分别记录，没有原生或仿真环境时可以省略 ARM64 执行验证；实际结果见[验证记录](migration-verification.md)。

### 构建与工具包生成

在 packager 目录顺序执行：

```powershell
dotnet cake --target=migrator --edition=Release
dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release
```

只编译打包器时使用 `dotnet build src/Zongsoft.Tools.Packager.csproj`，无需 migrator 项目或其产物，也不会自动生成 NuGet 包。制作完整工具包前先运行上面的 `migrator` 任务；也可以将独立发布的运行器完整目录分别放入 `src/.migrator/linux-x64/` 和 `src/.migrator/linux-arm64/`，再执行 `dotnet pack`。如果未准备该目录，工具仍可构建和处理普通包，存在有效升迁任务时会明确提示缺少对应 RID 的运行器；全部 INI 缺失并被跳过时不要求运行器。无需指定新的命令选项或 MSBuild 路径属性。

[`test`](../test/Zongsoft.Tools.Packager.Tests.csproj) 覆盖打包器输入、制包和两端 JSON 交接；[`migrator/test`](../migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj) 只引用运行器，覆盖数据库、S3 及 TDengine WebSocket。交接测试启动独立 migrator 的 `apply/check` 命令，验证真实 SQLite 批次、任务各自的连接参数、任务内部顺序和主端指纹；另验证任务数组变化使指纹变化，以及篡改 SQL 导致执行失败并清除完成标记。主测试项目对运行器设置 `ReferenceOutputAssembly=false`，仅保留构建依赖；两组测试均不使用程序集或 `using` 别名。两个测试项目可分别运行：

```powershell
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0
dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0
```

## migrate.sh 的实现原理

脚本由 [`MigrationBundle.Attach`](../src/MigrationBundle.cs) 根据包身份生成，使用 LF 换行及 `0755` 权限。以下是既有 hosting Web 参数（包名 `zongsoft.web`）对应的生成内容：

```sh
#!/bin/sh
set -eu
BASE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ "${1:-apply}" = check ]; then
	if cmp -s '/var/lib/zongsoft.web/packager/ready' "$BASE_DIR/id"; then exit 0; fi
	case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
		zh*) printf '%s\n' '此安装包的升迁尚未完成。' ;;
		*) printf '%s\n' 'Migration has not completed for this package.' ;;
	esac >&2
	exit 1
fi
exec "$BASE_DIR/Zongsoft.Tools.Packager.Migrator" "${1:-apply}" "$BASE_DIR/migration.json" '/var/lib/zongsoft.web/packager'
```

`BASE_DIR` 根据脚本自身所在目录确定，不依赖调用者当前目录；上述脚本安装后位于 `/opt/zongsoft/web/.migration/migrate.sh`。`${1:-apply}` 表示未给出动作时默认执行 `apply`。`exec` 用原生进程替换 Shell 进程，使运行器的退出码直接成为该脚本的退出码。脚本本身不包含 SQL、连接字符串或建桶实现。

| 动作 | 实际路径 | 依赖与结果 |
| --- | --- | --- |
| `apply`（默认） | Shell → migrator → `MigrationExecutor` → 数据库/S3 升迁器 | 需要原生 migrator 发布目录及系统库；成功返回 0，失败返回非零，不直接启动宿主 |
| `status` | Shell → migrator，读取状态并检查计划指纹 | 需要原生 migrator 发布目录；输出状态，本计划已完成时返回 0，否则返回 1 |
| `check` | Shell 直接使用 `cmp -s` 比较 `ready` 与包内 `id` | 不启动 migrator、不读取含凭据的 `migration.json`、不连接数据库/S3；文件存在且字节相同返回 0，否则返回 1 |

**`apply/status` 直接执行原生 `Zongsoft.Tools.Packager.Migrator`。** 应分发对应 RID 的完整发布目录，包括必要的 `.so` 文件；不经过 `dotnet` 启动，不处理 `.deps.json`，目标机无需 .NET 运行时，但仍需要系统原生库。

`MigrationExecutor` 在取得文件锁后清除旧 `ready`，校验全部 SQL 文件，当前实现再串行执行任务；串行遍历是实现方式，跨任务先后不构成执行契约。成功后记录状态，把当前计划指纹写入临时文件并通过替换文件形成 `ready`。指纹采用无缩进 JSON 的 UTF-8 SHA-256，避免 Windows/Linux 格式换行导致不同结果；`id` 是打包时产生的同一指纹。两者可读权限为 `0644`，使普通服务用户能够检查，而不必读取 `0600` 的参数文件。

[`Scriptor.Systemd`](../src/Scriptor.Systemd.cs) 在安装流程中插入 `migrate.sh apply`，并在服务 drop-in 中生成 `ExecStartPre=/bin/sh "/opt/zongsoft/web/.migration/migrate.sh" check`。因此初次安装要完成升迁，之后每次服务启动只检查完成标记，不会在每次启动时重新运行 SQL。直接执行宿主 DLL 会绕过这项 systemd 检查。

`check` 只证明这份包计划存在对应的成功标记，不是数据库健康检查，也不会重新计算 SQL 文件校验和或确认 Bucket/表仍然存在。migrator 本身也有 `check` 子命令，但它会加载计划并计算指纹；生成的 Shell 为服务启动采用上述无需启动 migrator 的文件比较分支。

## 安装与恢复

启用升迁时，安装停止服务、部署文件、清除完成标记、建立 systemd 启动检查、执行升迁，成功后才执行 installed/启动逻辑和 postinstalled 钩子。`preinstalled` 位于升迁之前，不应启动应用。自定义生命周期钩子需要履行对应阶段职责。`--daemon:none` 和自定义 `--installed` 均不会省略强制升迁步骤。

Debian 仅在 `postinst configure` 执行升迁；RPM 使用 `%post`。失败返回非零，不执行后续启动/钩子。升迁包默认服务停止、启动命令的失败不再被忽略。全部任务成功后才写完成标记；systemd 的 `20-packager-migration.conf` drop-in 在重启机器后仍会阻止未完成升迁的包启动。保留原服务文件，自定义服务也必须能执行该 `ExecStartPre` 检查。

状态保存在应用目录之外的 `/var/lib/<包名>/packager`。文件锁阻止同一包的运行器并发执行；状态记录任务和异常类型，不记录连接值或 SQL。执行外部数据库或 S3 操作前验证所有 SQL 校验和；`status/check` 不读取 SQL 内容。排除失败原因后可重新配置/安装，也可使用安装权限直接运行已安装入口：

```sh
/opt/zongsoft/web/.migration/migrate.sh status
/opt/zongsoft/web/.migration/migrate.sh apply
/opt/zongsoft/web/.migration/migrate.sh check
```

`apply` 重新执行全部 SQL 并检查 S3 初始化，不启动应用。`check` 只有在本包计划成功完成时返回零。卸载移除程序/服务文件，但保留数据库、Bucket 和升迁状态。失败不会撤销已执行的 SQL、已创建的 Bucket 或已部署的程序文件。

tar 的 `DESTDIR` 仅用于暂存：安装/卸载均跳过全部生命周期钩子、升迁和服务操作。升迁包采用打包时确定的安装路径；实际安装时改变 `INSTALL_PATH` 会在部署前报错，修改目录请使用 `--install-path` 重新打包。升迁安装路径要求绝对路径，由字母、数字、`.`、`_`、`+`、`-` 及 `/` 组成，不包含 `.`/`..` 路径段。

## 脚本幂等性与失败重试

首次安装、升级、覆盖安装以及每次 `apply` 均执行全部配置的 SQL 文件，顺序保证限定在各数据库任务内部。不维护逐文件成功历史，也不根据先前执行结果跳过文件。结构变更和数据操作的可重复执行性由脚本作者保证：新增、更名、删除前检查对象存在性及预期状态，数据插入与更新避免重复写入或重复累加。遇到不符合预期的状态应明确报错，不应直接跳过必要变更。

同一数据库任务内文件 A 成功、B 失败后，重试仍从该任务的首个文件开始，包括重新执行 A。B 也可能已有部分语句提交，脚本应处理这些中间状态，或由操作者在重试前修复。打包器不自动回滚 SQL，事务由脚本在数据库支持的范围内控制。

SQL 校验和只用于在外部资源操作之前验证包内文件与当前计划一致，不是执行历史，也不阻止重新生成的包执行修订后的 SQL。既有文件锁、状态及 ready 标记控制安装是否完成，不用于在 `apply` 时跳过文件。

## 运行时与产物

升迁包支持 glibc `linux-x64`、`linux-arm64`，以 Rocky Linux 9/glibc 2.34 为构建基线，不支持 Alpine/musl。原生运行器及必要 `.so` 文件位于 `.migration/`。目标机需要 glibc 2.34 或更高版本、libgcc、libstdc++、zlib、ICU、OpenSSL 和 CA 证书；PostgreSQL/SQL Server 的认证功能还可能需要发行版提供的 Kerberos/GSSAPI 库。每次发布用 `readelf`/`ldd` 核对实际动态依赖。生成的 Shell 还需要 POSIX `sh`、基础命令及 `cmp`（Rocky Linux 的 `diffutils` 软件包）。无需 .NET 运行时、数据库 CLI、AWS CLI 或 `mc`，可用既有 `--dependencies` 声明发行版对应的软件包依赖。

`.migration/migration.json` 包含**展开后的连接参数，包括密码和密钥**，安装权限为 `0600`。该权限不加密归档；安装包应按含凭据的产物分发和保管。原 `.env` 不会被自动复制，部署配置应放在载荷条目之外。`.migration/`（含 `.artifacts/`）为生成内容保留目录。SQL 路径相对于安装根目录，运行器只允许访问 `.migration/.artifacts/` 内的文件。配置在打包时固定，之后修改打包机的 `.env` 不会改变已有安装包。

## 本地化

主项目、共享参数校验和 migrator 各自使用 `.resx` 资源，默认英文，提供简体中文 `zh-Hans`。资源配置 `ResXFileCodeGenerator` 生成强类型访问类，共享资源分别嵌入两个程序集。原生发布保留中英文资源和全球化能力。运行器按照 `CurrentUICulture` 选择提示语言；生成的 Shell 根据 `LC_ALL`、`LC_MESSAGES`、`LANG` 的优先级选择中文或英文，`check` 无需启动 .NET。JSON 字段、状态值、动作名和指纹数据不随显示语言改变。
