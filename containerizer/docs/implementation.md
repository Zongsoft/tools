[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# Containerizer implementation

This document describes current responsibilities, data contracts and lifecycle behavior. See the [README](../README.md) for usage, the [template reference](templates.md) for template fields, and [SKILL](../SKILL.md) for development procedures.

## Architecture boundaries

Containerizer manages one host's delivery. Packager owns application packages and Migrator owns migration archives. Containerizer consumes their artifacts and launchers without referencing their executable projects or coordinating remote nodes.

| Area | Entry points and responsibilities |
| --- | --- |
| Maker input | `ManifestFactory` handles command context, variables, paths, version conflicts and input selection; `ContainerManifest` owns data, Profile reading, dependency completion and its own validation |
| Service planning | `ServicePlanner` prepares services for plan/make, diagnoses missing settings, invokes Web planning and validates dependencies and ports |
| Applications and templates | `ApplicationPlanner` derives entrypoints, environment, health and build-time runtime requirements; static `TemplateCatalog` reads infrastructure/ingress templates |
| Build coordination | `DeliveryBuilder` owns the workspace, image stages, bootstrap, migration collection, verification and publication; `ServiceBuildContext` keeps its component association read-only from creation |
| Engine and images | `ContainerEngine` performs engine operations; `ImageReference` owns repository/tag/digest rules; `ServiceImagePreparer` adapts health checks, Redis/Valkey authentication and data accounts |
| Application images | `ApplicationImageBuilder` installs application packages and creates final images; `RuntimeEnvironmentCache` owns reusable OS/runtime filesystems |
| Local preview | `RunContext` owns an entire session; its private `Endpoint` and `ImageCache` types handle forwarding and inner image storage |
| Target execution | `InstallationManager` controls transactions, `DockerHost` operates the OS and Docker, and `InstallationStore` owns state, history, assets and locks |
| Shared source | .shared/Containerization.props directly links protocol models, file validation and process helpers; no shared DLL is produced |

The maker uses Core Profile, ConnectionSettings and the repository's shared variable workflow. The executor is BCL-only and consumes JSON and generated assets, without parsing .container, Profile or YAML. Generated `compose.yaml` uses JSON syntax and is consumed by Compose.

Sessions, locks, resource lifetimes and installation transactions retain their own complete coordination boundaries. Pure rules use stateless types; side effects go through the existing engine, host and process boundaries. Visibility follows production callers: tests do not widen private members or introduce test-only entry points.

## Maker data flow

```text
Command context + local inputs
  → ManifestFactory / ContainerManifest
  → ServicePlanner
      ├─ plan: save draft
      └─ make: DeliveryBuilder
          → pin images → build applications / export images → adapt service images
          → bootstrap / configuration / migrations / Compose
          → completed manifest / JSON / checksums → compress → publish
```

Components retain declaration order, with automatically added nginx appended. Build contexts directly reference their components rather than relying on parallel list indices. Image identities are established before writing; serialization only reads the model.

Plan and make share service preparation. Plan does not connect to an engine, read .mirrors or collect system dependencies. Missing required settings produce diagnostics and a saved draft; other invalid structures still fail. Runtime resolution remains at the application image build entry, rather than moving make-stage external work into plan.

### Profiles, variables and paths

A .container accepts a fixed root/component vocabulary and rejects unknown fields, duplicate components/fields and nested components. Profile imports use Core; imported files participate in declaration validation, and source resolves relative to its declaring file. Imports are disabled for .settings and templates.

Shared Utility gathers variables from defaults, the system environment, .env files from the filesystem root down to source, and command context in the established precedence order. Section names join with underscores. Final root fields enter the invocation's variable view. Evaluation neither exports the whole environment nor modifies the process environment, and a matching name alone never creates a binding. Input selection and root paths evaluate immediately; service settings/environment references survive plan and evaluate per value during make. Completed manifests replay literal values; replanning a completed manifest escapes literal references to prevent reevaluation.

The CLI source resolves against the invocation directory; other managed local paths resolve against final source. Template configuration files resolve against the template directory. Linux container paths have separate validation independent of the maker OS. Generated manifests express source relative to output, and output/local component inputs relative to source.

Release names use name[-tag]@version-architecture. Core numeric versions have 2–4 parts, each at most 65535, and must be nonzero. Automatic versions use year modulo 1000, month and day, adding/incrementing a fourth part on conflicts. Explicit version conflicts fail. A build may complete its own draft in place on success, but cannot overwrite an existing archive.

### Package and migration inputs

`PackageReader.Select` returns both a path and the descriptor already read. Preparation reuses that descriptor within the invocation and rereads it when the component package path changes. Web resource extraction still rereads the package, and delivery integrity checks still run; there is no cross-command package metadata cache.

Supported Packager inputs are tar with a matching Shell launcher, deb and rpm. Readers extract identity, architecture, installation directory, service files, `runtimeconfig` and Web metadata. `Listen` comes from tar PAX, Debian control or an RPM custom tag. Directory selection checks the directory and then .packages, preferring the distribution's native format before tar in each location, with architecture and name-prefix filtering; multiple remaining candidates prompt for a selection, and file dates never choose a version.

Applications require one unambiguous service definition, `ExecStart` and WorkingDirectory. Entrypoints are foreground argument arrays; ambiguous Shell wrappers or complex commands must be corrected upstream. Application configuration is installed into the image with the original package rather than extracted by extension into target configuration mounts.

Migration inputs are explicitly selected `<prefix>(migrate)@<version>_linux-<architecture>.tar.gz` files with matching .sh launchers. CLI directory selection allows multiple candidates. Manifests read positive numeric migration# indices and validate ascending, unique versions and matching architecture. No selection means no migrations; nothing is inferred from application packages or default directories. Building does not parse business SQL or contact databases; it records the combined archive/launcher hash and ships the files unchanged. Independent `hosting/containerize.cmd` wrappers provide menus, file selection and command invocation; the tool does not depend on those scripts or their default-directory conventions.

### Publication and output

Temporary build files live in an independent system workspace. The output directory receives the completed manifest, delivery archive and any required .settings tag additions. A direct complete build does not first publish a draft. Only a successful complete build from a component list fills missing service tags into `.settings`; plan, failed builds and manifest replay neither remerge nor write that file.

An output lock and `ArtifactPublisher` coordinate publication, checking the input hash to avoid overwriting edits made during a build. Internal and external completed manifests are byte-identical. Failure cleans this invocation's temporary resources while retaining existing inputs. Multiple final files cannot be committed by one filesystem operation; publication does not promise a cross-file power-loss transaction. Archives use gzip Fastest.

## Image identities and caches

Repository, source tag, target-platform manifest digest, multi-platform index digest and local image ID are distinct. `ImagePlan.Reference` is the full local reference used on the target. `SourceTag` and `SourceReference` describe the origin, Digest pins the target manifest, and Id supports post-import content checks. The OS is always Linux and architecture comes from the manifest root.

Local images are reused when identity and platform evidence is sufficient; otherwise they are resolved or pulled. Pinned replay cannot fall back to a tag, change platforms, or substitute an index digest/image ID for the target manifest. Creation time and engine-reported size are recorded when available and omitted otherwise, without extra network work for display metadata.

The manifest identity associates repository, tag, Linux and architecture. Changes invalidate previous digest, timestamp and size values. Changing only the delivery version, delivery tag or host distribution does not alter infrastructure image identity.

The maker exports infrastructure images by ID without attaching release-specific tags to cached images. Application builds, account probes and auxiliary builders use unique owned resources. Cleanup removes this invocation's containers and anonymous volumes, temporary images and builders, retaining useful source/tool images without global prune.

| Cache layer | Identity and contents | Validation and updates |
| --- | --- | --- |
| Engine images | Source images, platforms, digests and verified associations | Resolve again when content/platform evidence mismatches; infrastructure tags are outside refresh scope |
| Shared application runtime | Grouped by engine, distribution, architecture, runtime family and major/minor; OS files, curl/CA and runtime, without applications | Signature includes base digest, platform and install recipe; archive hash/length, environment and runtime requirements are validated |
| Bootstrap | System package sets in the user cache, isolated by normalized source hash, distribution and architecture | Explicit import takes precedence over cache; outside refresh scope |
| Run base image | Outer-engine image with systemd and forwarding tools, identified by recipe and architecture | Ownership checked; contains no installed application state |
| Run image storage | Per-application inner Docker storage volume | Reused only after complete cleanup, environment matching and a clean marker |

The file cache root is %LOCALAPPDATA%/Zongsoft/containerizer on Windows. Linux uses absolute `XDG_CACHE_HOME`, otherwise ~/.cache, then appends Zongsoft/containerizer. Runtime environments live under `runtime/<engine>/<profile>/`.

Each shared runtime entry has a lock. One invocation refreshes each required environment at most once and resolves its OS base again. A new archive is fully produced and checked before atomically replacing the `environment.json` pointer; failure preserves the previous generation. Readers copy the selected generation while holding the lock before obsolete archives are removed. There is no time-based expiry. Runtime caches contain no application configuration, business data or database initialization results.

Before manual cleanup, stop builds/previews using the cache and delete only identified entries. Do not remove the locks directory while processes hold locks, or replace targeted cleanup with global image/volume prune. Delivery history and target migration state are not disposable caches.

### Registry mirrors

`RegistryMirrorSettings` reads Core Profile from the selected directory; shared `RegistryMirrors` handles exact registry matching, source ordering and cancellation. Sections, duplicate registry entries and empty candidate addresses are rejected. Mirrors change download paths, not logical repositories, pinned digests or platforms. Without a pinned digest, the first usable source may resolve a tag; subsequent sources must match the established identity.

Podman builds use verified local images and disallow pulling. Docker uses a dedicated BuildKit builder and temporary configuration, removing the builder/cache on completion. Run passes rules to inner online pulls through temporary JSON outside delivery assets. Neither .mirrors nor actual mirror addresses enter completed manifests or delivery JSON. Global engine configuration, package sources and credentials remain outside this mechanism.

## Applications, Web and service preparation

### Runtime and application health

`ApplicationPlanner` selects .NET/ASP.NET Core runtime requirements from the entry DLL's `runtimeconfig`; explicit runtime-* dependencies must match. Self-contained entrypoints do not add a framework runtime, but still prepare probe dependencies. Builds install an available patch in the required major/minor line, verify it is no older than the package's minimum patch and record the version actually reported by the final image. Ubuntu 22.04 uses `ppa:dotnet/backports` for .NET 9 and later runtimes; other Debian/Ubuntu paths use the corresponding Microsoft package repository, while RPM paths use the RHEL-family repository.

Application packages execute their original installation flow inside the build environment, with service startup controlled by the build adapter. The final image retains runtime files and a foreground entrypoint rather than using the target executor as its application launcher.

Absent or empty `Listen` uses kill -0 1 for process liveness. Nonempty `Listen` must contain semicolon-separated HTTP/HTTPS root URLs without credentials, non-root paths, queries or fragments. Loopback/wildcard/DNS listeners become all-interface container bindings; fixed non-loopback IPs are rejected.

Health probes select the first HTTP listener, otherwise the first HTTPS listener, and GET / inside the container. Any HTTP response indicates listener liveness; redirects are not followed. HTTPS requires a DNS identity and uses SNI and the image trust store. Connection, timeout and TLS validation failures are unhealthy. Defaults are a 10-second interval, 5-second timeout, 12 retries and 30-second start period. This does not establish business readiness.

### nginx handoff and rendering

`WebPackage` accepts a single application root containing .web/nginx/ with:

| File | Purpose |
| --- | --- |
| .bindings | Profile/INI site identities, hostnames, original bindings and explicit default relationships |
| `<package-name>.conf` | Packager's native-host configuration |
| `<package-name>.conf.template` | Container rendering input with semantic placeholders |

Each .bindings site accepts only host, bind and default, with comma/semicolon lists. Bind uses HTTP/HTTPS IP URLs with explicit ports. Default must reference a declared binding in the same site. Host supports concrete names and forms such as *.example.com, .example.com and example.*. Containerizer does not infer bindings by reparsing arbitrary nginx configuration.

`WebIngress` substitutes the application ID for {{zongsoft:application}} and replaces the UTF-8 path encoded in {{zongsoft:file:BASE64}} with a managed resource path. Unknown or unresolved markers fail. Relative packaged resources resolve against the application root and must be existing regular files. External absolute paths require explicit file! declarations on nginx. Links, invalid paths and overlapping targets are rejected; corresponding maker-side system paths are never read implicitly.

The nginx main configuration includes applications in application-ID order. Delivery assets use `config/nginx/nginx.conf`, `config/nginx/sites/<application>.conf` and distinct resource paths, all mounted read-only. User output still follows component declaration order.

Ingress planning validates protocol consistency, default selection, hostname conflicts and actual routing for each listener address/port. A listener uses its unique explicit default when present; otherwise it selects the first site in application-ID configuration load order and site declaration order, matching the generated includes. Published ports require IPv4 wildcard listeners; matching IPv6 wildcard bindings, when present, must have equivalent site/default relationships. Nginx port settings remap or disable original ports without rewriting listeners. Published wildcard-only sites need a matching concrete probe-host. Hostless sites must win the actual default route; a reachable port alone does not establish correct routing.

`ServicePlan.Web` stores application/site identity, Hosts, ProbeHosts and Bindings. Bindings include `IsExplicitDefault`, `IsDefault` and Publication; `PortPlan` describes physical publication only. Applications without hosting assets derive Web records and loopback publication directly from Listen. Declaring nginx without complete hosting assets fails. Caddy/haproxy do not participate in this handoff.

### Infrastructure services

`ServiceOptions` owns environment merging, parameters, ports, storage and Redis/Valkey persistence/authentication commands. After images are pinned, `ServiceImagePreparer` requires a template or image health check, sets authentication environment values and resolves numeric UID/GID; `ContainerEngine` owns actual engine operations.

Data-owner accepts numeric UID:GID or an account/group in the pinned image, including images whose root entrypoint subsequently drops privileges. Persistent directory ownership is set before startup while preserving cleanup markers. RHEL-family binds request private SELinux labels rather than disabling SELinux.

Temporary data uses anonymous volumes. Infrastructure starts use no-recreate; application/ingress recreation renews their anonymous volumes. Ordinary uninstall removes this application's containers and temporary volumes; persistent directories follow separate ownership records.

## Bootstrap and delivery protocol

Bootstrap collects Docker, Compose and dependencies for the target distribution/architecture, recording package name, version, architecture, HTTPS source URL, dependencies, hash and length. Debian/Ubuntu and RPM families use matching collectors. RHEL requires a prepared import instead of substituting Rocky/Alma packages.

Explicit collections live in `source/.containerizer/bootstrap/<distribution>_<architecture>/` with `bootstrap.lock.json` and packages. build/Import-Bootstrap.ps1 creates that structure from a collection with `metadata.tsv` using Collection, Profile, BaseImageReference and Destination. BaseImageReference must be an exact repository@sha256:... reference. Imported and cached collections are verified when consumed; a lock file's presence does not bypass file checks.

Offline includes package files. Online retains the same lock/metadata, downloads recorded files on the target, verifies them and runs a local package transaction with online repositories disabled. When Docker already exists, the implementation checks the daemon, Compose and command capabilities and reuses it; it does not reinstall every locked package or automatically upgrade the existing engine.

Delivery roots have this layout, including optional directories only when needed:

```text
containerizer                  Native executor
zh-Hans/                       Localized resources
install.sh / uninstall.sh       Thin launchers
README.md / README.zh-Hans.md   Delivery-specific usage
name[-tag]@version-arch.container
containerizer.json             Execution plan
checksums.sha256                Complete file checksums
compose.yaml                   Generated runtime configuration, without build
images/                        Offline image archives
packages/                      Bootstrap metadata and offline packages
config/                        Read-only runtime assets
migration/                     Original migration archives and launchers
```

The current JSON protocol is 1. Model properties serialize as camelCase, unknown members are rejected, and old field aliases are not provided. Package/bootstrap full base references use `baseImageReference`; booleans such as `isInMaintenance`, `isReinstall` and `isExplicitDefault` directly reflect model semantics.

The `containerizer.json` hash identifies release content; version text alone cannot replace content identity. Files lists other assets with path, length and SHA-256, while `checksums.sha256` additionally includes JSON's own hash. `SourceHash` identifies the included completed manifest. Opening a delivery validates protocol, integrity, extra files, paths, image platforms, service identities, Web dependency associations and mount constraints. Checksums detect consistency; they are not signatures or origin authentication.

Compose project identity combines normalized name with a name hash, excluding tag/version. Assets fix image, platform, pull_policy=never, ownership labels, log rotation and read-only/writable mounts. Startup neither builds nor implicitly pulls. Manager controls dependency order and starts individual services without recreating dependencies.

## Installation transactions and recovery

Installation uses an application lock, with a short-lived host lock protecting the shared executor, registration and directory ownership. Locks live under `/run/containerizer` outside purgeable assets. Host and application lock filenames are distinct.

| Stage | Behavior |
| --- | --- |
| Verification and registration | Validate delivery, Linux/root/distribution/architecture, disk and ports; check shared executor ownership/protocol capability, persist and reverify assets, register the transaction |
| `PrepareBootstrap` / `PrepareImages` | Prepare the engine, import offline or fetch pinned online images, verify ID/platform and apply target references |
| Maintenance barrier | Prepare owned directories, save `IsInMaintenance` before stopping applications/ingress |
| `StartInfrastructure` / `CheckLocalInfrastructure` | Start infrastructure for first installation or reinstall after ordinary uninstall; verify existing infrastructure during upgrades |
| `ApplyMigration` | Persist attempts, invoke original migration launchers and check again after success |
| `ReadyToStart` | Stop here for no-start or pre-existing maintenance |
| `StartApplications` / `StartIngress` / `CheckHealth` | Start applications then ingress and verify health |
| `CommitRelease` | Restore restart policies, save success history/current release and clear pending |

Prepare returns `Prepared` after image preparation. It may already have installed the shared executor and system dependencies, so it is not read-only. It does not enter stopping/migration stages. Reinstalling the same successful release checks images and health without rerunning SQL.

Upgrades compare name, distribution, architecture, project and dataRoot. Infrastructure IDs, image IDs/digests, effective configuration hashes and bootstrap package name/version/hash sets must match. Effective configuration includes environment, arguments, ports, mounts, template-derived runtime fields and configuration file contents. Ordinary uninstall followed by reinstall cannot bypass these checks.

### Maintenance and failure

`EnterMaintenanceAsync` is the common stopping entry. Manager saves the maintenance flag first. `DockerHost` saves original restart policies before modifying containers, then sets restart=no and stops them. One stopping operation validates/materializes historical release plans once and identifies application/ingress containers by ownership labels. Stop does not shut down infrastructure. Save failure prevents crossing the persistence barrier; partial stop failure leaves persisted state for follow-up.

Phase starts, successes, failures and migration attempts are persisted. Failure retains the original transaction and diagnostics without rolling back system/data changes or automatically starting old applications. Recover verifies the original assets/ID, continues that transaction and leaves ReadyToStart. It cannot substitute another delivery or erase failure history. Start requires a startable state; restart rejects maintenance or pending transactions.

Migration state is retained per migration version. Successful records are reused only after the original launcher's check succeeds; a new archive with the same version cannot skip that check merely because the version matches. Started, Failed, `Interrupted` and Unknown require explicit retry-migration for the corresponding version in the current transaction. Attempts are saved before execution and become `Succeeded` only after apply and check succeed. Containerizer logs retain version, operation and exit code rather than arbitrary script output or full SQL.

### Asset retention and cleanup

`InstallationStore` owns `apps/<name>/installation.json`, `releases/<id>/assets`, success history and migration directories. Once copied and verified, managed assets supply stable mount/recovery paths independently of the original extraction directory. Shared executor reuse/replacement checks ownership hashes and protocol ranges; unknown existing files conflict. Application version numbers do not select executor updates.

Ordinary uninstall retains registration, release assets, migration records, logs, persistent directories and images, removing this application's containers, anonymous volumes and networks. Reinstallation restores infrastructure containers and rechecks retained state without reinitializing preserved data.

Purge proceeds through: save `Uninstalling` → maintenance/host resource cleanup → save resource checkpoint → delete release assets → finish directory/log cleanup → delete registration last. Failure persists `CleanupFailed` and residual reasons; retry skips the completed resource stage. Manager owns ordering/checkpoints, while Store owns history, release asset and final registration file operations.

Deletion verifies actual ownership, directory markers, parent relationships, links and mount boundaries. Missing evidence cannot count as successful cleanup. Removing this application's image references preserves content still used by other containers or deployments. The shared executor, engine, other applications, original delivery media and remote databases remain outside cleanup; no reverse migrations or global prune are performed.

## Preview sessions and process execution

Run first validates the archive with shared `DeliveryBundle`, then creates a privileged systemd container labelled with session/application ownership, without mounting the outer engine socket. The outer native architecture must match. Inner Docker uses overlay2 and independent storage; containerd uses a session anonymous volume.

Installation state and business data start fresh each session. Inner `ImageCache` cleanup removes containers, business volumes, custom networks, application/old images and extra tags, retaining only current infrastructure/ingress image IDs. The inner engine stops before writing the clean marker. Application-only deliveries do not retain empty image storage. Reuse checks ownership, mount occupancy, environment, clean marker and image identity. Failed cleanup discards the session cache; unknown or occupied volumes produce errors.

TCP forwarding uses inner socat to reach actual host publications, and the outer layer reads real port mappings with bounded conflict retries. Web probes connect to local IPv4 while preserving the request hostname, Host and SNI, disable proxies/automatic redirects and use system certificate validation. HTTP 5xx fails; other responses, including 404, demonstrate an answering entry. This differs from the container's listener-liveness check. Ordinary TCP endpoints without Web semantics only test connectivity. UDP, cluster advertisement addresses and application callbacks are not rewritten.

`RunContext` remains in the foreground and keeps established failure scenes until cancellation. Cleanup uses independent timeouts rather than the cancelled work token. Exit status distinguishes post-readiness exit, startup cancellation and original failure: a graceful exit after readiness returns 0, cancellation during startup returns 130, and a retained failure keeps its original code. Cleanup failure cannot report success. Application locks and engine labels protect concurrent sessions and resources left after forced termination.

`IProcessRunner` provides captured `RunAsync` and instance streaming StreamAsync. Engine operations, preview installation and target logs all use the injected runner. `ArgumentList` preserves argument boundaries, with separate UTF-8 stdout/stderr handling. Captured mode continuously drains both pipes while bounding diagnostic buffers; streaming forwards lines and returns the original exit code. Cancellation terminates the process tree. Maker output retains localized callbacks and colors without test-only wrappers.

## Platform and verification boundaries

| Level | Current evidence and scope |
| --- | --- |
| Implemented paths | Windows/Linux maker paths; Docker/Podman build/preview branches; Linux x64/ARM64 targets; accepted distributions listed in README |
| Automated validation | Strict builds for all maker target frameworks, both test projects, Linux executor tests, and configuration/cache/Web/process cancellation/installation/cleanup failure coverage |
| Native compilation | Linux x64 and ARM64 Native AOT compilation; x64 protocol entry execution; ARM64 compilation is not ARM64 runtime acceptance |
| Isolated actual execution | Windows + rootful Podman + Debian 13 x64 build/preview paths, with Web/infrastructure access, port avoidance, session data isolation and cleanup |
| Without complete target acceptance | Outer Docker, rootless engines, other distribution combinations, real ARM64, complete domain HTTPS applications, standalone production hosts and actual business migrations |

Template architecture lists declare input capability, not compatibility of every image tag. Native AOT still requires Linux system libraries; preview bases prepare ICU and other prerequisites, while standalone targets must also meet runtime requirements. Package transactions and lifecycle validation use dedicated isolated environments. Unit tests, cross-compilation and shared-kernel previews do not establish production acceptance.

Current exit categories are 0 success; 2 input; 3 platform/capability; 4 acquisition/integrity/execution; 5 bootstrap transaction; 6 health; 7 migration/recovery/executor cancellation; 8 target lock conflict; 9 uninstall cleanup; and 130 for maker/preview startup cancellation. Interpret the code together with the command's phase diagnostics and residual state.
