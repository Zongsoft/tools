[English](README.md) | [简体中文](README.zh-Hans.md)

# Containerizer

Build application packages and infrastructure services into a container delivery for **one Linux host**, with installation, upgrades, recovery and removal. The maker uses Docker or Podman. Each delivery includes a native executor and uses Docker and Compose on the target, without requiring the .NET SDK.

[Quick start](#quick-start) · [Maker commands](#maker-commands) · [Configuration](#configuration) · [Services](#built-in-services) · [Local preview](#local-preview) · [Target operations](#target-operations)

## Concepts

| Term | Purpose |
| --- | --- |
| Application package | A Linux package from [Packager](../packager/README.md), containing the program, configuration and startup metadata |
| Infrastructure service | An image service such as Redis or MySQL, configured through a built-in or custom template |
| Ingress service | A proxy receiving Web requests; applications with nginx hosting metadata automatically add nginx |
| `name` | The installation identity on a target host; deliveries with the same name belong to the same deployment |
| `tag` / `version` | An optional delivery label / release version; the label does not change installation identity, and the release version is independent of application and image versions |
| `.settings` | Service defaults in the output directory, used when planning or building from a component list |
| `.container` | An editable build manifest; plan creates a draft, while a completed build records effective configuration and image identities |
| `.tar.gz` | The delivery archive containing the executor, launchers, runtime description and required assets |

The usual workflow is **package applications → generate and edit a manifest → build a delivery → preview locally → install on the target**. You can also deliver infrastructure alone or build directly from a component list.

## Requirements

- Install this tool and a .NET runtime matching one of its target frameworks. Invoke it as `dotnet containerize` or `dotnet-containerize`. See the [developer guide](SKILL.md#构建与验证) for source builds and local package installation.
- `plan` needs only local inputs. `make` and direct builds require an available Docker/Podman engine and a Compose provider; initial image and system dependency preparation usually needs network access.
- The target must match the delivery's Linux distribution and architecture. Run target commands as root; bootstrap prepares the system engine and dependencies.
- Accepted distributions are Ubuntu 22.04, Debian 12/13 and RHEL/Rocky/AlmaLinux 9, with x64 or ARM64 architecture. RHEL requires an imported bootstrap dependency collection suitable for that system.

These are implemented target configurations. See [Platform and verification boundaries](docs/implementation.md#platform-and-verification-boundaries) for runtime evidence. A template declaration or successful ARM64 compilation does not establish target acceptance.

## Quick start

Run these commands from the same working directory. Start with a minimal Redis delivery:

```console
dotnet containerize plan redis --name:example --distribution:debian --version:1.0 --output:.containerized
```

Open the generated `.containerized/example@1.0-x64.container` and adjust the Redis section as needed:

```ini
[redis]
tag=latest
repository=docker.io/library/redis
settings=port=16379;storage=temporary;persistence=none
```

Build and preview:

```console
dotnet containerize make .containerized/example@1.0-x64.container
dotnet containerize run .containerized/example@1.0-x64.tar.gz
```

`run` displays the actual access addresses and stays in the foreground. Press **Ctrl+C** to end the preview and clean up its environment. Temporary storage suits this trial; choose appropriate storage, authentication and persistence for a real data service.

To include an application, add a Packager output path or candidate directory to the component list:

```console
dotnet containerize plan ./packages redis --name:example --distribution:debian --output:.containerized
```

An explicit file is read as specified. A directory search examines immediate files, then its `.packages` subdirectory; within each directory, it prefers the distribution's `.deb` or `.rpm` format, then `.tar.gz` files with matching `.sh` launchers. Candidates are filtered by name prefix and target architecture. Multiple matches prompt for selection; file dates do not select a version automatically.

## Maker commands

| Command | Input and result |
| --- | --- |
| `dotnet containerize COMPONENT...` | Application packages, package directories or `service[@image-tag]`; produces a completed manifest and delivery |
| `dotnet containerize plan COMPONENT...` | Creates an editable manifest without connecting to an engine or downloading/building images |
| `dotnet containerize plan FILE.container` | Creates a new draft from an existing manifest; use `--version` for a new release |
| `dotnet containerize make FILE.container` | Builds a delivery from one manifest |
| `dotnet containerize FILE.container` | Shorthand for make |
| `dotnet containerize run FILE.tar.gz` | Previews an existing delivery; the only additional option is `--engine` |

Maker options use `--option:value`. Component-list inputs accept:

| Option | Meaning and default |
| --- | --- |
| `--name` | Required installation identity; starts with an ASCII letter or digit, then ASCII letters, digits, dots, underscores or hyphens; no consecutive dots |
| `--tag` | Optional delivery label, following the name rules; independent of service image tags |
| `--version` | A nonzero numeric version with 2–4 parts; omitted versions use the date and increment a fourth part on output conflicts |
| `--distribution` | Required; `ubuntu` means `ubuntu@22.04`, `debian` means `debian@13`; also accepts `debian@12`, `rhel@9`, `rocky@9`, `almalinux@9`; `redhat` resolves to `rhel` |
| `--architecture` | `x64` (default) or `arm64` |
| `--source` | Working source directory; defaults to the invocation directory |
| `--output` | Output directory; defaults to the final source directory |
| `--engine` | `auto` (default), `docker` or `podman` |
| `--imaging` | Infrastructure images: `offline` (default, included) or `online` (fetched by pinned digest on the target); application images are always included |
| `--bootstrap` | System dependencies: `offline` (default) or `online`; both use versions, sources and checksums recorded during the build |
| `--migration` | Selects Migrator archives and companion scripts from a directory; omission adds no migrations |
| `--refresh` | Rebuilds the shared application runtime environments needed by this build and resolves their OS base images again; off by default, with `--refresh:false` also accepted |
| `--title` / `--description` | Descriptive manifest metadata |

With a `.container` input, only `version`, `source`, `output`, `engine` and `refresh` can be overridden; edit other values in the manifest. Refresh does not update infrastructure tags, bootstrap or preview caches. Plan does not perform a refresh.

`--source` itself resolves against the invocation directory. All managed local relative inputs and outputs, including `./` and `../`, resolve against the **final source**. Absolute paths retain their meaning. A manifest's source resolves against the file declaring it; template companion files resolve against the template directory. Run has no source option and resolves its archive against the invocation directory.

Output names are `name[-tag]@version-architecture.container` and the matching `.tar.gz`. Existing deliveries are not overwritten; use `make FILE.container --version:1.1` for another release. Failure preserves the input draft. Successful deliveries contain the same completed manifest as the external copy.

## Configuration

### Defaults and variables

`output/.settings` has one section per component and accepts only `tag`, `repository` and `settings`. A section does not automatically select that service.

```ini
[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both;password=$(redis_password)

[mysql]
tag=latest
settings=root-password=$(mysql_root_password);database=example
```

Explicit component values take precedence over `.settings`, followed by template defaults; settings merge by individual parameter. The default image tag is the literal `latest`, without a search for the newest stable version. Repository is a fully qualified image repository without a URL scheme, tag or digest.

Planning or building from any `.container` never remerges `.settings`. **Only a successful complete build from a component list** fills missing service tags. Existing values, repositories and settings are not automatically rewritten. Plan, failed builds and manifest replay do not fill the defaults file.

Variables come from the shared `.env` workflow and command context. References use `$(name)` or `%name%`; `$$(name)` and `%%name%%` preserve literal references. The whole environment is not exported, and matching names do not imply automatic binding. Built-in bindings are MySQL's `mysql_root_password` and RustFS's `rustfs_access_key` and `rustfs_secret_key`.

Settings use a single-line connection string:

```ini
settings=port=16379;password="a;b=""c"""
```

Parameters are split before individual values are evaluated, so a semicolon in a password variable does not create a new parameter. An individual `password=` is explicitly empty and blocks fallback; `false` and `0` are valid values. An empty whole `settings=` in a manifest supplies no parameters, allowing template defaults or variable bindings.

Plan preserves variable references in service settings and environment values. Missing required service parameters produce diagnostics while the draft is saved successfully; make rejects them before image operations. Completed manifests contain evaluated values, possibly credentials, so manage their access and version control accordingly.

### Manifest fields

```ini
name=example
version=1.0
distribution=debian@13
architecture=x64
source=.
output=.containerized
bootstrap=offline
imaging=offline

[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both
```

Root fields are `name`, `tag`, `version`, `engine`, `distribution`, `architecture`, `bootstrap`, `imaging`, `source`, `output`, `title` and `description`, with additional `stage=plan|complete` and ordered archive paths `migration#1`, `migration#2`, etc. Stage is maintained by the tool and may be omitted in handwritten manifests.

| Component field | Use |
| --- | --- |
| `package` | Application package path; its presence identifies an application section |
| `tag`, `repository`, `imaging` | Infrastructure image selection and optional per-service delivery mode |
| `settings` | Infrastructure parameters, listed below |
| `template` | Built-in template name or custom template path; defaults to the component name |
| `environment!NAME` | Explicit container environment value; applications can override package service values, while infrastructure values must not conflict with parameter mappings |
| `dependences` | Application declarations for `nginx[@tag]` or `runtime-*`; runtimes must match package metadata; this is not a general service dependency list |
| `probe-host!SITE` | A concrete probe hostname for an application Web site declaring only wildcard names |
| `file!/absolute/container/path` | An explicit resource used by the nginx component; the value is a maker-side file path |
| `digest`, `identity`, `timestamp`, `size` | Tool-maintained image identity and optional display metadata; see [Image identities and caches](docs/implementation.md#image-identities-and-caches) |

Applications do not use template, settings or service image-selection fields. Their entrypoint, working directory, runtime and basic health checks come from package metadata or are inferred from it. Change application configuration in the Packager scheme and rebuild the application package.

### Web applications and migrations

Applications with complete `.web/nginx` handoff assets automatically add nginx; several applications can share it. Without hosting assets, valid application Listen ports are published directly on host loopback. Nginx ports come from packaged bindings and default to the same host port; adjust or disable them with a `container-port:host-port` list:

```ini
[example-web]
package=./packages/example-web.deb
probe-host!tenant=demo.example.com

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/example/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/example/key.pem=./certs/key.pem
```

The site name tenant and certificate paths must match the actual package. A concrete probe name must match the site's host rules. External file declarations belong to the nginx component using them. The tool does not guess sites, issue certificates or rewrite application callbacks and redirects.

Migrations come from [Migrator](../migrator/README.md) as `(migrate)@version_linux-architecture.tar.gz` archives with matching `.sh` launchers. Selected versions must be unique and execute in ascending order. Building only collects files; target installation and local preview execute the selected migrations. Rebuild migration packages after changing their connection settings.

## Built-in services

Every service provides port. Services with data mounts also provide `storage=persistent|temporary`, defaulting to persistent. The ports below bind to `127.0.0.1` by default. An asterisk marks required parameters; other parameters without stated defaults are optional.

| Service | Default host ports | Specific settings |
| --- | --- | --- |
| caddy | 80 | — |
| clickhouse | 8123 | `native-port` |
| consul | 8500 | — |
| elasticsearch | 9200 | `password*` |
| emqx | 1883 | `dashboard-port`, `websocket-port` |
| etcd | 2379 | — |
| grafana | 3000 | `admin-password*` |
| haproxy | 80 | — |
| influxdb | 8086 | — |
| kafka | 9092 | — |
| loki | 3100 | — |
| mariadb | 3306 | `root-password*`, `database`, `user`, `password` |
| memcached | 11211 | — |
| mongodb | 27017 | `root-user*`, `root-password*` |
| mosquitto | 1883 | — |
| mysql | 3306 | `root-password*`, `database`, `user`, `password` |
| nacos | 8848 | `mode=standalone`, `auth-token*`, `identity-key*`, `identity-value*`, `console-port`, `grpc-port` |
| nats | 4222 | `monitoring-port` |
| nginx | From Web bindings | Mapping lists such as `port=80:18080,443:none` |
| opensearch | 9200 | `password*` |
| otel | 4317 | `http-port` |
| postgresql | 5432 | `password*`, `user=postgres`, `database=postgres` |
| prometheus | 9090 | — |
| rabbitmq | 5672 | `user*`, `password*`, `management-port` |
| redis | 6379 | `persistence=both`, `password`, `maxmemory`, `maxmemory-policy` |
| rustfs | 9000, 9001 | `access-key*`, `secret-key*`, `console-port=127.0.0.1:9001` |
| sqlserver | 1433 | `password*`, `accept-eula*`; x64 only |
| tdengine | 6041, 6060 | `console-port=127.0.0.1:6060` |
| valkey | 6379 | Same as Redis |
| zookeeper | 2181 | — |

Except for nginx mapping lists, port parameters accept `16379` (loopback), `192.0.2.10:16379`, `[::1]:16379` or `none`. They change host publication, not container listeners; none retains internal access only. Auxiliary ports are unpublished by default, except the RustFS and TDengine consoles. Nginx, haproxy, memcached and otel have no data mounts and reject storage.

Temporary storage uses anonymous volumes. Data survives stopping, starting or restarting the same container and is removed when the tool deletes that container. Starting infrastructure does not implicitly recreate existing containers. Persistent directories survive ordinary uninstall and require explicit purge; changing settings does not migrate old data.

Redis/Valkey persistence accepts both, rdb, aof or none. AOF syncs every second and RDB uses the default snapshot schedule. Password defaults to empty; a nonempty value configures authentication and matching health checks. Maxmemory and maxmemory-policy use the image's accepted syntax. Storage and persistence are independent.

Parameters do not activate management features absent from an image or change credentials in existing databases. Operators must set SQL Server's accept-eula according to its license. Caddy and haproxy are standalone ingress templates and do not consume applications' nginx assets; their configuration must come from the selected image or a custom template.

See the [template reference](docs/templates.md) for complete repositories, auxiliary ports and environment mappings.

## Local preview

Run validates the delivery and executes its original install.sh inside an isolated Linux container, including bootstrap, image import, migrations, application startup and health checks. The selected Docker/Podman runs the outer container; an independent Docker runs inside it. The delivery must match the outer engine's native architecture. Outer Compose is not required.

- Each session creates fresh installation state and test data; clean base environments and verified infrastructure/ingress images can be reused.
- Requested ports are preferred, with free ports allocated on conflicts and actual addresses displayed. Web publishes on all IPv4 interfaces; ordinary TCP services publish on loopback. LAN reachability still depends on firewalls and VM networking.
- A required Web probe failure fails the preview. A failure limited to local forwarding for an infrastructure service produces a warning; installation or service health failures still prevent readiness.
- Domain probes connect directly to the local port while preserving Host/SNI, and HTTPS validates certificates. Hosts files, DNS and trust stores are unchanged. Browser access by domain still requires suitable name resolution. Redirect targets are displayed without following them.
- Ctrl+C after readiness returns 0 when cleanup succeeds; cancellation during startup returns 130. Installation/probe failure preserves the established environment until Ctrl+C cleanup and retains a failing exit status.
- A pre-existing preview environment for the same application prevents another session. Forced process termination can leave resources; inspect ownership using the reported details before handling them.

Preview actually executes packaged applications and migrations, so use test-appropriate configuration. Containers share the host kernel and cannot replace acceptance on a real target machine.

## Target operations

Copy the delivery to a matching Linux host and extract it into a separate directory. Run the following in a root session:

```sh
mkdir example-delivery
tar -xzf example@1.0-x64.tar.gz -C example-delivery
cd example-delivery
./install.sh
containerizer status --name example
```

The installer saves required assets in managed locations and installs or reuses `/usr/local/bin/containerizer`. Status, lifecycle operations, recovery and removal then work independently of the initial extraction directory.

Target options use space-separated values; `BUNDLE` means a delivery archive or extracted directory:

| Command | Effect |
| --- | --- |
| `containerizer install BUNDLE [--name NAME] [--no-start]` | Installs an archive or extracted directory; an existing deployment with the same name undergoes upgrade checks |
| `containerizer prepare BUNDLE --name NAME` | Saves assets and prepares the engine and images, stopping at Prepared; does not stop applications or run migrations |
| `containerizer upgrade BUNDLE --name NAME [--no-start]` | Upgrades an existing deployment |
| `containerizer list` | Lists installation records |
| `containerizer status --name NAME` | Outputs installation state as JSON |
| `containerizer logs [COMPONENT] --name NAME [--tail 100] [--follow]` | Displays or follows container logs |
| `containerizer stop --name NAME` | Enters maintenance and stops applications and ingress; infrastructure keeps running |
| `containerizer start --name NAME` | Starts prepared applications and ingress, leaving maintenance after health checks pass |
| `containerizer restart [COMPONENT] --name NAME` | Restarts applications/ingress by default, or the named component; rejected during maintenance or a pending transaction |
| `containerizer recover --name NAME [--retry-migration VERSION]` | Continues the original failed/interrupted transaction and leaves it ready for explicit start |
| `containerizer uninstall --name NAME [--purge]` | Uninstalls; `./uninstall.sh [--purge]` identifies the same installation from a delivery directory |

No-start still starts infrastructure and runs migrations; applications and ingress stop at ReadyToStart. Maintenance already active before the command also suppresses automatic application startup.

Upgrades allow application, application configuration and ingress changes, but reject changes to infrastructure image identities, effective configuration or bootstrap package sets. Failures preserve maintenance and diagnostics without automatically starting the previous release or rerunning failed SQL. Investigate before recover; specify a failed migration version from the current transaction only when retry is appropriate, then explicitly start.

Operators coordinate multiple hosts: prepare and stop each, upgrade the designated hosts with no-start, then start each after all required migrations succeed.

| Target location | Contents |
| --- | --- |
| `/var/lib/containerizer/apps/NAME/` | Installation records, release assets and migration state |
| `/var/lib/containerizer/data/NAME/` | Persistent service data |
| `/var/log/containerizer/NAME/` | Migration attempt result logs |
| `/var/cache/containerizer/NAME/` | Bootstrap downloads and staging |

Ordinary uninstall removes this application's containers, anonymous volumes and networks, retaining data, release assets, migration records and images for diagnosis, reinstallation or later purge. Purge also removes verified owned local assets and exclusive images, deleting registration last; interrupted cleanup can be retried. The shared executor, Docker/Compose, original delivery media and remote data are outside purge scope.

## Mirrors and caches

An optional .mirrors file belongs in the final output directory. Run reads it beside the selected archive, while plan does not read it:

```ini
docker.io=mirror.example.com/docker.io
mcr.microsoft.com=mirror.example.com/mcr
```

Choose trusted `host[:port][/path-prefix]` endpoints, separating multiple candidates with semicolons. Do not include a scheme, tag, digest, credentials or trailing slash. The tool reuses verified caches first, then tries mirrors in order followed by the original registry. Fallback cannot change pinned digests or platforms. These rules are excluded from deliveries; ordinary target installation uses the target engine's configuration.

Shared application runtime environments are cached under `%LOCALAPPDATA%/Zongsoft/containerizer` on Windows or `~/.cache/Zongsoft/containerizer` on Linux; an absolute XDG_CACHE_HOME replaces the Linux cache base. They contain neither application packages nor application data. Use make's refresh option to update OS bases or runtime patches. See [Image identities and caches](docs/implementation.md#image-identities-and-caches) for cache layers and manual cleanup boundaries.

## Further reading

- [Implementation](docs/implementation.md): maker data flow, Web handoff, protocol, installation state and resource boundaries.
- [Template reference](docs/templates.md): built-in service mappings and custom template format.
- [Developer guide](SKILL.md): code maintenance, builds, tests and isolated validation.
