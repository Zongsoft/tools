# Migration inputs and execution plans

See [README](../README.md) for commands, artifact names and execution, and [implementation](implementation.md) for project responsibilities and native builds.

## Version sources and variables

Generation resolves `--version` before expanding migration inputs and output paths. See the [hosting examples](../README.md#version-selection) for literal versions, file/directory paths and omission.

- Omitted, empty or whitespace values read exactly `<working directory>/.version`. Parent and child directories are not searched, and the `version` environment variable is ignored.
- A value parsed by `System.Version` is used directly. Two, three and four numeric parts are accepted; all-zero versions are rejected. This branch never opens a version file, even if `.version` exists and is invalid.
- Otherwise the expanded value is a file path, relative to the working directory or absolute. An existing directory selects its direct `.version`. Any file extension is allowed; `./1.0.0` selects a numeric-looking filename. Missing, unreadable or malformed files fail with the full resolved path.
- Core `ApplicationVersion` reads the file. No Edition selects the top-level version or the sole named Edition. Multiple named Editions require an explicit selection. A supplied Edition must match a section case-insensitively; a single-version file cannot accept a named Edition. The selected section's spelling is retained.
- `--name` remains mandatory, is not inferred from the file and need not match its application name. Version files are never created, saved or rewritten, on success or failure.

Explicit options override environment values for other variables. Version paths use the same variable expansion; unknown or cyclic references fail. Before selection, the final `$(version)` is unavailable and cannot be used to locate its own source. After selection, the final version and canonical Edition replace the generation variables, so input/output paths, plan identity and both artifact names agree. Selecting a version file outside the working directory never changes the base of migration inputs or output paths. Version resolution failures leave existing outputs untouched, including with `--overwrite`.

## Migrators and order

Section names are case insensitive: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`/`postgresql`, `tdengine`, and `amazon.s3`. Empty database sections initialize their target and users; empty S3 sections add no task. If any INIs are found, their combined contents must produce at least one task. Unknown sections are rejected even when empty.

Inputs are parsed in command-line order, then section order, then entry order. Wildcard matches are sorted by ordinal relative path and expanded at that argument position, without sorting the combined input list. All referenced databases and users initialize first; SQL tasks then execute in declaration/source order, followed by grants. SQL paths also support `*`, `?` and standalone `**` path segments. Each pattern's matches are sorted by ordinal relative path; overlapping matches from the same source section run once. Use zero-padded names to control ordering. Every installation and retry executes all SQL files. Script authors must ensure repeatability, including after partial failure; see the retry rules below. There is no automatic rollback across scripts or resources.

Database entries contain SQL paths with no value. Most drivers receive a complete file; SQL Server and TDengine require batch boundaries, and MySQL client `DELIMITER` directives are adapted before submission. The runner does not emulate interactive database clients.

Database connection credentials, default/explicit targets, creation settings and user permissions are described in the [complete database parameter reference](databases.md). Existing databases retain their settings and existing users retain passwords.

## How SQL scripts are submitted to drivers

[`MigrationLoader.Database`](../src/MigrationLoader.Database.Batches.cs) runs in the migration generator and prepares batches that can be submitted directly to the driver. Each batch is written to `<extraction directory>/.migration/.artifacts/<provider>/<sequence>.sql`; the plan records their order and SHA-256 checksums. Migrator reads and executes each prepared file without splitting it. Script authors remain responsible for SQL syntax, schema changes and business semantics. Invalid SQL is reported by the driver/database during execution.

| Migrator | Submission strategy |
| --- | --- |
| PostgreSQL, DuckDB, SQLite | Submit the complete file unchanged in one command; the driver handles SQL statement boundaries, function and trigger bodies |
| MySQL | Submit the complete file in one command; remove actual `DELIMITER` directive lines and replace custom terminators outside quotes/comments with `;`, preserving procedure bodies |
| SQL Server | Submit one command per standalone `GO` line, optionally followed by a `--` comment; preserve semicolons and batch-local variables; `GO 2` is unsupported |
| TDengine | Submit one command per semicolon outside quotes/comments |

MySQL connections always enable `AllowUserVariables=true`, so scripts using `SET @variable`, `PREPARE`, `EXECUTE` and `DEALLOCATE PREPARE` work within the same session. All files in a task use one connection. A retry opens a new session and runs the task's files from the beginning; scripts must establish the session variables and temporary objects they need.

Prepared batches use UTF-8 without a BOM, preserving line endings inside each batch; checksums cover the generated bytes. Providers that accept a complete file produce one batch, preserving function, trigger, procedure and session-variable scopes. This processing does not repair SQL syntax. MySQL delimiter scanning follows the normal backslash-escape convention; scripts that change `sql_mode` to `NO_BACKSLASH_ESCAPES` should avoid ambiguous escaped quotes around client delimiters. Vendor-client commands such as `\i`, `SOURCE`, `.read`, `:r` and `:setvar` are not implemented; specify additional SQL files as INI entries instead. `CommandTimeout` applies to each submitted command, which is a whole file for drivers that support batch submission. A failure stops subsequent commands/files, but the database determines what has already committed.

## TDengine WebSocket execution

[`Migrator.Database.TDengine`](../executor/src/Migrator.Database.TDengine.cs) uses the .NET WebSocket and JSON APIs directly, without `TDengine.Connector` or bundled TDengine client libraries. The database must expose a taosAdapter WebSocket service. `Server` is a hostname or IP address; `Port` defaults to `6041`. The endpoint is `/rest/ws`, using `wss` when `Secured=true` and `ws` otherwise.

The runner authenticates with `conn`, then queries database/user catalogs, initializes missing targets/accounts and executes SQL batches and grants. Each SQL task authenticates directly to its resolved target. A task uses one WebSocket session. Requests use increasing `req_id` values; fragmented responses are assembled and checked against the expected action and request ID. For SQL that returns a result set, the runner sends `free_result` without waiting for an acknowledgement; catalog existence checks first use `fetch` to check for rows. See the [taosAdapter query implementation](https://github.com/taosdata/taosadapter/blob/main/controller/ws/query/ws.go).

`Timeout` covers the WebSocket handshake and authentication. `CommandTimeout` covers each SQL request, response and result release. Authentication errors, SQL errors, invalid responses, disconnects, timeouts and cancellation stop the migration. Messages do not echo SQL or credentials returned by the server; status records the exception type. Consult server logs for SQL error details.

## Importing configuration files

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

`schema.sql` resolves against `migration/shared/`. Its parameter search starts there with `schema.env`, then `sqlite.env`, before moving to parent directories. `main.sql` starts in `migration/` with `main.env` and `sqlite.env`. They produce separate tasks with their own connection parameters. S3 entries also find parameters from their declaring INI.

Parameter files can explicitly import common settings and override values:

```ini
# migration/sqlite.env
#@import common.env
[sqlite]
Database=/var/lib/example/application.db
```

Database common.env files declare provider/database/user sections. Only the selected candidate and its explicit imports are merged; required parameters are not filled from another candidate. S3 retains its existing provider-named root-parameter shorthand.

- Import paths are relative to the file containing the directive, or absolute. Separate paths with spaces, tabs or `|`. Quoted escaping, globs and variable expansion are not supported in import arguments. Imports merge the complete Profile; placing the directive inside a section does not move imported root entries into that section.
- Missing imports are skipped under Core's optional import rules. Cycles and depths above 64 files, including the root, fail. Diamond and repeated imports are allowed and read again each time. Linked configuration files retain logical paths, so imports, SQL, and adjacent parameters resolve relative to the link location.
- Each file is checked for duplicate sections, duplicate keys and syntax. Root entries, unknown providers and conflicting provider aliases in an effective migration INI remain errors, including in imported files.
- Across files, the last declaration read wins for the same section/key, case insensitively. The effective collection retains the key's first position. Overridden SQL/bucket entries do not produce tasks. Use separate command-line INI inputs when same-named entries must execute independently.
- Effective sections are split into tasks at each change of declaration source, preserving effective entry order. Overlapping SQL selections from the same source within that section are deduplicated across task splits; different sources and independent inputs are not deduplicated. Each source retains its own parameters. SQL order is preserved across task splits and database sections.
- Imported parse and parameter-expansion errors identify the actual source without exposing parameter secrets. Imports are resolved during packaging and do not execute SQL or contact S3.

## Finding `.env` parameters

There is no parameter-file command option. For each migrator, start in the declaring INI's directory for the entry, then walk through its parents to the filesystem root. At **each directory**, try:

1. The migration filename with its extension changed to `.env`, reading the matching section.
2. `<migrator>.env`; `postgres.env` precedes `postgresql.env`. A matching section takes precedence over root entries. Only S3 provider-named files may omit their section.

A file without the applicable section is skipped unless it is S3-provider-named and has root entries. Once an applicable configuration, including its explicit imports, is found, it must be complete: values are not filled automatically from another candidate or a parent. A shared file must separate providers into sections. Missing files/configuration fail packaging and identify the searched paths. Filename case follows the build filesystem; use the lower-case names above for portability.

The [database reference](databases.md) lists all provider, database and user parameters, defaults, grants and pending recovery. S3 requires `Server`, `Region`, `AccessKey`, `SecretKey`; optional `Timeout` defaults to 30 seconds and accepts positive seconds or s/m suffixes up to one day.

S3 `Server` is a complete `http://` or `https://` endpoint without embedded credentials. The client uses path-style requests, including for RustFS. Bucket entries accept these options, separated by commas or `|`:

| Option | Meaning |
| --- | --- |
| `private` / `public` | Mutually exclusive; private by default. Public grants anonymous `s3:GetObject` through a bucket policy, without ACLs or anonymous writes/listing |
| `encryption:sse-s3` | Default AES-256 server-side encryption with S3-managed keys |
| `encryption:sse-kms` | Default KMS server-side encryption; optionally specify `encryption.key:<key identifier>`, otherwise use the service's default KMS key |
| `versioning:enabled` / `versioning:suspended` | Enable or suspend versioning; suspension does not remove existing versions |
| `tag.<name>:<value>` | Bucket tags with case-sensitive names and optionally empty values; up to 50 tags, with key/value limits of 128/256 UTF-16 code units; keys must be nonempty and must not start with aws: |

Option names and mode values are case-insensitive; tag names, tag values and key identifiers preserve case. The first unquoted colon separates an option from its value, preserving subsequent colons in KMS ARNs. Surrounding whitespace is trimmed. Quote tag names containing colons, for example `tag."team:owner":zongsoft`. Single- or double-quoted text may contain commas and `|`; double the enclosing quote to include a literal quote. Duplicate access, encryption, key or versioning options and duplicate tag names are rejected. Unknown options, missing values, invalid modes and unclosed quotes fail packaging. `encryption.key` requires `encryption:sse-kms`.

These optional examples use existing hosting bucket names; they do not imply that the hosting files have been changed:

```ini
[amazon.s3]
attachments=private,encryption:sse-s3,versioning:enabled,tag.application:zongsoft.web,tag.environment:$(environment)
learning=private,versioning:enabled
```

Omitting encryption, versioning or tags leaves the service defaults and does not call the corresponding API. `Encryption` defaults to null and is omitted from JSON. An explicit object requires `Mode` to be `sse-s3` or `sse-kms`; an empty object or empty Mode is invalid. Default encryption applies to future uploads, not existing objects. Amazon S3 baseline encryption cannot be disabled, so encryption:false is not an option. SSE-C requires customer keys on individual object requests and is not a bucket default encryption option; the migrator does not create or manage KMS keys. Quotas, lifecycle rules, CORS, logging, Object Lock and internal access are outside this feature.

Existing buckets are skipped without changing their policies or other settings. New buckets receive versioning, encryption, tags and finally public-read policy, in that order. The local pending record is removed only after all configuration succeeds. If configuration fails after creation, the record allows a later retry to reapply the specified settings without skipping individual operations. Authentication/authorization failures are not treated as missing buckets. Unsupported settings or rejected requests fail installation and prevent startup; they are not silently ignored. S3 public-access restrictions can also reject a public policy.

## migration.json fields

Steps have no separate Id: array position defines execution order, and logs/status use one-based step numbers. Database also has no Id: Step.DatabaseIndex is a nullable integer referencing the zero-based position in Databases. SQL steps require an in-range index with a matching provider; S3 must omit it. Index 0 is serialized. Different servers may have databases with the same name; each distinct target receives its own array entry. SQL filenames use unpadded decimal numbers (1.sql, 2.sql, …, 10.sql), starting at 1 per provider on every Load. Execution follows Steps and Scripts arrays, never filename sorting. All null model properties are omitted using shared source-generated JSON options for both formatted output and compact fingerprint serialization; false, empty strings and empty collections remain. Source/Content and calculated keys still use unconditional JsonIgnore. Status records phase (validation/databases/users/steps/permissions/complete), step (one-based number or null outside a step), and databaseIndex (zero-based target array index or null).

`migration.json` is a UTF-8 JSON object written by the migration generator and read by migrator. It contains resolved execution inputs. Output property names appear below; the reader matches property names case insensitively.

| Field | Type | Meaning |
| --- | --- | --- |
| `Name` | String | Normalized migration name, including the migration suffix and optional Edition, such as `zongsoft-migrate` or `zongsoft-migrate-enterprise`. |
| `Version` | String | Version specified when generating the migration artifacts. |
| `Runtime` | string | Target RID: `linux-x64`, `linux-arm64` or `win-x64`; fingerprinted and verified before execution |
| `Title` / `Summary` / `Description` | string? | Optional display text; omitted when unset and included in the fingerprint |
| `Steps` | Array | SQL/S3 tasks in effective declaration/source order, executed after database and user initialization. |
| `Steps[].Provider` | String | Canonical provider: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`, `tdengine`, or `amazon.s3`. |
| `Steps[].Settings` | String dictionary | S3 connection parameters; empty for database tasks. Database parameters are stored in Databases. |
| `Steps[].DatabaseIndex` | Integer? | Zero-based index into Databases; required and range/provider-checked for SQL steps, omitted for S3. |
| `Databases` | Array | Referenced, deduplicated database initialization descriptions. |
| `Databases[].Provider` / `Name` | Strings | Canonical provider and actual target name; the array position identifies each initialization description. |
| `Databases[].Settings` | String dictionary | Administrator connection settings, expanded and defaulted. |
| `Databases[].Options` | String dictionary | Effective creation settings and command timeout. |
| `Databases[].Users` | Array | All users declared under this referenced database. |
| `Databases[].Users[].Name` / `Password` / `Permission` | Strings | Account name, initial password and normalized permission profile. |
| `Databases[].Users[].Privileges` / `Roles` | String arrays | Normalized, deduplicated effective unified capabilities including the Permission preset, and existing role names. |
| `Databases[].Users[].Host` | String? | MySQL account host, default `%`; omitted for other providers. |
| `Steps[].Scripts` | Array | Ordered SQL batches for database tasks; `[]` for S3 tasks. |
| `Steps[].Scripts[].Path` | String | Batch path relative to the migration archive extraction root, such as `.migration/.artifacts/mysql/1.sql`. Steps of the same provider share the directory and consecutive numbering. |
| `Steps[].Scripts[].Checksum` | String | Uppercase hexadecimal SHA-256 of the actual UTF-8 batch bytes, checked before execution. |
| `Steps[].Buckets` | Array | Bucket descriptions for S3 tasks; `[]` for database tasks. |
| `Steps[].Buckets[].Name` | String | Bucket name, such as hosting's `attachments`. |
| `Steps[].Buckets[].Public` | Boolean | `true` requests a public read policy; `false` means private. |
| `Steps[].Buckets[].Encryption` | Optional object | Default encryption, containing `Mode` and optional `Key`. |
| `Steps[].Buckets[].Encryption.Mode` | String | `sse-s3` or `sse-kms`. |
| `Steps[].Buckets[].Encryption.Key` | Optional string | Existing KMS key identifier, only for `sse-kms`. |
| `Steps[].Buckets[].Versioning` | Optional string | `enabled` or `suspended`. |
| `Steps[].Buckets[].Tags` | Optional string dictionary | Case-sensitive bucket tag names, without the INI option's `tag.` prefix. |

Omitted bucket settings do not produce configuration requests. `Script.Source` and `Script.Content` belong only to the migration generator and are excluded from JSON. Original INI/ENV paths and local SQL source paths are not protocol fields. Parameters may contain passwords or keys; the file mode is `0600`.

The fingerprint is stored outside JSON: the migration generator hashes the model's compact JSON UTF-8 bytes with SHA-256 and writes the uppercase hexadecimal result to `.migration/id`. Migrator writes it to the state directory's `ready` only after all tasks succeed. This is not a direct hash of the indented `migration.json` file. Effective database settings/users, parameters, script paths, checksums, bucket options and array order contribute to it; indentation and file line endings do not. It identifies completion of the current plan, rather than providing a signature or per-script execution history.

## Repeatable SQL and retries

Every installation, upgrade, reinstall and `apply` runs all configured SQL files, preserving script order within each database task. There is no per-file success history or skip based on an earlier run. Script authors must make schema changes and data modifications repeatable: check object existence and expected state before adding, renaming or dropping objects, and prevent duplicate inserts or repeated accumulation. Unexpected states should fail explicitly rather than silently skip a required change.

Within one database task, if file A succeeds and file B fails, retry starts again with A. B may also have committed some statements before failing; scripts must handle those intermediate states, or the operator must repair them before retrying. The migration generator does not automatically roll back SQL. Use transactions where the database supports them.

SQL checksums only verify that packaged files match the current plan before any external resource changes. They are not an execution history and do not prevent revised SQL from running in a newly generated package. The lock, status and readiness marker govern installation completion; they do not skip files during `apply`.
