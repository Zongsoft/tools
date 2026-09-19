# Migration implementation

The generator src uses Core Profile and Searcher for input. MigrationLoader.Database prepares SQL batches; AmazonS3 parses bucket options. MigrationBundle collects the complete native publication and plan. Generator writes PAX tar using System.Formats.Tar, recording Migrator (assembly@version) and Runtime metadata. No project references packager.

`MigrateCommand.Version.cs` contains the private `VersionSource` nested type. The command removes the environment's `version` from preliminary variables and passes only the current option to the resolver. The resolver expands that value, gives numeric versions priority, resolves directories to `.version`, and opens files read-only with `File.OpenRead` and `ApplicationVersion.Load(Stream)`. Edition selection follows the file's case-insensitive collection and retains its spelling. Read/format failures are wrapped with the full path; nonzero-version and Edition validation occur before any output processing.

The process entry quotes individual option values and positional arguments and escapes backslashes before handing the expression to Core CommandLine. This preserves empty values and Windows paths containing spaces without merging adjacent arguments. and Edition into its variable dictionary before `Normalizer.Initialize`. `--name` stays independent of the version file; input/output paths continue to use the working directory. No save operation is performed. The plan protocol and native executor do not participate in source version lookup; they receive the final identity. Command tests cover literal/file/directory/default sources, Edition selection, variable expansion and unchanged source/output files on failures.

.shared compiles into the generator and executor through source links, without a shared DLL. MigrationPlan uses partial and nested Step/Script/Bucket models and source-generated JSON. Source/Content only exist on the generation side. Shared code validates provider parameters and plan structure. See the migration guide for fields and fingerprints.

The executor explicitly selects one of six databases or S3, and owns connections, initialization, ordered submission, checksums, locks, state and pending records. Every apply executes all SQL without per-file history. Only the executor declares database/AWS dependencies; TDengine uses WebSocket directly.

The external launcher and archive have identical stems. Generation embeds the exact archive name, version-independent state name and plan fingerprint in the launcher. Its second argument overrides the state directory. Check only compares ready; apply/status extract the internal .migration/ tree into a unique temporary directory, invoke its entry with the action and persistent state, propagate the exit code and clean up. Packager installation and systemd gates invoke this external entry without interpreting SQL or plan fields.

Both outputs are staged privately under the destination before backup/replacement. Failures restore prior outputs. Inside the archive, migration.json is 0600, other data 0644 and entries 0755. On Unix the external archive is 0600 and launcher 0755. Expanded credentials are included; do not publish such archives publicly.

Linux needs glibc >=2.34, libgcc, libstdc++, zlib, ICU, OpenSSL and CA certificates; some authentication needs Kerberos/GSSAPI. Shell execution needs sh, tar/gzip, core utilities and cmp. Windows needs system PowerShell, tar.exe and the bundled native DLLs. Targets need no .NET runtime. AOT preserves globalization and English/Chinese resources; publications reside in `executor/src/bin/<Configuration>/net10.0/<RID>/publish/`, with logs/ and symbols/ alongside that directory. Build and standalone publish outputs link native files into .migrator/<RID>/ beside the generator. NuGet packaging stores them once at tools/.migrator/<RID>/ and removes the per-framework copies from the package. MigrationBundle first checks the local .migrator/ directory, then the shared directory two levels above tools/<TFM>/any/. Source directories do not receive native output. Third-party warnings remain visible and reviewed.

Common package versions come from the repository root Directory.Packages.props; database drivers and AWS SDK versions remain in the executor project through VersionOverride. All projects use the CodeAnalysis version defined in the root Directory.Packages.props with strict builds and a separate IDE0049 verification. Resources use ResXFileCodeGenerator; no localization tests. Ordinary builds neither require packager nor start containers.

## Native AOT build scripts

Build scripts reside in executor/build:

- `setup.sh` prepares the dedicated Rocky Linux 9 x64 container: it installs compiler tools and development libraries, downloads and verifies .NET SDK 10.0.401, and extracts ARM64 RPMs into /aot/sysroots/linux-arm64 to supply cross-compilation headers, libraries and startup objects. Caches persist under /aot; ARM64 package installation scripts are not executed.
- `clang-arm64.sh` wraps clang for ARM64 cross-compilation: it disables the host's default clang configuration, selects the GCC directory in the ARM64 sysroot and forwards compiler arguments. publish.sh uses it with SysRoot and CppCompilerAndLinker to avoid linking against the host x64 toolchain.
- `publish.sh` keeps Linux x64/arm64 compilation caches under /aot, verifies outputs, then copies the complete publication to the corresponding executor publish/ directory in the workspace; symbols are stored separately.
- `publish.ps1` uses the SDK default publish/ directory for win-x64 on Windows, checks PE architecture and separates symbols from the publication.

These scripts are build-time tools; they are not executed on migration target machines.

## Native AOT validation

After publication, inspect ELF/PE architecture, native dependencies, globalization resources and Linux executable modes. On isolated targets without .NET, validate SQLite/DuckDB apply/status/check, repeat runs, failure/retry, locking, temporary-directory cleanup and persistent state across versions. ARM64 validation may be limited to compilation and architecture checks.

Fixed dependency versions produce trimming and dynamic-code warnings in SqlClient diagnostics, reflective configuration, SQL CLR types and parameter conversion; ConfigurationManager reflection; DuckDB complex collection/structure conversions; and MySqlConnector diagnostics. Full logs are in `executor/src/bin/<Configuration>/net10.0/<RID>/logs/publish.log`. Keep and review individual warnings. Successful compilation or native publication does not establish that advanced driver features work. Validate network authentication, complex types and real S3 services for the intended scenarios.

Generator tests cover input, SQL batches, naming, overwrite recovery and process handoff. Executor tests cover plan validation, database execution, state, S3 pending and TDengine sessions. See [README](../README.md#build-and-test) for commands.
