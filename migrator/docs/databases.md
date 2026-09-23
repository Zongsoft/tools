# Database initialization and users

The [migration guide](migration.md) covers SQL, imports and S3. This page lists all database `.env` parameters. Amazon S3 and RustFS behavior is unchanged.

## Configuration and targets

Configuration has three levels: provider, database, user.

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

The corresponding `.migration`:

```ini
[mysql]
sql/hosting/*.sql

[mysql analytics]
sql/analytics/*.sql
```

`[mysql]` uses Database and fails if it is missing. `[mysql hosting]` explicitly selects hosting. The default database can omit its database section and use provider defaults. User subsections also declare their database without an empty parent header. Nondefault databases require a node in the selected `.env`.

Only referenced databases and all their declared users enter the plan and fingerprint. Empty migration sections count as references. Each target initializes once; SQL is never broadcast. A source script is deduplicated for the same actual target and runs separately for different targets.

Parameters follow the declaration-source search: same-name `.env`, then provider-named `.env`, up through parent directories. Only explicit imports merge configuration. Database configuration requires provider sections; no filling from other candidates or expansion of unrelated providers.

Parameter/provider names and enum values are case insensitive. Database names, usernames and passwords retain spelling. Values support variable expansion. Unknown, misplaced or unsupported parameters fail. Spaces separate section levels; use variables or Path for file paths containing spaces.

## Provider section `[provider]`

| Parameter | Providers | Default and meaning |
| --- | --- | --- |
| `Database` | All | Default target; optional with explicit targets |
| `Server` | Network databases | Required server address |
| `Port` | Network databases | Table below; 1–65535 |
| `UserName` | Network databases | Built-in administrator below, overridable |
| `Password` | Network databases | Explicitly required, may be empty; current administrator password |
| `Bootstrap` | Network databases | Initialization connection database below; must already exist |
| `Timeout` | Network databases | `30s`, connection timeout |
| `CommandTimeout` | All | `300s`, per-command timeout |
| `Secured` | Network databases | true / false; omission behavior below |
| `TrustServerCertificate` | mssql | `false`, trust server certificate |

| Provider | UserName | Port | Bootstrap | Secured omitted |
| --- | --- | --- | --- | --- |
| mysql | root | 3306 | mysql | Driver default |
| postgres / postgresql | postgres | 5432 | postgres | Driver default |
| mssql | sa | 1433 | master | Encryption enabled |
| tdengine | root | 6041 | No database | Plain WebSocket |

Timeouts accept positive integer seconds and s/m suffixes, up to one day. SQLite and DuckDB provider sections accept only Database and CommandTimeout.

Top-level Password authenticates an existing administrator; user-section Password creates an application account. The service must already be initialized. The migrator neither installs services nor sets/resets administrator passwords or tries built-in passwords.

## Database section `[provider database]`

| Parameter | Provider | Default and meaning |
| --- | --- | --- |
| `CommandTimeout` | All | Inherits provider; overridable |
| `Charset` | mysql | `utf8mb4` |
| `Collation` | mysql | `utf8mb4_0900_ai_ci` |
| `Charset` | postgres | `UTF8`, mapped to ENCODING |
| `Collation` | postgres | Inherit template; explicit value maps to libc LC_COLLATE |
| `CType` | postgres | Inherit template; explicit value maps to libc LC_CTYPE |
| `Template` | postgres | `template0` |
| `Timezone` | postgres | Inherit server unless specified; persistent database setting |
| `ConnectionLimit` | postgres | `-1`; accepts -1 or a positive integer |
| `Collation` | mssql | Inherit instance |
| `Precision` | tdengine | `ms`; ms, us or ns |
| `Keep` | tdengine | `3650`, positive retention days |
| `Duration` | tdengine | `10`, positive file duration days, at most Keep |
| `Replica` | tdengine | `1`; 1 or 3, subject to deployment support |
| `Path` | sqlite, duckdb | Required for named database sections; absolute target-system file path |
| `Charset` | sqlite | `UTF-8`; UTF-8, UTF-16le or UTF-16be |

Deterministic defaults are filled during generation and included in validation and fingerprints. Creation settings apply only to new databases; existing databases are neither compared nor altered. Unsupported server settings fail.

MySQL uses both defaults when omitted. A nondefault Charset alone uses the server's default collation for that charset. Collation alone determines its charset. Specifying both checks compatibility against the server before creation. MySQL rejects Timezone.

PostgreSQL supports libc locales only; encoding, locale and template must be compatible. Locale names are not hard-coded. An explicit Timezone is applied after creation before reconnecting to the target. SQL Server rejects independent Charset and database Timezone. DuckDB database sections accept only Path and CommandTimeout. SQLite persists encoding at first initialization and rejects database Collation/Timezone.

For TDengine, omitted Duration becomes min(10, Keep) when Keep is smaller.

Without a default database section, file-database Database must itself be an absolute target-system file path: `/`-rooted on Linux, or a fully qualified drive/UNC path on Windows. Named sections specify Path. No relative path inference or extension is added:

```ini
[sqlite]
Database=application

[sqlite application]
Path=/var/lib/example/application.db
Charset=UTF-16le
```

## User section `[provider database username]`

| Parameter | Default and meaning |
| --- | --- |
| `Password` | Required and nonempty; only used when creating the account |
| `Permission` | `readwrite`; none, readonly, readwrite or admin |
| `Privileges` | Optional unified operation capabilities, combined with the Permission preset |
| `Roles` | Optional existing native roles to join |
| `Host` | mysql only; `%`, part of account identity |

SQLite and DuckDB reject user sections. Privileges/Roles are comma- or pipe-separated lists, not arbitrary SQL. Roles are not created automatically. There is no NativePrivileges parameter; native spellings such as CREATE, CREATE TABLE and ALL PRIVILEGES are rejected. Unified names are case-insensitive, normalized and deduplicated in the following order, then combined with Permission defaults. The resulting effective capabilities are stored in the plan and fingerprinted.

| Category | Unified Privileges |
| --- | --- |
| Data | Select, Insert, Update, Delete |
| Routine execution | Execute |
| Creation | CreateTable, CreateIndex, CreateView, CreateProcedure, CreateFunction |
| Modification | AlterTable, AlterIndex, AlterView, AlterProcedure, AlterFunction |
| Removal | DropTable, DropIndex, DropView, DropProcedure, DropFunction |

```ini
[mysql automao program]
Password=$(program_password)
Permission=ReadWrite
Privileges=CreateTable,CreateIndex,AlterTable
```

Capabilities request the privileges needed for the operation; they do not promise exclusive isolation between operations. Providers may combine coarser native permissions within the specified database, including all its business schemas. No Schemas parameter is required. Existing permissions are not revoked. Ordinary SQL routines are in scope; external code, cross-database dependencies and instance administration privileges are not added automatically.

A server account reused across referenced databases must have the same initial password. Conflicts fail before connecting without exposing passwords. MySQL identity includes Host. Existing passwords remain unchanged. Each apply adds privileges and role memberships; readonly never revokes previous grants, roles or PUBLIC rights. none skips the built-in permission set.

| Permission | Default capabilities |
| --- | --- |
| None | No preset; explicit Privileges and Roles still apply |
| ReadOnly | Select and sequence value generation |
| ReadWrite | Select, Insert, Update, Delete, Execute and sequence value generation |
| Admin | All 20 capabilities and dependencies; not an instance administrator |

Any explicit Select/Insert/Update/Delete also includes sequence value generation. ReadOnly can advance a sequence without obtaining business-table writes or sequence-reset privileges. CreateTable and CreateIndex include sequence creation; MySQL uses AUTO_INCREMENT and has no independent sequence grant. CreateTable includes implicit primary/unique indexes and in-database foreign-key references; AlterTable includes reference and structural dependencies. CreateView/AlterView include in-database query dependencies; routine bodies remain subject to the account's data permissions.

### Provider expansion and limitations

- MySQL: data and execution operations map to the corresponding native permissions. CreateProcedure/CreateFunction share CREATE ROUTINE; AlterProcedure/AlterFunction share ALTER ROUTINE plus CREATE ROUTINE; DropProcedure/DropFunction share ALTER ROUTINE. CreateIndex/DropIndex share INDEX; AlterIndex adds ALTER, CREATE and INSERT, while AlterTable also includes REFERENCES. CreateView includes CREATE VIEW, SHOW VIEW and SELECT; AlterView also includes DROP. DropTable/DropView share DROP. Native grants are deduplicated and database wildcard characters remain escaped. Admin expands the 20 capabilities without a global ALL grant or grant option.
- SQL Server: data and execution grants target the database. Required CREATE TABLE/VIEW/PROCEDURE/FUNCTION permissions target that database; ALTER, VIEW DEFINITION and applicable REFERENCES target business schemas. Schema ALTER also enables sequences, index maintenance and removal of other schema objects, an intentional widening of native granularity. Basic reads/writes add UPDATE on existing sequences individually; ReadOnly does not obtain table UPDATE. Execute adds SELECT on existing table-valued functions. No automatic db_owner membership is added.
- PostgreSQL: CONNECT targets the database and USAGE targets business schemas; object-creation capabilities include schema CREATE. Grants cover existing tables/sequences/routines and default object privileges for the migration connection account. CreateIndex, Alter* and Drop* check ownership rights for relevant existing objects; insufficient ownership raises MigrationPrivilegeException, derived from NotSupportedException. Neither ownership transfer nor SUPERUSER escalation occurs. Admin has the same ownership prerequisite; GRANT ALL is not a substitute. Schema CREATE permits multiple object types.
- TDengine: currently only Select, Insert and Delete are supported, using the existing READ/WRITE protocol. ReadWrite/Admin contain unsupported operations and fail early with UnsupportedOperation. Actual grants still require a server edition/version supporting that syntax. SQLite and DuckDB have no account-grant model.

AlterProcedure/AlterFunction include changing definitions. MySQL requires dropping and recreating routine bodies, so the provider grants the necessary rights, but does not rewrite or rebuild objects itself. Server prerequisites, such as MySQL binary-log restrictions on function creation, are not bypassed through instance privileges or global setting changes. Grants cannot guarantee acceptance of arbitrary SQL.

Roles must already exist. MySQL uses host `%` and verifies SHOW GRANTS against the target database, allowing empty USAGE but rejecting global, cross-database, nested or unverified role scopes; existing default roles are then merged. PostgreSQL checks inherited roles, instance attributes and pg_shdepend dependencies on other databases/shared objects; a database-specific owner role can satisfy DDL ownership. SQL Server adds target-database roles and preserves Login/User SID validation. These checks describe role scope at apply time, not continuous monitoring of later administrator changes.

Each apply appends grants to the business schemas/objects present after SQL execution. PostgreSQL default object privileges cover the migration connection account, not arbitrary future creators or new schemas. SQL Server supplemental readonly-sequence and table-valued-function grants require another apply after new objects appear. Unsupported errors identify provider, database, capability and a reason code without passwords, credential-bearing SQL or server error text. Ordinary connection/authentication errors remain execution failures.

## Execution and recovery

Validate the complete plan and all SQL checksums first. Ensure all referenced databases exist, create all accounts and database mappings, execute SQL tasks in order, then append permissions and roles for every referenced database, including empty tasks.

Multi-command initialization writes `database-<target digest>.pending`, containing only a settings digest, never credentials. Retry completes unfinished new-database settings; a settings mismatch fails explicitly instead of applying a different pending configuration. SQLite persists empty-database encoding before clearing pending.

The state lock and ready behavior remain: failure invalidates ready, every apply reruns all SQL, and no automatic rollback or database/user deletion occurs. Logs/status omit credentials, password-bearing SQL and server error text.
