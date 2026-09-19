# The Zongsoft Deployment Tool

![License](https://img.shields.io/github/license/Zongsoft/Zongsoft.Tools.Deployer)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Deployer)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Deployer)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/Zongsoft.Tools.Deployer?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

-----

## Abstraction

This is an application deployment tool that instructs the deployment tool to copy specific files to the destination location by specifying the deployment file.

It is recommended to define a default deployment file named `.deploy` in the deployment project directory, and the deployment file is a plain text file in `.ini` format.

## Format Specification

The deployment file is a plain text file in `.ini` format, and its content consists of **Section**(`Paragraph`) and **Entry**(`Entry`) enclosed in square brackets, the **Section** part represents the destination directory of deployment.

The **Section** and **Entry** values both support variable references in the format of dollar sign followed by parentheses `$(...)` or double percent signs `%...%`, the referenced variable is the deployment options passed in by the command line or environment variables.

Each entry consists of **KEY** and **VALUE** parts separated by an equal sign _(`=`)_, and the **VALUE** part is optional.

- The **KEY** part consists of _Parser-Name_ and _Parser-Argument_, separated by a colon _(`:`)_;
	- **_Parser-Name_**: Omit it, leave it empty, or specify `path` to use the default path parser. Other parsers are `nuget` and `delete`/`remove`. Parser names are case-insensitive.
	- **_Parser-Argument_**: Parsed by the specified parser, please refer to _**P**arser **A**rgument_ below for details.

- The **VALUE** part consists of _Destination_ and _Filtering_.
	- **_Destination_**: Indicates the destination path for deployment. If missing, the destination directory is specified by the **Section**, and the destination file name is the same as the source file.
	- **_Filtering_**: Indicates the preconditions for parsing, please refer to _**F**iltering_ below for details.

### Parser Argument

#### Path Parser

The default parser is named `path`, and its name can be omitted. It copies the source file indicated by the _Parser-Argument_ to the destination location.

The _Parser-Argument_ represents the path of the source file to be deployed, the source file path supports `*`, `?` and `**` wildcards, the `**` means multi-level directory matching.

For an absolute Windows source path containing a drive-letter colon, use the `path:` prefix, for example `path:D:\dir\files.ext` or `path:D:/dir/files.ext`. Otherwise, `D` in `D:/...` is interpreted as a resolver name. Alternatively, use a path relative to the deployment file or expand the absolute path from a variable.

```ini
[plugins zongsoft data]
path:D:/Zongsoft/framework/Zongsoft.Data/src/Zongsoft.Data.plugin

[plugins zongsoft data mysql]
drivers/mysql/src/Zongsoft.Data.MySql.plugin
```

The `path:` prefix selects the default path resolver; the source path starts after its colon. Relative paths can also use this prefix, such as `path:drivers/mysql/src/Zongsoft.Data.MySql.plugin`. This example references existing plugin files in [framework](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Data) and assumes the deployment file is located in `D:/Zongsoft/framework/Zongsoft.Data`. Adjust the checkout path for your environment.

> 💡 **Tip:** You should generally avoid absolute paths in `.deploy` files. Source paths are resolved relative to the directory containing the deployment file, while destination paths are resolved relative to the host directory or the target directory specified by the deployment options. See the deployment files in [framework](https://github.com/Zongsoft/framework), such as [Zongsoft.Data.deploy](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Data/src/Zongsoft.Data.deploy), for examples.

#### Delete Parser

The parser name is `delete` or `remove`, which means delete the specified destination file.

The _Parser-Argument_ represents the destination file to be deleted, and the full path of the destination file is a combination of the directory specified in the **Section** and the _Parser-Argument_.

> 🚨 **Note:** This parser does not support the _Destination_ part, so this part cannot be defined.

##### Examples

Delete the `Zongsoft.Messaging.Mqtt.option` file from the `~/plugins/zongsoft/messaging/mqtt` directory in the target location.

```ini
[plugins zongsoft messaging mqtt]
nuget:Zongsoft.Messaging.Mqtt
delete:Zongsoft.Messaging.Mqtt.option
```

> 💡 **Tip:** The deployment file in the `nuget:Zongsoft.Messaging.Mqtt` package in the example contains a default configuration file(i.e. `Zongsoft.Messaging.Mqtt.option`), but it is not needed in the real project, so the configuration file is removed later.

#### NuGet Parser

The parser name is: `nuget`, which means download the NuGet package and perform the deployment, and that the dependencies of the specified package are also downloaded.

The format of _Parser-Argument_: `package@version/path`, where `@version` and `/{path}` parts are optional.
- An unspecified version or `latest` selects the latest stable release. Use `--prerelease:true` to include preview releases. An explicitly requested prerelease version is still accepted.
- If the path part is unspecified:
	- If the root directory contains a `.deploy` file, execute that manifest without additionally selecting default assets or downloading unused dependencies;
	- Otherwise resolve the dependency closure and select the nearest assets: use a compatible RID managed runtime group in preference to `lib/{framework}`, plus native and eligible content assets.
		> The `{framework}` indicates the version of the *target framework* nearest to the one declared by the `$(Framework)` variable.

> 💡 **Tip:** _**Z**ongsoft_'s NuGet package usually has a deployment file named `.deploy` in it's root directory, and the `artifacts` directory in the package includes its plugin files(`*.plugin`)_(required, one or more)_, configuration files(`*.option`), the mapping files(`*.mapping`) for [_**Z**ongsoft.**D**ata_ ORM](https://github.com/Zongsoft/framework/tree/main/Zongsoft.Data), and other ancillary files.

> 💡 **Note:** The variable named `NuGet_Server` defines the NuGet package source for this parser.
> If undefined then `https://api.nuget.org/v3/index.json` is used as its default value.

##### Dependency Packages

_**N**uget_ by default ignores dependency packages whose names begin with `System.`, `Microsoft.Extensions.`, or `Zongsoft.`;
You can also specify the prefixes of dependency packages to ignore using the `--ignoreDependentPrefix` command-line option, multiple prefixes are separated by a comma(`,`), a semicolon(`;`), or a pipe(`|`). Matching is case insensitive and affects dependency edges, not explicitly requested root packages.

##### Examples

- Get the latest version of the `Zongsoft.Plugins` NuGet package and deploy the `Main.plugin` plugin file in it's `/plugins` directory to the destination `~/plugins` directory.
	> ```ini
	> [plugins]
	> nuget:Zongsoft.Plugins/plugins/Main.plugin
	> ```

- Get the `6.2.0` version of the `Zongsoft.Data` NuGet package and execute the `.deploy` deployment file in the package.
	> ```ini
	> [plugins zongsoft data]
	> nuget:Zongsoft.Data@6.2.0
	> nuget:Zongsoft.Data@6.2.0/.deploy
	> ```
	> **Note:** Since the root directory of the `Zongsoft.Data` package contains the `.deploy` file, the above two writing styles have the same effect.

- Deploy version 8.3.0 of the MySql. _(Assuming the value of the `Framework` variable is `net8.0`)_
	> ```ini
	> nuget:MySql.Data@8.3.0
	> ```

	> 1. First download the `MySql.Data@8.3.0` package and its dependencies _(ignore dependencies starting with `System.` and `Microsoft.Extensions.`)_:
	> ```
	> BouncyCastle.Cryptography     2.2.1
	> Google.Protobuf               3.25.1
	> K4os.Compression.LZ4.Streams  1.3.5
	> ZstdSharp.Port                0.7.1
	> ```
	> 2. Get the library files in the above dependency packages that are nearest to the `net8.0` *Target Framework* version specified by the `Framework` variable.
	> 3. Copy the library files from the downloaded NuGet package to the destination directory.

### Filtering

The part enclosed by `<` and `>` at the end of the entry is the filter condition, and entries that do not meet the filter criteria will be ignored.

Multiple conditions are supported. Each condition consists of a variable name and the comparison values, If the variable name starts with `!`, it means that the matching result of the condition is negated; If you are comparing multiple values, separate them with commas. As follows:

```plaintext
../.deploy/$(scheme)/options/app.$(environment).option       = web.option    <application>
../.deploy/$(scheme)/options/app.$(environment).option       = web.option    <!application>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <preview:A,B,C>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <!preview:X,Y,Z>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <application | debug:on>
../.deploy/$(scheme)/options/app.$(environment)-debug.option = web.option    <!application & !debug:on>
```

> 1. `<application>` means that there is a variable named `application` (*Regardless of its content*), then the result is true.
> 2. `<!application>` means that there is no variable named `application` (*Regardless of its content*), then the result is true.
> 3. `<preview:A,B,C>` means that the value of the variable named `preview` is any one of "`A`, `B`, `C`" (*Ignoring case*), then The result is true.
> 4. `<!preview:X,Y,Z>` means that the value of the variable named `preview` is not any one of "`X`, `Y`, `Z`" (*Ignoring case*), then the result is true.
> 5. `<application | debug:on>` indicates that there is a variable named `application` (*Regardless of its content*) **OR** a variable named `debug` is `on` (*Ignoring case*), the result is true.
> 6. `<!application & !debug:on>` means that there is no variable named `application` (*Regardless of its content*) **AND** the variable named `debug` is not `on`(*Ignoring case*), the result is true.

Supports matching and version comparison of *TargetFramework*. If *TargetFramework* ends with `^`, it means that the version of the current deployment *TargetFramework* must be greater than or equal to this version, as follows:

```plaintext
%NUGET_PACKAGES%/mysql.data/8.1.0/lib/netstandard2.1/*.dll     <framework:net7.0^>
%NUGET_PACKAGES%/mysql.data/6.10.9/lib/netstandard2.0/*.dll    <framework:net5.0,net6.0>
```

## Variables

This tool will sequentially load the environment variables, the contents of the `appsettings.json` file of the deployed application, and the command options for calling this tool into the variable set. If the variable has the same name, the value loaded later will overwrite the value of the variable with the same name loaded before. **Note:** Variable names are not case sensitive.

- If a property named `ApplicationName` is defined in `appsettings.json`, you can use `application` as a variable alias for that property.
- The variable named `Framework` represents the .NET *TargetFramework* identity, which is defined in https://learn.microsoft.com/en-us/dotnet/standard/frameworks

NuGet-related parameters can be specified via command options or environment variables:
- `NuGet_Server` indicates the NuGet server information, the default value is: `https://api.nuget.org/v3/index.json`.
- `NuGet_Packages` indicates the directory of NuGet packages, the default value is: `%USERPROFILE%/.nuget/packages`.

## Setup

- List tools

```bash
dotnet tool list
dotnet tool list -g
```

- Install tool

```bash
dotnet tool install -g zongsoft.tools.deployer
```

- Upgrade tool

```bash
dotnet tool update -g zongsoft.tools.deployer
```

- Uninstall tool

```bash
dotnet tool uninstall -g zongsoft.tools.deployer
```

### Installing a local source build for testing

Run `dotnet cake --edition Release` to build Release, run the .NET 8/9/10 regression suites, and generate the local tool package without pushing to NuGet. Restore, build, and tests use the same configuration; Debug references the local Core DLL and Release uses the declared Core NuGet package.

Install the generated `.nupkg` directly without publishing it to NuGet.org. The following commands use the .NET 10 SDK and run from `D:/Zongsoft/tools/deployer`; use the corresponding directory for another checkout location.

The deployer enables `GeneratePackageOnBuild`, so a Release build also creates the tool package:

```powershell
dotnet build src/Zongsoft.Tools.Deployer.csproj -c Release
```

After the build succeeds and `src/bin/Release/Zongsoft.Tools.Deployer.7.11.0.nupkg` exists, install it for the first time:

```powershell
dotnet tool install -g Zongsoft.Tools.Deployer --version 7.11.0 --source ./src/bin/Release --no-http-cache
```

If the tool is already installed, especially when rebuilding the same version, uninstall it first, then repeat the local installation command above:

```powershell
dotnet tool uninstall -g Zongsoft.Tools.Deployer
```

The example version `7.11.0` matches the current project; adjust it to the actual `.nupkg`. `--source` restricts installation to the local directory, avoiding a same-named package from NuGet.org; `--no-http-cache` disables the download cache. See the [.NET tool installation reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install). Check the installed version with `dotnet tool list -g`. Here, “local” describes the package source; `-g` still replaces the current user’s global tool. Do not run the Cake `pack` task for local testing: it pushes packages to NuGet.org.

## Deploy

- Execute the default deployment in the host(target) directory:
```bash
dotnet deploy --edition:Debug --framework:net10.0 --platform:win --architecture:x64
```

- If the host(target) directory does not have a default deployment file (`.deploy`), you must manually specify the deployment file name (multiple deployment files are supported). The following example assumes `Zongsoft.Data@6.2.0` has been downloaded and extracted into the NuGet package directory:
```bash
dotnet deploy --edition:Debug --framework:net10.0 --platform:win --architecture:x64 "%NUGET_PACKAGES%/zongsoft.data/6.2.0/.deploy"
```

- For the convenience of deployment, you can create a corresponding edition of the deployment script files in the host(target) project, for example:
	- deploy-debug.cmd
		> `dotnet deploy --edition:Debug --framework:net10.0 --platform:linux --architecture:x64`
	- deploy-release.cmd
		> `dotnet deploy --edition:Release --framework:net10.0 --platform:linux --architecture:x64`

### Command options

The command supports redirected or piped output without a PTY. Exit codes are 0 for success, 1 for validation/dependency/I/O failure, and 130 for cancellation. The complete plan is validated before target writes; skipped copies and deletions have separate counts.

- `verbosity` option
	- `quiet` Displays only the necessary output information, usually only error messages.
	- `normal` Displays warning and error messages, if this command option is not specified, it is the default.
	- `detail` Displays all output messages, this option can be enabled when troubleshooting.
- `overwrite` option
	- `alway` Always copy and overwrite the destination file.
	- `never` Copies the destination file only if it does not exist.
	- `newest` Deploys file copying only if the last modification time of the source file is later than or equal to the last modification time of the destination file. if this command option is not specified, it is the default.
- `destination` option
	> The specified deployment destination directory. If this command option is not specified, it defaults to the current directory.

### NuGet Packages

Ordinary NuGet roots in one command share a dependency graph. Explicit root versions stay fixed; dependencies use the lowest available version satisfying all constraints. Unsatisfiable ranges, cycles, downgrade constraints, and conflicting package assets at the same target fail before writes. This strict deployment resolver does not execute MSBuild/buildTransitive and is not a full substitute for dotnet restore. Separate directories do not imply separate assembly load contexts; use separate calls only for genuinely isolated hosts. Package manifests and explicit package paths expand as file requests.

Identical package assets at the same destination are copied once; duplicate origins remain in the plan and are counted as skipped. An explicit delete ends the prior deduplication interval. DLLs are not moved or merged across plugin directories.

Explicit library paths inside the `NuGet_Packages` cache select the nearest applicable framework directory; `nuget:package@version/lib/framework/file` follows the same rule. A framework specified in the path takes precedence, such as `lib/net9.0/*.dll`; `lib/*.dll` uses the `Framework` variable. Matching preserves subsequent subpaths and the directory structure produced by wildcard expansion. Paths outside the cache root are not adjusted.

#### Nearest Matching

Assuming the `Framework` variable is `net9.0`, when a deployment file has the following deployment items:
```ini
%NUGET_PACKAGES%/mysql.data/8.3.0/lib/net9.0/*.dll
```

However, the above package library directory does not contains the `net9.0` framework version, so the tool will use the library file that is most applicable(*nearest*) to that framework version. The path will be redirected to:
```ini
%NUGET_PACKAGES%/mysql.data/8.3.0/lib/net8.0/*.dll
```

## Others

### Reference examples

- The NuGet Packages
	- [`Zongsoft.Data.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Data/src/Zongsoft.Data.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data)
	- [`Zongsoft.Data.MySql.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Data/drivers/mysql/src/Zongsoft.Data.MySql.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Data.MySql)
	- [`Zongsoft.Security.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Security/src/Zongsoft.Security.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security)
	- [`Zongsoft.Security.Web.deploy`](https://github.com/Zongsoft/framework/blob/main/Zongsoft.Security/api/Zongsoft.Security.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Security.Web)
	- [`Zongsoft.Administratives.deploy`](https://github.com/Zongsoft/administratives/blob/main/src/Zongsoft.Administratives.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives)
	- [`Zongsoft.Administratives.Web.deploy`](https://github.com/Zongsoft/administratives/blob/main/src/api/Zongsoft.Administratives.Web.deploy)
	⇢ [NuGet](https://www.nuget.org/packages/Zongsoft.Administratives.Web)

- The hosting projects
	- [`daemon.deploy`](https://github.com/Zongsoft/hosting/blob/main/daemon/.deploy)
	- [`terminal.deploy`](https://github.com/Zongsoft/hosting/blob/main/terminal/.deploy)
	- [`web.deploy`](https://github.com/Zongsoft/hosting/blob/main/web/default/.deploy)

### Plans, locks, and cleanup

Source refactor compatibility changes: the default overwrite policy now follows the documented `newest` behavior; invalid overwrite values, filters, and undefined path variables fail. Writes must stay within `destination`, and linked write paths are rejected. Both nested manifests and `#@import` detect cycles. Combined filters retain left-to-right evaluation. `**` matches zero or more directory levels; directory copies preserve their internal relative structure.

| Option | Behavior |
| --- | --- |
| `--dry-run:true` | Builds a plan without target writes or directory creation. Explicit reports still write; online resolution can populate the NuGet cache. |
| `--offline:true` | Uses only unpacked local packages with valid nuspec metadata; missing packages fail without network requests. |
| `--explain:true` | Prints operation results and package/file origins, preserving original messages and paths. |
| `--report:./deployment.json` | Saves success or failure, manifest origins, package versions/parents, hashes, duplicate/skipped operations and diagnostics. |
| `--lockFile:./deployment.lock.json` | Writes selected versions, package content and manifest/source hashes after a successful non-preview deployment. |
| `--locked:true` | Uses package versions from lockFile and validates the plan/content without updating the lock. Locks retain source manifest paths. |
| `--prerelease:true` | Includes prereleases for unpinned requests; dependencies explicitly requiring a preview lower bound can also select previews. |
| `--previous:./previous.json` | Compares a successful prior report for the same target root and lists obsolete files as Stale; keeps them by default. |
| `--prune:true` | Requires previous. Deletes only files whose last effective prior operation copied them, whose hashes are unchanged, and which are no longer selected. Modified, unowned and outside-root files are not automatically removed. |

Boolean options accept a bare name or `true/false`. Locks, reports, and cleanup are opt-in. Give reports/locks independent paths: they cannot overwrite known source manifests, source files, or planned targets. An execution-time I/O error stops subsequent operations; completed writes are not rolled back.

```powershell
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --offline:true --dry-run:true --report:./preview.json .deploy
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --lockFile:./deployment.lock.json --report:./completed.json .deploy
dotnet deploy --framework:net10.0 --platform:win --architecture:x64 --lockFile:./deployment.lock.json --locked:true .deploy
```

RID fallback uses the repository-pinned dotnet/runtime v10.0.0 graph through NuGet.RuntimeModel (see [implementation details](docs/implementation.md)), with aliases `windows→win`, `mac/macos→osx`, and `x32→x86`. `contentFiles/any/{tfm}` honors nuspec include/exclude, copyToOutput, and flatten; content without an output-copy rule is not deployed. Legacy `content` is copied recursively. XML documentation in lib groups is still copied under the existing contract; not every XML file is disposable.

Package access, dependency resolution, asset selection, and RID fallback have separate implementations; framework and version models reuse NuGet/.NET types. See [implementation details](docs/implementation.md) for responsibilities and behavior. This refactor adds no package dependencies.

Variables load from the environment, the destination application's appsettings.json, and finally command options. Nested keys support `$(Database.Name)` and `%Items[0].Name%`; substitution preserves URL slashes.

Run regression tests without publishing:

```powershell
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net10.0 -p:GeneratePackageOnBuild=false
```

See [REFACTOR-TASKS.md](REFACTOR-TASKS.md) for task status and real-package validation.

Profile imports use Core 7.59.0: ProfileReader handles imports and recursion directly; ProfileOptions.Importing records imported manifest hashes without directive registration. See [implementation details](docs/implementation.md#profile-import-callbacks) and the [import refactor checklist](PROFILE-IMPORT-TASKS.md).

See [implementation details](docs/implementation.md#core-profile-declarations-and-saving) for Core Profile source/override rules and read/write responsibilities. Deployment does not save its manifests.

Local source searches and links follow [the implementation contract](docs/implementation.md#local-search-and-source-links).

## Development checks

Production and test projects use `Zongsoft.CodeAnalysis` 1.1.0. Use .NET SDK 10.0.401 or a compatible newer compiler. The analyzer is a private build dependency, not a runtime dependency of the tool.

```powershell
dotnet build src/Zongsoft.Tools.Deployer.csproj -p:ZongsoftCodeStyleStrict=true -p:GeneratePackageOnBuild=false
dotnet format style src/Zongsoft.Tools.Deployer.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

The build checks every configured target framework. See the [repository instructions](../AGENTS.md#代码规范检查) for editor configuration, resource generation and validation requirements.
