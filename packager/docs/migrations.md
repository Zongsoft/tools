# Installation migrations

[English](migrations.md) | [简体中文](migrations.zh-Hans.md)

`--migration` adds database/bootstrap and S3 bucket initialization to tar, Debian and RPM installation packages. It accepts one or more `.ini` paths separated by `;` or `|`. Quote the entire option so the shell preserves separators and variable expressions.

```text
--migration:../.deploy/$(scheme)/migration/zongsoft.db-$(environment).ini;../.deploy/$(scheme)/migration/zongsoft.fs-$(environment).ini
```

Paths on the command line are relative to `--source`. Migration INI paths support `*` and `?` in the filename, for example `../../.deploy/$(scheme)/migration/$(version)/*.ini`. Matches are processed in ordinal filename order within each argument; argument order is preserved. A missing INI file or a pattern without matches produces a warning and is skipped. Remaining files keep their order. If no input files are found, packaging proceeds without migration files, a migrator or migration startup checks; existing but empty/invalid INI files, missing `.env` parameters and missing SQL scripts remain errors. Paths inside an INI file are relative to that INI file, not the host directory. Both INI files and `.env` parameter files use Zongsoft.Core's `Profile` parser, as does the deployment tool. Full-line `#`/`;` comments and bare entries are supported. Entry names are case insensitive under `Profile`; duplicate keys are rejected. Repeated migrator sections, including the two PostgreSQL aliases, are rejected.

Migration files, SQL path expressions, bucket names/options and parameter values support the existing `$(name)` and `%name%` variables. They are resolved when packaging. SQL contents are not expanded; client delimiters are processed during packaging. Undefined variables, unmatched SQL patterns, unsupported providers/options, missing parameters and conflicting generated paths fail packaging with a nonzero exit code. Do not store shell expressions in parameter values; these files are INI, not shell scripts.

The intermediate file is named **`migration.json`**, installed at `<install-directory>/.migration/migration.json`.

## Migrators and order

Section names are case insensitive: `mssql`, `mysql`, `sqlite`, `duckdb`, `postgres`/`postgresql`, `tdengine`, and `amazon.s3`. Empty sections do not run. There must be at least one task.

Files run in command-line order, then section order, then entry order. SQL patterns support `*` and `?` in the filename only. Each pattern's matches are sorted by ordinal relative path; overlapping matches run once within the section. Use zero-padded names to control ordering. Every installation and retry executes all SQL files. Script authors must ensure repeatability, including after partial failure; see the retry rules below. There is no automatic rollback across scripts or resources.

Database entries contain SQL paths with no value. Most drivers receive a complete file; SQL Server and TDengine require batch boundaries, and MySQL client `DELIMITER` directives are adapted before submission. The runner does not emulate interactive database clients.

For network databases the runner connects to the bootstrap database, checks/creates the target database, then connects to the target and runs SQL. SQL Server defaults to bootstrap `master`, PostgreSQL to `postgres`; MySQL and TDengine omit the database unless configured. The credential needs the corresponding create/query/DDL permissions. SQLite and DuckDB create the parent directory and database file; `Database` must be an absolute Linux target path. Custom service users need filesystem permissions to use that file.

## How SQL scripts are submitted to drivers

[`MigrationLoader.Database`](../src/MigrationLoader.Database.Batches.cs) runs in the packager and prepares batches that can be submitted directly to the driver. Each batch is written to `<installation directory>/.migration/.artifacts/<task ID>/<four-digit sequence>.sql`; the plan records their order and SHA-256 checksums. Migrator reads and executes each prepared file without splitting it. Script authors remain responsible for SQL syntax, schema changes and business semantics. Invalid SQL is reported by the driver/database during installation.

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

Omitting encryption, versioning or tags leaves the service defaults and does not call the corresponding API. Default encryption applies to future uploads, not existing objects. Amazon S3 baseline encryption cannot be disabled, so encryption:false is not an option. SSE-C requires customer keys on individual object requests and is not a bucket default encryption option; the migrator does not create or manage KMS keys. Quotas, lifecycle rules, CORS, logging, Object Lock and internal access are outside this feature.

Existing buckets are skipped without changing their policies or other settings. New buckets receive versioning, encryption, tags and finally public-read policy, in that order. The local pending record is removed only after all configuration succeeds. If configuration fails after creation, the record allows a later retry to reapply the specified settings without skipping individual operations. Authentication/authorization failures are not treated as missing buckets. Unsupported settings or rejected requests fail installation and prevent startup; they are not silently ignored. S3 public-access restrictions can also reject a public policy.

## Zongsoft hosting example

Use the existing [`hosting/daemon`](https://github.com/Zongsoft/hosting/tree/main/daemon) or [`hosting/web/default`](https://github.com/Zongsoft/hosting/tree/main/web/default) deployment workflow first. The following files are **new deployment configuration to create**, not files assumed to exist in the hosting checkout. The SQL comes from the actual [Zongsoft.Upgrading schema](https://github.com/Zongsoft/framework/blob/main/upgrading/database/zongsoft.upgrading-sqlite.sql); select schemas for the plugins actually deployed to your host.

From `D:\Zongsoft\hosting`:

```cmd
mkdir .deploy\default\migration\1.1.0
copy ..\framework\upgrading\database\zongsoft.upgrading-sqlite.sql .deploy\default\migration\1.1.0\
```

Create `.deploy/default/migration/zongsoft.db-production.ini`:

```ini
[sqlite]
1.1.0/zongsoft.upgrading-sqlite.sql
```

Create the shared `.deploy/default/migration/sqlite.env`:

```ini
Database=$(UPGRADING_DATABASE)
CommandTimeout=5m
```

Set `UPGRADING_DATABASE` in the packaging environment to the **same absolute target path used by the deployed Upgrading plugin**. Configure actual target credentials in the packaging environment; no real server credentials are included in this documentation.

If the host also needs the `attachments` bucket, create `.deploy/default/migration/zongsoft.fs-production.ini`:

```ini
[amazon.s3]
attachments=private
```

Create `.deploy/default/migration/amazon.s3.env`:

```ini
Server=$(S3_SERVER)
Region=$(S3_REGION)
AccessKey=$(S3_ACCESS_KEY)
SecretKey=$(S3_SECRET_KEY)
```

Set these variables to the target RustFS/S3 configuration. Set `scheme=default` and `environment=production`. Add this **one option** to the existing daemon packaging command, whose source is `hosting/daemon`:

```text
"--migration:../.deploy/$(scheme)/migration/zongsoft.db-$(environment).ini;../.deploy/$(scheme)/migration/zongsoft.fs-$(environment).ini"
```

For `hosting/web/default`, the relative prefix is `../../.deploy/`:

```text
"--migration:../../.deploy/$(scheme)/migration/zongsoft.db-$(environment).ini;../../.deploy/$(scheme)/migration/zongsoft.fs-$(environment).ini"
```

Keep the host's existing payload entries and Nginx hooks. The Web entry is `Zongsoft.Hosting.Web.dll`: use `--name:Zongsoft.Hosting.Web --title:Zongsoft.Web --daemon:zongsoft.web --daemon-bind:8069`. The daemon entry is `Zongsoft.Hosting.Daemon.dll`: use `--name:zongsoft.daemon` from the current hosting script, with automatic title and service-name inference. In PowerShell/Bash use **single quotes** around the entire migration option to preserve `$()`; the double-quoted form above is for CMD.

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
| `.migration/.artifacts/<task ID>/<four-digit sequence>.sql` | Packager → migrator | Executable SQL batches, all checked before execution; mode `0644` |
| `.migration/id` | Packager → `migrate.sh check` | Plan fingerprint; mode `0644` |
| `/var/lib/<package-name>/packager/status.json` | Migrator → `status` | Current task, result and exception type |
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

`dotnet build src/Zongsoft.Tools.Packager.csproj` compiles the packager without the migrator project or its artifacts and does not automatically create a NuGet package. Prepare a complete tool package by running the `migrator` task above, or place an independently published runtime directory in `src/.migrator/<RID>/` before `dotnet pack`. Without that directory, the tool still builds and creates ordinary packages; using `--migration` reports the missing runtime. No additional command option or MSBuild path property is required.

[`test`](../test/Zongsoft.Tools.Packager.Tests.csproj) covers packager input, SQL batch preparation, package generation and JSON handoff; [`migrator/test`](../migrator/test/Zongsoft.Tools.Packager.Migrator.Tests.csproj) references only the migrator and covers databases, S3 and TDengine WebSocket. Handoff tests run the independent migrator with `check`, verify that it accepts the packager-generated fingerprint, and confirm that changing task order invalidates readiness. The packager test project sets `ReferenceOutputAssembly=false` on its migrator reference, keeping only a build dependency. Neither suite uses assembly or `using` aliases. Run the projects separately:

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

After acquiring the file lock, `MigrationExecutor` removes the old `ready`, validates all SQL files, then executes tasks in order. On success it records status, writes the current plan fingerprint to a temporary file and replaces `ready` with it. Fingerprints use the SHA-256 of compact JSON encoded as UTF-8, avoiding Windows/Linux formatting-newline differences; `id` contains the same fingerprint generated at packaging time. Both files have mode `0644`, allowing ordinary service users to check readiness without reading the `0600` parameter file.

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

Every installation, upgrade, reinstall and `apply` runs all configured SQL files in order. There is no per-file success history or skip based on an earlier run. Script authors must make schema changes and data modifications repeatable: check object existence and expected state before adding, renaming or dropping objects, and prevent duplicate inserts or repeated accumulation. Unexpected states should fail explicitly rather than silently skip a required change.

If file A succeeds and file B fails, retry starts again with A. B may also have committed some statements before failing; scripts must handle those intermediate states, or the operator must repair them before retrying. The packager does not automatically roll back SQL. Use transactions where the database supports them.

SQL checksums only verify that packaged files match the current plan before any external resource changes. They are not an execution history and do not prevent revised SQL from running in a newly generated package. The existing lock, status and readiness marker govern installation completion; they do not skip files during `apply`.

## Runtime and artifact handling

Migration packages support glibc `linux-x64` and `linux-arm64`, using Rocky Linux 9/glibc 2.34 as the build baseline; Alpine/musl is not included. The native migrator and required `.so` files are installed under `.migration/`. Target systems need glibc 2.34 or later, libgcc, libstdc++, zlib, ICU, OpenSSL and CA certificates; PostgreSQL/SQL Server authentication may also need the Kerberos/GSSAPI libraries supplied by the distribution. Exact dynamically linked requirements must be checked with `readelf`/`ldd` for each publication. The generated shell also needs POSIX `sh`, core utilities and `cmp` (the `diffutils` package on Rocky Linux). No .NET runtime, database CLI, AWS CLI or `mc` is required. Use the existing `--dependencies` option for distribution-specific package dependencies.

The `.migration/migration.json` file contains **expanded connection parameters, including passwords and keys**, with mode `0600`. This protects the installed file, not the archive: distribute and retain packages as credential-bearing artifacts. The original `.env` is not automatically copied; keep deployment configuration outside payload entries. The `.migration/` directory, including `.artifacts/`, is reserved for generated content. SQL paths are relative to the installation root; migrator only accepts files under `.migration/.artifacts/`. Configuration is captured at packaging; changing a build-machine `.env` afterwards does not change an existing package.

## Localization

The packager, shared validation and migrator use separate `.resx` resources with English defaults and Simplified Chinese (`zh-Hans`) translations. `ResXFileCodeGenerator` generates strongly typed accessors; shared resources are embedded in each program. Native publication retains English and Chinese resources and globalization support. The runner selects messages through `CurrentUICulture`; generated shell scripts select Chinese or English using `LC_ALL`, `LC_MESSAGES`, then `LANG`. Shell `check` does not launch .NET. JSON fields, status values, action names and fingerprint data do not depend on display language.
