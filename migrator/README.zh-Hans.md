# Zongsoft 升迁工具

[English](README.md) | [简体中文](README.zh-Hans.md)

dotnet-migrate 制作可移交的升迁归档和启动脚本。制作阶段描述并准备变更，执行阶段将升迁包应用到目标环境。

## 基础概念

- **升迁输入**：.migration 格式的 INI 文件，在 provider 段中列出 SQL 文件或 Amazon S3 桶。段落和条目顺序决定任务顺序。
- **连接配置**：.env 文件提供管理员凭据、建库设置、应用账号及 Amazon S3 凭据。制作升迁包时会展开变量。
- **升迁包和启动脚本**：同名前缀的 .tar.gz 归档与 .sh 或 .cmd 文件。归档保存解析后的计划和数据；启动脚本调用包内执行器。两个文件必须放在一起。
- **数据库目标**：provider 的 Database 指定默认库；具名升迁段可指定其他库。空数据库段仍表示需要初始化。
- **计划指纹（fingerprint）**：根据最终升迁计划计算的 SHA-256 摘要，用于识别升迁包中的计划内容。计划变化时指纹也会变化；它不是目标数据库的校验和，也不描述数据库当前的数据或结构。
- **目标机状态目录**：升迁器在本地保存执行记录的目录，不是数据库或桶的实时状态视图。默认位于启动脚本旁的 `.migration/<升迁名称>[-edition]/`，也可作为启动脚本参数指定。跨版本执行时应沿用同一目录，才能持续判断执行完成情况。
- **`ready` 文件**：状态目录中的成功标记。`apply` 开始时会删除旧标记，只有全部初始化、SQL 任务和授权都成功后才写入当前包的指纹。因此失败或中断不会留下仍可匹配的旧标记。
- **`status` 命令和 `status.json` 文件**：`status` 显示最近一次执行报告，包括阶段、步骤和失败原因；尚未执行过时会提示“尚未执行升迁”。只有当前包也与成功的 `ready` 标记匹配时才返回 0，否则返回 1。`status.json` 是运行报告，`ready` 才是成功标记。
- **`check` 命令**：比较当前包的指纹与 `ready` 标记，匹配返回 0，缺少标记或内容不同时返回 1。它只检查本地包和状态文件，不连接数据库或 Amazon S3、不验证凭据，也不预演 SQL。
- **现有资源**：已有数据库设置和用户密码会保留；权限和角色关系只追加。

制作阶段准备输入，不连接目标服务。执行阶段才会修改数据库和 Amazon S3 桶。

> 💡 提示：可以先制作并检查升迁包，不必连接目标服务。应用到生产环境前，先在预发布环境验证。

<a id="package-phase"></a>

## 制作阶段
### 安装和制作

```powershell
dotnet tool install -g Zongsoft.Tools.Migrator
```

本地源码测试：在 migrator 目录执行 `dotnet cake --edition Release --target build`，准备三个 RID 并生成工具包。已有原生产物时可执行 `dotnet cake --edition Release --target compile`。首次本地安装使用 `dotnet tool install -g Zongsoft.Tools.Migrator --version 0.1.0 --source ./src/bin/Release --no-http-cache`；替换相同版本前先卸载该全局工具。Cake `pack` 会推送 NuGet，不用于本地测试。

### 命令选项和示例

从 `D:/Zongsoft/hosting` 的 PowerShell 制作现有 Web 升迁输入：

```powershell
$env:scheme = 'default'
dotnet-migrate --name:zongsoft --version:1.0.0 --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

必填选项为 name、platform；可选 version、edition、architecture（默认 x64）、output（默认当前目录）、overwrite（默认 false）、title（默认输入名称）、summary 和 description。summary/description 采用 `file:`、`text:` 文本来源规则，文件相对当前目录。至少一个位置参数；每个参数支持变量、通配符及 `;`/`|` 列表，显式选项优先于环境变量。路径按参数位置展开，各模式按固定前缀下的相对路径 Ordinal 排序。缺失路径逐项警告，全缺失或无任务则失败；存在但无效的输入仍报错。只接受 `.migration`（INI 内容），导入同样检查扩展名；参数仍为 `.env`。SQL 内容不展开变量。

Linux 支持 glibc x64/arm64；win/windows 规范化为 win，仅支持 x64。unix 必须指定具体系统，osx/xos/macos 尚无运行器，均不生成产物。

### 选择版本与 Edition

`--version` 接受非零的 `System.Version` 版本号（两段、三段或四段数字）、版本文件路径，或包含 `.version` 的现有目录路径。相对路径基于当前工作目录。省略选项、空串或全空白值只读取当前目录直属的 `.version`，不使用环境变量 `version` 代替。版本文件由 Core `ApplicationVersion` 读取，始终不修改；文件缺失、不可读或内容无效时，在生成任何产物之前报错退出。

未指定非空 `--edition` 时：单版本文件使用顶层版本，只有一个具名 Edition 时自动选择，多个 Edition 时必须明确指定。指定的 Edition 必须存在，忽略大小写匹配，并采用文件中的拼写。`--name` 仍必填，与版本文件中的应用名称无关。直接指定版本号时不读取版本文件，采用命令提供的 Edition。

在 `D:/Zongsoft/hosting` 中，以下两种方式均使用现有 Web 宿主版本文件（先按上例设置 `scheme`）：

```powershell
dotnet-migrate --name:zongsoft --version:web/default/.version --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
dotnet-migrate --name:zongsoft --version:web/default --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

省略 `--version` 时，在 `D:/Zongsoft/hosting/web/default` 中执行：

```powershell
dotnet-migrate --name:zongsoft --platform:linux --output:../../packages '../../.deploy/$(scheme)/migration/$(version)/*.migration'
```

版本路径支持变量；数字形式优先作为版本号，文件名为 `1.0.0` 时可用 `./1.0.0` 明确指定文件。最终版本和 Edition 用于 `$(version)`/`$(edition)`、计划身份及产物名称。升迁输入和输出的相对路径始终基于当前目录，不随版本文件目录改变。完整展开规则见[版本来源与变量](README.zh-Hans.md#package-phase)。

数字版本优先于路径，全零版本无效。指定数字时不会读取 .version。要选择名称类似版本号的文件，请加 ./，例如 ./1.0.0。省略 version 或传入空白时，只读取当前工作目录中的 .version，忽略环境变量 version。目录值读取直属 .version；其他路径展开后以当前目录为基准。未知或循环变量会报错，最终 version 变量不能用于定位自身来源。

### 升迁输入与执行顺序

段落名称不区分大小写：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`/`postgresql`、`tdengine`、`amazon.s3`。空数据库段落初始化目标库及用户，空 Amazon S3 段落不创建任务；只要找到了 INI，全部输入合计必须产生至少一个任务。未知段落即使为空也会报错。

按命令行文件顺序、文件内段落顺序、段落内条目顺序解析，通配符匹配按相对路径 Ordinal 排序并在当前参数位置展开，不对全部输入重新排序。先初始化全部被引用库及用户，再按声明和来源顺序执行 SQL，最后追加权限。SQL 路径同样支持路径段 `*`、`?` 和独立段 `**`；每个模式匹配的文件按相对路径进行 Ordinal 排序，同一来源段落内重叠匹配只执行一次。建议文件名使用补零序号。每次安装和重试均执行全部 SQL，由脚本作者保证可重复执行，包括部分失败后的重试。不对跨脚本、跨资源操作自动回滚，详见下文重试规则。

数据库条目只写 SQL 路径，不写值。多数驱动接收完整文件；SQL Server、TDengine 需要分批，MySQL 的客户端 `DELIMITER` 指令在提交前适配。运行器不模拟交互式数据库客户端。

数据库连接账号、默认/显式目标、建库设置及用户权限见[数据库配置](#database-configuration)。已有库保留原设置，已有用户保留密码。

### 导入共享配置

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

`schema.sql` 相对于 `migration/shared/`，它的参数从该目录的 `schema.env`、`sqlite.env` 开始逐级查找；`main.sql` 则从 `migration/` 查找 `main.env`、`sqlite.env`。两者形成各自持有连接参数的任务。Amazon S3 条目也从其声明所属 INI 查找参数。

参数文件可以显式导入公共配置后覆盖值：

```ini
# migration/sqlite.env
#@import common.env
[sqlite]
Database=/var/lib/example/application.db
```

数据库 common.env 声明 provider、数据库和用户段。只有选中的候选文件及其显式导入参与合并；缺少必需参数时报错，不从其他候选文件补齐。Amazon S3 保留 provider 命名文件的根参数简写。

- 导入路径相对于包含该指令的文件，也可为绝对路径。多个路径用空格、Tab 或 `|` 分隔，不支持引号转义、通配符或变量展开。指令合并完整 Profile，即使位于段落内也不会把子文件根条目移入当前段落。
- 缺失的导入文件按 Core 可选导入规则跳过。循环导入或超过 64 层（含根文件）时失败；允许菱形及重复导入，每次重新读取。配置链接保留逻辑来源，导入、SQL 和旁侧参数以链接位置为基准。
- 每个文件单独检查重复段落、重复键及语法。升迁 INI 的根条目、未知段落和同一有效配置中的升迁器别名冲突仍报错，导入文件也不例外。
- 不同文件的同段同名条目按读取顺序覆盖，名称不区分大小写；有效集合中仍使用该键第一次出现的位置。被覆盖的 SQL/Bucket 条目不会生成任务。需要独立执行同名条目时，应使用多个命令行 INI 输入。
- 一个有效段落按连续的声明来源拆成任务，保留有效条目顺序；同一来源在该段落内的 SQL 重叠选择跨任务去重，不对不同来源或独立输入去重。各来源保留自己的参数。跨任务及数据库段保留有效 SQL 顺序。
- 导入文件解析和参数展开错误报告实际来源，参数错误不输出敏感值。所有解析在打包阶段完成，不因导入而执行 SQL 或访问 Amazon S3。

### 查找 .env 并配置 Amazon S3

不提供参数文件命令选项。针对每个升迁器，从条目声明所属 INI 的目录逐级向父目录查找，直至文件系统根目录。**每一级目录**均按以下顺序查找：

1. 与升迁文件同名、扩展名改为 `.env` 的文件，读取对应段落。
2. `<升迁器>.env`；PostgreSQL 先 `postgres.env` 后 `postgresql.env`。对应段落优先于根条目；仅 Amazon S3 升迁器命名的文件允许省略段落。

没有适用段落的文件继续查找，仅 Amazon S3 升迁器命名且有根条目的文件除外。一旦找到适用配置（包括其显式导入），必须完整有效；不与其他候选文件或父目录自动合并，也不因缺少必填项而回退。多个升迁器共享参数文件时必须分段。最终未找到配置则打包失败，提示查找过的路径。文件名大小写遵循打包机文件系统，建议统一使用上述小写文件名。

> 🚨 注意：第一个适用的 `.env` 文件会作为完整配置使用。迁移器不会从其他候选文件或父目录拼接参数；需要共享设置时，请显式使用 `#@import`。

[数据库配置](#database-configuration)列出完整的 provider、数据库、用户参数及默认值、权限与 pending 恢复规则。Amazon S3 必填 `Server`、`Region`、`AccessKey`、`SecretKey`；可选 `Timeout` 默认 30 秒，支持正整数秒数及 s/m 后缀，上限一天。

Amazon S3 的 `Server` 为完整的 `http://` 或 `https://` 端点，不能嵌入账号密码。客户端使用 path-style 请求，适用于 RustFS。Bucket 条目支持以下选项，以逗号或 `|` 分隔：

- **`private` / `public`：** 两者互斥，默认 private。public 通过 Bucket Policy 授予匿名 `s3:GetObject`，不使用 ACL，也不开放匿名写入或列表。
- **`encryption:sse-s3`：** 默认使用 Amazon S3 管理密钥的 AES-256 服务端加密。
- **`encryption:sse-kms`：** 默认使用 KMS 服务端加密；可添加 `encryption.key:<密钥标识>`，否则采用服务端默认 KMS 密钥。
- **`versioning:enabled` / `versioning:suspended`：** 启用或暂停版本控制。
  > 💡 提示：暂停版本控制不会删除已有历史版本，只会停止新增版本。
- **`tag.<名称>:<值>`：** 添加桶标签。名称区分大小写，值可为空，最多 50 个；名称和值分别不超过 128、256 个 UTF-16 代码单元。名称不得为空或以 `aws:` 开头。

选项名称和模式值不区分大小写；标签名称、标签值和密钥标识保留大小写。值以引号外的第一个冒号分隔，因此 KMS ARN 中的冒号不会被再次分段；选项首尾空白忽略。标签名称包含冒号时也须用引号包围（例如 `tag."team:owner":zongsoft`）。单引号或双引号包围的文本可包含逗号和 `|`，同种引号连续写两次表示一个引号。访问权限、加密模式、密钥和版本控制不得重复，同名标签也不得重复。未知选项、缺少值、无效模式及未闭合引号均使打包失败；`encryption.key` 只能与 `encryption:sse-kms` 一起使用。

以下展示 hosting 现有桶名的可选配置写法，不表示当前 hosting 文件已启用这些配置：

```ini
[amazon.s3]
attachments=private,encryption:sse-s3,versioning:enabled,tag.application:zongsoft.web,tag.environment:$(environment)
learning=private,versioning:enabled
```

省略加密、版本控制或标签时，不调用对应配置接口，沿用服务端默认。`Encryption` 默认是 null，整个对象不写入 JSON；显式提供对象时 `Mode` 必须为 `sse-s3` 或 `sse-kms`，空对象或空 Mode 无效。加密配置作用于后续上传对象，不重加密已有对象；Amazon S3 的基础服务端加密不能关闭，因此不提供 encryption:false。SSE-C 涉及每次对象请求的客户密钥，不作为桶默认加密选项；本功能不创建或管理 KMS 密钥。容量配额、生命周期、CORS、日志、对象锁和 `internal` 不在支持范围。

已有 Bucket 直接跳过，不改变其权限或其他配置。新桶按版本控制、加密、标签、公共读取策略的顺序初始化，全部成功后才清除本地 pending 记录。若建桶后配置失败，保留记录以便下次重试重新应用所指定的配置，不跳过部分配置操作。认证、权限错误不视为“桶不存在”；服务端不支持已指定的配置或拒绝请求时安装失败并阻止启动，不静默忽略。Amazon S3 服务端的公共访问限制也可能拒绝公开策略。

<a id="database-configuration"></a>

### 数据库 .env 配置

配置采用 provider、数据库和用户三级段落。参数名、provider 名和枚举值不区分大小写；数据库名、用户名和密码保留拼写。值支持变量展开。未知参数、层级错误或不支持的参数会在制作阶段失败。

`.env` 采用 provider → 数据库 → 用户三级结构：

```ini
[mysql]
Server=localhost
Database=hosting
Password=$(mysql_root_password)

[mysql hosting application]
Password=$(application_password)
Permission=readwrite

[mysql analytics]
Collation=utf8mb4_bin

[mysql analytics reporting]
Password=$(reporting_password)
Permission=readonly
```

对应 `.migration`：

```ini
[mysql]
sql/hosting/*.sql

[mysql analytics]
sql/analytics/*.sql
```

`[mysql]` 使用 `Database`，缺少默认库时报错；`[mysql hosting]` 显式选择 hosting。默认库可以没有数据库段，隐式使用 provider 默认设置。用户子段也形成数据库节点，无须空数据库段。非默认库必须在选中的 `.env` 中声明。

> 💡 提示：`[mysql]` 使用 provider 的 `Database` 作为默认目标。即使没有 `[mysql hosting]`，被引用的默认库也会用 provider 默认值创建；`[mysql hosting application]` 这样的用户段同样会声明 hosting。

只初始化 `.migration` 引用的库及其全部用户，空段也算引用；未引用库及用户不进入计划或指纹。同一目标初始化一次，SQL 不广播到其他库。同一来源脚本对同一实际目标去重，用于不同库则分别执行。

从条目或空段的声明来源逐级查找同名 `.env`、provider 名称 `.env`；只合并显式导入，不跨候选文件补齐参数，不展开无关 provider 的配置。数据库配置必须有 provider 段。

参数名、provider 名、枚举值不区分大小写；数据库名、用户名、密码保留拼写。值支持变量展开。未知参数、错误层级、不支持的参数报错。段落名称中的空白分隔层级，含空格的文件路径应通过变量或 `Path` 设置。

### Provider 段 [provider]

| 参数 | 适用范围 | 默认值及含义 |
| --- | --- | --- |
| `Database` | 全部 | 默认目标数据库；显式目标不要求此项 |
| `Server` | 网络数据库 | 必填，服务器地址 |
| `Port` | 网络数据库 | 见下表；1–65535 |
| `UserName` | 网络数据库 | 见下表，可覆盖内置管理员账号 |
| `Password` | 网络数据库 | 必须显式提供，可为空；管理员当前密码 |
| `Bootstrap` | 网络数据库 | 见下表；初始化连接使用，须已存在 |
| `Timeout` | 网络数据库 | `30s`，连接超时 |
| `CommandTimeout` | 全部 | `300s`，每条命令超时 |
| `Secured` | 网络数据库 | true / false；省略行为见下表 |
| `TrustServerCertificate` | mssql | `false`，是否信任服务器证书 |

| Provider | UserName | Port | Bootstrap | 未指定 Secured |
| --- | --- | --- | --- | --- |
| mysql | root | 3306 | mysql | 驱动默认值 |
| postgres / postgresql | postgres | 5432 | postgres | 驱动默认值 |
| mssql | sa | 1433 | master | 开启加密 |
| tdengine | root | 6041 | 不指定数据库 | 普通 WebSocket |

超时支持正整数秒数及 s、m 后缀，上限一天。SQLite、DuckDB provider 仅接受 Database、CommandTimeout。

顶层 Password 登录已有管理员，用户段 Password 仅创建应用账号。服务须已完成自身初始化；迁移器不安装服务、不设置或重置管理员密码、不尝试内置默认密码。

> 🚨 注意：provider 段的 `Password` 必须是已初始化内置管理员（例如 MySQL `root`）当前使用的密码。迁移器不会创建该管理员，也不会猜测或重置密码。

### 数据库段 [provider 数据库名]

| 参数 | Provider | 默认值及含义 |
| --- | --- | --- |
| `CommandTimeout` | 全部 | 继承 provider，可覆盖 |
| `Charset` | mysql | `utf8mb4` |
| `Collation` | mysql | `utf8mb4_0900_ai_ci` |
| `Charset` | postgres | `UTF8`，映射 ENCODING |
| `Collation` | postgres | 继承模板；显式值映射 libc LC_COLLATE |
| `CType` | postgres | 继承模板；显式值映射 libc LC_CTYPE |
| `Template` | postgres | `template0` |
| `Timezone` | postgres | 继承服务端；显式值为持久库级设置 |
| `ConnectionLimit` | postgres | `-1`；允许 -1 或正整数 |
| `Collation` | mssql | 继承实例默认值 |
| `Precision` | tdengine | `ms`；允许 ms、us、ns |
| `Keep` | tdengine | `3650`，正整数保留天数 |
| `Duration` | tdengine | `10`，正整数文件时间跨度天数，不大于 Keep |
| `Replica` | tdengine | `1`；允许 1、3，须满足部署能力 |
| `Path` | sqlite、duckdb | 具名库必填，目标系统绝对文件路径 |
| `Charset` | sqlite | `UTF-8`；允许 UTF-8、UTF-16le、UTF-16be |

确定的默认值在生成计划时补齐并参与验证和指纹。全部设置仅用于新库，已有库不比较、不修改。不支持的服务端设置明确失败。

MySQL 两项均省略时使用表中默认值；仅指定非默认 Charset 时使用该字符集的服务端默认排序规则；仅指定 Collation 时由它确定字符集；同时指定时在建库前查询服务端检查匹配。MySQL 拒绝 Timezone。

PostgreSQL 仅提供 libc locale，编码、locale、模板必须相容，不硬编码区域名称。Timezone 在新库创建后设置，之后重新连接业务库。SQL Server 不接受独立 Charset 或库级 Timezone。DuckDB 库段仅支持 Path、CommandTimeout。SQLite 编码在首次初始化时持久化，不接受库级 Collation、Timezone。

TDengine 指定较小 Keep、未指定 Duration 时，Duration 为 min(10, Keep)。

文件数据库没有默认库段时，Database 必须直接是目标系统绝对路径：Linux 以 `/` 开头，Windows 使用完整盘符或 UNC。具名库用 Path；不推导相对路径或扩展名：

```ini
[sqlite]
Database=application

[sqlite application]
Path=/var/lib/example/application.db
Charset=UTF-16le
```

### 用户段 [provider 数据库名 用户名]

- **`Password`：** 必填且非空，仅创建账号时使用。
- **`Permission`：** 默认 `readwrite`；可选 `none`、`readonly`、`readwrite`、`admin`。
- **`Privileges`：** 可选的统一操作能力，与 Permission 展开结果合并。
- **`Roles`：** 可选，要加入的已有原生角色。
- **`Host`：** 仅用于 MySQL，默认 `%`；与用户名共同确定账号身份。

SQLite、DuckDB 不接受用户段。Privileges、Roles 用逗号或 `|` 分隔，不接受任意 SQL，不自动创建角色。不暴露 NativePrivileges，也不接受 CREATE、CREATE TABLE、ALL PRIVILEGES 等原生权限拼写。统一名称不区分大小写，在计划中按以下顺序规范化、去重，并合并 Permission 的默认能力；有效权限参与指纹。

- **数据操作：** `Select`、`Insert`、`Update`、`Delete`。
- **执行例程：** `Execute`。
- **创建：** `CreateTable`、`CreateIndex`、`CreateView`、`CreateProcedure`、`CreateFunction`。
- **修改：** `AlterTable`、`AlterIndex`、`AlterView`、`AlterProcedure`、`AlterFunction`。
- **删除：** `DropTable`、`DropIndex`、`DropView`、`DropProcedure`、`DropFunction`。

```ini
[mysql automao program]
Password=$(program_password)
Permission=ReadWrite
Privileges=CreateTable,CreateIndex,AlterTable
```

操作能力保证请求的操作可以获得相应授权，不要求各能力在服务器上彼此隔离。驱动允许合并较粗的原生权限，但本次新增授权必须限定到指定数据库。数据库内的所有业务 schema 均在范围内，不增加 Schemas 参数。已有权限不会被撤销。普通 SQL 例程属于范围，外部代码、跨库依赖和实例管理权限不自动补齐。

被引用库中，同一服务器账号的初始密码必须一致，冲突在连接前报错且不输出密码；MySQL 身份包含 Host。已有用户保留密码，每次仅追加权限、角色成员关系。readonly 不撤销已有权限、角色或 PUBLIC 权限；none 仅跳过内置权限组合。

> 🚨 注意：用户段的 `Password` 只在创建账号时使用。修改配置不会轮换已有账号的密码；请通过数据库常规账号管理流程更新现有凭据。

- **`none`：** 无内置权限组合；显式 `Privileges` 和 `Roles` 仍生效。
- **`readonly`：** `Select` 和序列取值。
  > 💡 提示：只读账号可以推进序列，但该预设不会授予业务表写入或重置序列的权限。
- **`readwrite`：** `Select`、`Insert`、`Update`、`Delete`、`Execute` 和序列取值。
- **`admin`：** 全部 20 项统一能力及必要依赖。这是库级权限组合，不是数据库实例管理员账号。

显式配置任意 Select、Insert、Update、Delete 同样包含 Sequence 取值（即获取下一个序号所需的权限）。CreateTable、CreateIndex 包含创建 Sequence 所需权限；MySQL 使用 AUTO_INCREMENT，没有独立 Sequence 授权。CreateTable 包含主键、唯一约束的隐式索引及库内外键引用所需权限；AlterTable 同样补齐库内引用和结构变更依赖。CreateView/AlterView 包含库内查询依赖，例程体的业务对象访问仍受已有数据权限限制。

### 驱动展开与限制

- MySQL：基础操作映射同名原生权限；CreateProcedure/CreateFunction 合并为 CREATE ROUTINE，AlterProcedure/AlterFunction 合并为 ALTER ROUTINE 和 CREATE ROUTINE，DropProcedure/DropFunction 合并为 ALTER ROUTINE。CreateIndex/DropIndex 共用 INDEX；AlterIndex 补齐 ALTER、CREATE、INSERT、INDEX；AlterTable 还包含 REFERENCES。CreateView 包含 CREATE VIEW、SHOW VIEW、SELECT，AlterView 再包含 DROP；DropTable/DropView 共用 DROP。合并后的权限只授权一次，保留数据库名通配符转义。Admin 展开 20 项能力，不使用全局 ALL 或转授权。
- SQL Server：数据和执行权限授权到目标数据库；DDL 所需 CREATE TABLE/VIEW/PROCEDURE/FUNCTION 授权到该库，ALTER、VIEW DEFINITION 及适用的 REFERENCES 授权到业务 schema。ALTER ON SCHEMA 也允许创建序列及修改、删除 schema 内其他对象，这是允许的颗粒度扩大。基础读写为已有序列单独补齐 UPDATE，ReadOnly 不向业务表授予 UPDATE；Execute 为已有表值函数补齐 SELECT。不自动加入 db_owner。
- PostgreSQL：先授予目标库 CONNECT、业务 schema USAGE；创建对象的能力包含 schema CREATE。已有表、序列、例程及迁移连接账号将来创建的对象得到相应权限。CreateIndex、Alter*、Drop* 会检查相关现有对象的所有者权限；不足时抛出派生自 NotSupportedException 的 MigrationPrivilegeException，不自动转移所有权或授予 SUPERUSER。Admin 也必须满足所有权条件，GRANT ALL 不能替代。schema CREATE 会同时允许创建多种对象。
- TDengine：统一能力目前仅支持 Select、Insert、Delete，展开为已有 READ/WRITE 授权；ReadWrite/Admin 包含无法满足的操作，提前报告 UnsupportedOperation。实际授权仍要求服务器支持原有授权语法及版本类型。SQLite、DuckDB 没有数据库账号授权模型。

AlterProcedure/AlterFunction 表示可修改定义；MySQL 修改例程体需要删除再创建，驱动提供对应权限，但迁移器不代写或执行对象重建。服务器本身的限制（例如 MySQL 二进制日志对函数创建的要求）不会通过授予实例权限或修改全局配置绕过。授权不保证任意 SQL 都可执行。

Roles 必须已存在。MySQL 使用 `%` host，检查 SHOW GRANTS 仅包含目标库权限或无实际权限的 USAGE；跨库、全局、嵌套或无法确认范围的角色拒绝，随后合并已有默认角色。PostgreSQL 检查角色继承链、实例级属性和 pg_shdepend 中的跨库/共享对象依赖，拒绝无法限定范围的角色；目标库专用 owner 角色可以补足 DDL 所有权。SQL Server 只加入目标数据库角色，并保留 Login/User SID 检查。角色检查反映 apply 时的状态，不持续监控管理员以后对角色的修改。

每次 apply 在 SQL 执行后对当时存在的业务 schema 和对象追加授权。PostgreSQL 默认对象权限仅覆盖迁移连接账号，不承诺覆盖其他创建者或未来新 schema；SQL Server 的只读序列及表值函数补充授权需要在新对象出现后再次 apply。不支持异常包含 provider、数据库、统一能力和原因码，不包含密码、含凭据 SQL 或服务端错误。普通连接/认证错误仍属于执行错误。

### SQL 批次预处理

`MigrationLoader.Database` 由升迁制作工具调用，处理客户端分隔符并生成可以直接提交给驱动的批次。每个批次写入 `<解压目录>/.migration/.artifacts/<升迁器名称>/<序号>.sql`，顺序和 SHA-256 校验和记录在计划中。运行器按计划逐文件读取并提交预处理后的批次。SQL 语法、结构变更和业务含义仍由脚本作者负责，SQL 错误由执行时的驱动或数据库报告。

| 升迁器 | 提交方式 |
| --- | --- |
| PostgreSQL、DuckDB、SQLite | 整个文件原样提交为一个命令，由驱动处理语句边界、函数及触发器体 |
| MySQL | 整个文件作为一个命令提交；移除实际的 `DELIMITER` 指令行，将引用/注释之外的自定义结束符替换为 `;`，保留存储过程体 |
| SQL Server | 按独立行 `GO` 分批，允许其后有 `--` 注释；保留批次内分号和变量作用域，不支持 `GO 2` |
| TDengine | 按引用/注释之外的分号分批提交 |

MySQL 连接始终开启 `AllowUserVariables=true`，支持同一会话里的 `SET @变量`、`PREPARE`、`EXECUTE` 和 `DEALLOCATE PREPARE`。同一任务的文件共用一个连接。重试会创建新连接并从头执行任务中的文件，脚本应初始化所需的会话变量和临时对象。

批次以 UTF-8 无 BOM 编码写入归档，校验和针对生成后的字节计算；保留批次内的换行，不修复 SQL 语法。完整文件提交的升迁器仍生成一个批次，避免破坏函数、触发器、存储过程和会话变量的作用域。MySQL 分隔符扫描遵循常规反斜杠转义；脚本切换到 `NO_BACKSLASH_ESCAPES` 模式时，应避免在客户端分隔符附近使用含义不明确的转义引用。不实现 `\i`、`SOURCE`、`.read`、`:r`、`:setvar` 等厂商客户端命令；其他 SQL 文件通过 INI 条目指定。`CommandTimeout` 作用于每次提交的命令，支持批量提交的驱动对应整个文件。失败会停止后续命令和文件，但哪些变更已提交由数据库决定。

### TDengine WebSocket 执行

`Migrator.Database.TDengine` 使用 .NET 自带的 WebSocket 与 JSON API，不依赖 `TDengine.Connector`，也不随包携带 TDengine 客户端库。目标数据库需要提供 taosAdapter WebSocket 服务，`Server` 填主机名或 IP，`Port` 默认 `6041`。

运行器发送 `conn` 完成账号认证，通过目录查询检查数据库和用户，创建缺失对象并执行 SQL 和授权。每个 SQL 任务直接连接已解析目标库。请求使用递增 req_id，响应校验 action 和 req_id；存在性查询使用 fetch 检查行数，结果集通过 free_result 释放而不等待回复。

`Timeout` 覆盖 WebSocket 握手与登录；`CommandTimeout` 分别限制每条 SQL 的发送、响应和结果释放。认证错误、SQL 错误、异常响应、断连、超时或取消都会终止本次升迁。错误提示不回显服务端返回的 SQL 或凭据；状态记录异常类型，详细 SQL 原因从服务端日志核查。

### 产物命名和内容

名称已有 `-migrate`、`-migration`、`.migrate`、`.migration` 后缀时保留（忽略大小写），否则追加 `-migrate`。两文件使用同一前缀 `<升迁名称>[-edition]@<version>_<platform>-<architecture>`，扩展名分别为 `.tar.gz` 和 `.sh`/`.cmd`。不生成描述文件。例如：

```text
packages/zongsoft-migrate@1.0.0_linux-x64.tar.gz
packages/zongsoft-migrate@1.0.0_linux-x64.sh
```

归档包含 .migration/migration.json、.migration/.artifacts/ 下的 SQL 批次和解析后的升迁数据。两个输出先暂存再发布；替换已有文件须指定 --overwrite，发布失败会恢复原输出。

> 🚨 注意：归档包含从 `.env` 展开的数据库和 Amazon S3 凭据。请限制升迁包的查看、存储和下载权限。

<a id="execution-phase"></a>

## 执行阶段

### 在目标机运行升迁包
将二者放在目标机同一目录，执行 `sh zongsoft-migrate@1.0.0_linux-x64.sh [apply|status|check] [状态目录]`。Windows 执行同名前缀的 cmd。状态默认位于脚本目录 `.migration/<升迁名称>[-edition]/`，不含版本和 RID。不同版本共享执行锁、ready、status 和 pending 文件；执行失败不会写成功标记。

apply/status 每次创建独立临时目录，解压后调用原生执行器，结束清理并返回退出码。成功返回 0，执行失败返回 1，动作或参数无效返回 2。

- **`apply`（默认）：** 校验计划和 SQL 校验和，初始化数据库及用户，按顺序执行 SQL/Amazon S3 任务，最后追加数据库授权。
- **`status`：** 输出 `status.json` 中最近一次执行报告；返回 0 表示当前包与成功的 `ready` 标记匹配，返回 1 表示不匹配。
- **`check`：** 比较当前包的 SHA-256 计划指纹与 `ready`；匹配时返回 0，不匹配时提示尚未完成并返回 1。它不显示执行详情、不解压归档，也不启动执行器。

> 💡 提示：发布变更请执行 `apply`。用 `status` 查看最近一次执行报告，再用 `check` 确认当前包是否就是已成功执行的那一份。

> 🚨 注意：`ready` 匹配只说明相同计划曾成功执行，不代表数据库或 Amazon S3 资源之后没有被修改。

### 数据库初始化与恢复
先验证整个计划及全部 SQL 校验和，再依次确保全部目标库存在、创建账号及库内映射、按任务顺序执行 SQL、为全部目标库追加权限及角色。空任务也会建库和授权。

多命令初始化使用 `database-<目标摘要>.pending`，只包含设置摘要，不包含凭据。重试完成未完成的新库设置；pending 与设置不一致时明确失败，防止应用另一份配置。SQLite 空库编码持久化后才清除 pending。

状态锁和 ready 规则不变：失败使 ready 失效，每次 apply 执行全部 SQL，不自动回滚或删除数据库、用户。日志和状态不输出密码、含密码 SQL 或服务端错误内容。
执行器在连接服务前校验完整计划和 SQL 文件。随后确保被引用数据库存在，创建缺失用户和库内映射，按声明顺序执行任务，最后追加请求的权限和角色。空数据库段也会触发初始化。每次 apply 都持有状态锁；成功写入 ready，失败令其失效。

新库创建需要多条命令时，pending 文件只记录设置摘要，不保存凭据。重试会完成未结束的初始化；设置摘要不一致时报错。已有数据库设置和用户密码会保留。执行器不会自动回滚或删除数据库、用户、桶或 SQL 变更。

启动脚本将归档解压到唯一临时目录，调用原生执行器，退出时清理并传回退出码。状态和 pending 不含凭据。日志不输出密码、含凭据 SQL 或原始服务端错误；SQL 详情从服务端日志核查。
### SQL 可重复执行与失败重试

首次安装、升级、覆盖安装以及每次 `apply` 均执行全部配置的 SQL 文件，按 Steps 数组及各步骤的 Scripts 列表顺序执行。不维护逐文件成功历史，也不根据先前执行结果跳过文件。结构变更和数据操作的可重复执行性由脚本作者保证：新增、更名、删除前检查对象存在性及预期状态，数据插入与更新避免重复写入或重复累加。遇到不符合预期的状态应明确报错，不应直接跳过必要变更。

同一数据库任务内文件 A 成功、B 失败后，重试仍从该任务的首个文件开始，包括重新执行 A。B 也可能已有部分语句提交，脚本应处理这些中间状态，或由操作者在重试前修复。升迁制作工具不自动回滚 SQL，事务由脚本在数据库支持的范围内控制。

SQL 校验和只用于在外部资源操作之前验证包内文件与当前计划一致，不是执行历史，也不阻止重新生成的包执行修订后的 SQL。文件锁、状态及 ready 标记控制安装是否完成，不用于在 `apply` 时跳过文件。

> 🚨 注意：每次 `apply`（包括部分失败后的重试）都会从头执行配置中的 SQL。请保证脚本可重复执行；重试前先修复已经部分提交的变更。迁移器不会自动回滚 SQL。

## 最佳实践

1. **准备变更。** 将每个版本的 `.migration` 输入和 SQL 一起维护。通配符决定顺序时使用编号文件名；每项 SQL 都应能在部分失败后从当前任务首个文件重新执行。
2. **配置访问。** 在选中的 `.env` 中填写管理员连接信息，显式声明每个非默认数据库，并为各库配置独立、最小权限的应用账号。秘密值通过环境变量展开，不要将 `.env` 提交到源代码库；生成的升迁包包含展开后的凭据。
3. **制作并保护升迁包。** 将归档和匹配的启动脚本生成到受限目录，始终成对保管和分发。检查归档文件列表与计划时，避免打印或公开包含密码的设置。
4. **在预发布环境验证。** 为预发布环境使用稳定且独立于生产的状态目录，执行 `apply`，检查 `status` 并验证服务行为。成功后可用 `check` 确认该包与 ready 记录一致。执行失败后先修复部分变更和 SQL，再重试，因为每次 `apply` 都会重跑全部 SQL。
5. **发布到生产环境。** 先完成可恢复备份并确认恢复步骤，再将升迁包放到目标机。使用生产状态目录执行 `apply`，再通过 `status` 或 `check` 确认完成。`check` 只报告当前包是否已有匹配的成功记录，不检查服务连接，也不预演 SQL。此生产状态目录应跨包版本保留。
6. **保留状态并复核权限。** 单独检查已有授权：工具只追加权限和角色，不撤销已有的宽权限，也不轮换已有密码。`readwrite` 是默认权限，建议明确设置 `Permission`。

## 与 packager 配合

在 hosting/web/default 的打包命令中使用 `--migrator:../../packages/zongsoft`；daemon 使用 `--migrator:../packages/zongsoft`。packager 根据最终 Edition、版本和 RID 查找准确配套文件，应用同一名称后缀规则。任一文件缺失即失败，不选择其他版本。安装包原样包含归档和脚本；安装时显式传入 `/var/lib/<包名>/packager` 状态目录，升迁失败阻止启动。实际宿主命令和连接参数不由本工具自动修改。

<a id="build-and-test"></a>

## 构建与测试

生成端目标为 .NET 8/9/10，执行器为 .NET 10 Native AOT。普通 build/test 不启动原生发布。显式 `dotnet cake --edition Release --target executor` 使用独立 Rocky Linux 9/glibc 2.34 Pod 构建 Linux 双架构，并在 Windows 本机发布 win-x64；缓存和挂载定义见 executor/build/migrator.linux-x64.yaml。工具使用仓库根 `.editorconfig`，Pod 与 CI 将该文件只读挂载到 `/.editorconfig`，并将根 `Directory.Build.props` 和 `Directory.Packages.props` 只读挂载到容器根目录。Linux 发布使用 `-p:ZongsoftGuidelinesSynchronization=` 禁用配置同步。Cake 使用相对 YAML 路径，修改 DNS 或挂载配置后需在无构建运行时重建专用 Pod，不能只 start。原生产物统一位于 `executor/src/bin/<配置>/net10.0/<RID>/publish/`；完整工具包要求同一配置下三个 RID 均已发布。普通构建和独立发布通过文件链接将它们收录到程序目录的 `.migrator/<RID>/`。NuGet 工具包在 `tools/.migrator/<RID>/` 仅保存一份，供三个目标框架共用；生成端在本地 `.migrator/` 目录不存在时定位此共享目录。CI 分别在 Linux、Windows 准备发布目录后汇集制包。日志和符号分别保存在各 RID 目录的 logs/ 与 symbols/，不收录到工具包。

```powershell
dotnet build Zongsoft.Tools.Migrator.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test test/Zongsoft.Tools.Migrator.Tests.csproj -f net10.0
dotnet test executor/test/Zongsoft.Tools.Migrator.Executor.Tests.csproj -f net10.0
```

详见[实现说明](docs/implementation.zh-Hans.md)。

Privileges 使用 20 项跨驱动统一操作名称。ReadWrite 包含 Execute，所有读写授权均包含 Sequence 取值；驱动在指定库内展开原生权限，无法满足的操作或所有权条件明确失败。权限合并和角色范围限制见[数据库配置参考](README.zh-Hans.md#database-configuration)。

<a id="advanced-package-details"></a>

## 高级升迁包细节

<details>
<summary>计划字段与指纹格式</summary>

Steps 不再保存独立 Id：数组位置决定执行顺序，日志和状态使用从 1 开始的步骤序号。Database 同样不保存 Id：Step.DatabaseIndex 为可空整数，引用 Databases 数组位置（从零开始）。SQL 步骤必须填写范围内且 provider 匹配的索引，Amazon S3 必须省略；索引 0 仍写入 JSON。不同服务器上的同名数据库分别占用独立数组项。SQL 文件名使用不补零的十进制序号（1.sql、2.sql、…、10.sql），每次 Load 按 provider 从 1 编号；执行依据 Steps 和 Scripts 数组，不按文件名排序。源码生成 JSON 统一忽略值为 null 的模型成员，格式化计划与紧凑指纹序列化共享规则；false、空字符串、空集合仍保留。Source/Content 和计算键继续使用无条件 JsonIgnore。状态记录 phase（validation/databases/users/steps/permissions/complete）、step（从 1 开始，非步骤阶段为 null）、databaseIndex（从零开始的目标数组索引，无目标时为 null）。

`migration.json` 是升迁制作工具生成、migrator 读取的 UTF-8 JSON 对象。它保存已解析的执行输入，字段名按下表输出；运行器读取属性名时忽略大小写。

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `Name` | 字符串 | 规范升迁名称，包含自动补全的升迁后缀及 Edition（如有），例如 `zongsoft-migrate` 或 `zongsoft-migrate-enterprise`。 |
| `Version` | 字符串 | 制作升迁产物时指定的版本。 |
| `Runtime` | string | 目标 RID：`linux-x64`、`linux-arm64` 或 `win-x64`，参与指纹并在执行前校验 |
| `Title` / `Summary` / `Description` | string? | 可选展示文本；未设置时不输出，参与指纹 |
| `Steps` | 数组 | 按有效声明和来源顺序执行的 SQL/Amazon S3 任务，在建库及创建用户之后执行。 |
| `Steps[].Provider` | 字符串 | 规范升迁器名称：`mssql`、`mysql`、`sqlite`、`duckdb`、`postgres`、`tdengine`、`amazon.s3`。 |
| `Steps[].Settings` | 字符串键值对象 | Amazon S3 连接参数；数据库任务为空对象，数据库参数保存于 Databases。 |
| `Steps[].DatabaseIndex` | 整数? | Databases 数组索引（从零开始）；SQL 步骤必填并校验范围及 provider，Amazon S3 省略。 |
| `Databases` | 数组 | 仅包含被引用并去重的数据库初始化描述。 |
| `Databases[].Provider` / `Name` | 字符串 | 规范 provider 名与实际目标库名；初始化描述通过数组位置引用。 |
| `Databases[].Settings` | 字符串键值对象 | 已展开并填充默认值的管理员连接设置。 |
| `Databases[].Options` | 字符串键值对象 | 有效建库设置和命令超时。 |
| `Databases[].Users` | 数组 | 此引用数据库下声明的全部用户。 |
| `Databases[].Users[].Name` / `Password` / `Permission` | 字符串 | 账号名、初始密码、规范权限配置。 |
| `Databases[].Users[].Privileges` / `Roles` | 字符串数组 | 规范化、去重并包含 Permission 预设的有效统一能力，以及已有角色名。 |
| `Databases[].Users[].Host` | 字符串? | MySQL 账号主机，默认 `%`；其他 provider 省略。 |
| `Steps[].Scripts` | 数组 | 数据库任务的有序批次列表；Amazon S3 任务为 `[]`。 |
| `Steps[].Scripts[].Path` | 字符串 | 相对于升迁归档解压根目录的批次路径，例如 `.migration/.artifacts/mysql/1.sql`。同类任务共享目录和连续编号。 |
| `Steps[].Scripts[].Checksum` | 字符串 | 包内批次实际 UTF-8 字节的 SHA-256，使用大写十六进制；执行前校验内容完整性。 |
| `Steps[].Buckets` | 数组 | Amazon S3 任务的桶描述列表；数据库任务为 `[]`。 |
| `Steps[].Buckets[].Name` | 字符串 | Bucket 名称，例如 hosting 的 `attachments`。 |
| `Steps[].Buckets[].Public` | 布尔值 | `true` 表示配置公共读取策略；`false` 表示私有桶。 |
| `Steps[].Buckets[].Encryption` | 对象，可省略 | 默认加密配置，包含 `Mode` 和可选 `Key`。 |
| `Steps[].Buckets[].Encryption.Mode` | 字符串 | `sse-s3` 或 `sse-kms`。 |
| `Steps[].Buckets[].Encryption.Key` | 字符串，可省略 | SSE-KMS 使用的已有密钥标识；仅适用于 `sse-kms`。 |
| `Steps[].Buckets[].Versioning` | 字符串，可省略 | `enabled` 或 `suspended`。 |
| `Steps[].Buckets[].Tags` | 字符串键值对象，可省略 | 桶标签，键名区分大小写；不是 INI 中带 `tag.` 前缀的原始选项名。 |

省略的桶配置不发送对应配置请求。`Script.Source` 和 `Script.Content` 只供升迁生成端使用，不写入 JSON；原始 INI/ENV 路径和本机 SQL 源路径也不作为协议字段保存。连接参数可能包含密码或密钥，文件权限为 `0600`。

计划指纹不保存在 JSON 内：升迁制作工具对模型的无缩进 JSON UTF-8 字节计算 SHA-256，将大写十六进制结果写入 `.migration/id`；运行器全部执行成功后写入状态目录的 `ready`。它不是对带缩进的 `migration.json` 文件直接计算摘要。有效数据库设置及用户、参数、脚本路径、校验和、桶配置以及数组顺序均参与指纹；缩进和文件换行不参与。指纹用于当前安装计划的完成判断，不是签名或逐脚本执行历史。

</details>
