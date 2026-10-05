[English](README.md) | [简体中文](README.zh-Hans.md)

# Containerizer

`dotnet containerize` builds one Linux node delivery from packager installation packages and infrastructure templates. The archive carries a Native AOT `containerizer` executor, scripts, the `containerizer.json` delivery plan, Compose/configuration assets, selected migrator files and a copy of the generated `.container`. The executor reads `containerizer.json` for the execution plan, fixed image identities and asset inventory; `checksums.sha256` verifies this file and the delivery assets.

This implementation is under acceptance testing. See [implementation and verification](docs/implementation.md) before selecting a deployment combination. Compilation and unit tests do not establish clean-machine/offline support.

## Build and local packaging

The maker targets the frameworks in [its project](src/Zongsoft.Tools.Containerizer.csproj). The executor has its own [project](executor/src/Zongsoft.Tools.Containerizer.Executor.csproj), Native AOT toolchain and Linux x64/ARM64 publication. Debug uses the sibling Framework Core outputs; Release restores the shared Core package version.

```powershell
dotnet build Containerizer.slnx -p:ZongsoftCodeStyleStrict=true
dotnet test Containerizer.slnx
dotnet cake --target build --edition Release
$toolVersion = dotnet msbuild src/Zongsoft.Tools.Containerizer.csproj -getProperty:Version -nologo
dotnet tool install Zongsoft.Tools.Containerizer --tool-path ./.cache/tool --version "$toolVersion" --source ./src/bin/Release --no-http-cache
```

Cake `build` uses the dedicated containerizer AOT Pod, builds both executors, and creates the local NuGet package. With executors already prepared, `--target compile` builds/packages the maker. `--target pack` publishes to NuGet; it is not a verification command. Direct `dotnet build` does not prepare native artifacts. The tool package includes shared templates and both executors only once.

## Commands and workflow

The maker registers `plan` and `make` beside the default command using the same Core command design as packager. Options use `--option:value`; quoted values/paths are supported. `--help` / `-h`, including `plan --help` and `make --help`, show the syntax. No legacy `.version`, `[repositories]`, component `path`, `name`, `image` or `version` aliases are read.

| Command | Positional input | Result |
| --- | --- | --- |
| `dotnet containerize` | One or more built-in `ID[@TAG]`, package files or package directories | Complete `.container` and `.tar.gz` |
| `dotnet containerize plan` | The same components, or exactly one `.container` | Editable `.container` only; no engine, pulls, builds, executors or bootstrap collection |
| `dotnet containerize make` | Exactly one `.container` | Complete manifest/archive from that selection |
| `dotnet containerize FILE.container` | Exactly one manifest | Shorthand for make |

Mixing a manifest with components is an error. Package files are packager `.deb`, `.rpm`, or `.tar.gz` plus its companion `.sh`; directory selection checks that directory then `.packages`, using package metadata and the selected distribution/architecture. Applications do not use templates/settings.

| Named option / root entry | Type, default and constraints |
| --- | --- |
| `name` | Required application identity; letters/digits, dots, underscores, hyphens; starts with a letter/digit, no `..` |
| `tag` | Optional delivery label with the same identity restrictions; does not change installed application identity |
| `version` | Nonzero numeric version parsed by Core `Versioning.Version.Number`: 2–4 integer parts, each 0–65535; omitted for new input uses year modulo 1000, month, day and a collision suffix |
| `distribution` | Required: ubuntu[@22.04], debian[@13] (also @12), rhel/rocky/almalinux[@9]; redhat normalizes to rhel |
| `architecture` | `x64` (default) or `arm64`; declarations do not prove runtime acceptance |
| `engine` | `auto` (default), `docker`, `podman`; make requires an engine/Compose provider; plan only records this choice |
| `imaging` | `offline` (default) embeds infrastructure images; `online` pulls the pinned image at the target |
| `bootstrap` | `offline` (default) or `online` engine dependencies; independent of imaging |
| `source` | Directory; defaults to current directory for new input, or the manifest's source |
| `output` | Directory; defaults to source for new input; hosting scripts use `.containerized` |
| `migration` | Optional directory of existing migration archive/script pairs; copied, never executed during making |
| `title`, `description` | Optional text |

Use `--imaging:online` or `--imaging:offline` for component-list builds/plans. The root `imaging` entry defaults to offline; an optional service `imaging` entry overrides it. The former `--mode` option has no effect (unknown command options are ignored); root/service `mode` entries are no longer supported. Nacos `settings=mode=standalone` is a separate service setting and remains unchanged.

All options apply to component-list input, with or without plan. With an existing manifest, only `version`, `source`, `output`, `engine` are accepted; edit other selections in a draft. `--source` resolves from the calling directory. Other maker input/output paths resolve from final source. Recorded source is relative to the manifest; output and template selection paths are relative to source. Template-owned asset paths remain relative to the template. No command prompts for individual service settings.

```cmd
dotnet containerize plan redis mysql --name:example --distribution:debian --version:1.0 --output:.containerized
REM Edit .containerized/example@1.0-x64.container, then:
dotnet containerize make .containerized/example@1.0-x64.container
REM To produce another release without changing service choices:
dotnet containerize make .containerized/example@1.0-x64.container --version:1.1
```

Alternatively, `plan OLD.container --version:1.1` creates a new editable draft first. These are alternative paths: completed archives are never overwritten. Changing release version alone retains valid infrastructure digests and tags. Missing required settings in plan produce magenta warnings, save the draft and return 0; malformed values, unknown fields or write failures return a nonzero code. Make validates required settings before engine operations. A failure/cancellation retains the edited draft.

Explicit release version text is preserved in filenames and manifests (for example, `1.0` stays `1.0`). Migration versions use the same numeric rules for sorting and uniqueness: `1.0` and `1.0.0` are the same numeric version and cannot both appear in a selection; their original text is retained in the selected migration records. Infrastructure image tags are arbitrary strings and are not parsed as numeric versions.

Maker output uses Core terminal content segments: ordinary explanatory text, bold cyan parameter values/service names, cyan option names, green help values/result paths, yellow selection prompts and magenta warning text with yellow required settings. Redirected standard output is plain text; cleanup diagnostics retain their error-stream routing.

## Shared .settings and per-delivery .container

`.settings` lives in the final output directory and supplies defaults only to new component-list input. The root has no entries; each service has one section. A section does not add that service to the build. The suggested field order is not mandatory. All three entries may be omitted: tag falls back to literal `latest`, repository to the template, and settings to template defaults/bindings.

```ini
[redis]
tag=1.2.3
repository=registry.example.com/team/redis
settings=storage=persistent;persistence=both;password=$(redis_password)

[mysql]
settings=root-password=$(mysql_root_password)

[rustfs]
settings=access-key=$(rustfs_access_key);secret-key=$(rustfs_secret_key)
```

Tags are illustrative. The selected tag must exist; no stable/latest discovery is performed. Repository is a complete registry/namespace/repository without URL scheme, tag or digest; registry ports are allowed. Existing engine login supplies authentication. No template selector is added to `.settings`.

Explicit selections win over shared defaults, then template defaults/bindings; settings merge per key. Only successful component-list full builds append missing tags to `.settings`, creating sections/file as needed while preserving existing tags, comments and values. Repository/settings are never written back. Plan never writes shared defaults; component-list plan reads them. All manifest-based commands neither read nor write shared defaults, including hand-authored manifests.

`.container` roots use the entries above, except `migration` becomes ordered `migration#1`, `migration#2`, etc. `stage=plan` preserves service variable expressions until make; absent stage means editable input. `stage=complete` records literal resolved values. Plan from complete escapes variable-shaped literals for safe reuse. Imports use Core Profile directives with duplicate declarations rejected. Section names identify components.

The generated manifest starts with `# Generated by Zongsoft.Tools.Containerizer@<tool-version>` followed by a blank line. The tool name and version come from its assembly; a zero revision is omitted. Root entries are written in this order: `name`, optional `tag`, `version`, `engine`, `stage`, `distribution`, `architecture`, `bootstrap`, `imaging`, `source`, `output`, followed by optional `title`, `description`, and then numbered `migration#1`, `migration#2`, etc. Missing optional entries are omitted. This is the generated layout; input order remains unrestricted.

| Component entry | Scope and meaning |
| --- | --- |
| `package` | Application only; local package path. Its metadata supplies startup/runtime information |
| `dependences` | Application only; semicolon-separated nginx[@TAG] or runtime-* requirements |
| `tag` | Infrastructure image tag; defaults to latest in self-contained input |
| `repository` | Infrastructure image repository; template fallback if absent |
| `settings` | Infrastructure parameter collection, detailed below |
| `imaging` | Infrastructure online/offline override; otherwise root imaging |
| `template` | Existing optional infrastructure selector; defaults to section ID. Custom-template redesign is deferred |
| `environment!NAME` | Explicit container environment; validated environment variable name; supports references in drafts |
| `digest` | Pinned platform manifest `sha256:…`, populated by make; no fallback to tag when pinned resolution fails |
| `timestamp`, `size` | Optional creation time in UTC with second precision and engine-reported nonnegative integer bytes; unavailable values omitted |
| `identity` | Tool-maintained association of repository/tag, fixed Linux OS and root architecture with digest. Editing that identity invalidates old digest/display metadata; do not edit this field yourself |

All services use the root `architecture` and the fixed Linux OS; component `platform` and `architecture` entries are no longer supported. Changing root architecture invalidates recorded image digests and display metadata; make resolves and verifies images for the new architecture. Changing only the host Linux distribution does not invalidate infrastructure image digests. Root `tag` labels the delivery, service `tag` selects an image, and service `imaging` is an optional local override; these fields serve distinct purposes. Generated root metadata and image fields are records, not additional CLI options. Service edits should be made to the planned settings, not generated environment/command copies. Managed environment values are regenerated from parameters; conflicting explicit environment and settings are rejected. Unmanaged template environment values are recorded for replay.

Settings use Core ConnectionSettings for ordinary entries and the standard .NET connection-string parser for quoted values. Parse keys first, then resolve each value; a semicolon from a password variable cannot introduce another parameter. Use `key=value;key2=value2`. Generated ordinary values (including variable references, empty values and embedded equals signs) have no surrounding quotes. Values containing semicolons, quotes or leading/trailing whitespace are quoted; double a matching quote inside them: `password="a;b=""c"""`. Multiline values are unsupported. Empty `settings=` uses template defaults/bindings; `settings=password=` explicitly supplies empty and suppresses fallback. False and zero remain values. Required empty values fail make; optional values are forwarded, subject to the image's constraints.

The shared `.env` pipeline supplies explicit `$(name)` / `%name%` references and declared bindings; it does not export the entire environment. `$$(name)` / `%%name%%` preserve literal references. Templates bind MySQL root-password and RustFS access-key/secret-key; other service parameters require explicit references. Completed manifests contain expanded values, possibly secrets; operators manage their access/version control.

## Built-in service parameter reference

All 30 templates expose `port`. Templates with data mounts expose `storage`; templates without data mounts reject it. The following are supported configuration declarations, not a tested-image support matrix.

| Service | Default repository | Declared architectures | Default host:port:container | Storage |
| --- | --- | --- | --- | --- |
| `caddy` | `docker.io/library/caddy` | x64;arm64 | 127.0.0.1:80:80 | persistent / temporary |
| `clickhouse` | `docker.io/clickhouse/clickhouse-server` | x64;arm64 | 127.0.0.1:8123:8123 | persistent / temporary |
| `consul` | `docker.io/hashicorp/consul` | x64;arm64 | 127.0.0.1:8500:8500 | persistent / temporary |
| `elasticsearch` | `docker.elastic.co/elasticsearch/elasticsearch` | x64;arm64 | 127.0.0.1:9200:9200 | persistent / temporary |
| `emqx` | `docker.io/emqx/emqx` | x64;arm64 | 127.0.0.1:1883:1883 | persistent / temporary |
| `etcd` | `quay.io/coreos/etcd` | x64;arm64 | 127.0.0.1:2379:2379 | persistent / temporary |
| `grafana` | `docker.io/grafana/grafana` | x64;arm64 | 127.0.0.1:3000:3000 | persistent / temporary |
| `haproxy` | `docker.io/library/haproxy` | x64;arm64 | 127.0.0.1:80:80 | N/A |
| `influxdb` | `docker.io/library/influxdb` | x64;arm64 | 127.0.0.1:8086:8086 | persistent / temporary |
| `kafka` | `docker.io/apache/kafka` | x64;arm64 | 127.0.0.1:9092:9092 | persistent / temporary |
| `loki` | `docker.io/grafana/loki` | x64;arm64 | 127.0.0.1:3100:3100 | persistent / temporary |
| `mariadb` | `docker.io/library/mariadb` | x64;arm64 | 127.0.0.1:3306:3306 | persistent / temporary |
| `memcached` | `docker.io/library/memcached` | x64;arm64 | 127.0.0.1:11211:11211 | N/A |
| `mongodb` | `docker.io/library/mongo` | x64;arm64 | 127.0.0.1:27017:27017 | persistent / temporary |
| `mosquitto` | `docker.io/library/eclipse-mosquitto` | x64;arm64 | 127.0.0.1:1883:1883 | persistent / temporary |
| `mysql` | `docker.io/library/mysql` | x64;arm64 | 127.0.0.1:3306:3306 | persistent / temporary |
| `nacos` | `docker.io/nacos/nacos-server` | x64;arm64 | 127.0.0.1:8848:8848 | persistent / temporary |
| `nats` | `docker.io/library/nats` | x64;arm64 | 127.0.0.1:4222:4222 | persistent / temporary |
| `nginx` | `docker.io/library/nginx` | x64;arm64 | 127.0.0.1:80:80 | N/A |
| `opensearch` | `docker.io/opensearchproject/opensearch` | x64;arm64 | 127.0.0.1:9200:9200 | persistent / temporary |
| `otel` | `docker.io/otel/opentelemetry-collector-contrib` | x64;arm64 | 127.0.0.1:4317:4317 | N/A |
| `postgresql` | `docker.io/library/postgres` | x64;arm64 | 127.0.0.1:5432:5432 | persistent / temporary |
| `prometheus` | `docker.io/prom/prometheus` | x64;arm64 | 127.0.0.1:9090:9090 | persistent / temporary |
| `rabbitmq` | `docker.io/library/rabbitmq` | x64;arm64 | 127.0.0.1:5672:5672 | persistent / temporary |
| `redis` | `docker.io/library/redis` | x64;arm64 | 127.0.0.1:6379:6379 | persistent / temporary |
| `rustfs` | `docker.io/rustfs/rustfs` | x64;arm64 | 127.0.0.1:9000:9000 | persistent / temporary |
| `sqlserver` | `mcr.microsoft.com/mssql/server` | x64 | 127.0.0.1:1433:1433 | persistent / temporary |
| `tdengine` | `docker.io/tdengine/tsdb` | x64;arm64 | 127.0.0.1:6041:6041 | persistent / temporary |
| `valkey` | `docker.io/valkey/valkey` | x64;arm64 | 127.0.0.1:6379:6379 | persistent / temporary |
| `zookeeper` | `docker.io/library/zookeeper` | x64;arm64 | 127.0.0.1:2181:2181 | persistent / temporary |

| Shared/special parameter | Default and behavior |
| --- | --- |
| `port` | The loopback mapping above; integer 1–65535 uses 127.0.0.1; IPv4:port or [IPv6]:port overrides binding; `none` removes host publication while retaining internal endpoint metadata |
| `storage` | `persistent` (default) or `temporary`; all data mounts for that service. Persistent bind directories survive ordinary uninstall; `containerizer uninstall --name NAME --purge` or `./uninstall.sh --purge` removes verified owned assets |
| Redis/Valkey `persistence` | `both` (default), `none`, `rdb`, `aof`. AOF flushes everysec; RDB schedule is `3600 1 300 100 60 10000`. None disables automatic snapshot/AOF; it does not erase files or forbid manual saves |
| Redis/Valkey `password` | Optional, default empty/no authentication. Nonempty value configures requirepass and authenticated health check; explicit empty disables this simple password mechanism. No ACL file/multiuser configuration |
| RabbitMQ `management-port` | `none` by default, same syntax as port, internal TCP 15672. Requires a management-enabled image/service; mapping alone does not enable it |

Temporary storage uses container-owned anonymous volumes, may occupy disk, survives stopping/restarting the same container, and is removed with the container by tool uninstall. Infrastructure startup does not implicitly recreate an existing container. Explicit uninstall/reinstall creates fresh temporary volumes; persistent files are not converted or erased by changing storage. Manual engine removal outside the tool may require operator cleanup. Storage and persistence are independent: a test draft can use `storage=temporary;persistence=none`, while production uses persistent/both. AOF everysec does not promise zero data loss. Application upgrade cannot silently accept infrastructure changes.

The following are **all additional declared parameters**. Values are strings unless constrained below; upstream images enforce their own password policies, combinations and units. Optional unset values are omitted, using image behavior; explicit empty remains empty. No same-named `.env` binding is implied by the parameter name.

| Service | Parameter | Runtime mapping | Template default | Explicit variable binding | Requirement / constraint |
| --- | --- | --- | --- | --- | --- |
| `elasticsearch` | `password` | `ELASTIC_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `grafana` | `admin-password` | `GF_SECURITY_ADMIN_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `mariadb` | `root-password` | `MARIADB_ROOT_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `mariadb` | `database` | `MARIADB_DATABASE` | unset | — | optional; empty retained; image validates the string |
| `mariadb` | `user` | `MARIADB_USER` | unset | — | optional; empty retained; image validates the string |
| `mariadb` | `password` | `MARIADB_PASSWORD` | unset | — | optional; empty retained; image validates the string |
| `mongodb` | `root-user` | `MONGO_INITDB_ROOT_USERNAME` | unset | — | required; empty rejected at make; image validates the string |
| `mongodb` | `root-password` | `MONGO_INITDB_ROOT_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `mysql` | `root-password` | `MYSQL_ROOT_PASSWORD` | unset | mysql_root_password | required; empty rejected at make; image validates the string |
| `mysql` | `database` | `MYSQL_DATABASE` | unset | — | optional; empty retained; image validates the string |
| `mysql` | `user` | `MYSQL_USER` | unset | — | optional; empty retained; image validates the string |
| `mysql` | `password` | `MYSQL_PASSWORD` | unset | — | optional; empty retained; image validates the string |
| `nacos` | `mode` | `MODE` | standalone | — | optional; empty retained; image validates the string |
| `nacos` | `auth-token` | `NACOS_AUTH_TOKEN` | unset | — | required; empty rejected at make; image validates the string |
| `nacos` | `identity-key` | `NACOS_AUTH_IDENTITY_KEY` | unset | — | required; empty rejected at make; image validates the string |
| `nacos` | `identity-value` | `NACOS_AUTH_IDENTITY_VALUE` | unset | — | required; empty rejected at make; image validates the string |
| `opensearch` | `password` | `OPENSEARCH_INITIAL_ADMIN_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `postgresql` | `password` | `POSTGRES_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `postgresql` | `user` | `POSTGRES_USER` | postgres | — | optional; empty retained; image validates the string |
| `postgresql` | `database` | `POSTGRES_DB` | postgres | — | optional; empty retained; image validates the string |
| `rabbitmq` | `user` | `RABBITMQ_DEFAULT_USER` | unset | — | required; empty rejected at make; image validates the string |
| `rabbitmq` | `password` | `RABBITMQ_DEFAULT_PASS` | unset | — | required; empty rejected at make; image validates the string |
| `redis` | `maxmemory` | `--maxmemory` | unset | — | optional; empty retained; image validates the string |
| `redis` | `maxmemory-policy` | `--maxmemory-policy` | unset | — | optional; empty retained; image validates the string |
| `rustfs` | `access-key` | `RUSTFS_ACCESS_KEY` | unset | rustfs_access_key | required; empty rejected at make; image validates the string |
| `rustfs` | `secret-key` | `RUSTFS_SECRET_KEY` | unset | rustfs_secret_key | required; empty rejected at make; image validates the string |
| `sqlserver` | `password` | `MSSQL_SA_PASSWORD` | unset | — | required; empty rejected at make; image validates the string |
| `sqlserver` | `accept-eula` | `ACCEPT_EULA` | unset | — | required; empty rejected at make; image validates the string |
| `valkey` | `maxmemory` | `--maxmemory` | unset | — | optional; empty retained; image validates the string |
| `valkey` | `maxmemory-policy` | `--maxmemory-policy` | unset | — | optional; empty retained; image validates the string |

`maxmemory` uses the image's byte-size syntax, for example `512mb`; maxmemory-policy uses the upstream eviction policy name. Database/user settings normally affect first initialization only; changing a password in a new manifest does not migrate an existing database's credentials. SQL Server accept-eula must reflect the operator's acceptance (normally `Y`); the tool does not accept licenses on the operator's behalf. No generic native config-file input, template inheritance or additional resource-control interface is introduced.

## Publication, cache and replay

Output remains flat: `.settings`, `name[-tag]@version-architecture.container` and matching `.tar.gz`. A successful make completes its own draft; archive and external manifest are identical. Existing archives and unrelated manifests are not overwritten; publication checks for edits made during the build. Intermediate work uses system temporary directories; final atomic publication briefly stages beside its destination and removes staging afterward. Archives contain English/Chinese READMEs listing infrastructure `service@tag` and application `name@package-version`, plus one zh-Hans resource directory.

Verified Docker/Podman images and layers are reused, with platform/digest verification. No .images/.imaging or separate image archive cache exists. Successful direct builds only backfill missing tags; manifest replay pins recorded digests without rereading .settings. Save actual delivery archives for immutable delivery history.

Application images and helper containers use unique per-attempt identities; cleanup after success/failure/cancellation removes only that attempt's resources, without forced image removal or global prune. Application and bootstrap builds share this policy: Podman disables intermediate layer caching (`--layers=false`) and removes build containers; Docker uses a temporary Buildx `docker-container` builder, loads the result into the engine and removes the builder with its cache volume. Docker therefore requires Buildx and may download the BuildKit image and base layers into that isolated builder; the user's selected builder is unchanged. Infrastructure/system bases and other projects' caches remain. Failed cleanup warns without invalidating published artifacts; forced termination may leave resources for operator cleanup.

Packager selects application configuration from hosting/.deploy/<scheme>/ for each project/product and includes it in the installation package. Containerizer preserves these files inside the application image; it does not scan, externalize or mount application configuration. To change configuration, rebuild the installation package with the intended scheme and make a new delivery. For applications with a nginx dependence, packaged `.web/nginx/*.conf` fragments are copied unchanged to nginx conf.d while retaining the originals in the application image; author backends using container-network service names. When no supplied fragment exists, the existing simple single-application proxy is generated. Template and ingress configuration assets are bound read-only from the verified release assets. No mutable runtime configuration copy or site-edit comparison is maintained. There is no new Nginx parser or native config input. Package Listen metadata still drives process/listener health checks; listener success does not prove business readiness.

Delivery JSON, installation records and template declarations use protocol 1. The executor advertises minimum/maximum protocol 1 and rejects other schema values; this does not add compatibility with earlier record layouts. Replacing the global executor checks registered installations and rejects incompatible records.

## Bootstrap profiles

The matching distribution resolver collects exact packages, dependencies, URLs and hashes. Successful collections are verified and reused in the current user's cache, isolated by source directory, distribution and architecture. Windows uses `%LOCALAPPDATA%/Zongsoft/containerizer`; Linux uses `$XDG_CACHE_HOME/Zongsoft/containerizer` or `~/.cache/Zongsoft/containerizer`. Publication locks also stay outside output. Online mode downloads the same locked bytes at the target. Debian/Ubuntu offline installation stages verified local packages in a private APT archive cache before running with downloads and recommendations disabled.

An explicit collection at `<source>/.containerizer/bootstrap/<distribution>_<architecture>/` takes precedence. It contains `bootstrap.lock.json`, `packages/metadata.tsv` and all package files recorded by the lock. [Import-Bootstrap.ps1](build/Import-Bootstrap.ps1) creates this layout from a reviewed collector result. RHEL requires an entitled matching RHEL BaseOS/AppStream collection; the tool does not substitute Rocky/Alma packages. The import verifies recorded identity, architecture and hashes, not subscription entitlement or completeness on a clean target.

## Target lifecycle

Extract the node archive and run `./install.sh` as root, or use the installed global command. The maker is not needed on the target. Executor options use separate values:

```sh
containerizer install ./example.tar.gz --name example --no-start
containerizer list
containerizer status --name example
containerizer start --name example
containerizer logs redis --name example --follow --tail 100
```

| Command | Behavior |
| --- | --- |
| `install <directory-or-archive>` | Install; an existing name uses upgrade rules |
| `upgrade <directory-or-archive> --name NAME` | Update applications/ingress; reject infrastructure/bootstrap changes |
| `prepare <directory-or-archive> --name NAME` | Verify, stage delivery assets and obtain images; do not stop or migrate |
| `stop --name NAME` | Persist maintenance and stop applications/ingress; keep infrastructure |
| `start --name NAME` | Resume current deployment, or activate a ready pending transaction after checks |
| `restart [component] --name NAME` | Restart the selected component; default scope is applications/ingress; maintenance rejects it |
| `recover --name NAME` | Continue the original failed/interrupted transaction; remain in maintenance |
| `recover --name NAME --retry-migration VERSION` | Explicitly retry one failed/interrupted migration after its partial effects have been handled |
| `uninstall --name NAME` | Remove owned containers and network; retain data/configuration/history |
| `uninstall --name NAME --purge` | Additionally delete verified owned local persistent assets; remove registry last |
| `list`, `status --name NAME`, `logs [component] --name NAME` | Query registration, saved state and service output |

`upgrade` and `prepare` have one required positional package path; there is no `--bundle`. `install.sh` supplies its own directory. `uninstall.sh` identifies the application from its delivery, then operates on the registered deployment, including when the script is older than the current release.

`--no-start` stops at `ReadyToStart`; `prepare` alone does not authorize `start`. Successful migration fingerprints are checked, and unresolved attempts never rerun automatically. Only a healthy release becomes current. Cross-node ordering is an implementation procedure supplied by the delivery owner, not an inferred dependency graph.

Host paths and application paths inside images have separate purposes:

| Location | Purpose |
| --- | --- |
| Host `/var/lib/containerizer/apps/<name>/` | Installation registry, verified release assets and durable migration state (`migrations/<version>/`) |
| Host `/var/lib/containerizer/data/<name>/<service>/` | Persistent service data; multiple data mounts use separate `<mount>/` subdirectories |
| Host `/var/log/containerizer/<name>/` | Migration attempt logs |
| Host `/var/cache/containerizer/<name>/<release-id>/bootstrap/` | Re-creatable bootstrap downloads and package-manager staging |
| Host `/run/containerizer/` | Application locks and the separate host lock; recreated after reboot |
| Host `/usr/local/bin/containerizer` | Shared executor |
| Inside each application image | The package's own installation directory, such as `/opt/<company>/<product>`, and its declared working directory |

The fixed host roots are centralized in [Installation.Paths](.shared/Installation.cs); `GetDataPath(name)` provides the shared data root. The layout follows FHS categories for [persistent state](https://refspecs.linuxfoundation.org/FHS_3.0/fhs/ch05s08.html), [logs](https://refspecs.linuxfoundation.org/FHS_3.0/fhs/ch05s10.html), [cache](https://refspecs.linuxfoundation.org/FHS_3.0/fhs/ch05s05.html) and [runtime files](https://refspecs.linuxfoundation.org/FHS_3.0/fhs/ch03s15.html). The `containerizer` subdirectory identifies the managing tool; FHS does not prescribe these subdirectory names. Package installation paths inside images remain unchanged.

Newly generated deliveries use this layout. Existing target data and installation records are not migrated automatically; earlier deliveries must be rebuilt. Ordinary uninstall retains owned data, logs, cache and installation history. `--purge` verifies ownership, links and mount boundaries before deleting them, and removes registration last. Per-application locks remain outside deletable assets. After staging, recovery and normal operation do not need the original delivery directory. The global tool, engine packages, source media, shared resources and remote database/bucket contents are retained.

Exit categories are 2 input, 3 environment, 4 integrity/process, 5 acquisition/bootstrap, 6 health, 7 transaction/migration, 8 lock contention and 9 incomplete cleanup. Cancellation returns 130. Keep failed managed assets for diagnosis; do not delete failure markers to bypass recovery.
