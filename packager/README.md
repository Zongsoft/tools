# Zongsoft Packaging Tool

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-pack` is a .NET global tool that turns a published application directory into Linux-friendly installation packages: `.tar.gz`, `.deb`, and `.rpm`.

It is designed for .NET services and command-line applications that need repeatable packaging without shelling out to `tar`, `dpkg-deb`, `rpmbuild`, or `cpio` at generation time.

## Quick Links

- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Commands](#commands)
- [Package Entries](#package-entries)
- [systemd Services](#systemd-services)
- [Lifecycle Scripts](#lifecycle-scripts)
- [Variables](#variables)
- [Package Formats](#package-formats)
- [Troubleshooting](#troubleshooting)
- [Implementation Notes](docs/implementation.md)

## Features

- Generates `.tar.gz`, `.deb`, and `.rpm` packages from one command-line interface.
- Uses one shared package model for metadata, variables, file entries, service scripts, and output naming.
- Creates systemd service files automatically when no service file is supplied.
- Generates install and uninstall lifecycle scripts for all supported formats.
- Preserves Unix file modes when packaging on Unix-like hosts.
- Provides conservative executable mode defaults when packaging from Windows.
- Supports environment and command variables with `$(name)` and `%name%` syntax.
- Supports explicit file entries, recursive directories, last-segment globbing, aliases, and root-level aliases such as `/etc/nginx/conf.d/zongsoft.web.conf`.
- Writes package formats directly in .NET:
  - `.tar.gz` uses gzip-compressed PAX tar.
  - `.deb` uses an `ar` container with `control.tar.gz` and `data.tar.gz`.
  - `.rpm` uses RPM lead/header metadata with a gzip-compressed `newc` cpio payload.

## Installation

Install as a .NET global tool:

```bash
dotnet tool install -g Zongsoft.Tools.Packager
```

Update an existing installation:

```bash
dotnet tool update -g Zongsoft.Tools.Packager
```

Check the installed tool:

```bash
dotnet tool list -g
dotnet-pack
```

Uninstall:

```bash
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

## Quick Start

The examples use the real [Zongsoft.Hosting.Web](https://github.com/Zongsoft/hosting/tree/main/web/default) host in `D:/Zongsoft/hosting`. First follow the host's [deployment workflow](https://github.com/Zongsoft/hosting/blob/main/web/default/deploy.cmd) to prepare the application and its plugins. Run the script from a Windows console, set remote debug to `off` (Release), then select Linux, x64 and net10.0, and choose `exit` at the packaging prompt if you only want to prepare the host:

```cmd
cd /d D:\Zongsoft\hosting\web\default
deploy.cmd
```

The following Bash commands run from the hosting checkout root (use its mounted path under WSL). Collect the prepared host into a fresh `./publish` staging directory. The project excludes `plugins/` from build content, so the deployed plugins and host configuration must be included explicitly:

```bash
mkdir -p ./publish
cp -a ./web/default/bin/Release/net10.0/. ./publish/
cp -a ./web/default/plugins ./publish/
cp -a ./web/default/wwwroot ./publish/
cp ./web/default/appsettings.json ./web/default/web.config ./web/default/web*.option ./publish/
cp ./mime ./publish/
```

The package examples use `1.0.0` as the release version; set it to your actual release version. Keep `--name:Zongsoft.Hosting.Web`, `--title:Zongsoft.Web` and `--daemon:zongsoft.web` together, as in the host's [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd). The DLL is `Zongsoft.Hosting.Web.dll`, the package/service identifier is `zongsoft.web`, and the installation directory is `/opt/zongsoft/web`.

`--output:../packages/` is resolved relative to `./publish`, so packages are written to `./packages` under the hosting checkout. The examples below are alternatives; use a different output directory or `--overwrite` when repeating the same package format.

Create a Debian package:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --summary:"Zongsoft plugin-based Web host" \
  --description:"Hosts ASP.NET applications built with Zongsoft plugins."
```

The generated package name follows this pattern:

```text
<name>@<version>_<runtime>.<extension>
<name>-<edition>@<version>_<runtime>.<extension>
```

When `--daemon:<name>` is supplied and is not disabled, the daemon identifier is preferred for the generated package name:

```text
<daemon>@<version>_<runtime>.<extension>
<daemon>-<edition>@<version>_<runtime>.<extension>
```

Example:

```text
zongsoft.web@1.0.0_linux-x64.deb
```

## Commands

```bash
dotnet-pack tar <options...> [entries...]
dotnet-pack deb <options...> [entries...]
dotnet-pack rpm <options...> [entries...]
```

The three subcommands share the same common options. `rpm` adds package relationship options for RPM metadata.

### Examples

Create a portable tarball:

```bash
dotnet-pack tar \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

Create a Debian package that includes the host's real [Nginx configuration](https://github.com/Zongsoft/hosting/blob/main/.deploy/default/nginx/zongsoft.web.conf) under `/etc/nginx/conf.d`. This configuration forwards requests to the generated service's port `8069`; adjust its site settings for your environment:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --category:utils \
  . \
  ../.deploy/default/nginx/zongsoft.web.conf:/etc/nginx/conf.d/zongsoft.web.conf
```

Create an RPM package with dependency metadata:

```bash
dotnet-pack rpm \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --license:MIT \
  --dependencies:"aspnetcore-runtime-10.0 >= 10.0" \
  --provides:"zongsoft.web = 1.0.0"
```

Disable systemd generation and only package files:

```bash
dotnet-pack tar \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

### Required Options

| Option | Description |
| --- | --- |
| `--name:<name>` | Application/package name. Also used to locate the .NET host assembly for generated services. |
| `--version:<version>` | Package version. `0.0.0.0` is rejected. |
| `--platform:<platform>` | Target platform. Supported enum values include `linux`, `unix`, `osx`, `windows`/`win`, and `unknown`; Linux packages should use `linux`. |
| `--framework:<tfm>` | Target framework moniker, for example `net8.0`, `net9.0`, or `net10.0`. |

### Common Options

| Option | Default | Description |
| --- | --- | --- |
| `--source:<path>` | Current directory | Source directory whose files are packaged. |
| `--output:<path>` | Source directory | Output path for the generated package, if the value ends with a directory separator(`/` or `\`), it is a directory; otherwise, it is a file. Relative paths are resolved under `--source`. |
| `--exclude:<patterns>` | Empty | Comma- or semicolon-separated file patterns to skip while loading package entries. |
| `--edition:<name>` | Empty | Optional edition. Appended to package name; used as RPM release when present. |
| `--compilation:<name>` | `Release` | Build configuration used when locating a daemon host under `bin/<configuration>/<framework>`. |
| `--architecture:<arch>` | `x64` | Target CPU architecture, such as `x64`, `x86`, `arm64`, or `arm`. |
| `--overwrite` | `false` | Replace an existing package file. Without this switch, an existing file causes creation to fail. |
| `--install-path:<path>` | `/opt/<identity with dots replaced by />` | Linux installation directory. The identity is lowercased and every dot becomes a directory separator; for example, `Zongsoft.Hosting.Web` becomes `/opt/zongsoft/hosting/web`. With `--daemon:zongsoft.web`, the path is `/opt/zongsoft/web`. If `--daemon` is supplied and not disabled, its identifier is used instead of `--name` for the default path. |
| `--title:<text>` | Empty | Human-friendly package title and generated systemd description. |
| `--summary:<text-or-file>` | Empty | Short package summary. If the value is an existing file path, the file content is used. |
| `--description:<text-or-file>` | Empty | Long package description. If the value is an existing file path, the file content is used. |
| `--url:<url>` | `https://github.com/Zongsoft` | Project homepage. |
| `--license:<text>` | Empty | License expression or license name. |
| `--category:<text>` | Format default | Debian `Section` or RPM `Group`; defaults to `utils` for Debian and `Applications/System` for RPM. |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | Package maintainer/vendor text. |
| `--dependencies:<list>` | Empty | Comma- or semicolon-separated dependency list. Written to Debian `Depends` or RPM `Requires`. |

### RPM Options

| Option | Description |
| --- | --- |
| `--provides:<list>` | Comma- or semicolon-separated RPM `Provides` entries. |
| `--conflicts:<list>` | Comma- or semicolon-separated RPM `Conflicts` entries. |

RPM relationship entries may use `name`, `name = version`, `name >= version`, `name <= version`, `name > version`, `name < version`, or `name(>= version)` forms.

## Package Entries

Package entries are the files written into the package payload.

If no positional entry arguments are supplied, every file under `--source` is included recursively:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

If positional entries are supplied, only those files or directories are included. This example selects the host assemblies, runtime configuration, application settings, plugins and MIME definitions:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  "*.dll" \
  Zongsoft.Hosting.Web.deps.json \
  Zongsoft.Hosting.Web.runtimeconfig.json \
  appsettings.json \
  "web*.config" \
  "web*.option" \
  plugins \
  wwwroot \
  mime
```

Each entry can specify a destination alias after the last colon. The following partial file package uses existing files from the hosting checkout to demonstrate aliases; service generation is disabled for this example:

```bash
dotnet-pack deb \
  --name:zongsoft.hosting.web \
  --daemon:none \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  ../web/README.md:docs/hosting-web.md \
  ../zongsoft-logo.png:assets/logo.png \
  ../.deploy/default/nginx/zongsoft.web.conf:/etc/nginx/conf.d/zongsoft.web.conf
```

Entry rules:

- Relative paths are resolved from `--source`.
- Absolute paths outside `--source` are allowed; when no alias is supplied, only the file name is used.
- Directories are included recursively.
- Globbing supports `*` and `?` in the last path segment.
- `--exclude` skips matching files while loading entries. Patterns are relative to `--source`, use `/` as the normalized separator, support `*`, `?`, and `**`, and may be separated by commas or semicolons.
- Duplicate destination paths are reported as conflicts and skipped.
- Aliases beginning with `/` or `\` are root-level entries. In `.deb` and `.rpm`, they are installed at that root path. In `.tar.gz`, they are stored under `.root/` and copied by `install.sh`.
- Unix hosts preserve file permissions. Windows hosts assign `0755` to `.sh`, `.dll`, `.exe`, and extensionless files; other files use `0644`.

Exclude examples:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --exclude:"*.pdb;*.xml;logs/**"
```

## systemd Services

By default, every package uses the systemd script generator.

Service resolution order:

1. Use the file named by `--daemon:<name>` if it exists under `--source`.
2. Otherwise generate `<daemon>.service`.
3. If `--daemon` is omitted, use the lower-case package name as the service identifier.

Disable service generation with one of:

```bash
--daemon:none
--daemon:disable
--daemon:disabled
```

When a service file must be generated, the tool locates the .NET host in this order:

1. `<source>/<name>.dll`
2. `<source>/bin/<compilation>/<framework>/<name>.dll`
3. The only `.exe` in `<source>`, converted to the matching `.dll` name.
4. The only `.exe` in `<source>/bin/<compilation>/<framework>`, converted to the matching `.dll` name.

Generated services run:

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll
```

If `--daemon-bind:<value>` is supplied, the generated service passes it as `--urls`. A numeric value is treated as a local HTTP port:

```bash
--daemon-bind:8069
```

Generates:

```ini
ExecStart=dotnet /opt/zongsoft/web/Zongsoft.Hosting.Web.dll --urls http://127.0.0.1:8069
```

The Web host's `pack.cmd` passes both `Environment` and `ASPNETCORE_ENVIRONMENT` into the generated service. For example:

```bash
dotnet-pack deb \
  --name:Zongsoft.Hosting.Web \
  --title:Zongsoft.Web \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/ \
  --daemon-environments:Environment,ASPNETCORE_ENVIRONMENT \
  --Environment:Production \
  --ASPNETCORE_ENVIRONMENT:Production
```

## Lifecycle Scripts

Lifecycle scripts can be supplied as source-relative file paths, absolute file paths, or inline script text.

| Option | Runs |
| --- | --- |
| `--installing:<script>` | Before installation. |
| `--installed:<script>` | After installation. |
| `--uninstalling:<script>` | Before uninstall/removal. |
| `--uninstalled:<script>` | After uninstall/removal. |

Each main hook can be extended with pre/post snippets:

| Option | Runs |
| --- | --- |
| `--preinstalling:<paths>` / `--postinstalling:<paths>` | Around `installing`. |
| `--preinstalled:<paths>` / `--postinstalled:<paths>` | Around `installed`. |
| `--preuninstalling:<paths>` / `--postuninstalling:<paths>` | Around `uninstalling`. |
| `--preuninstalled:<paths>` / `--postuninstalled:<paths>` | Around `uninstalled`. |

Multiple pre/post script paths can be separated with `;` or `|`.

If no scripts are supplied, defaults are generated. For systemd packages they stop the service before install/removal, create or remove the `/etc/systemd/system/<service>` symlink, reload systemd, enable the service after installation, and remove the install directory after uninstallation.

Package-manager upgrades do not run the uninstall lifecycle. Debian `prerm`/`postrm` scripts are guarded by their action argument, and RPM `%preun`/`%postun` scripts run only when the final installed package instance is removed. This prevents an old package's removal scripts from deleting the newly installed payload during an upgrade or same-version reinstall. Tar packages keep the explicit `install.sh`/`uninstall.sh` lifecycle, and their generated uninstaller removes only the resolved `TARGET` path.

## Variables

Option values and entry arguments may reference variables in either form:

```text
$(name)
%name%
```

Variables are case-insensitive and are loaded from:

1. Environment variables.
2. Declared command options and their default values.
3. Extra command-line options accepted by the command parser.

When the same variable name appears more than once, the current implementation keeps the first value it sees. Avoid defining environment variables with the same names as package options unless that is intentional.

```bash
export APP_NAME=Zongsoft.Hosting.Web
export APP_VERSION=1.0.0

dotnet-pack deb \
  --daemon:zongsoft.web \
  --daemon-bind:8069 \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:../packages/
```

Common variables:

| Variable | Meaning |
| --- | --- |
| `name` | Package/application name. |
| `version` | Package version. |
| `edition` | Optional edition. |
| `platform` | Target platform. |
| `architecture` | Target architecture. |
| `framework` | Target framework. |
| `compilation` | Build configuration. |
| `source` | Normalized source directory. |
| `output` | Normalized output directory. |
| `RuntimeIdentifier` | Runtime identifier inferred from platform and architecture. |

## Package Formats

### `.tar.gz`

The tar command generates a `.tar.gz` archive and a same-named `.sh` installer script. The tar package contains application files, optional root-level entries under `.root/`, and executable `install.sh` and `uninstall.sh` scripts. Lifecycle scripts are merged into `install.sh` and `uninstall.sh`.

One-step install:

```bash
sudo sh ./packages/zongsoft.web@1.0.0_linux-x64.sh
```

Install:

```bash
tar -xzf ./packages/zongsoft.web@1.0.0_linux-x64.tar.gz
sudo ./install.sh
```

Install into a staging directory:

```bash
DESTDIR=/tmp/stage ./install.sh
```

Override the install path:

```bash
sudo env INSTALL_PATH=/srv/zongsoft/web ./install.sh
```

Uninstall:

```bash
cd /opt/zongsoft/web
sudo ./uninstall.sh
```

### `.deb`

The Debian package contains:

```text
debian-binary
control.tar.gz
data.tar.gz
```

Inspect and install:

```bash
dpkg-deb --info ./packages/zongsoft.web@1.0.0_linux-x64.deb
dpkg-deb --contents ./packages/zongsoft.web@1.0.0_linux-x64.deb
sudo dpkg -i ./packages/zongsoft.web@1.0.0_linux-x64.deb
```

Root-level entries under `/etc/` are also written to Debian `conffiles` metadata.

### `.rpm`

The RPM package contains RPM lead/signature/header metadata plus a gzip-compressed `newc` cpio payload.

Inspect and install:

```bash
rpm -qip ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qlp ./packages/zongsoft.web@1.0.0_linux-x64.rpm
rpm -qp --scripts ./packages/zongsoft.web@1.0.0_linux-x64.rpm
sudo rpm -Uvh ./packages/zongsoft.web@1.0.0_linux-x64.rpm
```

Root-level entries under `/etc/` are marked as RPM configuration files.

## Build From Source

Run the following commands from the tools repository's `packager` directory.

Restore and build:

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```

Build with Cake:

```bash
dotnet cake --target=build --edition=Release
```

Run tests through the Cake script:

```bash
dotnet cake --target=test --edition=Release
```

## Troubleshooting

`The source directory '<path>' does not exist.`

The `--source` value was not found after variable expansion and path normalization.

`The daemon host location failed.`

No existing service file was found and the tool could not locate a host `.dll` or a single `.exe` from which to infer the `.dll` name. Supply `--daemon:<service-file>` or disable service generation with `--daemon:none`.

`The version number is invalid.`

The version value is missing, invalid, or resolves to `0.0.0.0`.

`The source path '<path>' does not exist.`

A positional entry did not match an existing file, directory, or glob.

Package file already exists.

Re-run with `--overwrite` or choose another `--output` directory.

## More Details

See [docs/implementation.md](docs/implementation.md) for the internal design, packaging pipeline, and format-level implementation notes.

## License

This project is licensed under the [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) license.
