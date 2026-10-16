[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# Containerizer implementation

This document describes current responsibilities, data contracts and lifecycle behavior. See the [README](../README.md) for usage, the [template reference](templates.md) for template fields, and [SKILL](../SKILL.md) for development procedures.

## Architecture boundaries

Containerizer builds and manages container bundles for a single host. Packager owns application packages and Migrator owns migration archives. Containerizer consumes their artifacts and launchers without referencing their executable projects or coordinating remote nodes.

| Area | Entry points and responsibilities |
| --- | --- |
| Maker input | `ManifestFactory` handles command context, variables, paths, version conflicts and input selection; `ContainerManifest` owns data, Profile reading, dependency completion and its own validation |
| Service planning | `ServicePlanner` prepares services for plan/make, diagnoses missing settings, invokes Web planning and validates dependencies and ports |
| Applications and templates | `ApplicationPlanner` derives entrypoints, environment, health and build-time runtime requirements; static `TemplateCatalog` reads infrastructure/ingress templates |
| Build coordination | `ContainerBundleBuilder` owns the workspace, image stages, bootstrap, migration collection, verification and publication; `ServiceBuildContext` keeps its component association read-only from creation |
| Engine and images | `ContainerEngine` performs engine operations; `ImageReference` owns repository/tag/digest rules; `ServiceImagePreparer` adapts health checks, Redis/Valkey authentication and data accounts |
| Application images | `ApplicationImageBuilder` installs application packages and creates final images; `RuntimeEnvironmentCache` owns reusable OS/runtime filesystems |
| Local preview | `RunContext` owns an entire session; its private `Endpoint` and `ImageCache` types handle forwarding and inner image storage |
| Target execution | `InstallationManager` controls transactions, `DockerHost` operates the OS and Docker, and `InstallationStore` owns state, history, assets and locks |
| Shared source | .shared/Containerization.props directly links protocol models, file validation and process helpers; no shared DLL is produced |

The maker uses Core Profile, Settings and the repository's shared variable workflow. The executor is BCL-only and consumes JSON and generated assets, without parsing .container, Profile or YAML. Generated `compose.yaml` uses JSON syntax and is consumed by Compose.

Sessions, locks, resource lifetimes and installation transactions retain their own complete coordination boundaries. Pure rules use stateless types; side effects go through the existing engine, host and process boundaries. Visibility follows production callers: tests do not widen private members or introduce test-only entry points.

## Container bundles and service models

`ContainerBundle` represents an opened, verified container bundle and owns archive extraction, asset validation and temporary-directory disposal. Its `Plan` property is a `ContainerBundlePlan`, corresponding to `containerizer.json`. The maker uses `ContainerBundleBuilder` to produce the complete bundle. `ServicePlan.PackageInfo` describes only an input application package and belongs to `ServicePlan.Package`.

Service port mappings and application bindings use nested classes to express ownership:

```text
ContainerBundlePlan
├─ Files: FileEntry
├─ Migrations: Migration
└─ Services: ServicePlan
   ├─ Package: PackageInfo
   ├─ Mounts: Mount
   ├─ Health: HealthCheck
   ├─ Web: WebSite
   │  └─ Bindings: BindingInfo
   ├─ Ports: PortMapping
   │  └─ Bindings: BindingReference
   └─ Bindings: Binding
      ├─ Endpoints: Endpoint
      └─ Connections: Connection
```

`ServicePlan.PortMapping`, defined in `ServicePlan.PortMapping.cs`, records the host address, host port, container port and transport protocol. For example, `Address=127.0.0.1`, `Host=18080`, `Container=8080` and `Protocol=tcp` map TCP 18080 on the host loopback address to TCP 8080 in that service's container. Its `BindingReference` records the source application, binding and endpoint identities; declarations sharing one physical mapping retain their individual references.

`ServicePlan.Binding`, defined in `ServicePlan.Binding.cs`, records a binding declared by the application package (owned by application or nginx) and its publication policy. Its `Endpoint` preserves a declared listener, while `Connection` records a proxy route's original endpoint, selected connection endpoint and connection parameters. These are mutable reference types; bundle validation rejects missing objects and collections. Type nesting adds no JSON layers: services retain their `ports` and `bindings` collections, and endpoints and connections retain their `endpoints` and `connections` collections.

## Maker data flow

```text
Command context + local inputs
  → ManifestFactory / ContainerManifest
  → ServicePlanner
      ├─ plan: save draft
      └─ make: ContainerBundleBuilder
          → pin images → build applications / export images → adapt service images
          → bootstrap / configuration / migrations / Compose
          → completed manifest / JSON / checksums → compress → publish
```

Components retain declaration order, with automatically added nginx appended. Build contexts directly reference their components rather than relying on parallel list indices. Image identities are established before writing; serialization only reads the model.

Plan and make share service preparation. Plan does not connect to an engine, read .mirrors or collect system dependencies. Missing required settings produce diagnostics and a saved draft; other invalid structures still fail. Runtime resolution remains at the application image build entry, rather than moving make-stage external work into plan.

### Profiles, variables and paths

A .container accepts a fixed root/component vocabulary and rejects unknown fields, duplicate components/fields and nested components. Profile imports use Core; imported files participate in declaration validation, and source resolves relative to its declaring file. Imports are disabled for .settings and templates.

Service parameters use Core `Settings` for both parsing and serialization, including quote and backslash escaping. A private subtype validates names and rejects duplicate declarations in `OnEntryChanged`, before Core can overwrite them. Empty/null parameter values remain explicit empty strings; a missing parameter remains absent. Formatting uses `ISettings.Add` to preserve empty values and surrounding whitespace. Literal or decoded CR/LF values are rejected before manifest publication.

Shared Utility combines command options, the source .env and its explicit imports, and the system environment. Core queries descriptor defaults only after all these sources miss. Section levels form dot-separated namespaces; entry dots and hyphens become underscores. Final root fields are exposed first through `Variables.Wrap(result.Root)`; later dictionary changes remain visible. The source/output fields store absolute paths with `/` separators. Evaluation neither exports the whole environment nor modifies the process environment, and a matching name alone never creates a binding. Input selection and root paths evaluate immediately; service settings/environment references survive plan and evaluate per value during make. Completed manifests replay literal values; replanning a completed manifest escapes literal references to prevent reevaluation.

The variable contract is `Zongsoft.Common.IVariables`. Command options use `context.Options` directly, while computed values use Core `Variables`; `.env` files remain `Profile.ToVariables()` views. System values are read live through `Variables.Environments()`, without copying or rewriting names or values. The environment provider exposes only the default namespace; Windows ignores name case and Unix/Linux preserves case sensitivity. Path values used in templates use `/`; other escaping follows Core. The tool explicitly sets `TemplateEvaluatorOptions.Fallback=true`. It searches every provider at each namespace before moving through parents to global, then allows source-declared defaults. The first match wins, including null. Directive evaluation inherits Fallback, Recursive, Culture and MaximumDepth from its evaluator; unqualified references need no extra syntax. Use `${name}` or `${io.rustfs:access_key}`. Section levels form dot-separated namespaces; entry and command-option dots/hyphens become underscores. Invalid configuration names supply no variable and ambiguous configuration names fail only when queried.

Register context.Options once before configuration and the environment. Core VariablesExtension.TryGetValue queries all sources at each namespace with false. Only after global ordinary queries also miss does it query global with true for declared defaults. Global precedence is explicit options, the source directory's .env (including explicit imports), environment, then option defaults. HasDefaultValue distinguishes omission from explicit null; omission does not synthesize a type's zero value. Raw reads, recursive templates and directive evaluation share the Fallback setting. Computed overrides use a leading Variables source.

Templates use Core `TemplateEvaluator.Evaluate` / `TryEvaluate` directly, with `Recursive` enabled and the default depth limit of 64, counting the root and recursive strings. Every input follows Core escaping: `\${name}` emits a literal reference and `\\` emits a literal backslash. Prefer relative paths; use `/` in absolute paths, for example `../.shared/${product}.env` or `D:/deploy/${scheme}`. Missing variables, cycles and excessive depth follow the Core error contract.

`.env` directive arguments are evaluated through `Directives.Processing`, looking up explicit options, already-read entries in the current file (including completed imports), and environment/default values in that order. Later declarations are unavailable; an imported file does not implicitly search its parent's local entries. Maker manifest callbacks query actual invocation values before the current Profile, then descriptor defaults only if all sources miss. Each import uses its complete argument as one path. Source declarations and raw entries remain unchanged; tools explicitly request evaluation while Profile handles reading and imports.


The CLI source resolves against the invocation directory; other managed local paths resolve against final source. Template configuration files resolve against the template directory. Linux container paths have separate validation independent of the maker OS. Generated manifests express source relative to output, and output/local component inputs relative to source. Serialized local paths use `/` separators so a saved plan remains valid when its values are evaluated again during make.

Release names use name[-tag]@version-architecture. Core numeric versions have 2–4 parts, each at most 65535, and must be nonzero. Automatic versions use year modulo 1000, month and day, adding/incrementing a fourth part on conflicts. Explicit version conflicts fail. A build may complete its own draft in place on success, but cannot overwrite an existing archive.

### Package and migration inputs

`PackageReader.Select` returns both a path and the descriptor already read. Preparation reuses that descriptor within the invocation and rereads it when the component package path changes. Web resource extraction still rereads the package, and bundle integrity checks still run; there is no cross-command package metadata cache.

Supported Packager inputs are tar with a matching Shell launcher, deb and rpm. Readers extract identity, architecture, installation directory, service files, `runtimeconfig` and Web metadata. All formats read a regular JSON .bindings file beneath InstallPath, limit it to 1 MiB and validate it with the shared model before saving Descriptor.Bindings. Directory selection checks the directory and then .packages, preferring the distribution's native format before tar in each location, with architecture and name-prefix filtering; multiple remaining candidates prompt for a selection, and file dates never choose a version.

Applications require one unambiguous service definition, `ExecStart` and WorkingDirectory. Entrypoints are foreground argument arrays; ambiguous Shell wrappers or complex commands must be corrected upstream. Application configuration is installed into the image with the original package rather than extracted by extension into target configuration mounts.

Migration inputs are explicitly selected `<prefix>(migrate)@<version>_linux-<architecture>.tar.gz` files with matching .sh launchers. CLI directory selection allows multiple candidates. Manifests read positive numeric migration# indices and validate ascending, unique versions and matching architecture. No selection means no migrations; nothing is inferred from application packages or default directories. Building does not parse business SQL or contact databases; it records the combined archive/launcher hash and ships the files unchanged. Independent `hosting/containerize.cmd` wrappers provide menus, file selection and command invocation; the tool does not depend on those scripts or their default-directory conventions.

### Publication and output

Temporary build files live in an independent system workspace. The output directory receives the completed manifest, bundle archive and any required .settings tag additions. A direct complete build does not first publish a draft. Only a successful complete build from a component list fills missing service tags into `.settings`; plan, failed builds and manifest replay neither remerge nor write that file.

An output lock and `ArtifactPublisher` coordinate publication, checking the input hash to avoid overwriting edits made during a build. Internal and external completed manifests are byte-identical. Failure cleans this invocation's temporary resources while retaining existing inputs. Multiple final files cannot be committed by one filesystem operation; publication does not promise a cross-file power-loss transaction. Archives use gzip Fastest.

## Image identities and caches

Repository, source tag, target-platform manifest digest, multi-platform index digest and local image ID are distinct. `ImagePlan.Reference` is the full local reference used on the target. `SourceTag` and `SourceReference` describe the origin, Digest pins the target manifest, and Id supports post-import content checks. The OS is always Linux and architecture comes from the manifest root.

Local images are reused when identity and platform evidence is sufficient; otherwise they are resolved or pulled. Pinned replay cannot fall back to a tag, change platforms, or substitute an index digest/image ID for the target manifest. Creation time and engine-reported size are recorded when available and omitted otherwise, without extra network work for display metadata.

The manifest identity associates repository, tag, Linux and architecture. Changes invalidate previous digest, timestamp and size values. Changing only the bundle version, bundle tag or host distribution does not alter infrastructure image identity.

The maker exports infrastructure images by ID without attaching release-specific tags to cached images. Application builds, account probes and auxiliary builders use unique owned resources. Cleanup removes this invocation's containers and anonymous volumes, temporary images and builders, retaining useful source/tool images without global prune.

| Cache layer | Identity and contents | Validation and updates |
| --- | --- | --- |
| Engine images | Source images, platforms, digests and verified associations | Resolve again when content/platform evidence mismatches; infrastructure tags are outside refresh scope |
| Shared application runtime | Grouped by engine, distribution, architecture, runtime family and major/minor; OS files, curl/CA and runtime, without applications | Signature includes base digest, platform and install recipe; archive hash/length, environment and runtime requirements are validated |
| Bootstrap | System package sets in the user cache, isolated by normalized source hash, distribution, architecture and collector script hash | Explicit import takes precedence over cache; outside refresh scope |
| Run base image | Outer-engine image with systemd and forwarding tools, identified by recipe and architecture | Ownership checked; contains no installed application state |
| Run image storage | Per-application inner Docker storage volume | Reused only after complete cleanup, environment matching and a clean marker |

The file cache root is %LOCALAPPDATA%/Zongsoft/containerizer on Windows. Linux uses absolute `XDG_CACHE_HOME`, otherwise ~/.cache, then appends Zongsoft/containerizer. Runtime environments live under `runtime/<engine>/<profile>/`.

Each shared runtime entry has a lock. One invocation refreshes each required environment at most once and resolves its OS base again. A new archive is fully produced and checked before atomically replacing the `environment.json` pointer; failure preserves the previous generation. Readers copy the selected generation while holding the lock before obsolete archives are removed. There is no time-based expiry. Runtime caches contain no application configuration, business data or database initialization results.

Before manual cleanup, stop builds/previews using the cache and delete only identified entries. Do not remove the locks directory while processes hold locks, or replace targeted cleanup with global image/volume prune. Bundle history and target migration state are not disposable caches.

### Registry mirrors

`RegistryMirrorSettings` reads Core Profile from the selected directory; shared `RegistryMirrors` handles exact registry matching, source ordering and cancellation. Sections, duplicate registry entries and empty candidate addresses are rejected. Mirrors change download paths, not logical repositories, pinned digests or platforms. Without a pinned digest, the first usable source may resolve a tag; subsequent sources must match the established identity.

Podman builds use verified local images and disallow pulling. Docker uses a dedicated BuildKit builder and temporary configuration, removing the builder/cache on completion. Run passes rules to inner online pulls through temporary JSON outside bundle assets. Neither .mirrors nor actual mirror addresses enter completed manifests or bundle JSON. Global engine configuration, package sources and credentials remain outside this mechanism.

Before a Docker or Podman build, `ContainerEngine.BuildAsync` copies the maker process's explicitly defined HTTP/HTTPS/FTP/ALL/NO proxy variables (uppercase and lowercase) into `--build-arg` arguments. This also applies to remote engines. An absent variable is omitted; an explicit empty value is passed through. Proxy values do not enter manifests, Compose environments or Dockerfile `ENV` instructions.

## Applications, Web and service preparation

### Runtime and application health

`ApplicationPlanner` selects .NET/ASP.NET Core runtime requirements from the entry DLL's `runtimeconfig`; explicit runtime-* dependencies must match. Self-contained entrypoints do not add a framework runtime, but still prepare probe dependencies. Builds install an available patch in the required major/minor line, verify it is no older than the package's minimum patch and record the version actually reported by the final image. Ubuntu 22.04 uses `ppa:dotnet/backports` for .NET 9 and later runtimes; other Debian/Ubuntu paths use the corresponding Microsoft package repository, while RPM paths use the RHEL-family repository.

Application packages execute their original installation flow inside the build environment, with service startup controlled by the build adapter. The final image retains runtime files and a foreground entrypoint rather than using the target executor as its application launcher.

### Application bindings and health

Application entrypoints and environment values retain their package configuration. Containerizer does not inject or rewrite --urls. The default health command is ["CMD-SHELL","kill -0 1"]; an application component may supply a JSON command array through health, beginning with CMD or CMD-SHELL. This checks the declared contract only: metrics, OTLP export and gRPC reflection are not implicit business health checks.

ApplicationBindings consumes the validated root .bindings. Each ServicePlan.Bindings entry records owner, identity, aliases, original endpoints, direct-publication mode and selected proxy connections. publish!<binding-id-or-alias> accepts auto, none or a semicolon-separated list of transport://literal-host:port values. The selector must identify one application binding; duplicate overrides through an ID and an alias fail. auto keeps proxied bindings internal and publishes other bindings on 127.0.0.1 with the original port. none cancels direct publication while retaining proxy routes. Explicit addresses must use a declared TCP/UDP transport and one unambiguous container port. Bindings with multiple target ports require separate declarations.

The generated network uses IPv4. Cross-container connections and direct mappings therefore require a declared 0.0.0.0 endpoint; a compatible alternative in the same binding may supply it. Selection preserves scheme, path and the chosen route protocol. Loopback, fixed IP, DNS and IPv6-only declarations cannot establish reachability on this generated network and fail when a connection/publication is required. none can retain an internal-only declaration, but cannot make a proxied loopback endpoint reachable. Host publication can use a literal IPv4 or IPv6 address independently. No address is rewritten in the application's configuration.

ServicePlan.PortMapping stores address, transport, host/container ports and application-scoped binding/endpoint references. Identical physical mappings within one application combine references; conflicting mappings fail. Socket conflicts compare transport, address family, address and host port. TCP and UDP can share a number. ContainerBundle validates identities, endpoint references, selected connections and publication relationships using BCL-only models; the executor does not depend on Core.

### nginx handoff and rendering

WebPackage requires the installation-root JSON .bindings plus .web/nginx/<package-name>.conf and its .template companion. Sites refer to nginx-owned listener endpoints and application-owned route targets. Empty sites require no Web assets; nonempty sites require a complete handoff. Containerizer does not infer metadata from arbitrary nginx directives.

WebIngress substitutes the selected application DNS authority for {{zongsoft:binding:B:E}}. Both identities must belong to a declared route, and every referenced route target must occur in the template. TLS names and HTTP Host policy remain unchanged. The RPM native-host system CA bundle path is adapted to the nginx image's system CA bundle; explicit trust files use managed file markers.

The UTF-8 path encoded in {{zongsoft:file:BASE64}} is replaced with a managed resource path. Packaged relative resources resolve against the application root and must be regular files. External absolute paths require an explicit file! declaration on nginx. Links, invalid paths, overlapping destinations and unknown markers fail.

The nginx main configuration includes applications in application-ID order. Assets are mounted read-only under config/nginx, while user output retains component order. Each listener is identified by transport, address and port. A unique explicit default wins; otherwise the first site in actual include/declaration order is selected. Shared listeners validate host ownership and effective probe routing.

Nginx settings retain port=80:18080,443:none. An override applies to each declared transport using that container port, allowing TCP and QUIC/UDP on the same number. Published listeners require IPv4 wildcard addresses; optional IPv6 wildcard listeners must have matching site/default relationships. Explicit none is recorded on the source binding. TCP Web publications require a concrete probe hostname when only wildcard names are declared. UDP is preserved in Compose and the bundle protocol, and is never presented as a successful TCP/HTTP probe. Caddy/haproxy do not consume this handoff.
### Infrastructure services

`ServiceOptions` owns environment merging, parameters, ports, storage and Redis/Valkey persistence/authentication commands. After images are pinned, `ServiceImagePreparer` requires a template or image health check, sets authentication environment values and resolves numeric UID/GID; `ContainerEngine` owns actual engine operations.

Data-owner accepts numeric UID:GID or an account/group in the pinned image, including images whose root entrypoint subsequently drops privileges. Persistent directory ownership is set before startup while preserving cleanup markers. RHEL-family binds request private SELinux labels rather than disabling SELinux.

Temporary data uses anonymous volumes. Infrastructure starts use no-recreate; application/ingress recreation renews their anonymous volumes. Ordinary uninstall removes this application's containers and temporary volumes; persistent directories follow separate ownership records.

## Bootstrap and bundle protocol

Bootstrap collects Docker, Compose and dependencies for the target distribution/architecture, recording package name, version, architecture, HTTPS source URL, dependencies, hash and length. Debian/Ubuntu and RPM families use matching collectors. RHEL requires a prepared import instead of substituting Rocky/Alma packages. RPM collections also contain `requirements.txt` and `repodata/` generated by `createrepo_c`; the plan records root package names in `requirements` and the relative metadata directory in `repository`.

Explicit collections live in `source/.containerizer/bootstrap/<distribution>_<architecture>/` with `bootstrap.lock.json` and packages. build/Import-Bootstrap.ps1 creates that structure from a collection with `metadata.tsv` using Collection, Profile, BaseImageReference and Destination. BaseImageReference must be an exact repository@sha256:... reference. Imported and cached collections are verified when consumed; a lock file's presence does not bypass file checks.

Offline includes package files. Online retains the same lock/metadata, downloads recorded files on the target, verifies them and runs a local package transaction with online repositories disabled. RPM metadata is bundled and covered by bundle integrity checks in both modes. The executor stages verified RPM files and metadata in a private local repository, disables all other repositories and passes only root dependencies to DNF as `name >= 0` capability requirements. The dependency collection provides candidates instead of forcing every package to be installed, so compatible installed providers such as `coreutils-single` remain eligible. GPG checks are disabled for this local repository; bundle and package SHA-256 integrity checks run before execution. Dependency skipping and forced removal are not enabled. When Docker already exists, the implementation checks the daemon, Compose and command capabilities and reuses it; it does not reinstall every locked package or automatically upgrade the existing engine.

Bundle roots have this layout, including optional directories only when needed:

```text
containerizer                  Native executor
zh-Hans/                       Localized resources
install.sh / uninstall.sh       Thin launchers
README.md / README.zh-Hans.md   Bundle-specific usage
name[-tag]@version-arch.container
containerizer.json             Execution plan
checksums.sha256                Complete file checksums
compose.yaml                   Generated runtime configuration, without build
images/                        Offline image archives
packages/                      Bootstrap metadata and offline packages
config/                        Read-only runtime assets
migration/                     Original migration archives and launchers
```

The current JSON protocol is 1. Model properties serialize as camelCase, unknown members are rejected. Package/bootstrap full base references use `baseImageReference`; booleans such as `isInMaintenance`, `isReinstall` and `isExplicitDefault` directly reflect model semantics.

The `containerizer.json` hash identifies release content; version text alone cannot replace content identity. Files lists other assets with path, length and SHA-256, while `checksums.sha256` additionally includes JSON's own hash. `SourceHash` identifies the included completed manifest. Opening a bundle validates protocol, integrity, extra files, paths, image platforms, service identities, Web dependency associations and mount constraints. Checksums detect consistency; they are not signatures or origin authentication.

Compose project identity combines normalized name with a name hash, excluding tag/version. Assets fix image, platform, pull_policy=never, ownership labels, log rotation and read-only/writable mounts. Startup neither builds nor implicitly pulls. Manager controls dependency order and starts individual services without recreating dependencies.

## Installation transactions and recovery

Installation uses an application lock, with a short-lived host lock protecting the shared executor, registration and directory ownership. Locks live under `/run/containerizer` outside purgeable assets. Host and application lock filenames are distinct.

| Stage | Behavior |
| --- | --- |
| Verification and registration | Validate bundle, Linux/root/distribution/architecture, disk and ports; check shared executor ownership/protocol capability, persist and reverify assets, register the transaction |
| `PrepareBootstrap` / `PrepareImages` | Prepare the engine, import offline or fetch pinned online images, verify ID/platform and apply target references |
| Maintenance barrier | Prepare owned directories, save `IsInMaintenance` before stopping applications/ingress |
| `StartInfrastructure` / `CheckLocalInfrastructure` | Start infrastructure for first installation or reinstall after ordinary uninstall; verify existing infrastructure during upgrades |
| `ApplyMigration` | Persist attempts, invoke original migration launchers and check again after success |
| `ReadyToStart` | Stop here for no-start or pre-existing maintenance |
| `StartApplications` / `StartIngress` / `CheckHealth` | Start applications then ingress and verify health |
| `CommitRelease` | Restore restart policies, save success history/current release and clear pending |

Prepare returns `Prepared` after image preparation. It may already have installed the shared executor and system dependencies, so it is not read-only. It does not enter stopping/migration stages. Reinstalling the same successful release checks images and health without rerunning SQL.

Upgrades compare name, distribution, architecture, project and dataRoot. Infrastructure IDs, image IDs/digests, effective configuration hashes, bootstrap package name/version/hash sets and root dependency sets must match. Effective configuration includes environment, arguments, ports, mounts, template-derived runtime fields and configuration file contents. Ordinary uninstall followed by reinstall cannot bypass these checks.

### Maintenance and failure

`EnterMaintenanceAsync` is the common stopping entry. Manager saves the maintenance flag first. `DockerHost` saves original restart policies before modifying containers, then sets restart=no and stops them. One stopping operation validates/materializes historical release plans once and identifies application/ingress containers by ownership labels. Stop does not shut down infrastructure. Save failure prevents crossing the persistence barrier; partial stop failure leaves persisted state for follow-up.

Phase starts, successes, failures and migration attempts are persisted. Failure retains the original transaction and diagnostics without rolling back system/data changes or automatically starting old applications. Recover verifies the original assets/ID, continues that transaction and leaves ReadyToStart. It cannot substitute another bundle or erase failure history. Start requires a startable state; restart rejects maintenance or pending transactions.

Migration state is retained per migration version. Successful records are reused only after the original launcher's check succeeds; a new archive with the same version cannot skip that check merely because the version matches. Started, Failed, `Interrupted` and Unknown require explicit retry-migration for the corresponding version in the current transaction. Attempts are saved before execution and become `Succeeded` only after apply and check succeed. Containerizer logs retain version, operation and exit code rather than arbitrary script output or full SQL.

### Asset retention and cleanup

`InstallationStore` owns `apps/<name>/installation.json`, `releases/<id>/assets`, success history and migration directories. Once copied and verified, managed assets supply stable mount/recovery paths independently of the original extraction directory. Shared executor reuse/replacement checks ownership hashes and protocol ranges; unknown existing files conflict. Application version numbers do not select executor updates.

Ordinary uninstall retains registration, release assets, migration records, logs, persistent directories and images, removing this application's containers, anonymous volumes and networks. Reinstallation restores infrastructure containers and rechecks retained state without reinitializing preserved data.

Purge proceeds through: save `Uninstalling` → maintenance/host resource cleanup → save resource checkpoint → delete release assets → finish directory/log cleanup → delete registration last. Failure persists `CleanupFailed` and residual reasons; retry skips the completed resource stage. Manager owns ordering/checkpoints, while Store owns history, release asset and final registration file operations.

Deletion verifies actual ownership, directory markers, parent relationships, links and mount boundaries. Missing evidence cannot count as successful cleanup. Removing this application's image references preserves content still used by other containers or deployments. The shared executor, engine, other applications, original bundle media and remote databases remain outside cleanup; no reverse migrations or global prune are performed.

## Preview sessions and process execution

Run first validates the archive with shared `ContainerBundle`, then creates a privileged systemd container labelled with session/application ownership, without mounting the outer engine socket. The outer native architecture must match. Inner Docker uses overlay2 and independent storage; containerd uses a session anonymous volume.

Installation state and business data start fresh each session. Inner `ImageCache` cleanup removes containers, business volumes, custom networks, application/old images and extra tags, retaining only current infrastructure/ingress image IDs. The inner engine stops before writing the clean marker. Application-only bundles do not retain empty image storage. Reuse checks ownership, mount occupancy, environment, clean marker and image identity. Failed cleanup discards the session cache; unknown or occupied volumes produce errors.

TCP forwarding uses inner socat to reach actual host publications, and the outer layer reads real port mappings with bounded conflict retries. Web probes connect to local IPv4 while preserving the request hostname, Host and SNI, disable proxies/automatic redirects and use system certificate validation. HTTP 5xx fails; other responses, including 404, demonstrate an answering entry. This differs from the container's listener-liveness check. Ordinary TCP endpoints without Web semantics only test connectivity. UDP, cluster advertisement addresses and application callbacks are not rewritten.

`RunContext` remains in the foreground and keeps established failure scenes until cancellation. Cleanup uses independent timeouts rather than the cancelled work token. Exit status distinguishes post-readiness exit, startup cancellation and original failure: a graceful exit after readiness returns 0, cancellation during startup returns 130, and a retained failure keeps its original code. Cleanup failure cannot report success. Application locks and engine labels protect concurrent sessions and resources left after forced termination.

`IProcessRunner` provides captured `RunAsync` and instance streaming StreamAsync. Engine operations, preview installation and target logs all use the injected runner. `ArgumentList` preserves argument boundaries, with separate UTF-8 stdout/stderr handling. Captured mode continuously drains both pipes while bounding diagnostic buffers; streaming forwards lines and returns the original exit code. Cancellation terminates the process tree. Maker output retains localized callbacks and colors without test-only wrappers.

## Platform and verification boundaries

| Level | Current evidence and scope |
| --- | --- |
| Implemented paths | Windows/Linux maker paths; Docker/Podman build/preview branches; Linux x64/ARM64 targets; accepted distributions listed in README |
| Automated validation | Strict builds for all maker target frameworks, both test projects, Linux executor tests, and configuration/cache/Web/process cancellation/installation/cleanup failure coverage |
| Native compilation | Linux x64 and ARM64 Native AOT compilation; x64 protocol entry execution; ARM64 compilation is not ARM64 runtime acceptance |
| Isolated actual execution | Windows + rootful Podman builds/previews: tar/deb on Debian 13 x64 and rpm on Rocky 9 x64; daemon, Web, offline bootstrap, isolated MySQL/object-storage migrations, HTTP/metrics access, port avoidance, session data isolation and cleanup |
| Without complete target acceptance | Outer Docker, rootless engines, other distribution combinations, real ARM64, complete domain HTTPS applications, standalone production hosts and actual business migrations |

Template architecture lists declare input capability, not compatibility of every image tag. Native AOT still requires Linux system libraries; preview bases prepare ICU and other prerequisites, while standalone targets must also meet runtime requirements. Package transactions and lifecycle validation use dedicated isolated environments. Unit tests, cross-compilation and shared-kernel previews do not establish production acceptance.

Current exit categories are 0 success; 2 input; 3 platform/capability; 4 acquisition/integrity/execution; 5 bootstrap transaction; 6 health; 7 migration/recovery/executor cancellation; 8 target lock conflict; 9 uninstall cleanup; and 130 for maker/preview startup cancellation. Interpret the code together with the command's phase diagnostics and residual state.

Terminal warnings use dark-yellow text with bold cyan parameters; errors use dark-red text with bold magenta parameters; success messages use green text. Exception types are highlighted in magenta and messages retain their original text. All four tools share the repository’s `.shared/Utility.Output.cs`: `FormatOutput` and `FormatError` construct Core `CommandOutletContent`; `WriteOutput` and `WriteError` route terminal and text-writer output; tools using the command executor call `HandleFailure` to display exceptions and mark them handled, while retaining their own exit codes. Terminals renders the colors. Redirected output and caller-provided error writers preserve plain text and their original streams. The same shared file provides `HighlightSyntax` for help text and `MeasureOutput` for stage timing. Timing messages use the caller's localized format and stage name and are emitted on first disposal, including when a stage fails.

### Model ownership and serialization

`ServicePlan.PackageInfo`, `Mount`, `HealthCheck` and `WebSite` describe their service's input package, mounts, health and sites. A site's `BindingInfo` stores ingress listeners and source binding/endpoint references. `ContainerBundlePlan.FileEntry` stores relative path, length and hash; `Migration` stores this bundle's migration archive, script, version and combined identity. Owner partial files maintain these types. Nesting does not add JSON layers or change field names.

`ImagePlan` also serves engine resolution and runtime-environment caches; `BootstrapPlan` supports independent cache/import documents. Both remain independent. `ContainerBundle` owns archive reading and disposal, while `ContainerBundleBuilder` owns creation.

The [Packager implementation reference](../../packager/docs/implementation.md#binding-document) defines fields, identities and complete data origins. For example, `0.0.0.0:8069` is a source listener, proxy connection `demo-app:8069` derives from the container service name, and host `127.0.0.1:18080` comes from a publication override. Endpoint, connection and port-mapping models retain these distinct values. `none` cancels direct publication without changing proxy reachability.

Validate application/binding/endpoint ownership in `ports[].bindings`, and source/selected alternative endpoints in `bindings[].connections`. Conflict checks include owner, address family, IP, transport and host/container ports. TCP and UDP can share port 8443; IPv4 and IPv6 do not imply each other. HTTP/3 needs UDP publication and is not verified by a TCP probe. Default process health only checks PID 1; scraping, reflection and business-handler success are separate checks. Component configuration still owns ZeroMQ discovery, OPC registration and public endpoints; publication overrides do not rewrite protocol fields.
