# Implementation details

[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

This document describes responsibilities, execution order, and maintenance constraints. See the [README](../README.md) for syntax and options.

## Entry point and responsibilities

`Program.Main` uses Zongsoft.Core `CommandLine` to parse arguments, creates a cancellation token, and calls `Deployer.CreateVariables` and `DeployManyAsync`. With no input arguments it reads `.deploy` in the current directory. Exit codes are 0 for success, 1 for failure, and 130 for cancellation. Manifest diagnostics separate source locations from reasons on the next line; dependency constraints and cycle paths are listed on separate lines. Shared `Utility.Indent` preserves nested detail indentation and uses platform line endings. Execution writes to a `TextWriter` and does not require an interactive console handle.

| File/type | Responsibility |
| --- | --- |
| `Deployer.Command.cs` | Combine variables and locate the destination application's configuration. |
| Linked `tools/.shared` source | `Utility.cs` compiles into this project's `partial Utility`; it shares recursive variables and command helpers. `ArtifactPublisher` manages staging and publication; deployment copies and reports use its atomic single-file replacement. Boolean values in the variable map follow Core `Switch` semantics; enums use Core conversion. |
| `Deployer.cs` | Coordinate one invocation, traverse manifests, and collect requests and preflight failures. |
| `DeploymentSession.cs` | Own counters, active manifest paths, package requests, selected versions, and expansion buffers. |
| `DeploymentPlan.cs`, `DeploymentOperation.cs`, `PackageSelection.cs` | Store the plan, operation origins, selected packages, and hashes in separate types/files. |
| `Deployer.Execution.cs` | Validate destinations, locks, and previous records; execute operations. |
| `DeploymentResolverBase`, `DeploymentResolverManager`, `NugetResolver` | Convert local paths, delete/remove entries, and NuGet entries into operations. |
| `DeploymentPath` | Enforce destination boundaries, reject write-path links, and identify deployment manifest paths. |
| `NugetUtility` | Access package sources, metadata, versions, downloads, cache paths, and package hashes. |
| `NugetGraph` | Own one dependency-resolution attempt, fixed roots, constraints, candidate backtracking, and cycle diagnostics. |
| `NugetAssets` | Adapt explicit cache paths and select TFM/RID/content assets. |
| `NugetRuntime` | Load the pinned RID graph and expand platform/architecture aliases. |

A `Deployer` instance rejects concurrent calls. Sequential calls create fresh sessions and reset the package cache for that variable context. Counters distinguish copied, skipped, deleted, and failed operations. Previewed files are not counted as actual copies.

## Reusing libraries

Release references the Zongsoft.Core NuGet package; Debug references the local Core assembly. Core and test/analyzer versions are managed in the repository root Directory.Packages.props; NuGet.* versions are declared with VersionOverride in this tool project.

| Capability | Reused API and remaining tool responsibility |
| --- | --- |
| Command line | Core `CommandLine.Parse/Get`; the tool coordinates options, exit codes, and cancellation. |
| Environment dictionary | Core `DictionaryExtension.ToDictionary`, with an explicit case-insensitive comparer. |
| INI manifests | Core `Profile`, `ProfileOptions`, `ProfileContext`, and profile collections; no independent INI parser. |
| Localization | `ResXFileCodeGenerator` generates strongly typed `Properties.Resources` properties. Call sites read these properties and use `string.Format`; logs and reports retain the original messages. |
| Package metadata/content | NuGet `NuspecReader`, `PackageIdentity`, and `VersionRange`; `GetContentFiles` reads rules once per package expansion. |
| TFM | NuGetFramework represents framework identity and uses System.Version for framework/platform versions. NuGetVersion and VersionRange represent package versions and constraints. Explicit framework-filter rules, including ^, are handled separately from nearest-compatible asset selection. |
| RID | NuGet `JsonRuntimeFormat.ReadRuntimeGraph` loads the graph; `RuntimeGraph.ExpandRuntime` supplies fallback candidates. |

Core handles imports directly in ProfileReader with cycle/depth checks. ProfileOptions supplies two Action<ProfileContext> import callbacks. Deployer hashes imported files in Importing and records roots separately. Appsettings dotted keys, target containment, link rejection and ownership remain deployment responsibilities.

## From manifests to execution

1. **Prepare variables and a session.** Validate overwrite and boolean options, determine the destination root, and load a lock when requested.
2. **Parse all inputs.** Expand sections and entries, collecting file operations, deletions, and ordinary NuGet roots. Evaluate filters before expanding variables in active branches.
3. **Resolve dependencies.** Solve ordinary root requests together to select versions satisfying the dependency closure.
4. **Expand assets.** Replace package placeholders with file operations while retaining manifest order and origins.
5. **Validate the plan.** Check paths, source hashes, duplicate/conflicting destinations, report paths, locks, and prior ownership. Any preflight failure prevents destination execution.
6. **Execute or preview.** Recheck destination paths and source hashes before writes. Preview retains planned operations. An explicit report can record success or failure.

Online parsing/resolution can populate the NuGet cache, so dry-run does not mean zero disk writes. Offline mode requires unpacked local packages with valid nuspec metadata and fails on missing packages.

## Variables, manifests, and paths

`DeploymentEntry.Get` splits the source at the first colon before expanding variables. Entries without a colon use the name `path`. `DeploymentResolverManager.GetResolver` selects the default path resolver for null, empty, or whitespace-only names. It matches `path`, `nuget`, and `delete`/`remove` case-insensitively; unknown names return null. Default selection for an empty name is part of the resolver API contract. The default resolver has an empty Name; `path` is a lookup name selecting that instance. Use `path:D:\dir\files.ext` or `path:D:/dir/files.ext` for a literal Windows absolute source path so the drive letter is not parsed as a resolver name. Relative paths may omit the prefix or use `path:`.

Variable names may contain dots, hyphens, and indices and are case-insensitive. Environment values are overridden by ancestor `.env` files from root to working directory, destination appsettings and then command options. The destination is resolved recursively from all command options, environment variables and `.env` values before its configuration is loaded. Final values remain raw until read, so option order does not matter; referenced missing variables, cycles, and chains over 64 levels fail, while unused values are left alone. Boolean and enum option values expand before conversion; `expansion` obeys its Boolean value and unconvertible `verbosity` fails. Enum options follow Core conversion rules without an additional check that the enum member is defined; callers must supply a valid member. JSON comments and trailing commas are accepted. Objects and arrays produce keys such as `Database.Name` and `Items[0].Name`.

Shared `Utility.LoadEnvironmentVariables` collects the ancestor chain and loads direct `.env` files in reverse order with `Profile.Load`. Root entries keep their names; recursive section levels and entry names join with `_`. Core parsing, null/empty values and imports are preserved. Only missing files at the open stage are skipped; read, permission and parse failures propagate. Variables are per invocation; nested manifests do not reload them, and the process environment is not modified.

`Normalizer` expands `$(name)` and `%name%` without changing URL slashes. Undefined variables fail during deployment-path expansion, while filtered-out branches need not provide their variables. Combined filters retain left-to-right evaluation; both source and destination filters must match.

`NugetAssets.ResolveLibraryPath` handles explicit cache-path adaptation in one place. It examines paths relative to the NuGet_Packages root and locates the lib directory. A framework in the path takes precedence over the Framework variable; the official NuGetFrameworkUtility.GetNearest selects the nearest framework while subsequent subpaths are retained. Paths outside the cache, paths without a target framework, or paths with no applicable directory are returned unchanged.

`DeploymentUtility.GetFiles` expands package directories and selects the framework before Core Searcher enumerates assets; captures preserve directory suffixes. Asset directories in `GetPackageFiles` have already undergone RID/TFM selection, so enumeration uses resolveLibrary: false to avoid repeated matching. A lib subdirectory in content is treated as ordinary content and does not cause additional files to be selected.

Glob `**` matches zero or more directories; `?` matches a single character in directory names. Expansion preserves destination suffixes and avoids traversing linked directories. Core Searcher returns logical paths and wildcard captures; the deployment adapter uses them to form destination suffixes.

Destination paths are made absolute and checked using their path relative to destination. Write paths and ancestors reject reparse points, including dangling links. Sources may live outside destination. Source identities resolve links; active paths detect direct/indirect manifest cycles with a depth limit of 64. A completed manifest expansion may be reused for another destination. Imports maintain a separate active set and contribute hashes for files actually read. Missing optional imports retain Core's existing behavior.

## Package versions and assets

NugetUtility, NugetGraph, NugetAssets, NugetRuntime, and NugetResolver each handle a distinct responsibility. Each graph resolution creates a private NugetGraph instance; branch selections are copied for backtracking and the active dependency chain is kept in order for diagnostics. NugetResolver then expands the solved graph with an explicit stack, preserving root-first depth-first order and checking cancellation during asset enumeration. Managed and native assets share the RID traversal but select their fallback groups independently.

XML type comments describe responsibilities, while flow comments explain ordering and state boundaries. Construction, search, constraint collection, framework matching, and cache details remain private. Internal entry points serve production collaboration; visibility is not widened for unit tests. Tests exercise actual deployment and package-access entry points.

NugetUtility keeps package-access caches scoped to the invocation variable dictionary. Cache keys also include source, absolute cache root, offline mode, and prerelease mode, so changing those settings cannot reuse a different context. Dependency regression tests exercise the actual deployment plan and output files.

Ordinary roots are pinned to their requested versions. Dependencies are filtered against all known ranges, with the lowest available candidate tried first and backtracking when later constraints conflict. Unsatisfiable constraints, cycles, or search limits fail. Limits are currently 10,000 search attempts and 512 selected packages; constraints from each requested TFM participate.

Default transitive exclusions are `System.`, `Microsoft.Extensions.`, and `Zongsoft.`. Custom prefixes are appended and matched case-insensitively. Explicit root requests are not excluded. Unpinned requests exclude prereleases unless enabled or a dependency explicitly requires a prerelease lower bound.

This solver selects deployment files. It does not execute MSBuild/buildTransitive or implement every restore conflict rule. Separate plugin directories do not imply independent assembly loading; confirm the host contract before splitting invocations to deploy independent versions.

A package-root `.deploy` takes precedence over ordinary asset expansion. Explicit package paths request only those files. Ordinary assets follow these rules:

- Managed assets search RID fallback candidates for a compatible `runtimes/{rid}/lib/{tfm}` group. A selected runtime group replaces the whole ordinary lib group; otherwise ordinary `lib/{tfm}` is used.
- Native assets independently select the first existing `runtimes/{rid}/native` group. They need not come from the same fallback level as managed assets.
- `contentFiles/any/{tfm}` applies nuspec include/exclude, copyToOutput, and flatten rules. Content without an output-copy rule is omitted. The `content` directory is copied recursively.
- Identical package assets targeting the same file retain Duplicate origin records but copy once. Different content at the same package destination fails. Explicit deletion ends the preceding deduplication scope.
- Public DLLs are not moved across plugin directories; XML documentation in lib groups remains included.

## Pinned RID graph

`src/Resources/PortableRuntimeIdentifierGraph.json` is checked into the repository and embedded as `Deployer.RuntimeGraph.json`. Builds do not read a graph under `$(MSBuildToolsPath)`. Runtime execution neither searches SDK directories nor downloads a graph. All target frameworks embed the same snapshot.

The snapshot comes from [dotnet/runtime v10.0.0](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.NETCore.Platforms/src/PortableRuntimeIdentifierGraph.json). The graph and license retain their original upstream bytes and formatting (currently LF line endings), without newline, encoding, or indentation conversion. Both files have `-text` in .gitattributes to prevent Git conversion. The graph's repository and upstream SHA-256 are both `2057797FE984CF7BEB26911F3BF7EB58B6854B403563AA661196A2FD16A77BE9`. The upstream MIT license is retained at `src/Resources/dotnet-runtime-LICENSE.txt` and packaged as `licenses/dotnet-runtime-LICENSE.txt`.

NuGet expands the graph from nearer to farther candidates. The tool normalizes `windows→win`, `mac/macos→osx`, and `x32→x86` before querying it. For example, linux-musl-x64 fallback follows graph edges, not guessed string truncation. Unknown RIDs receive no invented fallback edges.

The embedded graph determines RID fallback independently of the locally installed SDK. Its version, hash, and license identify the exact resource used by all target frameworks.

## Reports, locks, and cleanup

Plans record manifest hashes, selected package versions/parents, package-content hashes, and operation sources, destinations, statuses, and file hashes. Package hashing uses sorted relative paths and file hashes, excluding nupkg, nupkg.sha512, and .nupkg.metadata so cache bookkeeping does not count as content changes.

Locked runs select root and transitive versions from the lock and validate manifests, operations, and package content. Locks retain source-manifest paths and are not freely relocatable restore locks. Report and lockFile cannot share a path, including during failure reporting. Failure to save a report marks the final result unsuccessful.

Previous reports must describe a successful deployment to the same root. The last effective operation determines ownership; Duplicate does not revoke it. Only a final Copy/Copied operation qualifies. Prune additionally requires that the file is no longer selected and its hash is unchanged, with another check immediately before deletion. Modified files, preexisting files merely skipped, and files recreated after an explicit deletion are not reclaimed using an obsolete copy record.

Execution-time I/O failures stop subsequent operations but do not roll back completed writes. Path checks are not an atomic defense against concurrent malicious filesystem replacement. A correct asset file set does not establish that a host can load and use it.

## Development conventions and validation

Cake passes the selected `--edition` as MSBuild `Configuration` during restore, build, and tests, so conditional Core references resolve consistently: a local DLL in Debug and the declared NuGet package in Release. The test project references the main project; Cake only selects `test/*.csproj` and does not scan build-output copies. `dotnet cake --edition Release` runs clean, restore, build, and tests for all three frameworks. Build generates the local tool package; only the explicit `pack` target pushes to NuGet.

Handwritten non-test C# files use the tool's existing `/* */` MIT header, Tab indentation, and CRLF. Regions group constants, fields, constructors, properties, methods, or concrete responsibilities. Separate method phases with blank lines, avoid multiple statements on one line, and do not introduce using type aliases. Do not leave a blank line before `#endregion`. Designer files remain generator-managed.

```powershell
dotnet restore Zongsoft.Tools.Deployer.slnx --source https://api.nuget.org/v3/index.json -p:GeneratePackageOnBuild=false
dotnet build Zongsoft.Tools.Deployer.slnx -f net10.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net8.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net9.0 --no-restore -p:GeneratePackageOnBuild=false
dotnet test test/Zongsoft.Tools.Deployer.Tests.csproj -f net10.0 --no-restore -p:GeneratePackageOnBuild=false
```

Tests use temporary destinations and synthetic local packages. RID order tests assert final file contents; localization tests assert en/zh-Hans templates and parameters. Host integration checks validate assembly loading and native first use on the target platform. Both implementation documents ship under docs with README links.

### Resource generation and access

In Visual Studio, set the Custom Tool for the neutral `src/Properties/Resources.resx` to `ResXFileCodeGenerator` and run Run Custom Tool to regenerate the adjacent `Resources.Designer.cs`. The project retains Generator, LastGenOutput, AutoGen, and DependentUpon metadata. Do not generate another class with the same name from the zh-Hans resource file; it supplies satellite resources for the same accessor.

Update keys and format arguments in both resx files, regenerate, then compile. For example, `Review.Missing` produces `Properties.Resources.Review_Missing`; call `string.Format(Properties.Resources.Review_Missing, path)`. Do not assemble resource keys or query ResourceUtility/ResourceManager directly. The generated accessor uses CurrentUICulture and retains the standard Culture override property. A plain dotnet build does not itself run Visual Studio's custom tool.

The generator owns Designer properties and comments. Only normalize CRLF and remove spaces on generated blank lines; do not handwrite resource properties. Existing en/zh-Hans tests assert complete localized templates and path arguments.

## Profile import callbacks

Reader uses ProfileOptions.MaximumDepth (default 64, positive integers only), counting the root as level one. Deployer uses the default. Importing runs after opening and recursion checks, before parsing; Imported runs after child loading and merging. Roots do not notify. Missing optional imports are skipped; other failures propagate. Child files share the captured blank-line and import settings.

Deployer only subscribes to `Importing`. Hashing reopens the input path and is not a strict snapshot of parsed bytes.

Core centralizes parsing, import paths and recursion guards in internal ProfileReader, with a private Context for the current Profile, section and line. Profile.Load remains a facade. After parsing a child, parent Profile.Import merges effective references and records relationships, then Imported notifies before activity cleanup.

ProfileOptions exposes Importing/Imported as Action<ProfileContext>. FilePath is the absolute import path, Depth is the loading depth and Referer identifies the direct referring profile. Profile is null before parsing and identifies the child after merging; each notification receives a separate get-only context. Reader shallow-copies options and delegate references, including the MaximumDepth value, with no import switch. Callbacks can throw to abort the whole load, but cannot silently skip imports. Deployer hashes context.FilePath in Importing without registering Imported.

## Core Profile declarations and saving

Core separates ordered local statements from the merged effective view. Imports replace effective references and entries identify their actual source declarations. Duplicate local keys still fail; local/import precedence follows reading order. ProfileReader owns loading and ProfileWriter owns saving.

Core Save() without an explicit destination writes changed declarations in the receiver and import subtree back to their respective sources. Explicit paths, streams and text writers output only the receiver's statements. Unchanged files are not rewritten. Multiple outputs are all prepared before individual commits; this is not a cross-file transaction. Deployer only reads manifests and records hashes through Importing; it does not invoke these save entry points.

## Local search and source links

Core Searcher handles local patterns and recursive matching. Results preserve the logical source name while reading the resolved target. A selected directory link may be expanded as a payload root; nested directory links are skipped and file links contribute target bytes under their logical names. Recursive patterns do not cross directory links to match further segments. Selected broken/cyclic links fail before output writes. Destination boundaries and output path validation apply to every write.

Linked INI and .deploy relative references use the logical configuration directory. Pattern results are sorted by logical relative path using Ordinal order; input argument order remains significant. See [Core local searching](../../../framework/Zongsoft.Core/docs/searcher.md).

Deployment operations keep logical Source and optional ResolvedSource for linked inputs. Copying and hashes use the resolved target; locked validation includes the target path and execution rechecks resolution, so an equal-content retarget is rejected. NuGet framework adjustment and expansion suffix mapping remain in the deployment adapter.

`Searcher.Search` selects files, directories, or both through `Searcher.Target` (default: `Both`); `Match.Origin` provides the logical fixed directory prefix used to form relative output paths.
