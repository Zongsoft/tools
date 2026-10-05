[English](implementation.md) | [简体中文](implementation.zh-Hans.md)

# Implementation and verification

The current baseline is [TASK#2](../TASK%232.md), superseding conflicting PLAN/TASK#1 requirements without legacy compatibility. This document records implementation boundaries, evidence and outstanding runtime acceptance.

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

The maker hashes effective service configuration and protects Compose assets. Packager selects application configuration using hosting/.deploy/<scheme>/; containerizer keeps it inside the application image. One final image build copies the cleaned installed filesystem, retaining application configuration while removing installation media, package script databases and host service definitions. Only packaged .web/nginx/*.conf files are copied for a declared nginx dependence; their originals remain in the application image. Template/ingress assets are bound read-only directly from the verified release, with no mutable runtime copy, compose.env or three-way comparison. Normal completion, handled failure and cancellation clean maker workspaces and per-attempt resources.

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
| Executor | [DeliveryBundle](../executor/src/DeliveryBundle.cs) verifies and opens delivery assets; [ExecutorArguments](../executor/src/ExecutorArguments.cs) parses commands; [IInstallationHost](../executor/src/IInstallationHost.cs) defines the installation host boundary. `DockerHost.Bootstrap/Services/Storage.cs` and `InstallationManager.Installation/Migrations/Recovery/Transaction/Uninstall.cs` separate host operations and transaction phases |

Ordinary string composition uses interpolation; multiline Dockerfile generation uses `StringBuilder`. Array initializers use collection expressions with explicit target types where inference requires them.

Long conditions are grouped by responsibility: named predicates describe port conflicts, image health checks and managed-directory boundaries. The shared `Files.IsLinuxPath` validates Linux paths on both sides; `Distribution.IsDebian` centralizes maker package-family selection. Resource keys and JSON property names remain stable. The delivery plan is stored as `containerizer.json`; maker and executor share `DeliveryPlan.FileName`, and the protocol remains 1.

The maker uses Core `Versioning.Version.Number` for release/date versions, migration ordering/uniqueness and .NET runtime selection. Each numeric part is 0–65535; equivalent forms such as `1.0` and `1.0.0` compare equally, while explicit release/migration text is preserved. Image tags remain strings. The executor retains its BCL version handling and source-generated JSON without a Core dependency.

Maker messages use `CommandContext.Output`, `Terminal` and `CommandOutletContent` for colors/styles and multiline help. Terminal handles plain redirected output; diagnostics previously sent to stderr use `Terminal.Default.Error`. Shared subprocess forwarding and the BCL-only executor retain their existing streams.

## FHS storage and cleanup

The [README layout](../README.md) separates service data and durable installation/migration state under `/var/lib/containerizer`, attempt logs under `/var/log/containerizer`, re-creatable bootstrap assets under `/var/cache/containerizer`, and locks under `/run/containerizer`. Logs and cache use the existing ownership records and markers. Bootstrap registers ownership on the active installation object before side effects so later transaction saves retain it. Removed cache directories can be recreated without changing their recorded ownership. Ordinary uninstall preserves these assets; purge checks identity, links and mount boundaries, with registration removed last. Default service data cannot point into another application's namespace or managed state/log/cache/runtime roots. Single data mounts use the service directory directly; templates with multiple data mounts retain separate named subdirectories. MySQL therefore uses `/var/lib/containerizer/data/<name>/mysql` on the host and still mounts it at `/var/lib/mysql` inside the container. Read-only configuration mounts do not affect this count. No installed target data is automatically moved.

The FHS and naming refactor passes 221 maker and 60 executor tests on Windows, plus all 60 executor tests in the isolated Linux builder, including link and mount-boundary checks. Strict Release compilation covers all maker frameworks, with separate IDE0049 verification. This is filesystem/transaction and build evidence, not target installation acceptance.

## Evidence and remaining release gates

This refactor adds default/plan/make entries sharing Core Profile, ConnectionSettings, the variable pipeline and ArtifactPublisher. Implemented areas include .settings, repository, draft/completed stages, digest identity, per-key defaults, explicit empty values, per-value expansion, temporary volumes, port controls and Redis/Valkey settings. Regression checks cover engine-free plan, missing-value warnings, successful completion/failure preservation, special-value replay, packaged Nginx fragments, executor --no-recreate and anonymous-volume uninstall commands. Real-engine temporary-data restart/uninstall acceptance remains pending. Both hosting defaults and scripts were migrated; 18 CMD checks used isolated directories and a fake tool without touching real services.

Applications no longer use templates. Effective package `Listen` metadata determines listener checks; absent/empty metadata uses PID 1 liveness. `PackageReader` reads tar PAX, Debian control and RPM application tag `1000001`; invalid metadata fails. Startup binds all container interfaces and publishes declared ports on host loopback. GET `/` requires an HTTP response without following redirects; it does not infer business health from status codes. HTTPS uses package DNS identity, SNI and system trust. Curl/CA dependencies are installed for automatic checks, including self-contained hosts. Delivery and installation records use protocol 1, matching the executor minimum/maximum and the existing template protocol number; other schema values are rejected.

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

The latest structure/terminal/version refactor passed 10 isolated maker CLI checks, including plain redirected output. The managed executor protocol entry reports minimum=maximum=1. The current local tool package and global installation include the latest maker and Linux Native AOT payloads; installed payload hashes are checked against local build outputs.

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
