# Zongsoft Migration Tool

[English](README.md) | [简体中文](README.zh-Hans.md)

dotnet-migrate creates a portable migration archive and launcher. The package phase describes and prepares changes; the execution phase applies them to a target environment.

## Basic concepts

- **Migration input**: a .migration INI file listing SQL files or Amazon S3 buckets under provider sections. Section and entry order define task order.
- **Connection configuration**: .env files supply administrator credentials, database creation settings, application accounts and Amazon S3 credentials. Values expand when the package is created.
- **Package and launcher**: a matching .tar.gz archive and .sh or .cmd script. The archive carries the resolved plan and migration data; the launcher starts the bundled executor. Keep both files together.
- **Database target**: provider Database selects a default; a named migration section can select another. Empty database sections still request initialization.
- **Plan fingerprint**: a SHA-256 digest calculated from the final migration plan. It identifies the exact plan included in a package; it is not a checksum of the target database and says nothing about its current data or schema.
- **Target state directory**: the executor's local bookkeeping directory, not a live view of database or bucket health. It keeps progress files and defaults to `.migration/<migration-name>[-edition]/` beside the launcher. You may pass another directory to the launcher; reuse the same directory across package versions so completion can be tracked consistently.
- **`ready` file**: a completion marker inside the state directory. `apply` removes the old marker before starting and writes the current package's fingerprint only after every initialization, SQL task and grant succeeds. A failed or interrupted run therefore cannot leave an old marker looking current.
- **`status` command and `status.json` file**: `status` displays the saved result of the latest run, including its phase, step and failure reason; before the first run it reports that no migration has run. It exits 0 only when the current package also matches the successful `ready` marker; otherwise it exits 1. `status.json` is a run report, while `ready` is the success marker.
- **`check` command**: compares the current package's fingerprint with the `ready` marker and exits 0 for a match or 1 if the marker is missing or different. It checks local package/state files only; it does not connect to databases or Amazon S3, validate credentials, or preview SQL.
- **Existing resources**: database settings and user passwords are preserved. New grants and role memberships are additive.

Package creation prepares inputs without contacting the target. Only execution changes databases or Amazon S3 buckets.

> 💡 **Tip:** You can create and review a package without connecting to the target services. Validate it in a staging environment before applying it to production.

<a id="package-phase"></a>

## Package phase
### Install and generate

```powershell
dotnet tool install -g Zongsoft.Tools.Migrator
```

For a local source installation, run `dotnet cake --edition Release --target build` from migrator to prepare all three RIDs and create the NuGet tool package. With native artifacts already prepared, use `--target compile`. Install with `dotnet tool install -g Zongsoft.Tools.Migrator --version 0.1.0 --source ./src/bin/Release --no-http-cache`; uninstall first when replacing the same version. Cake `pack` pushes to NuGet and is not for local testing.

### Command options and example

From PowerShell in `D:/Zongsoft/hosting`, use the existing Web migration inputs:

```powershell
$env:scheme = 'default'
dotnet-migrate --name:zongsoft --version:1.0.0 --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

Required options: name and platform. Optional: version, edition, architecture (x64), output (current directory), overwrite (false), title (input name), summary and description. Summary/description support existing file:/text: sources relative to the current directory. At least one positional path is required; each supports variables, wildcards and semicolon/pipe lists. Explicit options override environment variables. Expand each argument at its position, sorting relative matches ordinally within that pattern. Missing paths warn and skip; no valid inputs/tasks fails. Existing invalid inputs fail. Only .migration files are accepted, including imports; contents remain INI, with .env parameters. SQL contents are not variable-expanded.

> - Linux supports glibc x64/arm64.
> - Windows normalize to win, x64 only.
> - Unix requires a concrete OS; osx/xos/macos have no executor and fail without outputs.

### Choose a version and Edition

`--version` accepts a nonzero `System.Version` (two, three or four numeric parts), a version file, or an existing directory containing `.version`. Relative paths resolve from the current working directory. Omitting the option, or passing an empty/whitespace value, reads only that directory's direct `.version`; the `version` environment variable is not a fallback. Files are read with Core `ApplicationVersion`, without changing them. Missing, unreadable or invalid files fail before any output is generated.

With no nonblank `--edition`, a single-version file supplies its top-level version; one named Edition is selected automatically; multiple Editions require `--edition`. Explicit Editions must exist, match case-insensitively and retain the file's spelling. `--name` is still required and independent of the application's name in that file. A literal version bypasses the version file and uses the supplied Edition.

From `D:/Zongsoft/hosting`, these alternatives use the existing Web host version file (set `scheme` as above):

```powershell
dotnet-migrate --name:zongsoft --version:web/default/.version --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
dotnet-migrate --name:zongsoft --version:web/default --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

To omit `--version`, run from `D:/Zongsoft/hosting/web/default`:

```powershell
dotnet-migrate --name:zongsoft --platform:linux --output:../../packages '../../.deploy/$(scheme)/migration/$(version)/*.migration'
```

Version paths support variables. Numeric values take precedence over paths; use `./1.0.0` for a file named `1.0.0`. The resolved version and Edition populate `$(version)`/`$(edition)`, plan identity and artifact names. Migration inputs and output paths stay relative to the working directory, even when the version file is elsewhere. See [version sources](README.md#package-phase) for expansion rules.

Numeric versions take precedence over paths; all-zero versions are rejected. Numeric values do not open .version. Prefix a numeric-looking filename with ./, such as ./1.0.0. If version is omitted or blank, only the .version file directly inside the current working directory is read; the version environment variable is ignored. Directories select their own .version. Other paths are expanded and resolved from the working directory. Unknown or cyclic variables fail, and the final version variable cannot locate its own source.

### Migration inputs and ordering

Section names are case insensitive: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`/`postgresql`, `tdengine`, and `amazon.s3`. Empty database sections initialize their target and users; empty Amazon S3 sections add no task. If any INIs are found, their combined contents must produce at least one task. Unknown sections are rejected even when empty.

Inputs are parsed in command-line order, then section order, then entry order. Wildcard matches are sorted by ordinal relative path and expanded at that argument position, without sorting the combined input list. All referenced databases and users initialize first; SQL tasks then execute in declaration/source order, followed by grants. SQL paths also support `*`, `?` and standalone `**` path segments. Each pattern's matches are sorted by ordinal relative path; overlapping matches from the same source section run once. Use zero-padded names to control ordering. Every installation and retry executes all SQL files. Script authors must ensure repeatability, including after partial failure; see the retry rules below. There is no automatic rollback across scripts or resources.

Database entries contain SQL paths with no value. Most drivers receive a complete file; SQL Server and TDengine require batch boundaries, and MySQL client `DELIMITER` directives are adapted before submission. The runner does not emulate interactive database clients.

Database connection credentials, default/explicit targets, creation settings and user permissions are described in the [database configuration](#database-configuration). Existing databases retain their settings and existing users retain passwords.

### Import shared configuration

Migration INIs and `.env` files support Core's `#@import` directive:

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

`schema.sql` resolves against `migration/shared/`. Its parameter search starts there with `schema.env`, then `sqlite.env`, before moving to parent directories. `main.sql` starts in `migration/` with `main.env` and `sqlite.env`. They produce separate tasks with their own connection parameters. Amazon S3 entries also find parameters from their declaring INI.

Parameter files can explicitly import common settings and override values:

```ini
# migration/sqlite.env
#@import common.env
[sqlite]
Database=/var/lib/example/application.db
```

Database common.env files declare provider/database/user sections. Only the selected candidate and its explicit imports are merged; required parameters are not filled from another candidate. Amazon S3 retains its existing provider-named root-parameter shorthand.

- Import paths are relative to the file containing the directive, or absolute. Separate paths with spaces, tabs or `|`. Quoted escaping, globs and variable expansion are not supported in import arguments. Imports merge the complete Profile; placing the directive inside a section does not move imported root entries into that section.
- Missing imports are skipped under Core's optional import rules. Cycles and depths above 64 files, including the root, fail. Diamond and repeated imports are allowed and read again each time. Linked configuration files retain logical paths, so imports, SQL, and adjacent parameters resolve relative to the link location.
- Each file is checked for duplicate sections, duplicate keys and syntax. Root entries, unknown providers and conflicting provider aliases in an effective migration INI remain errors, including in imported files.
- Across files, the last declaration read wins for the same section/key, case insensitively. The effective collection retains the key's first position. Overridden SQL/bucket entries do not produce tasks. Use separate command-line INI inputs when same-named entries must execute independently.
- Effective sections are split into tasks at each change of declaration source, preserving effective entry order. Overlapping SQL selections from the same source within that section are deduplicated across task splits; different sources and independent inputs are not deduplicated. Each source retains its own parameters. SQL order is preserved across task splits and database sections.
- Imported parse and parameter-expansion errors identify the actual source without exposing parameter secrets. Imports are resolved during packaging and do not execute SQL or contact Amazon S3.

### Find .env files and configure Amazon S3

There is no parameter-file command option. For each migrator, start in the declaring INI's directory for the entry, then walk through its parents to the filesystem root. At **each directory**, try:

1. The migration filename with its extension changed to `.env`, reading the matching section.
2. `<migrator>.env`; `postgres.env` precedes `postgresql.env`. A matching section takes precedence over root entries. Provider-named files may omit their section; for databases, root entries become provider settings and top-level sections become database/user sections.

A file without the applicable section is skipped unless its filename matches the provider and it has root entries. Once an applicable configuration, including its explicit imports, is found, it must be complete: values are not filled automatically from another candidate or a parent. A shared file must separate providers into sections. Missing files/configuration fail packaging and identify the searched paths. Filename case follows the build filesystem; use the lower-case names above for portability.

> 🚨 **Warning:** The first applicable `.env` file is used as a complete configuration. Migrator does not combine values from another candidate or parent directory; add shared settings through an explicit `#@import`.

The [database configuration](#database-configuration) lists all provider, database and user parameters, defaults, grants and pending recovery. Amazon S3 requires `Server`, `Region`, `AccessKey`, `SecretKey`; optional `Timeout` defaults to 30 seconds and accepts positive seconds or s/m suffixes up to one day.

Amazon S3 `Server` is a complete `http://` or `https://` endpoint without embedded credentials. The client uses path-style requests, including for RustFS. Bucket entries accept these options, separated by commas or `|`:

- **`private` / `public`:** Mutually exclusive; private is the default. Public grants anonymous `s3:GetObject` through a bucket policy, without ACLs, anonymous writes or listing.
- **`encryption:sse-s3`:** Use AES-256 server-side encryption with Amazon S3-managed keys by default.
- **`encryption:sse-kms`:** Use KMS server-side encryption. Optionally add `encryption.key:<key identifier>`; otherwise the service's default KMS key is used.
- **`versioning:enabled` / `versioning:suspended`:** Enable or suspend versioning.
  > 💡 **Tip:** Suspending versioning preserves existing object versions; it only stops adding new versions.
- **`tag.<name>:<value>`:** Add bucket tags. Names are case-sensitive, values may be empty, and up to 50 tags are allowed. Names and values are limited to 128 and 256 UTF-16 code units; names must be nonempty and cannot start with `aws:`.

Option names and mode values are case-insensitive; tag names, tag values and key identifiers preserve case. The first unquoted colon separates an option from its value, preserving subsequent colons in KMS ARNs. Surrounding whitespace is trimmed. Quote tag names containing colons, for example `tag."team:owner":zongsoft`. Single- or double-quoted text may contain commas and `|`; double the enclosing quote to include a literal quote. Duplicate access, encryption, key or versioning options and duplicate tag names are rejected. Unknown options, missing values, invalid modes and unclosed quotes fail packaging. `encryption.key` requires `encryption:sse-kms`.

These optional examples use existing hosting bucket names; they do not imply that the hosting files have been changed:

```ini
[amazon.s3]
attachments=private,encryption:sse-s3,versioning:enabled,tag.application:zongsoft.web,tag.environment:$(environment)
learning=private,versioning:enabled
```

Omitting encryption, versioning or tags leaves the service defaults and does not call the corresponding API. `Encryption` defaults to null and is omitted from JSON. An explicit object requires `Mode` to be `sse-s3` or `sse-kms`; an empty object or empty Mode is invalid. Default encryption applies to future uploads, not existing objects. Amazon S3 baseline encryption cannot be disabled, so encryption:false is not an option. SSE-C requires customer keys on individual object requests and is not a bucket default encryption option; the migrator does not create or manage KMS keys. Quotas, lifecycle rules, CORS, logging, Object Lock and internal access are outside this feature.

Existing buckets are skipped without changing their policies or other settings. New buckets receive versioning, encryption, tags and finally public-read policy, in that order. The local pending record is removed only after all configuration succeeds. If configuration fails after creation, the record allows a later retry to reapply the specified settings without skipping individual operations. Authentication/authorization failures are not treated as missing buckets. Unsupported settings or rejected requests fail installation and prevent startup; they are not silently ignored. Amazon S3 public-access restrictions can also reject a public policy.

<a id="database-configuration"></a>

### Database .env configuration

Configuration has provider, database and user levels. Parameter/provider names and enum values are case-insensitive; database names, usernames and passwords retain their spelling. Values support variable expansion. Unknown, misplaced and unsupported parameters fail during package creation.

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

In a provider-named file such as `mysql.env`, the filename can supply the provider level. The equivalent shorthand is:

```ini
Server=localhost
Database=hosting
Password=$(mysql_root_password)

[hosting application]
Password=$(application_password)
Permission=readwrite
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

> 💡 **Tip:** `[mysql]` selects the provider's `Database`. That default database can be created with provider defaults even when `[mysql hosting]` is absent; a user section such as `[mysql hosting application]` also declares `hosting`.

Parameters follow the declaration-source search: same-name `.env`, then provider-named `.env`, up through parent directories. Only explicit imports merge configuration. Database configuration requires a provider section unless the file is provider-named and has root entries; no filling from other candidates or expansion of unrelated providers.

Parameter/provider names and enum values are case insensitive. Database names, usernames and passwords retain spelling. Values support variable expansion. Unknown, misplaced or unsupported parameters fail. Spaces separate section levels; use variables or Path for file paths containing spaces.

### Provider section [provider]

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

> 🚨 **Warning:** The provider-level `Password` must be the current password of an already initialized built-in administrator (such as MySQL `root`). Migrator does not create that administrator or guess/reset its password.

### Database section [provider database]

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

### User section [provider database username]

- **`Password`:** Required and nonempty; used only when creating the account.
- **`Permission`:** `readwrite` by default; accepts `none`, `readonly`, `readwrite` or `admin`.
- **`Privileges`:** Optional unified operation capabilities, combined with the Permission preset.
- **`Roles`:** Optional existing native roles to join.
- **`Host`:** MySQL only; defaults to `%` and forms part of the account identity.

SQLite and DuckDB reject user sections. Privileges/Roles are comma- or pipe-separated lists, not arbitrary SQL. Roles are not created automatically. There is no NativePrivileges parameter; native spellings such as CREATE, CREATE TABLE and ALL PRIVILEGES are rejected. Unified names are case-insensitive, normalized and deduplicated in the following order, then combined with Permission defaults. The resulting effective capabilities are stored in the plan and fingerprinted.

- **Data:** `Select`, `Insert`, `Update`, `Delete`.
- **Routine execution:** `Execute`.
- **Creation:** `CreateTable`, `CreateIndex`, `CreateView`, `CreateProcedure`, `CreateFunction`.
- **Modification:** `AlterTable`, `AlterIndex`, `AlterView`, `AlterProcedure`, `AlterFunction`.
- **Removal:** `DropTable`, `DropIndex`, `DropView`, `DropProcedure`, `DropFunction`.

```ini
[mysql automao program]
Password=$(program_password)
Permission=ReadWrite
Privileges=CreateTable,CreateIndex,AlterTable
```

Capabilities request the privileges needed for the operation; they do not promise exclusive isolation between operations. Providers may combine coarser native permissions within the specified database, including all its business schemas. No Schemas parameter is required. Existing permissions are not revoked. Ordinary SQL routines are in scope; external code, cross-database dependencies and instance administration privileges are not added automatically.

A server account reused across referenced databases must have the same initial password. Conflicts fail before connecting without exposing passwords. MySQL identity includes Host. Existing passwords remain unchanged. Each apply adds privileges and role memberships; readonly never revokes previous grants, roles or PUBLIC rights. none skips the built-in permission set.

> 🚨 **Warning:** A user-section `Password` is used only when that account is created. Changing it does not rotate an existing account's password; rotate existing credentials through your database's normal account-management process.

- **`none`:** No built-in permissions; explicit `Privileges` and `Roles` still apply.
- **`readonly`:** `Select` and sequence value generation.
  > 💡 **Tip:** Read-only accounts can advance sequences, but do not receive business-table write or sequence-reset permissions from this preset.
- **`readwrite`:** `Select`, `Insert`, `Update`, `Delete`, `Execute` and sequence value generation.
- **`admin`:** All 20 capabilities and their dependencies. This is a database-level preset, not an instance administrator account.

Any explicit Select/Insert/Update/Delete also includes sequence value generation. CreateTable and CreateIndex include sequence creation; MySQL uses AUTO_INCREMENT and has no independent sequence grant. CreateTable includes implicit primary/unique indexes and in-database foreign-key references; AlterTable includes reference and structural dependencies. CreateView/AlterView include in-database query dependencies; routine bodies remain subject to the account's data permissions.

### Provider expansion and limitations

- MySQL: data and execution operations map to the corresponding native permissions. CreateProcedure/CreateFunction share CREATE ROUTINE; AlterProcedure/AlterFunction share ALTER ROUTINE plus CREATE ROUTINE; DropProcedure/DropFunction share ALTER ROUTINE. CreateIndex/DropIndex share INDEX; AlterIndex adds ALTER, CREATE and INSERT, while AlterTable also includes REFERENCES. CreateView includes CREATE VIEW, SHOW VIEW and SELECT; AlterView also includes DROP. DropTable/DropView share DROP. Native grants are deduplicated and database wildcard characters remain escaped. Admin expands the 20 capabilities without a global ALL grant or grant option.
- SQL Server: data and execution grants target the database. Required CREATE TABLE/VIEW/PROCEDURE/FUNCTION permissions target that database; ALTER, VIEW DEFINITION and applicable REFERENCES target business schemas. Schema ALTER also enables sequences, index maintenance and removal of other schema objects, an intentional widening of native granularity. Basic reads/writes add UPDATE on existing sequences individually; ReadOnly does not obtain table UPDATE. Execute adds SELECT on existing table-valued functions. No automatic db_owner membership is added.
- PostgreSQL: CONNECT targets the database and USAGE targets business schemas; object-creation capabilities include schema CREATE. Grants cover existing tables/sequences/routines and default object privileges for the migration connection account. CreateIndex, Alter* and Drop* check ownership rights for relevant existing objects; insufficient ownership raises MigrationPrivilegeException, derived from NotSupportedException. Neither ownership transfer nor SUPERUSER escalation occurs. Admin has the same ownership prerequisite; GRANT ALL is not a substitute. Schema CREATE permits multiple object types.
- TDengine: currently only Select, Insert and Delete are supported, using the existing READ/WRITE protocol. ReadWrite/Admin contain unsupported operations and fail early with UnsupportedOperation. Actual grants still require a server edition/version supporting that syntax. SQLite and DuckDB have no account-grant model.

AlterProcedure/AlterFunction include changing definitions. MySQL requires dropping and recreating routine bodies, so the provider grants the necessary rights, but does not rewrite or rebuild objects itself. Server prerequisites, such as MySQL binary-log restrictions on function creation, are not bypassed through instance privileges or global setting changes. Grants cannot guarantee acceptance of arbitrary SQL.

Roles must already exist. MySQL uses host `%` and verifies SHOW GRANTS against the target database, allowing empty USAGE but rejecting global, cross-database, nested or unverified role scopes; existing default roles are then merged. PostgreSQL checks inherited roles, instance attributes and pg_shdepend dependencies on other databases/shared objects; a database-specific owner role can satisfy DDL ownership. SQL Server adds target-database roles and preserves Login/User SID validation. These checks describe role scope at apply time, not continuous monitoring of later administrator changes.

Each apply appends grants to the business schemas/objects present after SQL execution. PostgreSQL default object privileges cover the migration connection account, not arbitrary future creators or new schemas. SQL Server supplemental readonly-sequence and table-valued-function grants require another apply after new objects appear. Unsupported errors identify provider, database, capability and a reason code without passwords, credential-bearing SQL or server error text. Ordinary connection/authentication errors remain execution failures.

### SQL batch preparation

`MigrationLoader.Database` runs in the migration generator and prepares batches that can be submitted directly to the driver. Each batch is written to `<extraction directory>/.migration/.artifacts/<provider>/<sequence>.sql`; the plan records their order and SHA-256 checksums. Migrator reads and executes each prepared file without splitting it. Script authors remain responsible for SQL syntax, schema changes and business semantics. Invalid SQL is reported by the driver/database during execution.

| Migrator | Submission strategy |
| --- | --- |
| PostgreSQL, DuckDB, SQLite | Submit the complete file unchanged in one command; the driver handles SQL statement boundaries, function and trigger bodies |
| MySQL | Submit the complete file in one command; remove actual `DELIMITER` directive lines and replace custom terminators outside quotes/comments with `;`, preserving procedure bodies |
| SQL Server | Submit one command per standalone `GO` line, optionally followed by a `--` comment; preserve semicolons and batch-local variables; `GO 2` is unsupported |
| TDengine | Submit one command per semicolon outside quotes/comments |

MySQL connections always enable `AllowUserVariables=true`, so scripts using `SET @variable`, `PREPARE`, `EXECUTE` and `DEALLOCATE PREPARE` work within the same session. All files in a task use one connection. A retry opens a new session and runs the task's files from the beginning; scripts must establish the session variables and temporary objects they need.

Prepared batches use UTF-8 without a BOM, preserving line endings inside each batch; checksums cover the generated bytes. Providers that accept a complete file produce one batch, preserving function, trigger, procedure and session-variable scopes. This processing does not repair SQL syntax. MySQL delimiter scanning follows the normal backslash-escape convention; scripts that change `sql_mode` to `NO_BACKSLASH_ESCAPES` should avoid ambiguous escaped quotes around client delimiters. Vendor-client commands such as `\i`, `SOURCE`, `.read`, `:r` and `:setvar` are not implemented; specify additional SQL files as INI entries instead. `CommandTimeout` applies to each submitted command, which is a whole file for drivers that support batch submission. A failure stops subsequent commands/files, but the database determines what has already committed.

### TDengine WebSocket execution

`Migrator.Database.TDengine` uses the .NET WebSocket and JSON APIs directly, without `TDengine.Connector` or bundled TDengine client libraries. The database must expose a taosAdapter WebSocket service. `Server` is a hostname or IP address; `Port` defaults to `6041`. The endpoint is `/rest/ws`, using `wss` when `Secured=true` and `ws` otherwise.

The runner authenticates with `conn`, then queries database/user catalogs, initializes missing targets/accounts and executes SQL batches and grants. Each SQL task authenticates directly to its resolved target. A task uses one WebSocket session. Requests use increasing `req_id` values; fragmented responses are assembled and checked against the expected action and request ID. For SQL that returns a result set, the runner sends `free_result` without waiting for an acknowledgement; catalog existence checks first use `fetch` to check for rows. See the [taosAdapter query implementation](https://github.com/taosdata/taosadapter/blob/main/controller/ws/query/ws.go).

`Timeout` covers the WebSocket handshake and authentication. `CommandTimeout` covers each SQL request, response and result release. Authentication errors, SQL errors, invalid responses, disconnects, timeouts and cancellation stop the migration. Messages do not echo SQL or credentials returned by the server; status records the exception type. Consult server logs for SQL error details.

### Artifact naming and contents

Names already ending in -migrate, -migration, .migrate or .migration retain that suffix (case insensitive); otherwise append -migrate. Both files use `<migration-name>[-edition]@<version>_<platform>-<architecture>`, with .tar.gz and .sh/.cmd extensions. No descriptor file is generated:

```text
packages/zongsoft-migrate@1.0.0_linux-x64.tar.gz
packages/zongsoft-migrate@1.0.0_linux-x64.sh
```

The archive contains .migration/migration.json, prepared SQL under .migration/.artifacts/, and resolved migration data. Both outputs are staged before publication; replacing existing files requires --overwrite, and failed publication restores previous outputs.

> 🚨 **Warning:** The archive contains expanded database and Amazon S3 credentials from `.env`. Restrict who can inspect, store or download the package.

<a id="execution-phase"></a>

## Execution phase

### Run the package
Keep both files together on the target. Run `sh zongsoft-migrate@1.0.0_linux-x64.sh [apply|status|check] [state-directory]`, or the corresponding cmd on Windows. The default state directory is `.migration/<migration-name>[-edition]/` beside the script, without version or RID. Versions share the lock, ready, status and database/Amazon S3 pending files.

Apply/status extract into a unique temporary directory, invoke the native executor, clean up and return its exit code. Exit codes are 0 for success, 1 for failure and 2 for invalid actions or arguments.

- **`apply` (default):** Validates the plan and SQL checksums, initializes databases and users, executes SQL/Amazon S3 tasks in order, then adds database grants.
- **`status`:** Prints the saved run report from `status.json`; exit code 0 means this package matches the successful `ready` marker, and 1 means it does not.
- **`check`:** Compares the package's SHA-256 plan fingerprint with `ready`; it exits 0 for a match or 1 with an incomplete notice otherwise. It does not show run details, extract the archive or start the executor.

> 💡 **Tip:** Run `apply` to execute a release. Use `status` to read the last run report, then `check` to confirm that the package currently on disk is the one recorded as successfully applied.

> 🚨 **Warning:** A matching `ready` marker confirms only that the same plan completed successfully before. It does not prove that the database or Amazon S3 resources have not changed since then.

### Database initialization and recovery
Validate the complete plan and all SQL checksums first. Ensure all referenced databases exist, create all accounts and database mappings, execute SQL tasks in order, then append permissions and roles for every referenced database, including empty tasks.

Multi-command initialization writes `database-<target digest>.pending`, containing only a settings digest, never credentials. Retry completes unfinished new-database settings; a settings mismatch fails explicitly instead of applying a different pending configuration. SQLite persists empty-database encoding before clearing pending.

The state lock and ready behavior remain: failure invalidates ready, every apply reruns all SQL, and no automatic rollback or database/user deletion occurs. Logs/status omit credentials, password-bearing SQL and server error text.
The executor validates the full plan and packaged SQL before contacting services. It initializes referenced databases, creates missing users and database mappings, runs tasks in order, and adds requested permissions and roles. Empty database sections also request initialization. Each apply takes a state lock; success writes ready and failure invalidates it.

Multi-command creation of a new database records a settings digest in a pending file, never credentials. Retries finish incomplete creation; a different digest fails. Existing settings and passwords are preserved. The executor does not automatically roll back or delete databases, users, buckets or SQL changes.

The launcher extracts into a unique temporary directory, starts the native executor, cleans up and returns its exit code. Status and pending files contain no credentials. Logs omit passwords, credential-bearing SQL and raw server errors; use service logs for SQL diagnostics.
### Repeatable SQL and retries

Every installation, upgrade, reinstall and `apply` runs all configured SQL files, preserving script order within each database task. There is no per-file success history or skip based on an earlier run. Script authors must make schema changes and data modifications repeatable: check object existence and expected state before adding, renaming or dropping objects, and prevent duplicate inserts or repeated accumulation. Unexpected states should fail explicitly rather than silently skip a required change.

Within one database task, if file A succeeds and file B fails, retry starts again with A. B may also have committed some statements before failing; scripts must handle those intermediate states, or the operator must repair them before retrying. The migration generator does not automatically roll back SQL. Use transactions where the database supports them.

SQL checksums only verify that packaged files match the current plan before any external resource changes. They are not an execution history and do not prevent revised SQL from running in a newly generated package. The lock, status and readiness marker govern installation completion; they do not skip files during `apply`.

> 🚨 **Warning:** Every `apply`, including a retry after partial failure, starts the configured SQL again. Make the scripts repeatable and repair partially applied changes before retrying; Migrator does not roll them back.

## Best practices

1. **Prepare the change.** Keep each release’s `.migration` inputs and SQL together. Use numbered SQL filenames where wildcard expansion determines order, and make every change safe to rerun from the first file after a partial failure.
2. **Configure access.** In the selected `.env`, set the administrator connection, explicitly declare every nondefault database, and define a separate least-privilege application user for each database. Use environment expansion for secrets, keep `.env` out of source control, and remember that the generated archive contains resolved credentials.
3. **Create and protect the package.** Generate the archive and matching launcher into a restricted directory. Keep the pair together and distribute both as sensitive release artifacts. Review the archive’s file list and plan without printing or publishing password-bearing settings.
4. **Validate in staging.** Use a stable state directory for staging, separate from production. Run `apply`, inspect `status`, and verify the resulting service behavior. After a successful apply, `check` can confirm that this package matches the ready record. If an apply fails, repair the partial state and SQL before retrying; all SQL runs again.
5. **Promote to production.** Take a recoverable backup, confirm the restore procedure, and place the package on the target. Run `apply` with the production state directory, then confirm completion with `status` or `check`. `check` only reports whether this exact package has a successful ready record; it does not test connectivity or preview SQL. Keep that production state directory across package versions.
6. **Preserve state and access boundaries.** Review existing grants separately: permissions and roles are only added, existing broader grants are not revoked, and existing passwords are not rotated. `readwrite` is the default permission, so set `Permission` explicitly.

## Packager integration

Use `--migrator:../../packages/zongsoft` in hosting/web/default packaging commands, or `--migrator:../packages/zongsoft` in daemon. Packager uses its final Edition, version and RID with the same name suffix rule to locate both artifacts. Missing companions fail without fallback. Installation packages include both files unchanged; installers pass `/var/lib/<package-name>/packager` and prevent service startup on failure. This tool does not modify existing host commands or connection settings.

<a id="build-and-test"></a>

## Build and test

The generator targets .NET 8/9/10; the executor targets .NET 10 Native AOT. Ordinary build/test does not publish native code. `dotnet cake --edition Release --target executor` builds both Linux RIDs in the dedicated Rocky Linux 9/glibc 2.34 Pod and win-x64 on Windows. See executor/build/migrator.linux-x64.yaml for mounts and DNS; Cake passes a relative YAML path. All tools inherit the repository root `.editorconfig`; the Pod and CI mount that file read-only at `/.editorconfig` and both root props files at `/Directory.Build.props` and `/Directory.Packages.props`. Linux publishing disables configuration synchronization using `-p:ZongsoftGuidelinesSynchronization=`. Changes to DNS or mounts require recreating that dedicated Pod when no build is running. Native publications reside in `executor/src/bin/<Configuration>/net10.0/<RID>/publish/`; a complete tool package requires all three RIDs for the same configuration. Build and standalone publish outputs link these files into `.migrator/<RID>/` beside the generator. The NuGet tool package stores one shared copy at `tools/.migrator/<RID>/` for all three target frameworks; the generator resolves that location when its local `.migrator/` directory is absent. CI prepares Linux and Windows publication directories separately before assembling the tool package. Each RID keeps logs/ and symbols/ alongside publish/; they are excluded from the tool package.

```powershell
dotnet build Zongsoft.Tools.Migrator.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test test/Zongsoft.Tools.Migrator.Tests.csproj -f net10.0
dotnet test executor/test/Zongsoft.Tools.Migrator.Executor.Tests.csproj -f net10.0
```

See the [implementation notes](docs/implementation.md).

Privileges uses 20 provider-independent operation names. ReadWrite includes Execute; every read/write permission includes sequence value generation. Providers expand native grants within the target database, and unsupported operations or ownership requirements fail explicitly. See [database permission details](README.md#database-configuration) for provider mappings and role-scope limits.

<a id="advanced-package-details"></a>

## Advanced package details

<details>
<summary>Plan fields and fingerprint format</summary>

Steps have no separate Id: array position defines execution order, and logs/status use one-based step numbers. Database also has no Id: Step.DatabaseIndex is a nullable integer referencing the zero-based position in Databases. SQL steps require an in-range index with a matching provider; Amazon S3 must omit it. Index 0 is serialized. Different servers may have databases with the same name; each distinct target receives its own array entry. SQL filenames use unpadded decimal numbers (1.sql, 2.sql, …, 10.sql), starting at 1 per provider on every Load. Execution follows Steps and Scripts arrays, never filename sorting. All null model properties are omitted using shared source-generated JSON options for both formatted output and compact fingerprint serialization; false, empty strings and empty collections remain. Source/Content and calculated keys still use unconditional JsonIgnore. Status records phase (validation/databases/users/steps/permissions/complete), step (one-based number or null outside a step), and databaseIndex (zero-based target array index or null).

`migration.json` is a UTF-8 JSON object written by the migration generator and read by migrator. It contains resolved execution inputs. Output property names appear below; the reader matches property names case insensitively.

| Field | Type | Meaning |
| --- | --- | --- |
| `Name` | String | Normalized migration name, including the migration suffix and optional Edition, such as `zongsoft-migrate` or `zongsoft-migrate-enterprise`. |
| `Version` | String | Version specified when generating the migration artifacts. |
| `Runtime` | string | Target RID: `linux-x64`, `linux-arm64` or `win-x64`; fingerprinted and verified before execution |
| `Title` / `Summary` / `Description` | string? | Optional display text; omitted when unset and included in the fingerprint |
| `Steps` | Array | SQL/Amazon S3 tasks in effective declaration/source order, executed after database and user initialization. |
| `Steps[].Provider` | String | Canonical provider: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`, `tdengine`, or `amazon.s3`. |
| `Steps[].Settings` | String dictionary | Amazon S3 connection parameters; empty for database tasks. Database parameters are stored in Databases. |
| `Steps[].DatabaseIndex` | Integer? | Zero-based index into Databases; required and range/provider-checked for SQL steps, omitted for Amazon S3. |
| `Databases` | Array | Referenced, deduplicated database initialization descriptions. |
| `Databases[].Provider` / `Name` | Strings | Canonical provider and actual target name; the array position identifies each initialization description. |
| `Databases[].Settings` | String dictionary | Administrator connection settings, expanded and defaulted. |
| `Databases[].Options` | String dictionary | Effective creation settings and command timeout. |
| `Databases[].Users` | Array | All users declared under this referenced database. |
| `Databases[].Users[].Name` / `Password` / `Permission` | Strings | Account name, initial password and normalized permission profile. |
| `Databases[].Users[].Privileges` / `Roles` | String arrays | Normalized, deduplicated effective unified capabilities including the Permission preset, and existing role names. |
| `Databases[].Users[].Host` | String? | MySQL account host, default `%`; omitted for other providers. |
| `Steps[].Scripts` | Array | Ordered SQL batches for database tasks; `[]` for Amazon S3 tasks. |
| `Steps[].Scripts[].Path` | String | Batch path relative to the migration archive extraction root, such as `.migration/.artifacts/mysql/1.sql`. Steps of the same provider share the directory and consecutive numbering. |
| `Steps[].Scripts[].Checksum` | String | Uppercase hexadecimal SHA-256 of the actual UTF-8 batch bytes, checked before execution. |
| `Steps[].Buckets` | Array | Bucket descriptions for Amazon S3 tasks; `[]` for database tasks. |
| `Steps[].Buckets[].Name` | String | Bucket name, such as hosting's `attachments`. |
| `Steps[].Buckets[].Public` | Boolean | `true` requests a public read policy; `false` means private. |
| `Steps[].Buckets[].Encryption` | Optional object | Default encryption, containing `Mode` and optional `Key`. |
| `Steps[].Buckets[].Encryption.Mode` | String | `sse-s3` or `sse-kms`. |
| `Steps[].Buckets[].Encryption.Key` | Optional string | Existing KMS key identifier, only for `sse-kms`. |
| `Steps[].Buckets[].Versioning` | Optional string | `enabled` or `suspended`. |
| `Steps[].Buckets[].Tags` | Optional string dictionary | Case-sensitive bucket tag names, without the INI option's `tag.` prefix. |

Omitted bucket settings do not produce configuration requests. `Script.Source` and `Script.Content` belong only to the migration generator and are excluded from JSON. Original INI/ENV paths and local SQL source paths are not protocol fields. Parameters may contain passwords or keys; the file mode is `0600`.

The fingerprint is stored outside JSON: the migration generator hashes the model's compact JSON UTF-8 bytes with SHA-256 and writes the uppercase hexadecimal result to `.migration/id`. Migrator writes it to the state directory's `ready` only after all tasks succeed. This is not a direct hash of the indented `migration.json` file. Effective database settings/users, parameters, script paths, checksums, bucket options and array order contribute to it; indentation and file line endings do not. It identifies completion of the current plan, rather than providing a signature or per-script execution history.

</details>
