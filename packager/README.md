# The Zongsoft Packaging Tool

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Packager)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Packager)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

README: [English](README.md) | [Simplified Chinese](README-zh_CN.md)

-----

## Overview

[**Z**ongsoft.**T**ools.**P**ackager](https://github.com/Zongsoft/tools/tree/main/packager) is a .NET global tool for creating Linux application installation packages.

It packages an application directory into one of the following formats:

- `.tar.gz`: portable gzip-compressed tarball with an embedded `install.sh` script.
- `.deb`: Debian binary package containing `control.tar.gz` and `data.tar.gz`.
- `.rpm`: RPM package containing metadata headers and a gzip-compressed `cpio` payload.

The tool is designed for published .NET services and command-line applications. It can include a systemd unit file automatically, generate install/uninstall lifecycle scripts, preserve file modes, and resolve variable references from command-line options or environment variables.

## Setup

- List installed tools:

```bash
dotnet tool list
dotnet tool list -g
```

- Install:

```bash
dotnet tool install -g Zongsoft.Tools.Packager
```

- Upgrade:

```bash
dotnet tool update -g Zongsoft.Tools.Packager
```

- Uninstall:

```bash
dotnet tool uninstall -g Zongsoft.Tools.Packager
```

After installation, the command name is:

```bash
dotnet-pack
```

## Basic Usage

```bash
dotnet-pack tar <options...> [entries...]
dotnet-pack deb <options...> [entries...]
dotnet-pack rpm <options...> [entries...]
```

The three subcommands share the same core options. The `rpm` command also supports RPM-specific dependency metadata options.

### Minimal Examples

Create a `.tar.gz` archive from the current directory:

```bash
dotnet-pack tar \
  --name:Zongsoft.Example \
  --version:1.0.0 \
  --platform:linux \
  --framework:net10.0
```

Create a Debian package from a publish directory:

```bash
dotnet-pack deb \
  --name:Zongsoft.Example \
  --title:"Zongsoft Example Service" \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./bin/Release/net10.0/publish \
  --output:./packages \
  --category:utils \
  --summary:"Example service" \
  --description:"A sample service packaged by Zongsoft.Tools.Packager."
```

Create an RPM package with package relationships:

```bash
dotnet-pack rpm \
  --name:Zongsoft.Example \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./bin/Release/net10.0/publish \
  --output:./packages \
  --license:MIT \
  --dependencies:"dotnet-runtime-10.0 >= 10.0" \
  --provides:"zongsoft-example = 1.0.0" \
  --conflicts:"zongsoft-example-legacy"
```

## Package Entries

Package entries are the files that will be written into the package payload.

If no positional entry arguments are specified, the tool recursively includes all files under the source directory:

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 --source:publish
```

If one or more entry arguments are specified, only those paths are included:

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 \
  --source:publish \
  MyApp.dll appsettings.json wwwroot
```

Each entry can optionally specify an alias after the last colon:

```bash
dotnet-pack deb --name:MyApp --version:1.0.0 --platform:linux --framework:net10.0 \
  --source:publish \
  appsettings.Production.json:appsettings.json \
  ../shared/logo.png:assets/logo.png
```

Entry rules:

- Relative paths are resolved from `--source`.
- Absolute paths outside `--source` are allowed; if no alias is specified, their file name is used.
- Directories are included recursively.
- File globbing supports `*` and `?` in the last path segment.
- Duplicate destination paths are reported as conflicts and skipped.
- File modes are preserved on Unix-like hosts. On Windows, executable-looking files such as `.sh`, `.dll`, `.exe`, and extensionless files default to `0755`; other files default to `0644`.

For `.deb` and `.rpm`, entries are stored under the package install path. For `.tar.gz`, entries are stored relative to the archive root and `install.sh` copies them into the install path during installation.

## Command Options

### Required Options

| Option | Description |
| --- | --- |
| `--name:<name>` | Application/package name. Also used to infer default install path and service name. |
| `--version:<version>` | Package version. `0.0.0.0` is rejected. |
| `--platform:<platform>` | Target platform. Supported values include `linux`, `unix`, `osx`, `windows`/`win`, and `unknown`; Linux packages should use `linux`. |
| `--framework:<tfm>` | .NET target framework moniker, for example `net8.0`, `net9.0`, or `net10.0`. |

### Common Options

| Option | Default | Description |
| --- | --- | --- |
| `--source:<path>` | Current directory | Source directory whose files are packaged. |
| `--output:<path>` | Source directory | Output directory for the generated package. Relative paths are resolved under `--source`. |
| `--edition:<name>` | Empty | Optional package edition. It is appended to the package name and used as RPM release when present. |
| `--compilation:<name>` | `Release` | Build configuration used when locating a daemon host file under `bin/<configuration>/<framework>`. |
| `--architecture:<arch>` | `x64` | Target CPU architecture. Common values are `x64`, `x86`, `arm64`, and `arm`. |
| `--overwrite` | `false` | Overwrite an existing output package. Without this switch, an existing package path causes creation to fail. |
| `--install-path:<path>` | `/opt/<vendor>/<package>` or `/opt/<package>` | Installation directory. For names containing dots, the text before the first dot is used as vendor directory. |
| `--title:<text>` | Empty | Human-readable package title, also used in generated systemd descriptions. |
| `--summary:<text-or-file>` | Empty | Short package summary. If the value points to an existing file, the file content is used. |
| `--description:<text-or-file>` | Empty | Long package description. If the value points to an existing file, the file content is used. |
| `--url:<url>` | `https://github.com/Zongsoft` | Project homepage. |
| `--license:<text>` | Empty | License expression or license name. |
| `--category:<text>` | Format-dependent | Debian `Section` or RPM `Group`. Defaults are `utils` for Debian and `Applications/System` for RPM. |
| `--maintainer:<text>` | `Zongsoft Studio <zongsoft@gmail.com>` | Package maintainer/vendor text. |
| `--dependencies:<list>` | Empty | Comma- or semicolon-separated dependency list. Written to Debian `Depends` or RPM `Requires`. |

### systemd and Lifecycle Script Options

The packager currently uses a systemd-oriented script generator for all package formats.

| Option | Description |
| --- | --- |
| `--daemon:<name>` | systemd unit file name or identifier. If omitted, the lower-case package name is used. The `.service` suffix is added when needed. |
| `--daemon-type:<type>` | Use `web` to generate an `ExecStart` with `--urls <bind>`. Other values generate a normal service. |
| `--daemon-bind:<urls>` | URL binding passed to generated web services, for example `http://0.0.0.0:8080`. |
| `--environments:<names>` | Comma- or semicolon-separated environment variable names to copy into the generated service file. |
| `--script-installing:<path>` | Source-relative shell script file run before install. |
| `--script-installed:<path>` | Source-relative shell script file run after install. |
| `--script-uninstalling:<path>` | Source-relative shell script file run before uninstall. |
| `--script-uninstalled:<path>` | Source-relative shell script file run after uninstall. |

If lifecycle scripts are not provided, default scripts are generated to stop the service before install/uninstall, create or update `/etc/systemd/system/<service>`, reload systemd, and enable the service.

### RPM-Only Options

| Option | Description |
| --- | --- |
| `--provides:<list>` | Comma- or semicolon-separated RPM `Provides` entries. Entries may use `name`, `name = version`, `name >= version`, or `name(>= version)` forms. |
| `--conflicts:<list>` | Comma- or semicolon-separated RPM `Conflicts` entries using the same relation syntax. |

## Variables

Option values and entry arguments may reference variables in either of the following forms:

```text
$(name)
%name%
```

Variables are case-insensitive and are loaded from:

1. Environment variables.
2. Declared command options and their default values.
3. Extra command-line options accepted by the command-line parser.

Later values override earlier values with the same name. This makes it convenient to write reusable packaging commands:

```bash
dotnet-pack deb \
  --name:%APP_NAME% \
  --version:%APP_VERSION% \
  --platform:linux \
  --framework:net10.0 \
  --source:./bin/%compilation%/%framework%/publish
```

Frequently used variables include:

| Variable | Meaning |
| --- | --- |
| `name` | Package/application name. |
| `version` | Package version. |
| `edition` | Optional edition. |
| `platform` | Target platform. |
| `architecture` | Target architecture. |
| `framework` | Target .NET framework. |
| `compilation` | Build configuration. |
| `source` | Normalized source directory. |
| `output` | Normalized output directory. |
| `RuntimeIdentifier` | Runtime identifier inferred from platform and architecture. |

## systemd Service Generation

When a package is created, the tool ensures that a systemd unit file is present in the package entries.

Service resolution order:

1. If a file named by `--daemon` exists under `--source`, it is used.
2. Otherwise a temporary `.service` file is generated.

To generate a service, the tool locates the .NET host file in this order:

1. `<source>/<name>.dll`
2. `<source>/bin/<compilation>/<framework>/<name>.dll`
3. The only `.exe` file in `<source>`; the service starts the matching `.dll`.
4. The only `.exe` file in `<source>/bin/<compilation>/<framework>`; the service starts the matching `.dll`.

A normal generated service starts:

```ini
ExecStart=dotnet <install-path>/<host>.dll
```

A web generated service starts:

```ini
ExecStart=dotnet <install-path>/<host>.dll --urls <daemon-bind>
```

## Output Naming

Generated package names use the following convention:

```text
<name>@<version>_<runtime>.<extension>
<name>-<edition>@<version>_<runtime>.<extension>
```

Examples:

```text
Zongsoft.Example@1.0.0_linux-x64.tar.gz
Zongsoft.Example@1.0.0_linux-x64.deb
Zongsoft.Example-enterprise@1.0.0_linux-x64.rpm
```

The package metadata name is `<name>` or `<name>-<edition>`.

## Format Notes

### `.tar.gz`

The tar package contains:

- Application files.
- Optional lifecycle scripts under `.install/`.
- `install.sh`, an installer script that supports install and uninstall flows.

Install:

```bash
tar -xzf Zongsoft.Example@1.0.0_linux-x64.tar.gz
sudo ./install.sh
```

Install into a staging directory:

```bash
DESTDIR=/tmp/stage ./install.sh
```

Uninstall:

```bash
sudo ./install.sh uninstall
```

### `.deb`

The Debian package is written directly by the tool and contains:

```text
debian-binary
control.tar.gz
data.tar.gz
```

The control archive contains `control` plus optional maintainer scripts:

```text
preinst
postinst
prerm
postrm
```

Install and inspect:

```bash
sudo dpkg -i Zongsoft.Example@1.0.0_linux-x64.deb
dpkg-deb --info Zongsoft.Example@1.0.0_linux-x64.deb
dpkg-deb --contents Zongsoft.Example@1.0.0_linux-x64.deb
```

### `.rpm`

The RPM package is written directly by the tool. Its payload is a gzip-compressed `cpio` archive and its metadata header includes package name, version, release, scripts, dependencies, file names, file modes, file digests, and install prefix.

Install and inspect:

```bash
sudo rpm -Uvh Zongsoft.Example@1.0.0_linux-x64.rpm
rpm -qip Zongsoft.Example@1.0.0_linux-x64.rpm
rpm -qlp Zongsoft.Example@1.0.0_linux-x64.rpm
```

## Recommended Packaging Flow

1. Publish your application:

```bash
dotnet publish ./src/MyApp/MyApp.csproj -c Release -f net10.0 -o ./publish
```

2. Package it:

```bash
dotnet-pack deb \
  --name:MyCompany.MyApp \
  --title:"MyApp Service" \
  --version:1.0.0 \
  --platform:linux \
  --architecture:x64 \
  --framework:net10.0 \
  --source:./publish \
  --output:./packages \
  --daemon:myapp.service \
  --daemon-type:web \
  --daemon-bind:http://0.0.0.0:5000 \
  --environments:ASPNETCORE_ENVIRONMENT \
  --ASPNETCORE_ENVIRONMENT:Production
```

3. Inspect the package before distribution:

```bash
dpkg-deb --info ./publish/packages/MyCompany.MyApp@1.0.0_linux-x64.deb
dpkg-deb --contents ./publish/packages/MyCompany.MyApp@1.0.0_linux-x64.deb
```

## Build From Source

Restore and build:

```bash
dotnet restore Zongsoft.Tools.Packager.slnx
dotnet build Zongsoft.Tools.Packager.slnx -c Release
```

With Cake:

```bash
dotnet cake --target=build --edition=Release
```

## Troubleshooting

- `The source directory '<path>' does not exist.`
  The `--source` path was not found after variable expansion and path normalization.

- `The daemon host location failed.`
  No existing service file was found and the tool could not locate a host `.dll` or a single `.exe` from which to infer the host name.

- `The version number is invalid.`
  The version value is empty, invalid, or resolves to `0.0.0.0`.

- `The source path '<path>' does not exist.`
  An entry argument did not match an existing file, directory, or glob.

- Package already exists.
  Re-run with `--overwrite` or choose another `--output` directory.

## More Details

See [implementation.md](implementation.md) for the internal design, generation pipeline, and package format details.

## License

This project is licensed under the [MIT](https://github.com/Zongsoft/tools/blob/main/LICENSE) license.
