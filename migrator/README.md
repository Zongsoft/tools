# Zongsoft Migration Tool

Database configuration uses provider/database/user sections: provider `Database` selects the default, which may omit a database section. Only `.migration`-referenced databases and their users initialize, including empty sections. Existing settings and passwords are preserved, and grants are additive. See the [complete database parameter reference](docs/databases.md).

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-migrate` creates a standalone migration archive and launcher. Generation does not connect to databases/S3. A version file can supply the version and Edition; it is only read, never modified. Running the generated launcher performs database initialization, SQL execution and S3 bucket configuration.

## Installation

```powershell
dotnet tool install -g Zongsoft.Tools.Migrator
```

For a local source installation, run `dotnet cake --edition Release --target build` from migrator to prepare all three RIDs and create the NuGet tool package. With native artifacts already prepared, use `--target compile`. Install with `dotnet tool install -g Zongsoft.Tools.Migrator --version 0.1.0 --source ./src/bin/Release --no-http-cache`; uninstall first when replacing the same version. Cake `pack` pushes to NuGet and is not for local testing.

## Usage

From PowerShell in `D:/Zongsoft/hosting`, use the existing Web migration inputs:

```powershell
$env:scheme = 'default'
dotnet-migrate --name:zongsoft --version:1.0.0 --platform:linux --output:packages '.deploy/$(scheme)/migration/$(version)/*.migration'
```

Required options: name and platform. Optional: version, edition, architecture (x64), output (current directory), overwrite (false), title (input name), summary and description. Summary/description support existing file:/text: sources relative to the current directory. At least one positional path is required; each supports variables, wildcards and semicolon/pipe lists. Explicit options override environment variables. Expand each argument at its position, sorting relative matches ordinally within that pattern. Missing paths warn and skip; no valid inputs/tasks fails. Existing invalid inputs fail. Only .migration files are accepted, including imports; contents remain INI, with .env parameters. SQL contents are not variable-expanded.

Linux supports glibc x64/arm64. win/windows normalize to win, x64 only. unix requires a concrete OS; osx/xos/macos have no executor and fail without outputs.

## Version selection

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

Version paths support variables. Numeric values take precedence over paths; use `./1.0.0` for a file named `1.0.0`. The resolved version and Edition populate `$(version)`/`$(edition)`, plan identity and artifact names. Migration inputs and output paths stay relative to the working directory, even when the version file is elsewhere. See [version sources](docs/migration.md#version-sources-and-variables) for expansion rules.

## Artifacts and execution

Names already ending in -migrate, -migration, .migrate or .migration retain that suffix (case insensitive); otherwise append -migrate. Both files use `<migration-name>[-edition]@<version>_<platform>-<architecture>`, with .tar.gz and .sh/.cmd extensions. No descriptor file is generated:

```text
packages/zongsoft-migrate@1.0.0_linux-x64.tar.gz
packages/zongsoft-migrate@1.0.0_linux-x64.sh
```

Keep both files together on the target. Run `sh zongsoft-migrate@1.0.0_linux-x64.sh [apply|status|check] [state-directory]`, or the corresponding cmd on Windows. Default action is apply; every apply executes all SQL, which must be idempotent. Default persistent state is `.migration/<migration-name>[-edition]/` beside the script, without version or RID. Versions share the lock, ready, status and database/S3 pending files.

Apply/status extract into a unique temporary directory, invoke the native executor, clean up and return its exit code. Check compares the embedded fingerprint with ready, without extraction or launching the executor. Exit codes are 0 success, 1 failure, 2 invalid action/arguments. Both outputs are staged before publication, require overwrite to replace existing files, and restore previous outputs on failure.

## Packager integration

Use `--migrator:../../packages/zongsoft` in hosting/web/default packaging commands, or `--migrator:../packages/zongsoft` in daemon. Packager uses its final Edition, version and RID with the same name suffix rule to locate both artifacts. Missing companions fail without fallback. Installation packages include both files unchanged; installers pass `/var/lib/<package-name>/packager` and prevent service startup on failure. This tool does not modify existing host commands or connection settings.

## Build and test

The generator targets .NET 8/9/10; the executor targets .NET 10 Native AOT. Ordinary build/test does not publish native code. `dotnet cake --edition Release --target executor` builds both Linux RIDs in the dedicated Rocky Linux 9/glibc 2.34 Pod and win-x64 on Windows. See executor/build/migrator.linux-x64.yaml for mounts and DNS; Cake passes a relative YAML path. All tools inherit the repository root `.editorconfig`; the Pod and CI mount that file read-only at `/.editorconfig` and both root props files at `/Directory.Build.props` and `/Directory.Packages.props`. Linux publishing disables configuration synchronization using `-p:ZongsoftGuidelinesSynchronization=`. Changes to DNS or mounts require recreating that dedicated Pod when no build is running. Native publications reside in `executor/src/bin/<Configuration>/net10.0/<RID>/publish/`; a complete tool package requires all three RIDs for the same configuration. Build and standalone publish outputs link these files into `.migrator/<RID>/` beside the generator. The NuGet tool package stores one shared copy at `tools/.migrator/<RID>/` for all three target frameworks; the generator resolves that location when its local `.migrator/` directory is absent. CI prepares Linux and Windows publication directories separately before assembling the tool package. Each RID keeps logs/ and symbols/ alongside publish/; they are excluded from the tool package.

```powershell
dotnet build Zongsoft.Tools.Migrator.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test test/Zongsoft.Tools.Migrator.Tests.csproj -f net10.0
dotnet test executor/test/Zongsoft.Tools.Migrator.Executor.Tests.csproj -f net10.0
```

See the [migration guide](docs/migration.md), [implementation](docs/implementation.md).

Privileges uses 20 provider-independent operation names. ReadWrite includes Execute; every read/write permission includes sequence value generation. Providers expand native grants within the target database, and unsupported operations or ownership requirements fail explicitly. See the database reference for coarse mappings and role-scope limits.
