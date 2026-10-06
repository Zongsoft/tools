[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# Implementation and verification

The current baselines are [TASK#2](../TASK%232.md), [TASK#3](../TASK%233.md), [TASK#4](../TASK%234.md) and [TASK#5](../TASK%235.md), superseding conflicting earlier requirements without legacy compatibility. This document records implementation boundaries, evidence and outstanding runtime acceptance.

## Web binding handoff

[TASK#5](../TASK%235.md) records the final `.bindings`/template contract, protocol 1, publication and Host/SNI probes. Strict Release builds and IDE0049 verification pass; 363 maker, 77 executor and 642 packager tests pass. Real Windows/rootful Podman acceptance uses isolated Debian x64 deliveries: Nginx 80/8080 with internal application 8069, occupied-port reassignment, retained Host, shared read-only PEM resources and disabled HTTPS publication with working internal TLS. Both global tools were replaced from the local feed and all target-framework DLLs/native payload hashes match the builds.

Loopback HTTP succeeds, but this machine's WLAN address refuses the connection despite the engine's 0.0.0.0 publication record. LAN forwarding remains an environment acceptance limit; run lists interface addresses separately without claiming reachability. IPv6 external access and ARM64 hardware execution are unverified; ARM64 evidence is cross-compilation only. Earlier counts below belong to their named changes, not the current aggregate.

## make acceleration and TDengine

[TASK#4](../TASK%234.md) is the authoritative record for this iteration: confirmed design, implementation, live port checks, cleanup and before/after measurements. It overrides conflicting earlier plans without compatibility layers. Public runtime caching, `--refresh`, both hosting script entries and the TDengine Explorer mapping are implemented. See [README](../README.md#publication-cache-and-replay) for operation and cleanup.

## Architecture

| Area | Implementation |
| --- | --- |
| Maker | Core command entry, explicit CLI/root precedence, Profile/ConnectionSettings, source-relative inputs, shared variable pipeline |
| Inputs | Independent packager tar PAX, Debian ar/gzip and RPM header/gzip-cpio readers; migration artifact selection without payload interpretation |
| Sources | Built-in/custom templates, recorded configuration values, dependency and host-port validation |
| Images | Native engine cache verification, tag/default/repository selection, platform manifest digests, optional creation time/size, Docker-format export and application images retaining package-selected configuration |
| Bootstrap | Distribution-specific exact dependency collection; reusable/importable locks; online and offline use the same package hashes |
| Publication | System temporary staging; flat manifest/archive publication with rollback; identical archive-root manifest and source hash; success-only missing-default additions |
| Executor | Native AOT; JSON source generation; stable assets, registry and independent locks; persistent transaction phases, maintenance and migration attempts |
| Lifecycle | Eleven commands; upgrade constraints, explicit recovery, owned-resource uninstall/purge |

Maker and executor source-link [shared protocol code](../.shared/Containerization.props). Only the maker carries Core; the YAML parser and YamlDotNet dependency have been removed. Neither references another tool executable as a library.

Image manifest, index and configuration digests are parsed and compared with Core `Checksum`; `.container` validation shares the same OCI SHA-256 adapter. Malformed hexadecimal input becomes a validation error, and outgoing references retain canonical lowercase formatting. File/text hashing already uses `Checksum.Compute` in the maker, and template JSON arrays now use Core `GetArray`. Profile, ConnectionSettings and the shared variable pipeline remain in use; quoting and archive/path guards retain their format-specific behavior. The executor continues to use its existing BCL-only implementation.

Generated `.container` service sections no longer contain `platform` or `architecture`, and those section entries are rejected on input. The maker uses Linux and the root architecture for image identity and engine verification. Changing root architecture invalidates old digests and display metadata; changing only the Linux distribution retains infrastructure digests. Root/service tags have different meanings, and service imaging remains an explicit override. Executor JSON and Compose platform requirements are unchanged.

The maker hashes effective service configuration and protects Compose assets. Packager selects application configuration using hosting/.deploy/<scheme>/; containerizer keeps it inside the application image. One final image build copies the cleaned installed filesystem, retaining application configuration while removing installation media, package script databases and host service definitions. Packaged .bindings and .conf.template drive Web planning and configuration rendering; original files remain in the application image. The Web renderer assigns `config/nginx/nginx.conf` and `config/nginx/sites/<application>.conf` to generated configurations. Configuration sources can declare a relative delivery path; the shared collector copies them and records read-only mounts without Nginx-specific rules. Other assets retain target-based isolation. Template/ingress assets are bound read-only directly from the verified release, with no mutable runtime copy, compose.env or three-way comparison. Normal completion, handled failure and cancellation clean maker workspaces and per-attempt resources.

Infrastructure image resolution reuses verified source/shared cache references without creating delivery tags in the maker engine. Delivery tags are recorded in JSON/Compose and created on the target after image verification; offline archives are exported by image ID. Repeated releases do not add project/version tags to the maker cache. Temporary inspection containers are removed together with their anonymous volumes, while reusable source, runtime and toolkit images remain cached. Cleanup does not prune the global engine cache.

## Source organization

Following packager's responsibility-based file naming, related implementation lives under its owning type:

| Family | Files and responsibility |
| --- | --- |
| Commands | [ContainerizeCommand](../src/ContainerizeCommand.cs) shares execution; `.Plan.cs` and `.Make.cs` define derived commands, `.Help.cs` formats help. Overrides select manifest/build behavior without command-type tests |
| Maker models | [ContainerManifest.Component](../src/ContainerManifest.cs) and [PackageReader.Descriptor](../src/PackageReader.cs) nest models used by their owner; `PackageReader.Tar/Deb/Rpm.cs` split format readers |
| Service preparation | [ServiceBuildContext](../src/ServiceBuildContext.cs) carries effective service settings, environment, assets and plan; [ServiceDefaults](../src/ServiceDefaults.cs) reads shared defaults; [ComponentSelector](../src/ComponentSelector.cs) selects input components |
| Images | [ContainerEngine](../src/ContainerEngine.cs) handles engine operations; `ApplicationImageBuilder.Image.cs` separates image preparation from application metadata, and [BootstrapPackageBuilder](../src/BootstrapPackageBuilder.cs) prepares engine dependency packages |
| Delivery | `DeliveryBuilder.Services.cs` prepares service configuration; `DeliveryBuilder.Delivery.cs` locates executors and writes delivery instructions/scripts |
| Protocol | [DeliveryPlan](../.shared/DeliveryPlan.cs) describes the delivery; [Installation.Models](../.shared/Installation.Models.cs) nests transaction/ownership/migration state; [BootstrapPlan.Package](../.shared/BootstrapPlan.cs) nests dependency package metadata; JSON property names remain unchanged |
| Host paths | [Installation.Paths](../.shared/Installation.cs) centralizes FHS data/state/log/cache/runtime/executor roots; `GetDataPath` is shared by maker and executor, while application package paths remain separate |
| Executor | [DeliveryBundle](../.shared/DeliveryBundle.cs) verifies and opens delivery assets; [ExecutorArguments](../executor/src/ExecutorArguments.cs) parses commands; [IInstallationHost](../executor/src/IInstallationHost.cs) defines the installation host boundary. `DockerHost.Bootstrap/Services/Storage.cs` and `InstallationManager.Installation/Migrations/Recovery/Transaction/Uninstall.cs` separate host operations and transaction phases |

Ordinary string composition uses interpolation; multiline Dockerfile generation uses `StringBuilder`. Array initializers use collection expressions with explicit target types where inference requires them.

Long conditions are grouped by responsibility: named predicates describe port conflicts, image health checks and managed-directory boundaries. The shared `Files.IsLinuxPath` validates Linux paths on both sides; `Distribution.IsDebian` centralizes maker package-family selection. Resource keys and JSON property names remain stable. The delivery plan is stored as `containerizer.json`; maker and executor share `DeliveryPlan.FileName`, and the current delivery protocol is 1.

The maker uses Core `Versioning.Version.Number` for release/date versions, migration ordering/uniqueness and .NET runtime selection. Each numeric part is 0–65535; equivalent forms such as `1.0` and `1.0.0` compare equally, while explicit release/migration text is preserved. Image tags remain strings. The executor retains its BCL version handling and source-generated JSON without a Core dependency.

Maker messages use `CommandContext.Output`, `Terminal` and `CommandOutletContent` for colors/styles and multiline help. Terminal handles plain redirected output; diagnostics previously sent to stderr use `Terminal.Default.Error`. Shared subprocess forwarding and the BCL-only executor retain their existing streams.

## FHS storage and cleanup

The [README layout](../README.md) separates service data and durable installation/migration state under `/var/lib/containerizer`, attempt logs under `/var/log/containerizer`, re-creatable bootstrap assets under `/var/cache/containerizer`, and locks under `/run/containerizer`. Logs and cache use the existing ownership records and markers. Bootstrap registers ownership on the active installation object before side effects so later transaction saves retain it. Removed cache directories can be recreated without changing their recorded ownership. Ordinary uninstall preserves these assets; purge checks identity, links and mount boundaries, with registration removed last. Default service data cannot point into another application's namespace or managed state/log/cache/runtime roots. Single data mounts use the service directory directly; templates with multiple data mounts retain separate named subdirectories. MySQL therefore uses `/var/lib/containerizer/data/<name>/mysql` on the host and still mounts it at `/var/lib/mysql` inside the container. Read-only configuration mounts do not affect this count. No installed target data is automatically moved.

The FHS and naming refactor passes 221 maker and 60 executor tests on Windows, plus all 60 executor tests in the isolated Linux builder, including link and mount-boundary checks. Strict Release compilation covers all maker frameworks, with separate IDE0049 verification. This is filesystem/transaction and build evidence, not target installation acceptance.

## Local verification architecture and evidence

`RunCommand` owns command validation and Ctrl+C registration. `RunContext` owns one session and its cleanup; its `.Environment.cs` partial prepares the clean base and invokes the original installer, and nested `Endpoint` handles port allocation and host probes. They reuse `ContainerEngine`, `BuildStorage`, `ProcessRunner`, `Files` and styled `Output`. `DeliveryBundle` moved to linked shared source so maker and executor verify the same assets without referencing each other's executable project.

The executor resolves installation and recovery phase descriptions from bilingual resources while keeping persisted phase identifiers unchanged. `run` supplies the local interface culture through `LANG` for the installer process only; applications retain their packaged environment.

Internal installation state and test data belong to the outer verification container. Inner Docker uses overlay2 in one labeled image-store volume per application; containerd uses a session-owned anonymous volume. The nested `RunContext.ImageCache` validates ownership, attachments, environment profile and a clean marker before reuse. Cleanup removes inner containers, data volumes, custom networks, application/obsolete images and extra tags, stops the engine and only then records the clean marker. It retains current infrastructure/ingress image IDs; application-only deliveries retain no empty store. Identity/platform validation skips imports only for matching content, independent of release versions. Failed internal cleanup uses independent cancellation budgets for outer-container and whole-cache removal. No foreign or attached volume is force-deleted, and errors identify retained resources. The application lock prevents concurrent locally managed sessions; engine labels detect a scene left by forced termination. There is no global prune and no application-configuration rewrite. This cache is separate from outer-engine images and has an additional disk cost.

JSON ServicePlan.Web records application/site identity, hosts, probe identities, original bindings, explicit/effective defaults and publication references. PortPlan records physical mappings only. WebPackage validates final INI handoff; WebIngress plans routing/publication and renders semantic templates with collected resources. The shared BCL-only verifier validates the JSON relationships. No legacy port scheme/hostname fields or simple-proxy fallback remain.

Real acceptance on 2026-10-05 used a synthetic Debian 13 x64 offline delivery in Windows/rootful Podman, with a real packaged executor, isolated Redis/MySQL/Nginx images and no production application settings or migrations. The original install script completed bootstrap and installer phases; Windows received HTTP 200, Redis PONG and a MySQL protocol-10 handshake. A second run reused the clean base, avoided an occupied host port 80, returned HTTP 200 from its assigned port and had no Redis key written by the first session. Both Ctrl+C exits removed their outer containers and both recorded storage volumes. A concurrent attempt for the same application was rejected.

Unit tests cover Docker/Podman orchestration, retained installer failure, startup cancellation (including an interrupted create), bounded port-binding retry, independent cleanup cancellation, cleanup failure reporting, existing-scene/architecture preflight, optional versus required entries, HTTP 404 and external redirects. They complement the executor's migration-failure tests; no real SQL migration was executed for this acceptance. Script checks use isolated copies and a fake tool.

Ubuntu 22.04 reached systemd startup in the initial prototype only. Other distributions, outer Docker, rootless engines, real ARM64 and named HTTPS applications have not completed end-to-end run acceptance. Both native executors are cross-built separately; compilation is not runtime acceptance. The verification base explicitly installs ICU required by the current native executor; a bare Linux deployment still needs the executor's runtime libraries. Privileged nesting shares the engine kernel and cannot establish production host compatibility.

Resource keys use dot-separated names, exception keys end in .Message, and neutral/Chinese entries retain the same multiline XML layout. ResXFileCodeGenerator regenerates the strongly typed accessor. Final validation passed all 236 maker and 60 executor tests, strict Release builds with no warnings, and IDE0049 verification. The local global tool was updated and ten installed binary/resource hashes matched the build outputs.

This fix resolves Redis/Valkey data-owner account names from the pinned image even when its configured user is root. Numeric ownership is written into mount records before deployment; the ownership marker is retained for purge verification. A separate Debian 13 x64 delivery using Redis 8.10.2 with persistent storage and both AOF/RDB completed the original installation flow. The directory and appendonlydir belonged to UID/GID 999; local PING and SET succeeded, and the value survived a Redis container restart. No production application or migration was run. Local engine commands use the selected executable; onsite commands share BootstrapPlan.ENGINE. Resource text receives the outer/inner engine names and option choices as arguments. Docker/Podman failure guidance follows the selection, and run does not require outer Compose. Both hosting scripts only pass engine choices and contain no engine execution commands. Release strict builds, 248 maker and 63 executor tests, and IDE0049 verification passed; both native executors were rebuilt, with ARM64 still limited to cross-compilation.

## Workstation registry mirrors

The maker reads an optional output-directory `.mirrors` with Core Profile. Linked `RegistryMirrors` expands exact registry mappings, retains ordered routes and handles source failures/cancellation. Image resolution binds a logical repository and verified platform/digest independently of its transport endpoint; a verified local binding remains usable after mirror rules change. `ImagePlan.SourceRepository` is transient and excluded from protocol JSON.

Podman builds use the resolved local image ID with `--pull=never`. Docker creates temporary BuildKit configuration and a private builder, resolves its helper image through the same policy on the builder host architecture, and disposes the builder/cache and configuration after success or failure. `run` passes validated JSON to the installer under `/run`, outside delivery assets. The executor applies it only to online image retrieval; normal onsite installation has no workstation rules. Neither global engine configuration nor package-feed URLs are changed.

The hosting `.mirrors` contains the 11 registries listed by [DaoCloud](https://github.com/DaoCloud/public-image-mirror). Existing aliases are preserved; additions use its recommended registry-prefix form. The legacy Kubernetes and experimental Ollama entries are annotated. Actual endpoint/content availability for every registry is not implied by accepting the configuration.

## Evidence and remaining release gates

Final checks passed strict Release builds across every maker target framework, 304 maker plus 76 executor tests, and independent IDE0049 checks for all four projects. Isolated installation verified that plan ignores even an invalid `.mirrors`; package/native/resource contents and user-authorized global installation were checked against local build hashes.

### Delivery archive compression comparison (2026-10-06)

The sole delivery in the hosting output directory, `zongsoft@26.10.5-x64.tar.gz` (958,123,274 bytes), was tested on Windows x64 with an Intel Core i7-8700K and .NET 10.0.12. Both gzip levels consumed the exact same 2,497,001,472-byte decompressed tar. Three trials per level alternated Optimal/Fastest in the system temporary directory, retaining normal filesystem caches and including the output flush to disk. SHA-256 checks were outside the timed intervals. The table reports medians; MB is decimal.

| Level | Compression | Compressed size | Decompression to tar |
| --- | ---: | ---: | ---: |
| Optimal | 36.98 seconds | 958.10 MB | 5.06 seconds |
| Fastest | 13.79 seconds | 1,196.22 MB | 6.43 seconds |

Optimal compression trials were 37.106, 36.978 and 36.590 seconds; Fastest trials were 13.794, 13.903 and 13.604 seconds. Fastest saved 23.18 seconds (62.7%, about 2.68 times the compression speed), adding 238.12 MB (24.85%). Decompression did not improve. All six decoded tar streams matched the input length and SHA-256, and the original delivery hash remained unchanged.

A separate check invoked the compiled tool's actual `Files.Archive` once per level on the same 86 extracted files: Optimal took 35.95 seconds and produced 958,124,266 bytes; Fastest took 14.25 seconds and produced 1,196,221,435 bytes. Actual `Files.Extract` times were 6.48 and 7.30 seconds. Both regenerated deliveries passed the existing `DeliveryBundle` inventory, length and checksum verification. Regenerated tar metadata can differ, so whole-archive byte identity with the original is not required.

For the user's frequent local build-and-verify workflow, the default now uses Fastest without new options, formats or compression dependencies, accepting this sample's roughly 25% size increase. These results concern compression; this comparison did not rerun full make, run or onsite installation, and does not establish a 2.68-times improvement for an entire command. Shared application runtime caching remains a design discussion; this change does not implement that cache policy.

Strict Release builds passed for every maker target framework and the executor, together with all 380 existing tests and independent IDE0049 checks for all four projects. One blank line between a declaration and a using statement was also added to satisfy ZS2003, without changing its runtime behavior. Automatic approval initially rejected deletion of this benchmark temporary directory with only blocked by policy; the operator subsequently confirmed manual removal of the test copies.

### Registry mirror and proxy measurements (2026-10-06)

User-authorized checks used Windows/rootful Podman, Debian 13 x64 and a 919,052,946-byte offline delivery containing the hosting Redis/MySQL/Nginx/RustFS and packaged daemon/Web. A temporary application identity/output kept the user's delivery unchanged; migrations were omitted. The outer validation base and maker infrastructure/OS images were already cached. Each run used fresh installation state/data; only the inner infrastructure image store differed between cold and repeated runs. Runtime proxy overrides were removed afterward; permanent units/scripts and registry settings were unchanged.

| Operation, with `.mirrors` | VM proxy enabled | VM proxy disabled |
| --- | ---: | ---: |
| `make`, complete delivery | 195.9 s | 191.0 s |
| `run`, empty inner image cache | 212.8 s | 192.6 s |
| `run`, verified inner image cache | 163.4 s | 151.2 s |

`run` timings include archive verification/extraction and end at readiness, excluding user testing and exit cleanup (about 19–22 s). These are individual observations, not a performance guarantee. The cache saves 49.3 s with the proxy and 41.5 s without it, so it is retained. Offline imports, copying and health checks are not accelerated by registry mirrors; `make` still installs application dependencies through unchanged software-package feeds.

Independent manifest queries bypassed the VM's existing native registry mirrors through a temporary native-CLI configuration. With the proxy, DaoCloud/Docker Hub took 1.4/3.5 s; without it DaoCloud succeeded in 2.2 s while direct Docker Hub reached the 20 s timeout. The actual maker, without a proxy, pulled an uncached Redis fixture through DaoCloud in 13.5 s and reused its verified logical identity after switching to an unreachable mirror in 0.9 s. The expanded 11-entry hosting configuration parsed successfully. The archive contains both READMEs and neither `.mirrors` nor mirror addresses in its delivery plan.

Cleanup inspection found that a maker data-owner inspection container could leave its image-declared anonymous volume. Shared build-resource removal now includes `--volumes`, preserving named/bind assets while deleting only the helper container's anonymous volumes. Success and account-resolution failure tests cover this; a real cached-Redis ownership probe verifies an unchanged volume inventory. All benchmark containers, its isolated image-store volume, fixture image, temporary infrastructure tags and the two anonymous helper volumes were removed. Existing user resources were retained. Native x64/ARM64 payloads were rebuilt; ARM64 and an outer Docker engine remain outside real runtime acceptance.

The execution approval layer rejected deletion of the system temporary benchmark directory with `blocked by policy` and no further reason. Its copied archives, isolated tool and diagnostic files therefore remain for operator removal; engine benchmark resources were cleaned successfully.

### Repeated-run image cache and RustFS console (2026-10-06)

The RustFS template now publishes its console on loopback port 9001 by default, using the common `console-port` override. Secondary ports for EMQX, NATS, ClickHouse, OTLP HTTP and Nacos are declared but remain opt-in. Three RustFS default/disabled/custom cases and seven secondary-port cases cover the shared settings behavior.

A separate 356 MB Debian 13 x64 delivery containing Redis, RustFS and an Nginx Web fixture ran twice under Windows/rootful Podman with the same application and release version. The engine reported overlay2. Web returned HTTP 200; the RustFS console at `/rustfs/console/` returned HTTP 200/HTML, while its root returned S3 403. First and second readiness times were 51.5 and 43.0 seconds including extraction/verification; these measurements do not predict production-package timings. The second run reused verified Redis/RustFS image IDs and imported the Web image again. Redis data written in the first session was absent in the second. Read-only cache inspection after each exit found exactly the two current infrastructure image IDs, no container records or data-volume directories, and a clean marker. Test containers and the fixture cache were removed; the outer volume inventory was unchanged. No production application or migration ran.

Cache regressions cover changed engine profiles, dirty stores, foreign/attached volumes, interrupted creation, whole-cache discard after failure, ownership rechecks, application-only stores and changed content at an unchanged release version. A simulated image-cleanup timeout still removed the outer container with an independent token. Strict Release builds, all 271 maker/73 executor tests and IDE0049 verification passed. Both Linux executors were rebuilt; ARM64 remains cross-compilation only. The local global tool was reinstalled from the local package, and eleven installed binary/resource/template hashes matched the build outputs. The obsolete unused VFS verification base was removed after ownership/use checks. Archive verification, asset copying, bootstrap installation and health checks still run every time; acceleration does not skip those acceptance steps.

This refactor adds default/plan/make entries sharing Core Profile, ConnectionSettings, the variable pipeline and ArtifactPublisher. Implemented areas include .settings, repository, draft/completed stages, digest identity, per-key defaults, explicit empty values, per-value expansion, temporary volumes, port controls and Redis/Valkey settings. Regression checks cover engine-free plan, missing-value warnings, successful completion/failure preservation, special-value replay, packaged Nginx fragments, executor --no-recreate and anonymous-volume uninstall commands. Real-engine temporary-data restart/uninstall acceptance remains pending. Both hosting defaults and scripts were migrated; 18 CMD checks used isolated directories and a fake tool without touching real services.

Applications no longer use templates. Effective package `Listen` metadata determines listener checks; absent/empty metadata uses PID 1 liveness. `PackageReader` reads tar PAX, Debian control and RPM application tag `1000001`; invalid metadata fails. Startup binds application interfaces; only packages without a hoster publish Listen as Web entries. Hoster publication follows its handoff. GET `/` requires an HTTP response without following redirects; it does not infer business health from status codes. HTTPS uses package DNS identity, SNI and system trust. Curl/CA dependencies are installed for automatic checks, including self-contained hosts. Delivery and installation records use protocol 1, matching executor minimum/maximum. Infrastructure template syntax remains independently numbered 1. Other delivery schemas are rejected without compatibility handling.

The suite passes 207 maker and 34 executor tests after TASK#2. All solution projects pass strict Release compilation, and all four source/test projects pass independent IDE0049 verification. Tests cover ignoring companion application templates, rejecting application template fields, package metadata, date versions, tag/default precedence, cache identity and fixed-digest replay, flat delivery publication and rollback, offline APT cache staging, and image import/retagging. Earlier packager integration checks generated temporary tar/deb/rpm fixtures and queried native metadata without installing them; loopback probe cases covered HTTP responses, TLS trust/SNI, timeout and refused connection.

Decision 35 uses unique per-attempt image/container names and ID-based export. Published application images are removed; failure/cancellation also disposes registered resources with bounded uncanceled timeouts. ContainerEngine.BuildAsync is shared by application and bootstrap builds: Podman uses --layers=false and --force-rm; Docker creates a private Buildx docker-container builder, loads the final image, then removes the builder and its cache volume. The default Docker builder is not changed. Cleanup warnings preserve the original result and continue other removals. Shared infrastructure/system images and unrelated builders are retained; there is no global prune or forced image deletion. Earlier named-image checks missed Podman intermediate cache images; the corrected verification compares every image ID and includes external build containers.

Local verification covers strict C# compilation across the maker target frameworks; parser/path/metadata tests; fake-host lifecycle tests including migration recovery, verified release configuration assets, pre-stop failures and resumable purge; safe archive extraction and schema rejection. Native AOT publications produced x64 and ARM64 ELF binaries. x64 executed its protocol entry inside the dedicated Rocky-based build container. ARM64 publication is cross-compilation, not hardware execution.

TASK#1 local NuGet packaging and content inspection verified two shared native payloads, their Chinese resources, and 30 infrastructure/ingress templates without duplicate per-framework assets, YamlDotNet or runtime analyzer payloads. The maker help entry ran successfully. Earlier checks verified isolated tool installation, installed native hashes, rejection of the removed `--bundle` option, shell/bootstrap-import syntax, and 14 isolated hosting CMD probes for shared references, quoting, caller state and failure exit handling.

The decision 35 package check also corrected NuGet directory handling for extensionless native executors. Both executors now occupy `.containerizer/linux-{x64,arm64}/containerizer`, with one `zh-Hans` directory beside each executable. The local package and the explicitly requested refreshed global installation match the maker/native/resource build hashes; the installed maker's help entry passes. This installation check does not execute a node build or target lifecycle operation.

Before TASK#1, on 2026-10-04, the real hosting `containerize.cmd` completed with Redis, MySQL, daemon and Web, Ubuntu 22.04 x64, automatic engine selection, offline bootstrap/images, and the original `.migration`. The retained delivery is `zongsoft@26.10.4.3_ubuntu-22.04_x64`; the suffix preserves earlier outputs. Its manifest records absolute source/output paths and resolved infrastructure versions. All 113 locked node assets matched their lengths and hashes; migration archive/script bytes matched the inputs, and applications have no template identity/hash.

The produced archive installed successfully in a dedicated Ubuntu 22.04 x64 systemd container with external networking disabled and no existing Docker installation. Bootstrap installed from 67 locked local packages; Podman-exported images were loaded and retagged by verified image ID. Nested Docker uses VFS in this verification fixture. The unchanged migration needs a local S3 endpoint in addition to the four selected services, so a separate isolated RustFS fixture shares the target network namespace. The migration succeeded in one attempt, the registry reports `Installed` without maintenance, and all four services are healthy. Redis returned PONG, the migrated database contains 33 tables, and Web `/Application` returned HTTP 200. Daemon uses PID 1 liveness; Web uses the metadata-derived loopback listener check. The archive, manifest and verification report remain under the hosting output directory for review. This proves this isolated combination, not clean-VM acceptance or other distributions/architectures.

| Combination or acceptance | State |
| --- | --- |
| Maker unit tests and executor simulated transactions | Locally verified; test commands below reproduce evidence |
| Decision 35 maker resource cleanup | Podman repeated build/export, failure and cancellation verified with unchanged image inventory; Docker private-builder lifecycle verified by simulation, real Docker pending |
| Linux x64 AOT build and protocol startup in build container | Verified |
| Linux ARM64 AOT build and ELF architecture | Verified; runtime pending |
| Podman/Docker export → Docker load/run roundtrip | Podman export to Docker load/run verified for the four hosting services on isolated Ubuntu 22.04 x64 |
| Clean offline Ubuntu, Debian 12/13, RHEL, Rocky, Alma; both architectures | Pending |
| Real packager daemon/Web and Automao packages | Hosting daemon/Web build and isolated installation verified; Automao pending |
| Built-in templates against locked upstream image versions | Selected Redis/MySQL digests verified in this isolated combination; other combinations pending |
| Package configuration retention and installation-media exclusion | Isolated image verification required after changes |
| Power loss, full disk, daemon/host reboot during maintenance and cleanup | Pending real fault injection; simulated transaction tests cover selected failures |
| Real databases, migration partial SQL and cross-node procedure | Original hosting migration succeeded against isolated MySQL/S3 fixtures; partial SQL failure and cross-node procedure pending |

No distribution/template/platform combination is declared release-supported until its relevant gates pass. RHEL imports must originate from entitled matching repositories and be independently tested for dependency closure; matching metadata/hashes alone is not that proof. Raw YAML input is removed under [the template contract](templates.md). Earlier hosting installation evidence describes the previous maker flow and does not establish target acceptance for TASK#1.

Known engineering limits requiring release review: OS filesystem writes and Docker restart-policy changes cannot form one atomic transaction; abrupt host loss at their boundary needs real boot/recovery validation. Persisted state is atomically replaced, but directory-entry durability under power failure is not yet proven. The generated template catalogue includes checks tied to upstream utilities/default layouts and must be reviewed at each admitted image digest. Application startup cannot be inferred safely for every packager layout; ambiguous packages must be corrected upstream.

The latest structure/terminal/version refactor passed 10 isolated maker CLI checks, including plain redirected output. Following the Web handoff change, the managed executor protocol entry reports minimum=maximum=1. The current local tool package and global installation include the latest maker and Linux Native AOT payloads; installed payload hashes are checked against local build outputs.

Core `Version.Number` now converts explicitly to long/ulong, avoiding numeric overloads during text output. Core passes 62 Versioning tests on each of net8.0/net9.0/net10.0; the maker Debug build and 207 tests use the rebuilt local Core. Release continues to reference the configured NuGet Core package.

The application configuration removal passes 214 maker tests and 34 executor tests on Windows; the executor tests also pass inside the isolated Linux builder, including read-only asset permissions. A small temporary tar package was built with the real Podman engine: JSON, plugin options, certificate and Nginx configuration retained their bytes in the final image, application mounts were absent, the Nginx copy matched its source, and installation input/logs were removed. Named image/container cleanup completed; later inspection found untagged stage images, addressed by the cache cleanup correction above. Release strict builds and independent IDE0049 checks pass. Both Native AOT executors were rebuilt; x64 protocol startup passed, while ARM64 remains cross-compilation without hardware execution. This check did not install a node delivery or operate existing services.

The cache cleanup correction passes 221 maker tests and 34 executor tests, strict Release compilation for all solution projects and independent IDE0049 checks. An isolated real Podman fixture built/exported twice, failed deliberately, and canceled a build; after each attempt the complete image ID inventory was unchanged and no external build containers remained. No Docker executable is installed on this machine; Docker coverage uses simulated private-builder creation, cache ownership, failure and cancellation. Small nested Package, Paths, Component and Descriptor models now live beside their owning types; their API and JSON shape are unchanged. Both native executors are rebuilt after the shared-source organization change.

## Reproduce local checks

```powershell
dotnet build Containerizer.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test Containerizer.slnx
dotnet format style src/Zongsoft.Tools.Containerizer.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style executor/src/Zongsoft.Tools.Containerizer.Executor.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style test/Zongsoft.Tools.Containerizer.Tests.csproj --no-restore --verify-no-changes --diagnostics IDE0049
dotnet format style executor/test/Zongsoft.Tools.Containerizer.Executor.Tests.csproj --no-restore --verify-no-changes --diagnostics IDE0049
```

Use `executor/build/containerizer.linux-x64.yaml` for a dedicated build Pod, then `setup.sh` and `publish.sh <RID> <configuration>`. These install compilers only inside that build environment. Each publication saves compiler and ELF dependency reports beside its output. `build/Import-Bootstrap.ps1` only converts a reviewed dependency collection into a local profile; it does not run its packages.

Publishing, real package installation, service control, migrations and destructive lifecycle acceptance are separate operations requiring an explicit target environment. Local verification must never use existing user services as fixtures.

Additional check: ingress recreation renews temporary anonymous volumes and removes only captured volumes from replaced owned containers after they are unused; unchanged containers retain their data. See [Docker Compose up](https://docs.docker.com/reference/cli/docker/compose/up/) for flag semantics. Package inspection verified 30 templates, two native executors and single-level Chinese resources. User-authorized global installation and installed-payload hash checks passed; plan probes used both real hosting sources and isolated output directories without target installation.
