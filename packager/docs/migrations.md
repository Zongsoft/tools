# Installation migrations

[English](migrations.md) | [简体中文](migrations.zh-Hans.md)

`--migration` adds database/bootstrap and S3 bucket initialization to tar, Debian and RPM installation packages. It accepts one or more `.ini` paths separated by `;` or `|`. Quote the entire option so the shell preserves separators and variable expressions.

```text
--migration:../../.deploy/$(scheme)/migration/$(version)/*.ini
```

Paths on the command line are relative to `--source`. Migration INI paths support `*` and `?` in any path segment and a standalone `**` for zero or more directory levels, for example `../../.deploy/$(scheme)/migration/$(version)/*.ini`. Matches are processed in ordinal relative-path order within each argument; argument order is preserved. A missing INI file or a pattern without matches produces a warning and is skipped. Remaining files keep their order. If no input files are found, packaging proceeds without migration files, a migrator or migration startup checks; invalid existing INIs, missing `.env` parameters and missing SQL scripts remain errors. A valid empty INI adds no task; if any INIs were found but none contains a nonempty section, packaging fails with no migration tasks. Paths inside an INI file are relative to that INI file, not the host directory. Both INI files and `.env` parameter files use Zongsoft.Core's `Profile` parser, as does the deployment tool. Full-line `#`/`;` comments and bare entries are supported. Entry names are case insensitive under `Profile`; duplicate keys are rejected. Repeated migrator sections within one INI, including both PostgreSQL aliases, are rejected; separate files and repeated file arguments are parsed independently.

Migration files, SQL path expressions, bucket names/options and parameter values support the existing `$(name)` and `%name%` variables. They are resolved when packaging. SQL contents are not expanded; client delimiters are processed during packaging. Undefined variables, unmatched SQL patterns, unsupported providers/options, missing parameters and conflicting generated paths fail packaging with a nonzero exit code. Do not store shell expressions in parameter values; these files are INI, not shell scripts.

The intermediate file is named **`migration.json`**, installed at `<install-directory>/.migration/migration.json`.

SQL batches are grouped by canonical provider, for example `.migration/.artifacts/mysql/0001.sql` and `.migration/.artifacts/postgres/0001.sql`. Each plan load starts a separate counter at 0001 for each provider; tasks using the same provider share consecutive numbers. PostgreSQL aliases share the postgres directory. Each nonempty section remains an independent task with its own parameters and script list; task IDs identify logs and status, not directories. Overlapping SQL matches are deduplicated within a section, but sections, files and repeated INI arguments remain independent. S3 configuration stays in the plan without an empty artifact directory.

## Migrators and order

Section names are case insensitive: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`/`postgresql`, `tdengine`, and `amazon.s3`. Empty sections add no task. If any INIs are found, their combined contents must produce at least one task. Unknown sections are rejected even when empty.

Inputs are parsed in command-line order, then section order, then entry order. Wildcard matches are sorted by ordinal relative path and expanded at that argument position, without sorting the combined input list. Execution order across tasks is not guaranteed and must not express dependencies; SQL within each database task runs in script-list order. SQL paths also support `*`, `?` and standalone `**` path segments. Each pattern's matches are sorted by ordinal relative path; overlapping matches run once within the section. Use zero-padded names to control ordering. Every installation and retry executes all SQL files. Script authors must ensure repeatability, including after partial failure; see the retry rules below. There is no automatic rollback across scripts or resources.

Database entries contain SQL paths with no value. Most drivers receive a complete file; SQL Server and TDengine require batch boundaries, and MySQL client `DELIMITER` directives are adapted before submission. The runner does not emulate interactive database clients.

For network databases the runner connects to the bootstrap database, checks/creates the target database, then connects to the target and runs SQL. SQL Server defaults to bootstrap `master`, PostgreSQL to `postgres`; MySQL and TDengine omit the database unless configured. The credential needs the corresponding create/query/DDL permissions. SQLite and DuckDB create the parent directory and database file; `Database` must be an absolute Linux target path. Custom service users need filesystem permissions to use that file.

## How SQL scripts are submitted to drivers

[`MigrationLoader.Database`](../src/MigrationLoader.Database.Batches.cs) runs in the packager and prepares batches that can be submitted directly to the driver. Each batch is written to `<installation directory>/.migration/.artifacts/<provider>/<four-digit sequence>.sql`; the plan records their order and SHA-256 checksums. Migrator reads and executes each prepared file without splitting it. Script authors remain responsible for SQL syntax, schema changes and business semantics. Invalid SQL is reported by the driver/database during installation.

| Migrator | Submission strategy |
| --- | --- |
| PostgreSQL, DuckDB, SQLite | Submit the complete file unchanged in one command; the driver handles SQL statement boundaries, function and trigger bodies |
| MySQL | Submit the complete file in one command; remove actual `DELIMITER` directive lines and replace custom terminators outside quotes/comments with `;`, preserving procedure bodies |
| SQL Server | Submit one command per standalone `GO` line, optionally followed by a `--` comment; preserve semicolons and batch-local variables; `GO 2` is unsupported |
| TDengine | Submit one command per semicolon outside quotes/comments |

MySQL connections always enable `AllowUserVariables=true`, so scripts using `SET @variable`, `PREPARE`, `EXECUTE` and `DEALLOCATE PREPARE` work within the same session. All files in a task use one connection. A retry opens a new session and runs the task's files from the beginning; scripts must establish the session variables and temporary objects they need.

Prepared batches use UTF-8 without a BOM, preserving line endings inside each batch; checksums cover the generated bytes. Providers that accept a complete file produce one batch, preserving function, trigger, procedure and session-variable scopes. This processing does not repair SQL syntax. MySQL delimiter scanning follows the normal backslash-escape convention; scripts that change `sql_mode` to `NO_BACKSLASH_ESCAPES` should avoid ambiguous escaped quotes around client delimiters. Vendor-client commands such as `\i`, `SOURCE`, `.read`, `:r` and `:setvar` are not implemented; specify additional SQL files as INI entries instead. `CommandTimeout` applies to each submitted command, which is a whole file for drivers that support batch submission. A failure stops subsequent commands/files, but the database determines what has already committed.

## TDengine WebSocket execution

[`Migrator.Database.TDengine`](../migrator/src/Migrator.Database.TDengine.cs) uses the .NET WebSocket and JSON APIs directly, without `TDengine.Connector` or bundled TDengine client libraries. The database must expose a taosAdapter WebSocket service. `Server` is a hostname or IP address; `Port` defaults to `6041`. The endpoint is `/rest/ws`, using `wss` when `Secured=true` and `ws` otherwise.

The runner authenticates with `conn`, then sends `query` requests for `CREATE DATABASE IF NOT EXISTS`, `USE` and each SQL batch. A task uses one WebSocket session. Requests use increasing `req_id` values; fragmented responses are assembled and checked against the expected action and request ID. For SQL that returns a result set, the runner sends `free_result` without reading rows or waiting for an acknowledgement. See the [taosAdapter query implementation](https://github.com/taosdata/taosadapter/blob/main/controller/ws/query/ws.go).

`Timeout` covers the WebSocket handshake and authentication. `CommandTimeout` covers each SQL request, response and result release. Authentication errors, SQL errors, invalid responses, disconnects, timeouts and cancellation stop the migration. Messages do not echo SQL or credentials returned by the server; status records the exception type. Consult server logs for SQL error details.

## Finding `.env` parameters

There is no parameter-file command option. For each migrator, start in the migration INI's directory, then walk through its parents to the filesystem root. At **each directory**, try:

1. The migration filename with its extension changed to `.env`, reading the matching section.
2. `<migrator>.env`; `postgres.env` precedes `postgresql.env`. A matching section takes precedence over root entries. A provider-named file may omit its section.

A file without the applicable section is skipped unless it is provider-named and has root entries. Once an applicable configuration is found, it must be complete: values are not merged with another file or a parent. A shared file must separate providers into sections. Missing files/configuration fail packaging and identify the searched paths. Filename case follows the build filesystem; use the lower-case names above for portability.

| Migrator | Required parameters | Optional parameters |
| --- | --- | --- |
| `mssql`, `mysql`, `postgres`, `tdengine` | `Server`, `Database`, `UserName` | `Port`, `Password`, `BootstrapDatabase`, `Timeout`, `CommandTimeout`, `Secured` |
| `mssql` only | — | `TrustServerCertificate` (default `false`) |
| `sqlite`, `duckdb` | `Database` | `CommandTimeout`; `Timeout` is accepted but file databases do not have a network connection timeout |
| `amazon.s3` | `Server`, `Region`, `AccessKey`, `SecretKey` | `Timeout` |

Parameter names are case insensitive. `Timeout` defaults to 30 seconds, `CommandTimeout` to 300 seconds; positive integers optionally ending in `s` or `m` are accepted, up to one day. `Port` is 1–65535; boolean values use `true`/`false`. An omitted password is empty. SQL Server enables encryption by default. MySQL/PostgreSQL use their driver's defaults unless `Secured` is provided. TDengine uses WebSocket, port `6041`, without TLS unless `Secured=true`. `TrustServerCertificate` is only accepted for SQL Server.

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

## Zongsoft hosting example

The local `D:/Zongsoft/hosting` checkout already contains these migration files. Web's `web/default/pack.cmd` and `deploy.cmd` select INIs using the version directory wildcard:

```text
.deploy/default/migration/
├── mysql.env
├── amazon.s3.env
└── 1.0.0/
    ├── mysql.ini
    └── amazon.s3.ini
```

The first entries in `1.0.0/mysql.ini` are shown below. The actual file also includes Administratives schema and province/city/district/street data scripts; preserve its complete list and order:

```ini
[mysql]
../../../../../framework/Zongsoft.Security/database/Zongsoft.Security-mysql.sql
../../../../../framework/upgrading/database/zongsoft.upgrading-mysql.sql
../../../../../discussions/database/Zongsoft.Discussions-mysql.sql
```

Paths resolve from the directory containing `1.0.0/mysql.ini` into adjacent local repositories. The current `1.0.0/amazon.s3.ini` contains:

```ini
[amazon.s3]
learning
temporary
upgrading
attachments
```

Bare bucket entries default to private. Encryption, versioning and tags are unspecified, so their JSON properties are omitted and the corresponding configuration APIs are not called. Shared `mysql.env` and `amazon.s3.env` live in the parent directory and may omit provider sections. Populate them from the target MySQL/RustFS configuration; actual credentials are not reproduced here. Database names and deployment locations must match the host's plugin configuration.

When packaging from `hosting/web/default`, the existing command uses this option. CMD expands `%scheme%` first; the packager expands `$(version)` using the resolved application version:

```text
--migration:"../../.deploy/%scheme%/migration/$(version)/*.ini;"
```

The trailing empty path is ignored. With `scheme=default` and version `1.0.0`, ordinal relative-path order yields `amazon.s3.ini`, then `mysql.ini`; this does not promise execution order across tasks. To reuse these files with `hosting/daemon` as the source, add the option below; daemon's current pack.cmd does not itself enable migrations:

```text
"--migration:../.deploy/$(scheme)/migration/$(version)/*.ini"
```

Also provide `--scheme:default` or the equivalent environment variable. The README's `hosting/publish` staging source uses the same `../.deploy/` prefix. A missing version directory only produces a warning and is skipped, so ensure the chosen version contains the initialization scripts it needs.

Keep Web's existing payload entries and Nginx hooks. Its entry is `Zongsoft.Hosting.Web.dll`, with `--name:Zongsoft.Hosting.Web --title:Zongsoft.Web --daemon:zongsoft.web --daemon-bind:8069`. The daemon command uses `--name:zongsoft.daemon`; the existing host lookup rules determine its entry assembly. The double quotes above are for CMD. In PowerShell/Bash, single-quote the entire packager-expression option to preserve `$()`, and replace `%scheme%` with the packager's `$(scheme)` or a value already expanded by the shell.

## migration.json fields

`migration.json` is a UTF-8 JSON object written by the packager and read by migrator. It contains resolved execution inputs. Output property names appear below; the reader matches property names case insensitively.

| Field | Type | Meaning |
| --- | --- | --- |
| `FormatVersion` | Integer | Intermediate format version, currently `1`; distinct from application and packager versions. |
| `Package` | String | Final package name, such as hosting Web's `zongsoft.web`, including the selected Edition suffix when present. |
| `Version` | String | Application version selected for this package. |
| `Tasks` | Array | Independent tasks in input parsing order. Positions do not express dependencies or guarantee execution order across tasks. |
| `Tasks[].Id` | String | Unique task identifier within the plan, such as `0002-mysql`, used for logs and status rather than SQL directory names. |
| `Tasks[].Provider` | String | Canonical provider: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`, `tdengine`, or `amazon.s3`. |
| `Tasks[].Parameters` | String dictionary | Connection parameters resolved from `.env`, with variables expanded. Parameter names are case insensitive; values remain strings. See the parameter rules above. |
| `Tasks[].Scripts` | Array | Ordered SQL batches for database tasks; `[]` for S3 tasks. |
| `Tasks[].Scripts[].Path` | String | Batch path relative to the installation root, such as `.migration/.artifacts/mysql/0001.sql`. Tasks of the same provider share the directory and consecutive numbering. |
| `Tasks[].Scripts[].Checksum` | String | Uppercase hexadecimal SHA-256 of the actual UTF-8 batch bytes, checked before execution. |
| `Tasks[].Buckets` | Array | Bucket descriptions for S3 tasks; `[]` for database tasks. |
| `Tasks[].Buckets[].Name` | String | Bucket name, such as hosting's `attachments`. |
| `Tasks[].Buckets[].Public` | Boolean | `true` requests a public read policy; `false` means private. |
| `Tasks[].Buckets[].Encryption` | Optional object | Default encryption, containing `Mode` and optional `Key`. |
| `Tasks[].Buckets[].Encryption.Mode` | String | `sse-s3` or `sse-kms`. |
| `Tasks[].Buckets[].Encryption.Key` | Optional string | Existing KMS key identifier, only for `sse-kms`. |
| `Tasks[].Buckets[].Versioning` | Optional string | `enabled` or `suspended`. |
| `Tasks[].Buckets[].Tags` | Optional string dictionary | Case-sensitive bucket tag names, without the INI option's `tag.` prefix. |

Omitted bucket settings do not produce configuration requests. `Script.Source` and `Script.Content` belong only to the packager and are excluded from JSON. Original INI/ENV paths and local SQL source paths are not protocol fields. Parameters may contain passwords or keys; the file mode is `0600`.

The fingerprint is stored outside JSON: the packager hashes the model's compact JSON UTF-8 bytes with SHA-256 and writes the uppercase hexadecimal result to `.migration/id`. Migrator writes it to the state directory's `ready` only after all tasks succeed. This is not a direct hash of the indented `migration.json` file. Parameters, script paths, checksums, bucket options and array order contribute to it; indentation and file line endings do not. It identifies completion of the current plan, rather than providing a signature or per-script execution history.

## Collaboration between the packager and migrator

The two programs exchange `migration.json` and the SQL files inside the installation package. As in Zongsoft upgrading's upgrader/deployer design, protocol source is linked into both projects; neither process loads the other's assembly.

| Component | Output and responsibility |
| --- | --- |
| [`src`](../src/Zongsoft.Tools.Packager.csproj) | The `dotnet-pack` tool: parse INI through Core Profile, find `.env` parameters, expand variables and SQL paths, prepare SQL batches, parse bucket options, build the plan and collect installation payloads |
| [`.shared`](../.shared/MigrationPlan.cs) | Source files only: plan models, JSON serialization/fingerprints and provider/parameter validation with localized resources; compiled separately into both programs, with no shared DLL |
| [`migrator`](../migrator/src/Zongsoft.Tools.Packager.Migrator.csproj) | `Zongsoft.Tools.Packager.Migrator`: the independent Linux command-line program, database/S3 execution, file integrity checks, locking, status and readiness |

`MigrationPlan` is partial and groups its `Step`, `Script` and `Bucket` models as nested types. The build-side `Script.Source` (original path) and `Script.Content` (prepared batch text) properties are partial extensions compiled only in the packager and excluded from JSON. The runtime uses the packaged script path and checksum. The plan contains `FormatVersion=1`, `Package`, `Version` and ordered `Tasks`; scripts contain packaged `Path` and SHA-256 `Checksum` values. Its fingerprint is SHA-256 of compact JSON encoded as UTF-8. The namespace is `Zongsoft.Tools.Packager.Migration`; `MigrationProvider` owns canonical names, aliases and parameter rules, using nested Database/AmazonS3 descriptors. `MigrationUtility` only reads parameter values and parses timeouts. Plan structure is validated by `MigrationPlan.Validate`.

`MigrationLoader` handles file/section traversal, variables, parameter lookup and error locations. Its private nested `Database` owns SQL selection, sorting, deduplication, batch preparation and checksums; `AmazonS3` owns bucket text and duplicate names. Runtime `Migrator.Database` is an abstract partial base with shared ADO.NET execution helpers. Nested `MsSql`, `MySql`, `Postgres`, `Sqlite` and `DuckDB` own their connection and bootstrap rules; `TDengine` owns its WebSocket session. `Migrator.Create` uses explicit dispatch. `MigrationExecutor` validates, locks, schedules and records completion. Drivers and AWS SDK belong only to migrator. Plan JSON is source-generated; TDengine requests, S3 policies and execution status use `Utf8JsonWriter`.

The main project neither references nor builds migrator. `.shared/Migration.props` links protocol source and resources into each program. Like upgrading, the packager consumes an independently prepared executable through a directory convention. Migrator targets **net10.0** with `PublishAot` and `IsAotCompatible`; each native publication directory contains `Zongsoft.Tools.Packager.Migrator` and its required native libraries. The packager includes `src/.migrator/<RID>/` as ordinary Content. The host framework does not determine the native migrator's dependencies.

1. **Build**: explicit Cake `migrator` prepares glibc `linux-x64` and `linux-arm64` directories under `src/.migrator/`. Symbols are stored separately. The complete tool-package workflow requires both publications; ordinary `dotnet build/test` never starts a container.
2. **Package**: `MigrationLoader` produces the plan. `MigrationBundle` verifies the selected RID's ELF entry and collects its complete directory, adding SQL batches, `migration.json`, `id` and `migrate.sh`. It does not parse or prune assembly dependencies.
3. **Install**: `migrate.sh apply` directly executes the native migrator. A successful exit permits application startup. The host does not reference migration assemblies.

Packaging does not connect to databases or S3. The programs communicate through these files:

| File | Writer → reader | Content |
| --- | --- | --- |
| `.migration/migration.json` | Packager → migrator | Expanded parameters, ordered tasks, SQL paths/checksums and bucket descriptions; mode `0600` |
| `.migration/.artifacts/<provider>/<four-digit sequence>.sql` | Packager → migrator | Executable SQL batches, all checked before execution; mode `0644` |
| `.migration/id` | Packager → `migrate.sh check` | Plan fingerprint; mode `0644` |
| `/var/lib/<package-name>/packager/status.json` | Migrator → `status` | Task, result and exception type recorded when the latest run completed or failed; not live progress |
| `/var/lib/<package-name>/packager/ready` | Migrator → `migrate.sh check` | Fingerprint written after all tasks succeed; mode `0644` |

For the Web host, the plan is `/opt/zongsoft/web/.migration/migration.json` and the completion marker is `/var/lib/zongsoft.web/packager/ready`. The shell returns the result to installation through the exit code: `0` for success, `1` for execution failure, and `2` for invalid standalone runner arguments. Installation continues to host startup only after success.


### Native build environment

[`migrator/build/packager.linux-x64.yaml`](../migrator/build/packager.linux-x64.yaml) defines an independent Rocky Linux 9 Pod and a persistent toolchain cache. Its workspace mount defaults to `D:\Zongsoft\tools\packager` through `/mnt/d/`; adjust that mount when the checkout is elsewhere. The framework reference Pod is not changed. Podman must be running before the explicit Cake `migrator` task.

`setup.sh` installs the .NET 10 SDK and clang/lld, then extracts ARM64 RPMs from the same Rocky baseline into a sysroot. `publish.sh` publishes x64 natively and ARM64 with the target sysroot and cross linker. Publications include necessary `.so` and language resources; symbols and full warning/ELF evidence stay under `migrator/src/bin/aot/<RID>/`. Dependency versions remain fixed in the migrator project. Native execution validation is separate from compilation; ARM64 execution may be omitted when no native or emulated environment is available. See the [verification record](migration-verification.md) for actual results.

### Building the tool package

Run these commands in order from the packager directory:

```powershell
dotnet cake --target=migrator --edition=Release
dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release
```

`dotnet build src/Zongsoft.Tools.Packager.csproj` compiles the packager without the migrator project or its artifacts and does not automatically create a NuGet package. Prepare a complete tool package by running the `migrator` task above, or place an independently published runtime directory in `src/.migrator/<RID>/` before `dotnet pack`. Without that directory, the tool still builds and creates ordinary packages; a valid migration task reports the missing RID runtime; skipping all missing INIs does not require one. No additional command option or MSBuild path property is required.

[`test`](../test/Zongsoft.Tools.Packager.Tests.csproj) covers packager input, SQL batch preparation, package generation and JSON handoff; [`migrator/test`](../migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj) references only the migrator and covers databases, S3 and TDengine WebSocket. Handoff tests run the independent migrator with `apply/check`, verify real SQLite batches, separate task parameters, internal SQL order and the packager fingerprint, and cover task-array fingerprint changes and SQL tampering that fails execution and clears readiness. The packager test project sets `ReferenceOutputAssembly=false` on its migrator reference, keeping only a build dependency. Neither suite uses assembly or `using` aliases. Run the projects separately:

```powershell
dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0
dotnet test migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj -f net10.0
```

## How migrate.sh works

[`MigrationBundle.Attach`](../src/MigrationBundle.cs) generates this script for the package identity, with LF line endings and mode `0755`. The existing hosting Web parameters, whose package name is `zongsoft.web`, produce:

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

`BASE_DIR` comes from the script's own location, independently of the caller's working directory. This script is installed at `/opt/zongsoft/web/.migration/migrate.sh`. `${1:-apply}` defaults to `apply` when no action is supplied. `exec` replaces the shell process with the native process, so the runner's exit code becomes the script's exit code. The script itself contains no SQL, connection strings or bucket-creation implementation.

| Action | Execution path | Dependencies and result |
| --- | --- | --- |
| `apply` (default) | Shell → migrator → `MigrationExecutor` → database/S3 migrators | Requires the native migrator publication and OS libraries; returns 0 on success and nonzero on failure; does not directly start the host |
| `status` | Shell → migrator, reading status and checking the plan fingerprint | Requires the native migrator publication; prints status and returns 0 when this plan is ready, otherwise 1 |
| `check` | Shell directly compares `ready` with the packaged `id` using `cmp -s` | Does not launch migrator, read credential-bearing `migration.json` or connect to databases/S3; returns 0 when both files exist and match byte for byte, otherwise 1 |

**`apply/status` directly execute the native `Zongsoft.Tools.Packager.Migrator`.** Distribute its complete RID publication, including required `.so` libraries. There is no `dotnet` launch, `.deps.json` processing or target .NET runtime requirement. Native OS libraries remain required.

After acquiring the file lock, `MigrationExecutor` removes the old `ready`, validates all SQL files, then currently executes tasks serially; this traversal is an implementation detail, not an ordering contract across tasks. On success it records status, writes the current plan fingerprint to a temporary file and replaces `ready` with it. Fingerprints use the SHA-256 of compact JSON encoded as UTF-8, avoiding Windows/Linux formatting-newline differences; `id` contains the same fingerprint generated at packaging time. Both files have mode `0644`, allowing ordinary service users to check readiness without reading the `0600` parameter file.

[`Scriptor.Systemd`](../src/Scriptor.Systemd.cs) inserts `migrate.sh apply` into installation and generates `ExecStartPre=/bin/sh "/opt/zongsoft/web/.migration/migrate.sh" check` in the service drop-in. Installation must complete migrations; subsequent service starts only check the marker rather than rerunning SQL. Launching the host DLL directly bypasses this systemd check.

`check` only establishes that a matching completion marker exists for this packaged plan. It is not a database health check and does not rehash SQL files or verify that buckets/tables still exist. The runner also exposes a `check` command that loads the plan and calculates its fingerprint; the generated shell uses the file-comparison branch above for startup without launching migrator.

## Installation and recovery

With migrations enabled, installation stops the service, deploys files, invalidates the ready marker, installs the systemd startup guard, runs migrations, and only then runs the installed/start hook and postinstalled hooks. `preinstalled` hooks run before migrations; they must not start the application. Custom lifecycle hooks must honor these responsibilities. The mandatory migration step also runs with `--daemon:none` and a custom `--installed` script.

Debian runs migrations only for `postinst configure`; RPM uses `%post`. A failed migration returns nonzero and prevents subsequent start/hooks. Default service stop/start failures are no longer ignored for migration packages. The ready marker is only written after all tasks succeed. systemd's `20-packager-migration.conf` drop-in prevents starting an incomplete package on a later reboot. The original service file is preserved; custom services must also support this `ExecStartPre` guard.

State lives in `/var/lib/<package-name>/packager`, outside the application directory. A file lock prevents concurrent runners for the same package. Status records identify the task and exception type, without connection values or SQL. The runner verifies all SQL checksums before making changes. Repair the cause and retry configuration/reinstall, or invoke the installed entry directly (with installation privileges):

```sh
/opt/zongsoft/web/.migration/migrate.sh status
/opt/zongsoft/web/.migration/migrate.sh apply
/opt/zongsoft/web/.migration/migrate.sh check
```

`apply` reruns all SQL files and checks S3 initialization; it does not start the application. `check` returns zero only when this package's plan is ready. Uninstall removes package/service files but retains databases, buckets and migration state. Failure does not undo database changes, buckets or deployed application files.

Tar `DESTDIR` is staging only: install/uninstall skips all lifecycle hooks, migrations and service operations. Migration packages use the build-time installation path; changing `INSTALL_PATH` for a live installation fails before deployment. Rebuild using `--install-path` to change it. Migration installation paths must be absolute and use letters, digits, `.`, `_`, `+`, `-` and `/`, without `.`/`..` path segments.

## Repeatable SQL and retries

Every installation, upgrade, reinstall and `apply` runs all configured SQL files, preserving script order within each database task. There is no per-file success history or skip based on an earlier run. Script authors must make schema changes and data modifications repeatable: check object existence and expected state before adding, renaming or dropping objects, and prevent duplicate inserts or repeated accumulation. Unexpected states should fail explicitly rather than silently skip a required change.

Within one database task, if file A succeeds and file B fails, retry starts again with A. B may also have committed some statements before failing; scripts must handle those intermediate states, or the operator must repair them before retrying. The packager does not automatically roll back SQL. Use transactions where the database supports them.

SQL checksums only verify that packaged files match the current plan before any external resource changes. They are not an execution history and do not prevent revised SQL from running in a newly generated package. The existing lock, status and readiness marker govern installation completion; they do not skip files during `apply`.

## Runtime and artifact handling

Migration packages support glibc `linux-x64` and `linux-arm64`, using Rocky Linux 9/glibc 2.34 as the build baseline; Alpine/musl is not included. The native migrator and required `.so` files are installed under `.migration/`. Target systems need glibc 2.34 or later, libgcc, libstdc++, zlib, ICU, OpenSSL and CA certificates; PostgreSQL/SQL Server authentication may also need the Kerberos/GSSAPI libraries supplied by the distribution. Exact dynamically linked requirements must be checked with `readelf`/`ldd` for each publication. The generated shell also needs POSIX `sh`, core utilities and `cmp` (the `diffutils` package on Rocky Linux). No .NET runtime, database CLI, AWS CLI or `mc` is required. Use the existing `--dependencies` option for distribution-specific package dependencies.

The `.migration/migration.json` file contains **expanded connection parameters, including passwords and keys**, with mode `0600`. This protects the installed file, not the archive: distribute and retain packages as credential-bearing artifacts. The original `.env` is not automatically copied; keep deployment configuration outside payload entries. The `.migration/` directory, including `.artifacts/`, is reserved for generated content. SQL paths are relative to the installation root; migrator only accepts files under `.migration/.artifacts/`. Configuration is captured at packaging; changing a build-machine `.env` afterwards does not change an existing package.

## Localization

The packager, shared validation and migrator use separate `.resx` resources with English defaults and Simplified Chinese (`zh-Hans`) translations. `ResXFileCodeGenerator` generates strongly typed accessors; shared resources are embedded in each program. Native publication retains English and Chinese resources and globalization support. The runner selects messages through `CurrentUICulture`; generated shell scripts select Chinese or English using `LC_ALL`, `LC_MESSAGES`, then `LANG`. Shell `check` does not launch .NET. JSON fields, status values, action names and fingerprint data do not depend on display language.

The packager uses FileMatcher for all path expansion. Each pattern sorts paths relative to its fixed prefix. Matching is case insensitive on Windows and case sensitive on Unix. Symbolic links/reparse points are rejected rather than followed. Plan structure, SQL repeatability, task-order guarantees and the native runner are unchanged.
