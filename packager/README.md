# Zongsoft Packaging Tool

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` is a .NET global tool that turns an application directory, such as a frontend `dist` or a published .NET application, into Linux installation packages (**`.tar.gz`**, **`.deb`**, and **`.rpm`**) and can generate systemd services, install/uninstall scripts, and Nginx site configuration on demand.

Package formats are written directly in .NET without calling external `tar`, `dpkg-deb`, `rpmbuild`, or `cpio` commands, so Linux packages can be built on Windows as well.

> 🚨 **Warning:** `dotnet-pack` only **builds** packages; it never installs them. Installing a package on a target host, however, runs lifecycle scripts that can change system files, services, and the application directory. Inspect the package and test it in a staging environment before installing it on a production host.

## Contents

- [Features](#features)
- [Basic concepts](#basic-concepts)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Command reference](#command-reference)
- [Application identity and version files](#application-identity-and-version-files)
- [Package entries](#package-entries)
- [systemd services](#systemd-services)
- [Lifecycle scripts](#lifecycle-scripts)
- [Web hosting configuration](#web-hosting-configuration)
- [Migrator artifact integration](#migrator-artifact-integration)
- [Variables](#variables)
- [Package formats](#package-formats)
- [Recommended workflow](#recommended-workflow)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Related documents](#related-documents)

## Features

- **One command, three formats:** the `tar`, `deb`, and `rpm` subcommands share one set of metadata, variables, file entries, service scripts, and output naming rules.
- **Generated services:** creates a systemd service when no `.service` file is supplied; `--listen` sets the listening address.
- **Complete lifecycle:** generates install and uninstall scripts for every format, with custom hooks and pre/post snippets.
- **Flexible payload selection:** explicit files, recursive directories, path-segment globbing (including `**`), exclusion patterns, target aliases, and root-level entries such as `/etc/nginx/conf.d/zongsoft.web.conf`.
- **Application versioning:** reads and updates the source directory's `.edition` manifest (falling back to `.version`), including multiple _**E**ditions_.
- **Web hosting configuration:** generates Nginx site configuration from `web.profile` and activates it at install time.
- **Migration integration:** includes artifacts produced by the independent [migrator tool](../migrator/README.md) and runs them at install time.
- **Variables:** `$(name)` and `%name%` references, with values from environment variables and `.env` files.
- **Cross-platform packaging:** preserves file modes on Unix-like hosts and applies conservative executable modes on Windows.

## Basic concepts

Read these concepts first; every later section builds on them.

### Packaging pipeline

A single packaging run performs these steps in order:

1. **Fix the source directory:** resolve `--source` (the current directory by default).
2. **Determine the application identity:** merge values from the source `.edition` manifest or fallback `.version` identifier with the `--name`, `--edition`, and `--version` options.
3. **Load variables:** defaults, environment variables, ancestor `.env` files, then command options.
4. **Collect package entries:** select payload files from positional arguments and `--exclude`.
5. **Generate supporting content:** systemd service, lifecycle scripts, Nginx configuration, migration artifacts.
6. **Encode and write:** produce `.tar.gz` (with its `.sh` companion), `.deb`, or `.rpm`.
7. **Save the version:** save source version files only after packaging succeeds; when `.edition` exists, write back both `.edition` and `.version`.

### Key terms

| Term | Meaning |
| --- | --- |
| **Source directory** | The published or staged application directory named by `--source`. Relative package entries, the output directory, and migration artifacts resolve from it. |
| **Application identity** | The combination of name, optional _**E**dition_, and _**V**ersion_ number. It determines the package name, the default install path, and the packaged `.version`. |
| **Package entries (payload)** | The files written into the package. Without positional arguments the whole source directory is included recursively; with them, only the listed files and directories. |
| **Install root** | The application's install directory on the target host, derived from the identifier by default, e.g. `/opt/zongsoft/web`. Ordinary payload installs relative to it. |
| **Root-level entry** | An entry whose alias starts with `/`; it installs to an absolute system path such as `/etc/...` instead of below the install root. |
| **Service (daemon)** | A systemd service generated or included by default and enabled after installation. Use `--daemon:none` to package files only. |
| **Lifecycle scripts** | Scripts that run on the target host before/after install and before/after removal. Generated by default and customizable. |
| **Variables** | Values referenced as `$(name)` or `%name%` in option values and package entries. |

### Responsibilities

| The packager does | The packager does not |
| --- | --- |
| Generate package files and the `.sh` installer that accompanies a tar package | Install, upgrade, or remove packages: tar packages install via `.sh`; `.deb`/`.rpm` via the system package manager |
| Generate systemd services, lifecycle scripts, and Nginx configuration | Start or stop services, configure certificates, or run Nginx on the target machine |
| Write dependency declarations (`Depends`, `Requires`, etc.) | Download or embed dependency packages |
| Match and carry migration artifacts unchanged | Parse or execute migration plans (the [migrator](../migrator/README.md) does that) |

## Installation

Install, update, check, and uninstall as a .NET global tool:

```bash
# Install
dotnet tool install -g Zongsoft.Tools.Packager

# Update
dotnet tool update -g Zongsoft.Tools.Packager

# Check
dotnet tool list -g
dotnet-pack

# Uninstall
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

### Installing from local source

You can install from a `.nupkg` built from source for testing, without publishing to [nuget.org](https://nuget.org). The commands below use the .NET 10 SDK and run from this repository's `packager` directory; packager needs no native migration executor or AOT build environment.

1. Build the local tool package:

   ```powershell
   dotnet pack src/Zongsoft.Tools.Packager.csproj -c Release
   ```

2. After confirming that `src/bin/Release/Zongsoft.Tools.Packager.<version>.nupkg` exists, install it (the version is read from the project file):

   ```powershell
   $toolVersion = dotnet msbuild src/Zongsoft.Tools.Packager.csproj -getProperty:Version -nologo
   dotnet tool install -g Zongsoft.Tools.Packager --version "$toolVersion" --source ./src/bin/Release --no-http-cache
   ```

3. If the tool is already installed (especially after rebuilding the same version), run `dotnet tool uninstall -g Zongsoft.Tools.Packager` and repeat step 2. Then verify the version with `dotnet tool list -g`.

Notes:

- `--source` restricts this installation to the local directory so a same-named NuGet.org package is not selected; `--no-http-cache` disables the download cache. See the [.NET tool install documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install).
- "Local" refers to the package source; `-g` still replaces the current user's global tool.

> 🚨 **Warning:** Do not run the Cake `pack` task for local testing; it pushes the package to NuGet.org.

## Quick start

This walkthrough uses the real [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) host from the hosting repository to show the full "prepare, package, inspect" flow. For other applications, replace the name, version, and file list.

### Step 1: Prepare the application

Prepare the host and its plugins with the host's [deployment script](https://github.com/Zongsoft/hosting/blob/main/web/default/deploy.cmd). Define root-level `framework=net10.0` in the hosting root's `.env`, and set the matching process variable `framework` in the current Windows console for Cake. The script no longer prompts for the framework. Set remote debugging to `off` (Release), then choose Linux and x64. To prepare only the host, enter `exit` at the packaging prompt (this branch currently returns `1`, without indicating a preceding deployment failure):

```cmd
set "framework=net10.0"
set "Environment=production"
cd /d D:\Zongsoft\hosting\web\default
deploy.cmd
```

### Step 2: Build the package

Package directly from the host directory: without `--source`, the source is the current directory, and positional arguments select its payload. Alternatively, run the host's [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd). The standalone script defaults to tar, Release, and x64 and packages existing files only. The current Web environment prompt does not assign its input back to `environment`, so set `Environment=production` first, choose `deb` and version `1.0.0`, and leave the migration prompt empty. The corresponding core command follows, omitting empty Edition and migrator options:

```cmd
dotnet-pack deb ^
	--name:Zongsoft.Hosting.Web ^
	--title:Zongsoft.Web ^
	--version:1.0.0 ^
	--compilation:Release ^
	--platform:linux ^
	--architecture:x64 ^
	--Environment:production ^
	--DOTNET_ENVIRONMENT:production ^
	--ASPNETCORE_ENVIRONMENT:production ^
	--listen:8069 ^
	--daemon:zongsoft.web ^
	--web:nginx ^
	--daemon-environments:Environment,DOTNET_ENVIRONMENT,ASPNETCORE_ENVIRONMENT ^
	--exclude:**/logs/;bin/$(compilation)/$(framework)/*.staticwebassets.* ^
	--output:.packages ^
	../../mime ^
	appsettings.json ^
	web*.config ^
	web*.option ^
	wwwroot ^
	plugins ^
	bin/$(compilation)/$(framework):~
```

| Option or argument | Effect |
| --- | --- |
| `--name:Zongsoft.Hosting.Web` | Application name; the entry assembly is `Zongsoft.Hosting.Web.dll`. |
| `--title:Zongsoft.Web` | Human-readable title, also used as the service description. |
| `--version:1.0.0` | Example release version number; use your actual version number. |
| `--compilation`, `framework` | The option supplies the build configuration; merged Variables supply the framework (hosting `.env` in this example). Both are referenced by `$(compilation)` and `$(framework)` in exclusions and payload arguments. |
| `--Environment`, `--DOTNET_ENVIRONMENT`, `--ASPNETCORE_ENVIRONMENT` | Custom variables written into the generated service environment through `--daemon-environments`. |
| `--listen:8069` | The generated service listens on `http://127.0.0.1:8069`. |
| `--daemon:zongsoft.web` | Package and service identifier `zongsoft.web`; install path `/opt/zongsoft/web`. |
| `--web:nginx` | Generates an Nginx site configuration from the host's `web.profile`. |
| `--exclude` | Skips log directories and the static web asset manifests produced by the build. |
| `--output:.packages` | Writes packages to `.packages` under the host directory. |
| `../../mime` through `plugins` | Select the payload: the MIME definitions at the hosting repository root, host settings and configuration, static files, and deployed plugins. |
| `bin/$(compilation)/$(framework):~` | The `~` directory alias places the build output directly at the installation root. |

> 💡 **Tip:** Replace `deb` with `tar` or `rpm` to build the other formats. When building the same format again, use another output directory or add `--overwrite`.

Daemon scripts generate `zongsoft.daemon.service` and pass `Environment` and `DOTNET_ENVIRONMENT`. Terminal scripts pass the same variables with `--daemon:disabled`; this does not set process variables for an interactive terminal. All three hosts can include existing migration artifacts without creating them. First run `migrate.cmd` from the hosting root, then enter `zongsoft` at the host's migration prompt. See the [hosting README](https://github.com/Zongsoft/hosting/blob/main/README.md#installation-and-migration-packages) for complete script parameters; this README defines the tool's behavior.

### Step 3: Inspect the result

The example produces `.packages/zongsoft.web@1.0.0-x64.deb` in the host directory. From the host directory in Linux or WSL, these read-only commands show its metadata and file list without running lifecycle scripts:

```bash
dpkg-deb --info ./.packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./.packages/zongsoft.web@1.0.0-x64.deb
```

See [Package formats](#package-formats) for inspecting and installing the other formats.

### Output file names

A package file name combines the identifier, optional _**E**dition_, _**V**ersion_ number, and architecture:

```text
<name>@<version>-<architecture>.<extension>
<name>-<edition>@<version>-<architecture>.<extension>
```

- When a non-disabled `--daemon:<name>` is supplied, the daemon identifier replaces `<name>`, e.g. `zongsoft.web@1.0.0-x64.deb`.
- `<architecture>` is the lowercase architecture name, such as `x64` or `arm64`; `<extension>` is `tar.gz`, `deb`, or `rpm`.
- The `.sh` installer that accompanies a tar package uses the same base name.

## Command reference

> The .NET examples in this and later sections follow the quick start conventions: they run in the `web/default` host directory of the hosting repository after deploy.cmd has built Release net10.0 and deployed the plugins.

### Syntax

```bash
dotnet-pack tar <options...> [entries...]
dotnet-pack deb <options...> [entries...]
dotnet-pack rpm <options...> [entries...]
```

- Specify exactly one of `tar`, `deb`, or `rpm`. The subcommands share common options; `deb` and `rpm` add their own relationship options.
- Write options as `--key:value` or `--key=value`; quote values containing spaces according to your shell.
- Positional arguments are [package entries](#package-entries); when omitted, the whole source directory is packaged.

Exit codes:

| Code | Meaning |
| --- | --- |
| `0` | Packaging succeeded |
| `1` | Argument, input, or packaging failure |
| `2` | No command specified |

In the tables below, **required** options must always be supplied; _conditionally required_ options are required only when the source directory has neither `.edition` nor `.version`.

### Identity and target

| Option | Default | Description |
| --- | --- | --- |
| `--name:<name>` | _Conditionally required_ | Application/package name; also locates the .NET host assembly when a service is generated. |
| `--version:<version>` | _Conditionally required_ | Release version number; overrides the selected version number when a source `.edition` manifest or `.version` identifier exists. A zero version number _(`0.0.0.0`)_ is rejected. |
| `--edition:<name>` | Defined by `.edition` | Optional product Edition appended to the package name. |
| `--platform:<platform>` | **Required** | Target platform: `linux`, `unix`, `osx`, `windows`/`win`, or `unknown`. Linux packages normally use `linux`. |
| `--framework:<tfm>` | `framework` variable or empty | Optional .NET target framework, such as `net10.0`, used to locate the host under `bin/<compilation>/<framework>`. An omitted or empty option uses the merged variable. If the final value is empty, this build directory is skipped; hosts in the source directory can still be located. |
| `--architecture:<arch>` | `x64` | Target CPU architecture, such as `x64`, `x86`, `arm64`, or `arm`. |
| `--compilation:<name>` | `Release` | Optional .NET build configuration used to locate the host under `bin/<compilation>/<framework>`; also available as `$(compilation)`. |

Ordinary file packaging needs neither `--framework` nor `--compilation`; these options do not run a build. Both may also be omitted when the application DLL is already in the source directory or an existing `.service` file is supplied. Supply the framework through `--framework`, an environment variable or an ancestor `.env` to locate a host in a .NET build directory; `--compilation` defaults to `Release`.

### Input and output

| Option | Default | Description |
| --- | --- | --- |
| `--source:<path>` | current directory | Source directory to package. |
| `--output:<path>` | source directory | Package output directory. Always treated as a directory, with or without a trailing separator; file names are not supported. Relative paths resolve from `--source`. |
| `--exclude:<patterns>` | empty | File patterns skipped while loading entries; separate multiple patterns with commas or semicolons. |
| `--overwrite[:boolean]` | `false` | Overwrite existing artifacts; a tar archive and its installer are committed as a group. |

### Installation and service

| Option | Default | Description |
| --- | --- | --- |
| `--install-path:<path>` | `/opt/<identifier path>` | Linux install directory. The identifier is lowercased and every dot becomes `/`: `Zongsoft.Hosting.Web` maps to `/opt/zongsoft/hosting/web`. With a non-disabled `--daemon:zongsoft.web`, the daemon identifier is used instead, giving `/opt/zongsoft/web`. |
| `--daemon:<name-or-file>` | auto-detect/generate | systemd service identifier or an existing `.service` file; `none`, `disable`, or `disabled` turns the service off. |
| `--daemon-environments:<names>` | empty | When generating a service, reads these names from the variable view and writes them as `Environment=`; separate with `,` or `;`. |
| `--listen:<port-or-url>` | empty | Listening port or address of the generated service; a port binds to `127.0.0.1`, a complete address is passed to the host's `--urls` unchanged. |

### Package description

| Option | Default | Description |
| --- | --- | --- |
| `--title:<text>` | empty | Human-readable package title, also used as the generated systemd description. |
| `--summary:<text-or-file>` | empty | Short summary; see [Text sources](#text-sources). |
| `--description:<text-or-file>` | empty | Long description; see [Text sources](#text-sources). |
| `--homepage:<url>` | `https://github.com/Zongsoft` | Project homepage. |
| `--license:<text>` | empty | License expression or name. |
| `--category:<text>` | format default | Debian `Section` (default `utils`) or RPM `Group` (default `Applications/System`). |
| `--maintainer:<text>` | `Zongsoft` | Package maintainer. |
| `--manufacturer:<text>` | `Zongsoft` | Software manufacturer; a missing, null, or empty value uses the default. Whitespace alone does not trigger the default. |

### Extensions

| Option | Default | Description |
| --- | --- | --- |
| `--web:<hoster[:filepath]>` | empty | Generates Web hoster configuration; currently `nginx`, reading `source/web.profile` by default. See [Web hosting configuration](#web-hosting-configuration). |
| `--migrator:<name>` | empty | Input name of migration artifacts to include, optionally with a directory. See [Migrator artifact integration](#migrator-artifact-integration). |
| `--installing`, etc. | generated | Lifecycle hooks and pre/post snippets; see [Lifecycle scripts](#lifecycle-scripts). |

### Dependencies and relationships

`--dependencies:<list>` uses the same `name[:range]` syntax for `tar`, `deb`, and `rpm`. Ranges follow [NuGet interval notation](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges), with `[10.0)` additionally accepted as shorthand for `[10.0,)`.

| Input | Required version |
| --- | --- |
| `runtime` or `runtime:(,)` | Any version |
| `runtime:10.0`, `runtime:[10.0,)`, or `runtime:[10.0)` | Greater than or equal to 10.0 |
| `runtime:[10.0]` | Exactly 10.0 |
| `runtime:(10.0,)` | Greater than 10.0 |
| `runtime:(,11.0]` | Less than or equal to 11.0 |
| `runtime:(,11.0)` | Less than 11.0 |
| `runtime:[10.0,11.0)` | At least 10.0 and below 11.0 |
| `runtime:(10.0,11.0]` | Above 10.0 and at most 11.0 |
| `runtime:[10.0,11.0]` / `runtime:(10.0,11.0)` | Both boundaries included / excluded |

Separate required groups with commas or semicolons **outside ranges**. Within a group, `|` means that any one alternative is sufficient. Quote the complete value:

```text
--dependencies:"aspnetcore-runtime-10.0:[10.0,11.0);openssl:[3.0) | libressl:[4.0)"
```

Tar records the validated input in the PAX global attribute `Dependencies`, joining required groups with `; ` and preserving ranges and alternatives. Its installer does not check or install dependencies. Debian receives a `Depends` field; RPM receives `Requires` entries. For `runtime:[10.0,11.0) | alternative:[9.0)`, Debian writes `runtime (>= 10.0) | alternative (>= 9.0), runtime (<< 11.0) | alternative (>= 9.0)`. RPM writes `((runtime >= 10.0 with runtime < 11.0) or alternative >= 9.0)`. RPM alternatives require RPM 4.13+, and bounded ranges using `with` require RPM 4.14+. Debian expansion fails with a diagnostic if one group would produce more than 1024 relationship groups.

Only the interval notation is shared. Version endpoints retain their original text and use the target package manager's comparison rules; the packager does not normalize, reorder, or compare them as NuGet versions. Debian virtual packages can have different providers satisfying the lower and upper bounds; RPM `with` requires the same package to satisfy both. Package names are not mapped between distributions. Native names such as Debian `libc6:any` or RPM `pkgconfig(openssl)` remain format-specific; a range is introduced by `:[`, `:(`, or a colon followed by a digit-starting bare version. Use brackets for native versions starting with a letter.

Floating versions such as `10.*`, malformed intervals, and empty alternatives fail packaging. Duplicate constraints are retained. An omitted or empty dependency list writes no application dependency. The packager only writes declarations; it does not download or embed dependencies, so the installation environment needs an available package repository. Other relationship options below retain their native syntax.

#### Debian relationship options

| Option | control field |
| --- | --- |
| `--provides:<list>` | Provides |
| `--replaces:<list>` | Replaces |
| `--breaks:<list>` | Breaks |
| `--conflicts:<list>` | Conflicts |
| `--recommends:<list>` | Recommends |
| `--suggests:<list>` | Suggests |

- Version relationships must be parenthesized, e.g. `zongsoft.daemon (>= 1.0.0)`; supported operators are `<<`, `<=`, `=`, `>=`, and `>>`.
- Recommends and Suggests accept `|` alternatives (any one satisfies the relationship); Provides accepts only `=`. Depends is generated from the uniform dependency syntax above.
- Separate entries with commas or semicolons and quote the whole option. Invalid relationships or line breaks fail packaging.

#### RPM relationship options

| Option | Description |
| --- | --- |
| `--provides:<list>` | RPM `Provides` entries. |
| `--conflicts:<list>` | RPM `Conflicts` entries. |

RPM Provides and Conflicts entries support `name`, `name = version`, `name >= version`, `name <= version`, `name > version`, `name < version`, or `name(>= version)`. Requires is generated from the uniform dependency syntax above.

### Option value conventions

- **Booleans:** `--overwrite` is equivalent to `--overwrite:true`. Explicit values can be `true/false`, `1/0`, `yes/no`, `on/off`, `enable/disable`, or `enabled/disabled`.
- **Enumerations:** follow the [conversion rules](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Common/Convert.cs) of [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) without checking whether the member is defined; supply valid values.
- **Output conflicts:** checked before packaging and again before commit; existing artifacts remain if generation fails.

### Common examples

Package a frontend `dist` directory from the frontend project root:

```bash
dotnet-pack tar \
  --name:example.frontend \
  --version:1.0.0 \
  --platform:linux \
  --source:./dist \
  --output:../packages \
  --daemon:none
```

Omitting positional arguments includes the entire `dist` directory; `--daemon:none` disables service generation. Replace `tar` with `deb` or `rpm` for those formats.

Build a portable tarball:

```bash
dotnet-pack tar \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

Build an RPM package with dependency metadata:

```bash
dotnet-pack rpm \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  --license:MIT \
  --dependencies:"aspnetcore-runtime-10.0:[10.0)" \
  --provides:"zongsoft.web = 1.0.0" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

Build a Debian package with Nginx configuration (see [Web hosting configuration](#web-hosting-configuration)):

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  --category:utils \
  --web:nginx \
  --exclude:*.profile \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

Disable the systemd service and package files only:

```bash
dotnet-pack tar \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

## Application identity and version files

The packager reads only the files directly inside `--source`: `.edition` first using [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) [`ApplicationManifest`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationManifest.cs), then `.version` using [`ApplicationIdentifier`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationIdentifier.cs) only when `.edition` is absent. A corrupt, unreadable file or a directory occupying either selected path stops packaging; no parent or child directory is searched.

### Source manifest format

A single-version `.edition` contains `Zongsoft.Hosting.Web@1.0.0`. A named-Edition manifest can select its current Edition on the first line:

```ini
Zongsoft.Hosting.Web=Enterprise

[Community]
1.0.0

[Enterprise]
2.0.0
```

The single-version and named-Edition forms cannot be mixed. Omitting `=Enterprise` leaves [`Editions.Current`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationManifest.cs) unset. Legacy multi-Edition `.version` files must be renamed to `.edition`; the identifier reader only reads the first nonblank line and does not interpret Edition sections. A fallback identifier can contain `Zongsoft.Hosting.Web-Community@1.0.0`.

### Identity merge rules

| Identity | Rule |
| --- | --- |
| Name | An omitted or blank `--name` uses the source name. An explicit name must match case-insensitively; source spelling is retained. |
| Edition from a manifest | A nonblank `--edition` must exist, matched case-insensitively with file spelling retained. Otherwise use Current, then the only named Edition; multiple Editions without Current require an explicit choice. A manifest without named Editions uses its top-level version. |
| Edition from an identifier | A nonblank `--edition` replaces or adds the Edition; otherwise use the identifier's Edition. |
| Version number | `--version` overrides the selected version. The final version must be nonzero. |

When both files are absent, valid `--name` and `--version` are required. Identity still comes only from these source files and explicit identity options, which can reference environment or `.env` variables. Source is fixed before loading identity; final identity values then populate output, payload, scripts, and migration matching.

### Packaged version file

The installation-root `.version` is always generated from the final name, Edition and version using [`ApplicationIdentifier.Save(Stream)`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Services/ApplicationIdentifier.cs): UTF-8 without BOM, mode `0644`. It replaces payload entries targeting the same location, including rooted aliases, and bypasses exclusions.

The source directory's direct `.edition` is never payload, even when explicitly selected or renamed through an alias. Entries targeting the installation-root `.edition` are also omitted. Other subdirectory files keep the ordinary payload rules.

### Saving the source manifest or version identifier

| Files found before packaging | Save after success |
| --- | --- |
| `.edition`, with or without `.version` | Rewrite both `.edition` and `.version`; create `.version` if missing. |
| Only `.version` | Update only `.version` in single-line identifier format. |
| Neither file | Create `.edition` and `.version` together. |

Only the selected manifest Edition's version changes, and the final named Edition becomes Current. Other Editions retain their names, versions and order; comments and blank lines follow [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core)'s preservation rules. Manifests use UTF-8 without BOM and CRLF; identifiers use [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core)'s serialization. A trailing LF or CRLF is optional and does not affect identifier parsing.

Source files are saved only after all package artifacts succeed. Single-file saves are atomic; creating or updating both files uses grouped publication with rollback. When neither file existed, concurrently created files are not overwritten. Parse, validation, or packaging failures leave source files unchanged. If saving fails, the command reports the source paths and the already generated package, retains the package, and returns an error.

## Package entries

Package entries are the files written into the package payload, specified as positional arguments.

### Selecting the payload

**Everything:** without positional arguments, every file under `--source` is included recursively:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:bin/Release/net10.0 \
  --output:../../../.packages
```

**Explicit selection:** with positional arguments, only the listed files or directories are included. This example matches the host [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd): it selects the MIME definitions, application settings, host configuration, static files, and plugins, and uses the `~` directory alias to place the build output at the installation root:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --exclude:"**/logs/;bin/Release/net10.0/*.staticwebassets.*" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### Target aliases

Any entry can specify a target alias after its last colon, i.e. its relative location in the package. This example demonstrates aliases with files that exist in the hosting repository and disables service generation:

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  ../README.md:docs/hosting-web.md \
  ../../zongsoft-logo.png:assets/logo.png \
  web.profile:docs/web.profile
```

An alias starting with `/` or `\` is a **root-level entry** and installs to an absolute system path:

| Format | Root-level entry handling |
| --- | --- |
| `.deb` | Installed at the root path; entries under `/etc/` are listed in `conffiles`. |
| `.rpm` | Installed at the root path; entries under `/etc/` are marked as configuration files. |
| `.tar.gz` | Stored under `.root/` in the archive and copied to the root path by `install.sh`. |

### Excluding files

`--exclude` skips matching files while loading entries. Patterns are relative to `--source`, always use `/` as the separator, support `*`, `?`, and `**`, and are separated by commas or semicolons:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --exclude:"**/logs/;bin/Release/net10.0/*.staticwebassets.*" \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### Path and matching rules

- **Relative paths** resolve from `--source`. Absolute paths outside `--source` are allowed; without an alias, only the file name is used.
- **Directories** are included recursively. The directory itself is packaged, so empty directories and source modes are kept (Windows defaults to `0755`); synthesized parent directories use `0755`.
- **Globbing:** any path segment supports `*` and `?`; a standalone `**` segment matches zero or more directories. Matches for each argument are sorted ordinally by path relative to its fixed prefix, and arguments keep their written order. Matching is case-insensitive on Windows and case-sensitive on Unix.
- **Conflicts:** file/directory conflicts are rejected; duplicate target paths are reported as conflicts and skipped.
- **Symbolic links:** source links keep their logical names and read their targets; nested directory links inside a directory payload are skipped. See the [implementation notes](docs/implementation.md#local-searches-and-source-links) for local search and link rules.

### File modes

| Packaging host | Mode source |
| --- | --- |
| Unix-like | Source file modes are preserved. |
| Windows | `.sh`, `.dll`, `.exe`, and extensionless files get `0755`; other files get `0644`. |

## systemd services

When services are enabled and a host or an existing service file can be located, every package format carries a systemd service, which the lifecycle scripts register, enable, and remove.

### Service source

1. If the file named by `--daemon:<name>` exists under `--source`, that file is used.
2. Otherwise `<daemon>.service` is generated.
3. When `--daemon` is omitted, the lowercase final application name (`Package.Name`) is the service identifier, without an Edition.

Use `--daemon:none`, `--daemon:disable`, or `--daemon:disabled` to disable the service and package files only.

### Locating the .NET host

When generating a service, the host assembly is located in this order:

1. `<source>/<name>.dll`
2. The only `.exe` under `<source>`, converted to the same-named `.dll`
3. `<source>/bin/<compilation>/<framework>/<name>.dll`
4. The only `.exe` under `<source>/bin/<compilation>/<framework>`, converted to the same-named `.dll`

Steps 3 and 4 are skipped without a nonblank `framework` and `compilation`. If no host is found, a diagnostic is printed and packaging continues without a service; a package with migration artifacts requires a locatable host or `--daemon:none`.

The generated service runs:

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

### Listening address

`--listen:<value>` is passed to the application as `--urls`. When omitted, no `--urls` is added; an existing `.service` file keeps its `ExecStart`.

| Value | Resulting `--urls` |
| --- | --- |
| `--listen:8069` (port only) | `http://127.0.0.1:8069` |
| `--listen:http://0.0.0.0:8069` (complete address) | Used unchanged |
| `--listen:"http://0.0.0.0:8069;https://0.0.0.0:8443"` (multiple addresses) | Semicolon-separated; quote the whole value |

For example, `--listen:8069` generates:

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

> 💡 **Tip:** A port-only value binds to `127.0.0.1`, which suits a local reverse proxy. Use a complete address such as `http://0.0.0.0:8069` when the application must accept direct network connections.

HTTPS requires a usable default server certificate configured in the host; the packager does not generate or configure certificates. See [Kestrel endpoint configuration](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/servers/kestrel/endpoints.md). HTTPS in the table above is optional; the current hosting scripts do not enable it.

> 🚨 **Warning:** HTTPS endpoints fail to start if the host has no usable default certificate. Configure and protect the certificate in the application environment.

### Service environment variables

Values of the variables listed in `--daemon-environments` are written to the generated service's `Environment=`. The Web host's [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) uses it for `Environment`, `DOTNET_ENVIRONMENT`, and `ASPNETCORE_ENVIRONMENT`. This Bash example specifies the framework explicitly; the host scripts resolve it from Variables:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --listen:8069 \
  --daemon:zongsoft.web \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --output:.packages \
  --daemon-environments:Environment,DOTNET_ENVIRONMENT,ASPNETCORE_ENVIRONMENT \
  --Environment:Production \
  --DOTNET_ENVIRONMENT:Production \
  --ASPNETCORE_ENVIRONMENT:Production \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

Values can come from extra command options (as above), environment variables, or `.env` files; see [Variables](#variables).

## Lifecycle scripts

Lifecycle scripts run on the target host during installation and removal.

### Main hooks

| Stage | Before | After |
| --- | --- | --- |
| Install | `--installing:<text-or-file>` | `--installed:<text-or-file>` |
| Uninstall/remove | `--uninstalling:<text-or-file>` | `--uninstalled:<text-or-file>` |

A main hook can be a source-relative file path, an absolute file path, or inline script text; see [Text sources](#text-sources).

### Pre and post snippets

Each main hook accepts file-based pre and post snippets, all empty by default:

| Main hook | Pre-files option | Post-files option |
| --- | --- | --- |
| `--installing` | `--preinstalling:<paths>` | `--postinstalling:<paths>` |
| `--installed` | `--preinstalled:<paths>` | `--postinstalled:<paths>` |
| `--uninstalling` | `--preuninstalling:<paths>` | `--postuninstalling:<paths>` |
| `--uninstalled` | `--preuninstalled:<paths>` | `--postuninstalled:<paths>` |

Separate multiple paths with `;` or `|`. Pre/post options accept file lists only, not inline text or `text:`.

### Default scripts

When no scripts are supplied, defaults are generated. For packages with a systemd service they:

- stop the service before installation and removal;
- create or remove the `/etc/systemd/system/<service>` symlink and reload systemd;
- enable the service after installation;
- remove the install directory after uninstallation.

### Stage differences by format

| Format | When the uninstall lifecycle runs |
| --- | --- |
| Debian | `prerm` enters the uninstall lifecycle only for `remove` or `deconfigure`; `postrm` finishes it only for `remove` or `purge`. |
| RPM | `%preun`/`%postun` run only when the final installed instance is removed (`$1=0`); the payload is kept while an instance remains. |
| tar | Explicit `install.sh`/`uninstall.sh`; the generated uninstaller removes only the resolved `TARGET` path. |

> 🚨 **Warning:** Generated lifecycle scripts can stop or enable services and remove the application install directory during uninstall. Review the generated scripts and target paths before running them on a host.

### Text sources

Summary, description, and the four main hooks share one resolver:

| Form | Interpretation |
| --- | --- |
| `file:<path>` | Reads the file explicitly (relative to source). |
| `text:<text>` | Keeps the literal text after the colon. |
| No prefix | Expands package variables first. An existing source-relative file is read; multiline values are text; an obviously missing file path fails; anything else is text. |

File contents are used as-is: they are neither expanded nor interpreted as another path. Put shell expressions in a file or a `text:` value.

## Web hosting configuration

`--web:nginx[:filepath]` reads an INI `web.profile` (in the source directory by default), handles imports through [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core)'s [Profile](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Configuration/Profiles/Profile.cs), and generates `.web/nginx/<PackageName>.conf` below the install root. A minimal `web.profile`:

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://app:8069
```

For the Web host, `server=~` uses the generated service's listening port (such as `8069`); adjust site settings for your environment.

### Payload and conversion are independent

`--web` only converts; it does not decide whether the input `*.profile` file is packaged. Positional arguments and `--exclude` keep that responsibility. For example, `--exclude:*.profile` excludes the input file without preventing Nginx generation.

| `--web` value | Behavior |
| --- | --- |
| omitted, empty, or `none` | No Web configuration |
| `nginx` | Generates Nginx configuration |
| `iis` | Reserved, not implemented |

### Activation at install time

| Scenario | Behavior |
| --- | --- |
| Bare-metal install (default) | Creates the system loading link and validates the configuration; reloads Nginx if running, never starts a stopped one. |
| Container build | Pass `HOSTER_WEB_ACTIVATION=0` to installation to disable activation, then collect `.web/nginx/*.conf` from the known install root. |
| Ordinary removal | Deletes `.web`. |

`HOSTER_WEB_ACTIVATION` accepts `0`/`1` and case-insensitive `false`/`true`; an explicit empty value is invalid. Configuration files are still delivered when activation is disabled.

> 💡 **Tip:** See the [Web configuration guide](docs/web.md) for field scopes, defaults, and inheritance; complete examples of matching, certificates, load balancing, health checks, native settings, variables, and container builds; and the modules and minimum versions required by active checks and cookie affinity.

## Migrator artifact integration

Migrations (schema and data changes for databases, storage, and similar resources) are prepared in advance by the independent [migrator tool](../migrator/README.md). The packager only includes an already generated **migration archive** and its **launcher script**: it does not parse `.migration`/`.ini`, SQL, or execution plans, and does not carry a native executor.

Enable it with `--migrator:<name-or-path>`. For a source at `hosting/web/default/` and artifacts in `hosting/.migration/`, specify `--migrator:zongsoft`. Omitting the option or supplying an empty value disables migration integration.

### Artifact names

Artifacts are located by the package's **final** _**E**dition_, _**V**ersion_ number, platform, and architecture, including values obtained from the source `.edition` manifest or `.version` identifier and the default x64. The _**E**dition_ segment is omitted when absent. For enterprise, 1.0.0, and Linux x64:

```text
zongsoft-enterprise(migrate)@1.0.0_linux-x64.tar.gz
zongsoft-enterprise(migrate)@1.0.0_linux-x64.sh
```

- The file stem is `<name>[-<edition>](migrate)@<version>_<RID>`, using the name verbatim and omitting `-<edition>` when absent. Supply the generator's `--name` without the automatic `(migrate)` marker.
- Do not include an _**E**dition_, _**V**ersion_ number, RID, extension, wildcard, or path list in the value.
- The migration name may differ from the host name, but _**E**dition_, _**V**ersion_ number, and RID must match.

### Lookup locations

Lookup starts from the final `--source` directory, not the command working directory:

| Form | Search scope |
| --- | --- |
| Bare name, e.g. `zongsoft` | From source up through the filesystem root. Each level checks the directory itself, then its direct `.migration/` child, before moving up; other child directories are not searched. |
| Contains `/` or `\`, e.g. `./zongsoft` | Only the explicit directory. Relative paths resolve from `--source`; absolute paths are used directly. `./zongsoft` searches only the source directory. |

Matching rules:

- Lookup moves to the next location only when **both** the archive and the script are absent.
- Finding only one fails immediately with the full path of the missing companion.
- A complete pair is validated immediately (archive metadata and RID); a validation failure stops lookup.
- Both files must come from the same directory; pairs are never combined across directories, and other _**E**ditions_, _**V**ersion_ numbers, or architectures are never substituted.
- If the root is reached without a pair, the error lists the expected names and checked directories, including each `.migration/`.

### Packaging and installation behavior

- Both files are copied unchanged into the install root's `.migration/`; the archive is not unpacked while packaging. When run, the migrator launcher extracts the archive into a separate temporary directory, whose root directly contains the plan and executor; it does not create an additional `.migration/` layer inside the installation directory. The script has mode `0755` and the archive `0600`. Payload collisions fail packaging.
- At installation, the generated hook runs `apply` with `/var/lib/<package-name>/packager` as the state directory; a failed migration prevents the service from starting.
- systemd `ExecStartPre` runs `check`, which compares the package plan fingerprint with the local `ready` completion marker; it does not inspect live database or bucket state.
- Migration also runs without a daemon; DESTDIR staging skips lifecycle hooks; uninstallation preserves migration state, databases, and buckets.
- Targets require POSIX sh, tar/gzip, cmp, and the system libraries the executor needs; see the migrator's migration guide.

> 🚨 **Warning:** Installing a package with migration artifacts runs `apply` and may create or change databases, users, permissions, and Amazon S3 buckets. Back up production data and validate the package in staging before installation.

## Variables

Option values and entry arguments can reference variables in either of two equivalent forms:

```text
$(name)
%name%
```

Variable names are case-insensitive and may contain dots, hyphens, and indices.

### Load order

Variables load in this order; later values overwrite earlier ones, including empty values:

1. Descriptor defaults
2. System environment variables
3. The direct `.env` file of each directory from the filesystem root down to the source directory (farthest first)
4. Explicit command options, including extra options such as `--Environment:Production`

An omitted or empty `--framework` uses a nonempty `framework` from the merged variables; a nonempty option takes precedence. If no nonempty variable is available, the existing handling remains. Whitespace-only option values keep their existing behavior.

`.env` rules:

- `--source` is resolved and fixed from the environment and options before `.env` files load. `.env` cannot determine or redirect source, and no separate working-directory chain or child directory is searched.
- Files are read with [`Profile.Load`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Core/src/Configuration/Profiles/Profile.cs), supporting INI and `#@import`. Root entries keep their names; section levels and entry names join with `_`. For example, `access_key=example` under `[io rustfs]` creates `io_rustfs_access_key`, and root `environment=Development` creates `environment`.
- Missing files are skipped; read or parse failures stop packaging.
- Variables are isolated per invocation and never modify the process environment.

### Expansion rules

- Variables expand lazily and recursively: unused invalid references do not block packaging, while referenced unknown, cyclic, or over-64-level references fail.
- Option text is kept until used. After `source` is located, explicit `name`, `edition`, and `version` expand before validating the source `.edition` manifest or `.version` identifier and converting the version; `platform`, `architecture`, and `overwrite` also expand before conversion.
- `name`, `edition`, and `version` come only from the source `.edition` manifest or `.version` identifier and explicit identity options; same-named environment or `.env` variables never replace the identity, although explicit options may reference `.env` variables. Identity values available only from the source `.edition` manifest or `.version` identifier cannot locate that source directory.
- The final identity and resolved `source` and `output` override same-named values in the collection.
- `--migrator` requires explicit activation; `--overwrite` can come from the environment or `.env` and be overridden on the command line.

Pass a literal `$(APP_VERSION)` or `%APP_VERSION%` for the packager to expand (use single quotes in Bash to prevent shell expansion), or let the shell expand values directly:

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --listen:8069 \
  --daemon:zongsoft.web \
  --name:"$APP_NAME" \
  --version:"$APP_VERSION" \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --output:.packages \
  ../../mime \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  wwwroot \
  plugins \
  bin/Release/net10.0:~
```

### Common variables

| Variable | Meaning |
| --- | --- |
| `name`, `version`, `edition` | Package identity and optional release edition |
| `platform`, `architecture`, `RuntimeIdentifier` | Target operating system, CPU architecture, and combined runtime identifier |
| `framework`, `compilation` | Target .NET framework and build configuration |
| `source`, `output` | Normalized source and package output directories |

## Package formats

All three formats share the same metadata and payload; they differ only in container structure and installation:

| Format | Container | Installed by |
| --- | --- | --- |
| `.tar.gz` | gzip-compressed PAX tar with a same-named `.sh` installer | Running the `.sh`, or `install.sh` after extraction |
| `.deb` | `ar` container with `debian-binary`, `control.tar.gz`, and `data.tar.gz` | System package manager (e.g. `dpkg`) |
| `.rpm` | RPM lead/signature/header metadata plus a gzip-compressed `newc` cpio payload | System package manager (e.g. `rpm`) |

Debian/RPM payloads use automatically cleaned temporary files and streaming digests instead of assembling the whole package body in memory, so reserve temporary disk space. See [payload streams](docs/implementation.md#payload-streams-and-directory-entries).

Inspect a package before installing it. The listing and metadata commands below are read-only and do not run lifecycle scripts.

### `.tar.gz`

The tar command produces a `.tar.gz` archive and a same-named `.sh` installer. The archive contains the application files, optional root-level entries under `.root/`, and executable `install.sh` and `uninstall.sh` scripts that merge the lifecycle scripts.

The archive records the following metadata as PAX global attributes. All attributes remain readable after the archive is renamed and add no installed files or command options:

| Attribute | Meaning |
| --- | --- |
| `Packager` | Generator identity, `Zongsoft.Tools.Packager@<assembly-version>`, independent of the application version. |
| `PackageName` | Final system package name, including the service identity selected by `--daemon` and any Edition suffix; distinct from the application identity in `.version`. |
| `PackageSize` | Sum of payload file sizes in bytes, including generated payload files and root-level files, excluding tar headers and `install.sh`/`uninstall.sh`; not filesystem disk usage. |
| `Version` | Final application version, matching the generated `.version`. |
| `Architecture` | Target CPU architecture from `--architecture` (default `x64`), using the same lowercase value as the filename, such as `x64`, `arm64`, `x86`, or `arm`. |
| `Manufacturer` | Software manufacturer; defaults to `Zongsoft` when null or empty, while whitespace-only values are retained. |
| `Maintainer` | Package maintainer, separate from the generator identity in `Packager` and software manufacturer in `Manufacturer`. |
| `Homepage` | Project home page. |
| `License` | Application license expression or name. |
| `Summary` | Summary, falling back to the title and then the application name when blank. |
| `Description` | Full description, falling back to the effective summary when blank; Unicode and line breaks are preserved through the text escaping described below. |
| `InstallPath` | Default installation path; installation-time overrides still apply where supported. |
| `Dependencies` | Validated dependency declarations in the shared input syntax, with required groups separated by `; `; informational only. |
| `Category` | Explicit package category, without Debian or RPM default categories. |

Blank optional values (`License`, `Homepage`, `Maintainer`, `InstallPath`, `Dependencies`, and `Category`) are omitted. `PackageSize` is `0` for an empty payload. No `BuildTime` attribute is written.

The .NET PAX writer rejects literal line breaks in attribute values. `Summary` and `Description` therefore escape backslashes, carriage returns, and line feeds as `\\`, `\r`, and `\n`, respectively; readers must decode these sequences to restore the original text. Other attributes retain their existing plain-text representation.

Inspect the contents:

```bash
tar -tzf ./.packages/zongsoft.web@1.0.0-x64.tar.gz
```

> 🚨 **Warning:** The installation commands below use `sudo` to run the generated installer, which can write system paths and manage services. Stage with `DESTDIR` or test on a staging host first.

| Action | Command |
| --- | --- |
| One-step install | `sudo sh ./.packages/zongsoft.web@1.0.0-x64.sh` |
| Extract and install | `tar -xzf ./.packages/zongsoft.web@1.0.0-x64.tar.gz && sudo ./install.sh` |
| Install into a staging directory | `DESTDIR=/tmp/stage ./install.sh` |
| Override the install path | `sudo env INSTALL_PATH=/srv/zongsoft/web ./install.sh` |
| Uninstall | `cd /opt/zongsoft/web && sudo ./uninstall.sh` |

Overriding the install path applies only to packages without migration artifacts. With migration artifacts the install path is fixed when the package is built; set `--install-path` at packaging time instead.

### `.deb`

Inspect the metadata and payload:

```bash
dpkg-deb --info ./.packages/zongsoft.web@1.0.0-x64.deb
dpkg-deb --contents ./.packages/zongsoft.web@1.0.0-x64.deb
```

> 🚨 **Warning:** Installing with `dpkg` runs package lifecycle scripts as a privileged operation. Review the scripts and test on a staging host first.

```bash
sudo dpkg -i ./.packages/zongsoft.web@1.0.0-x64.deb
```

Root-level entries under `/etc/` are written to Debian `conffiles` metadata.

### `.rpm`

Inspect the metadata, payload, and lifecycle scripts:

```bash
rpm -qip ./.packages/zongsoft.web@1.0.0-x64.rpm
rpm -qlp ./.packages/zongsoft.web@1.0.0-x64.rpm
rpm -qp --scripts ./.packages/zongsoft.web@1.0.0-x64.rpm
```

> 🚨 **Warning:** Installing with `rpm` runs package lifecycle scripts as a privileged operation. Review the scripts and test on a staging host first.

```bash
sudo rpm -Uvh ./.packages/zongsoft.web@1.0.0-x64.rpm
```

Root-level entries under `/etc/` are marked as RPM configuration files.

### Application metadata

`--homepage` identifies the application's home page. `--manufacturer` identifies the software manufacturer, while `--maintainer` identifies the package maintainer. The variables `homepage`, `manufacturer`, and `maintainer` follow the usual defaults → environment → ancestor `.env` → explicit options order. Manufacturer values that resolve to null or an empty string use `Zongsoft`; a value consisting only of whitespace does not use the default.

| Format | Homepage | Manufacturer | Maintainer |
| --- | --- | --- | --- |
| tar.gz | PAX global extended attribute `Homepage` | PAX global extended attribute `Manufacturer` | PAX global extended attribute `Maintainer` |
| deb | `Homepage` | Custom control field `Manufacturer` | `Maintainer` |
| rpm | `URL` (1020) | `VENDOR` (1011) | `PACKAGER` (1015) |

Debian text fields follow the existing normalization rules: surrounding whitespace is trimmed, and a whitespace-only manufacturer field is omitted. Tar and RPM retain the manufacturer value. These fields are format metadata and add no installed files.

### Packager version metadata

Every package records the identity of the packager that built it, logically `Packager:Zongsoft.Tools.Packager@<assembly-version>`. The value is read from the packager's own assembly, is independent of the application version, and needs no extra option.

| Format | Location | How to read |
| --- | --- | --- |
| tar.gz | PAX global extended attribute `Packager` | A PAX-aware reader, e.g. Python `tarfile` `pax_headers["Packager"]` |
| deb | `Packager` field of `control` in `control.tar.gz` | `dpkg-deb -f <package.deb> Packager` |
| rpm | `RPMVERSION` string tag (1064) in the main header | `rpm -qp --queryformat '%{RPMVERSION}\n' <package.rpm>` |

The RPM `PACKAGER` tag (1015) holds the `--maintainer` value. This metadata lives in format headers; it adds no install-directory files and does not change `.version` or `migration.json`.

## Recommended workflow

1. **Prepare the application.** Build the application and deploy its plugins, use the application directory itself as `--source`, and select the required binaries, configuration, plugins, and static files with positional arguments; no staging directory is needed. When packaging every file, keep the output directory outside the source so older packages cannot become payload.
2. **Choose the payload.** Without positional entries, every file under `--source` is included. For a smaller, reviewable package, list files and directories explicitly and use `--exclude` for files that must stay out.
3. **Verify the package identity.** Check `--name`, `--version`, the optional `--edition`, platform, and architecture; confirm the generated service identifier, `--install-path`, and `--listen` address match the target environment.
4. **Inspect the output.** Use `tar -tzf`, `dpkg-deb --info`/`--contents`, or `rpm -qip`/`-qlp`/`-qp --scripts`; for service-managing packages, also review lifecycle scripts and the systemd unit.
5. **Validate in staging.** Test installation, service startup, configuration paths, and removal, using `DESTDIR` with tar packages or a disposable host. Record environment-specific configuration instead of relying on production-only conditions.
6. **Release to production carefully.** Back up application data and confirm recovery steps first. With `--migrator`, installation runs migrations that may change databases and Amazon S3 buckets; keep the same migration state directory across package versions.

> 💡 **Tip:** `--overwrite` only replaces existing package files in the output directory; it never overwrites or updates an installed application.

> 🚨 **Warning:** A successful build only means the package files were created. It does not prove the package installs on the target or that the application starts.

## Troubleshooting

| Message | Cause and fix |
| --- | --- |
| `The '<path>' directory does not exist.` | After variable expansion and normalization, `--source` points to a missing location. |
| `The source path '<path>' does not exist.` | A positional entry matched no existing file, directory, or glob. |
| `A valid nonzero --version or selected source version is required. Source: <path>` | No valid command-line version number or version number from the source `.edition` manifest or `.version` identifier, or the version number is zero; text that is not a version number fails after expansion, before selecting a version from the source `.edition` manifest or `.version` identifier. |
| `The daemon host location failed.` | No existing service file or usable host `.dll` was found, and no name could be inferred from a single `.exe`. Use `--daemon:<service-file>`, or `--daemon:none` to disable the service. |
| `The output file '<path>' already exists. Use --overwrite to replace it, or choose another --output directory.` | Names the conflicting package or tar installer. Add `--overwrite` to replace it, or choose another `--output`; without overwrite, existing artifacts stay unchanged. |

## Building from source

Run these commands from the `packager` directory of the tools repository.

| Task | Command |
| --- | --- |
| Restore and build | `dotnet restore Zongsoft.Tools.Packager.slnx`<br>`dotnet build Zongsoft.Tools.Packager.slnx -c Release` |
| Run regression tests | `dotnet test test/Zongsoft.Tools.Packager.Tests.csproj -f net10.0` |
| Cake build (includes local packing) | `dotnet cake --target=build --edition=Release` |
| Cake test (default target) | `dotnet cake --target=test --edition=Release` |

- Cake passes the same `--edition` configuration to restore, build, and test.
- Debug references the local [Zongsoft.Core](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Core) build output; Release uses the [NuGet package](https://www.nuget.org/packages/Zongsoft.Core) declared by the project, and the test project follows the main project.
- See the [repository guide](../AGENTS.md#代码规范检查) for code style checks and resource generation.

## Related documents

- [Web configuration guide](docs/web.md): full `web.profile` syntax, Nginx mapping, and installation behavior.
- [Implementation notes](docs/implementation.md): internal design, packaging pipeline, and format-level details.
- [migrator tool](../migrator/README.md): builds migration archives and launcher scripts.

## License

This project is licensed under the [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) license.
