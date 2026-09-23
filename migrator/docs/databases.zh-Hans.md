# 数据库初始化与用户权限

[升迁指南](migration.zh-Hans.md)介绍 SQL、导入和 S3。本页完整列出数据库 `.env` 参数。Amazon S3、RustFS 的配置和执行方式不变。

## 配置和目标选择

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

只初始化 `.migration` 引用的库及其全部用户，空段也算引用；未引用库及用户不进入计划或指纹。同一目标初始化一次，SQL 不广播到其他库。同一来源脚本对同一实际目标去重，用于不同库则分别执行。

从条目或空段的声明来源逐级查找同名 `.env`、provider 名称 `.env`；只合并显式导入，不跨候选文件补齐参数，不展开无关 provider 的配置。数据库配置必须有 provider 段。

参数名、provider 名、枚举值不区分大小写；数据库名、用户名、密码保留拼写。值支持变量展开。未知参数、错误层级、不支持的参数报错。段落名称中的空白分隔层级，含空格的文件路径应通过变量或 `Path` 设置。

## Provider 段 `[provider]`

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

## 数据库段 `[provider 数据库名]`

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

## 用户段 `[provider 数据库名 用户名]`

| 参数 | 默认值及含义 |
| --- | --- |
| `Password` | 必填且非空，仅创建账号时使用 |
| `Permission` | `readwrite`；none、readonly、readwrite、admin |
| `Privileges` | 可选，额外统一操作能力；与 Permission 展开结果合并 |
| `Roles` | 可选，要加入的已有原生角色 |
| `Host` | 仅 mysql，默认 `%`；与用户名共同确定账号身份 |

SQLite、DuckDB 不接受用户段。Privileges、Roles 用逗号或 `|` 分隔，不接受任意 SQL，不自动创建角色。不暴露 NativePrivileges，也不接受 CREATE、CREATE TABLE、ALL PRIVILEGES 等原生权限拼写。统一名称不区分大小写，在计划中按下表顺序规范化、去重，并合并 Permission 的默认能力；有效权限参与指纹。

| 分类 | 统一 Privileges |
| --- | --- |
| 数据操作 | Select、Insert、Update、Delete |
| 执行例程 | Execute |
| 创建 | CreateTable、CreateIndex、CreateView、CreateProcedure、CreateFunction |
| 修改 | AlterTable、AlterIndex、AlterView、AlterProcedure、AlterFunction |
| 删除 | DropTable、DropIndex、DropView、DropProcedure、DropFunction |

```ini
[mysql automao program]
Password=$(program_password)
Permission=ReadWrite
Privileges=CreateTable,CreateIndex,AlterTable
```

操作能力保证请求的操作可以获得相应授权，不要求各能力在服务器上彼此隔离。驱动允许合并较粗的原生权限，但本次新增授权必须限定到指定数据库。数据库内的所有业务 schema 均在范围内，不增加 Schemas 参数。已有权限不会被撤销。普通 SQL 例程属于范围，外部代码、跨库依赖和实例管理权限不自动补齐。

被引用库中，同一服务器账号的初始密码必须一致，冲突在连接前报错且不输出密码；MySQL 身份包含 Host。已有用户保留密码，每次仅追加权限、角色成员关系。readonly 不撤销已有权限、角色或 PUBLIC 权限；none 仅跳过内置权限组合。

| Permission | 默认能力 |
| --- | --- |
| None | 无预设能力，仍处理显式 Privileges 和 Roles |
| ReadOnly | Select，以及 Sequence 取值 |
| ReadWrite | Select、Insert、Update、Delete、Execute，以及 Sequence 取值 |
| Admin | 全部 20 项统一能力及必要依赖；不等于实例管理员 |

显式配置任意 Select、Insert、Update、Delete 同样包含 Sequence 取值。取值包括获取下一个序号，所以 ReadOnly 可以推进序列，但不会因此获得业务表写入或重置序列权限。CreateTable、CreateIndex 包含创建 Sequence 所需权限；MySQL 使用 AUTO_INCREMENT，没有独立 Sequence 授权。CreateTable 包含主键、唯一约束的隐式索引及库内外键引用所需权限；AlterTable 同样补齐库内引用和结构变更依赖。CreateView/AlterView 包含库内查询依赖，例程体的业务对象访问仍受已有数据权限限制。

### 驱动展开与限制

- MySQL：基础操作映射同名原生权限；CreateProcedure/CreateFunction 合并为 CREATE ROUTINE，AlterProcedure/AlterFunction 合并为 ALTER ROUTINE 和 CREATE ROUTINE，DropProcedure/DropFunction 合并为 ALTER ROUTINE。CreateIndex/DropIndex 共用 INDEX；AlterIndex 补齐 ALTER、CREATE、INSERT、INDEX；AlterTable 还包含 REFERENCES。CreateView 包含 CREATE VIEW、SHOW VIEW、SELECT，AlterView 再包含 DROP；DropTable/DropView 共用 DROP。合并后的权限只授权一次，保留数据库名通配符转义。Admin 展开 20 项能力，不使用全局 ALL 或转授权。
- SQL Server：数据和执行权限授权到目标数据库；DDL 所需 CREATE TABLE/VIEW/PROCEDURE/FUNCTION 授权到该库，ALTER、VIEW DEFINITION 及适用的 REFERENCES 授权到业务 schema。ALTER ON SCHEMA 也允许创建序列及修改、删除 schema 内其他对象，这是允许的颗粒度扩大。基础读写为已有序列单独补齐 UPDATE，ReadOnly 不向业务表授予 UPDATE；Execute 为已有表值函数补齐 SELECT。不自动加入 db_owner。
- PostgreSQL：先授予目标库 CONNECT、业务 schema USAGE；创建对象的能力包含 schema CREATE。已有表、序列、例程及迁移连接账号将来创建的对象得到相应权限。CreateIndex、Alter*、Drop* 会检查相关现有对象的所有者权限；不足时抛出派生自 NotSupportedException 的 MigrationPrivilegeException，不自动转移所有权或授予 SUPERUSER。Admin 也必须满足所有权条件，GRANT ALL 不能替代。schema CREATE 会同时允许创建多种对象。
- TDengine：统一能力目前仅支持 Select、Insert、Delete，展开为已有 READ/WRITE 授权；ReadWrite/Admin 包含无法满足的操作，提前报告 UnsupportedOperation。实际授权仍要求服务器支持原有授权语法及版本类型。SQLite、DuckDB 没有数据库账号授权模型。

AlterProcedure/AlterFunction 表示可修改定义；MySQL 修改例程体需要删除再创建，驱动提供对应权限，但迁移器不代写或执行对象重建。服务器本身的限制（例如 MySQL 二进制日志对函数创建的要求）不会通过授予实例权限或修改全局配置绕过。授权不保证任意 SQL 都可执行。

Roles 必须已存在。MySQL 使用 `%` host，检查 SHOW GRANTS 仅包含目标库权限或无实际权限的 USAGE；跨库、全局、嵌套或无法确认范围的角色拒绝，随后合并已有默认角色。PostgreSQL 检查角色继承链、实例级属性和 pg_shdepend 中的跨库/共享对象依赖，拒绝无法限定范围的角色；目标库专用 owner 角色可以补足 DDL 所有权。SQL Server 只加入目标数据库角色，并保留 Login/User SID 检查。角色检查反映 apply 时的状态，不持续监控管理员以后对角色的修改。

每次 apply 在 SQL 执行后对当时存在的业务 schema 和对象追加授权。PostgreSQL 默认对象权限仅覆盖迁移连接账号，不承诺覆盖其他创建者或未来新 schema；SQL Server 的只读序列及表值函数补充授权需要在新对象出现后再次 apply。不支持异常包含 provider、数据库、统一能力和原因码，不包含密码、含凭据 SQL 或服务端错误。普通连接/认证错误仍属于执行错误。

## 执行与恢复

先验证整个计划及全部 SQL 校验和，再依次确保全部目标库存在、创建账号及库内映射、按任务顺序执行 SQL、为全部目标库追加权限及角色。空任务也会建库和授权。

多命令初始化使用 `database-<目标摘要>.pending`，只包含设置摘要，不包含凭据。重试完成未完成的新库设置；pending 与设置不一致时明确失败，防止应用另一份配置。SQLite 空库编码持久化后才清除 pending。

状态锁和 ready 规则不变：失败使 ready 失效，每次 apply 执行全部 SQL，不自动回滚或删除数据库、用户。日志和状态不输出密码、含密码 SQL 或服务端错误内容。
