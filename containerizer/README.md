# Zongsoft Containerizer

![License](https://img.shields.io/github/license/Zongsoft/tools)
![NuGet Version](https://img.shields.io/nuget/v/Zongsoft.Tools.Containerizer)
![NuGet Downloads](https://img.shields.io/nuget/dt/Zongsoft.Tools.Containerizer)
![GitHub Stars](https://img.shields.io/github/stars/Zongsoft/tools?style=social)

[English](README.md) | [简体中文](README.zh-Hans.md)

`dotnet-containerize` packs applications and infrastructure services into a **container delivery for one Linux host**: a single `.tar.gz` that carries its Compose configuration, its images and its own native executor. Installing, upgrading, stopping, recovering and uninstalling the whole stack on the target is then one command per step — and the host needs neither the .NET SDK nor this tool.

The maker runs on Windows or Linux and uses Docker or Podman. A delivery targets Ubuntu 22.04, Debian 12/13 or RHEL-family 9, on x64 or ARM64.

> 🚨 **Warning:** `dotnet-containerize` itself only writes local files. Running `install.sh` on a host installs the container engine, imports images, starts containers, executes migrations and can change databases. Rehearse with `run` and read the delivery README before touching a production host.

## Contents

- [Features](#features)
- [Basic concepts](#basic-concepts)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Command reference](#command-reference)
- [Configuration](#configuration)
- [Built-in services](#built-in-services)
- [Local preview](#local-preview)
- [Target operations](#target-operations)
- [Registry mirrors and caches](#registry-mirrors-and-caches)
- [Reference examples](#reference-examples)
- [Related documents](#related-documents)

## Features

- **One host, one archive:** applications, infrastructure services, ingress, migrations, bootstrap packages and images travel together as `name[-tag]@version-architecture.tar.gz`.
- **Review before you build:** `plan` writes an editable `.container` manifest without connecting to an engine; `make` turns a reviewed manifest into a delivery.
- **Thirty built-in services:** Redis, MySQL, MariaDB, PostgreSQL, MongoDB, Kafka, Nacos, RustFS, nginx and more, each configured with a one-line `settings` value; custom templates extend the set.
- **Applications from Packager packages:** `.deb`, `.rpm`, or `.tar.gz` plus its launcher; the entrypoint, runtime, environment and health check are derived from the package metadata.
- **Automatic Web ingress:** an application that ships Packager's `.web/nginx` handoff assets gets a managed nginx ingress with rendered sites, ports and certificates; several applications can share it.
- **Ordered migrations:** Migrator archives run in ascending version order during installation and preview, with per-version state and explicit retry instead of automatic re-execution.
- **Offline or online acquisition:** images and system dependencies can be carried in the archive, or fetched on the target from digests recorded at build time.
- **Rehearsal on the maker:** `run` installs the real delivery inside a disposable Linux container and prints the addresses that actually answered.
- **Lifecycle without the SDK:** the delivery's Native AOT executor performs every target operation, including maintenance, recovery and cleanup.
- **Registry mirrors:** an optional `.mirrors` file redirects image downloads without changing pinned digests or platforms.
- **Bilingual by default:** console output, generated READMEs and this documentation ship in English and Simplified Chinese.

## Basic concepts

| Term | Meaning |
| --- | --- |
| **Delivery** | Everything needed to install one application stack on one Linux host: the `.tar.gz` archive, its `.container` manifest and the assets inside it. |
| **Application package** | A Linux package from [Packager](../packager/README.md) holding a program, its configuration and its startup metadata. |
| **Infrastructure service** | An image-based service such as Redis or MySQL, described by a built-in or custom template. |
| **Ingress service** | A proxy that receives Web requests. Applications with nginx handoff assets add nginx automatically. |
| **`name`** | The installation identity on the target host. Two deliveries with the same name are the same deployment. |
| **`tag`** | An optional delivery label. It distinguishes builds but does not change installation identity. |
| **`version`** | The release version of the delivery, independent of application and image versions. |
| **`.settings`** | Shared per-service defaults in the output directory, used when planning or building from a component list. |
| **`.container`** | The editable build manifest. `plan` writes a draft; a completed build records the effective configuration and image identities. |
| **Executor** | The native `containerizer` program inside the delivery; it performs all target-side operations. |

A delivery is produced in this order:

1. **Package** the applications with [Packager](../packager/README.md) and prepare migrations with [Migrator](../migrator/README.md).
2. **Plan** a component list into an editable `.container` manifest.
3. **Review** the shared `.settings` defaults and edit the manifest for this release.
4. **Make** the delivery: images are resolved and built, assets are collected, checksums are written, and the archive is published.
5. **Rehearse** the archive locally with `run`.
6. **Ship** the archive to a matching host, extract it and run `./install.sh`.

| Containerizer does | Containerizer does not |
| --- | --- |
| Package applications, pin image identities and record delivery checksums | Build application packages or migration archives — Packager and Migrator do that |
| Generate Compose configuration, bootstrap dependencies and lifecycle commands | Build, deploy or configure application business code |
| Render nginx sites from Packager's handoff assets and publish their ports | Issue certificates, change DNS, or rewrite application callbacks and redirects |
| Run migrations in order and keep per-version state | Parse SQL or decide whether a failed migration may be retried |
| Install, upgrade, stop, start, recover and uninstall on one host | Coordinate several hosts; an operator sequences those steps |

## Requirements

- **Maker:** the tool installed as a .NET global tool, plus a .NET runtime matching one of its target frameworks. Invoke it as `dotnet containerize` or `dotnet-containerize`.
- `plan` needs only local inputs. `make`, direct builds and preview require a running Docker or Podman engine with a Compose provider; the first preparation of images, runtimes and system dependencies usually needs network access.
- **Target host:** a Linux distribution and architecture matching the delivery, driven from a root session. Bootstrap prepares the container engine and its dependencies.
- **Accepted targets:** Ubuntu 22.04, Debian 12/13 and RHEL/Rocky/AlmaLinux 9, on x64 or ARM64. RHEL requires an imported bootstrap dependency collection prepared for that system.

These are the implemented target configurations; the observed runtime evidence is narrower. See [Platform and verification boundaries](docs/implementation.md#platform-and-verification-boundaries). A template declaration or a successful ARM64 compilation is not target acceptance.

## Installation

Install, update, check and uninstall as a .NET global tool:

```console
# Install
dotnet tool install -g Zongsoft.Tools.Containerizer

# Update
dotnet tool update -g Zongsoft.Tools.Containerizer

# Check
dotnet tool list -g
dotnet-containerize --help

# Uninstall
dotnet tool uninstall -g Zongsoft.Tools.Containerizer
```

### Installing from local source

A `.nupkg` built from source can be installed for testing without publishing to [nuget.org](https://nuget.org). The tool package embeds two Native AOT executor payloads, so a full local build first prepares them; the commands and the dedicated AOT environment are documented in the [developer guide](SKILL.md#构建与验证). To package from a checkout and install into an isolated tool directory:

```powershell
dotnet cake --target build --edition Release

$toolVersion = dotnet msbuild src/Zongsoft.Tools.Containerizer.csproj -getProperty:Version -p:Configuration=Release -nologo
dotnet tool install Zongsoft.Tools.Containerizer --tool-path ./.cache/tool --version $toolVersion --source ./src/bin/Release --no-http-cache
./.cache/tool/dotnet-containerize --help
```

`--source` restricts the installation to the local directory so a same-named NuGet.org package is not selected, and `--no-http-cache` disables the download cache. Use `-g` instead of `--tool-path` to replace the current user's global tool.

> 🚨 **Warning:** Do not run the Cake `pack` task for local testing; it pushes the package to NuGet.org.

## Quick start

This walkthrough uses the real [Zongsoft hosting](https://github.com/Zongsoft/hosting) repository, which delivers the `zongsoft.daemon` and `zongsoft.web` hosts together with Redis, MySQL, RustFS and nginx on one Debian 13 host. The same steps apply to any project; the hosting repository simply provides ready-made inputs.

> 💡 **Tip:** The hosting repository wraps every command below in [containerize.cmd](https://github.com/Zongsoft/hosting/blob/main/containerize.cmd), an interactive menu that fixes the source to the hosting root and the output to `.containerized`. The direct commands shown here are what the wrapper finally calls, and they work in any project.

### Step 1: Prepare applications and migrations

Build, deploy and package the hosts as described in the [hosting README](https://github.com/Zongsoft/hosting/blob/main/README.md#installation-and-migration-packages). In each host directory, `deploy.cmd` packages what it just deployed when you answer `deb` at its format prompt, and [pack.cmd](https://github.com/Zongsoft/hosting/blob/main/daemon/pack.cmd) packages existing files only. Either way the result is:

```text
daemon/.packages/zongsoft.daemon@1.0.0-x64.deb
web/default/.packages/zongsoft.web@1.0.0-x64.deb
```

Then create the migration archive from the hosting root with [migrate.cmd](https://github.com/Zongsoft/hosting/blob/main/migrate.cmd):

```text
.migration/zongsoft(migrate)@1.0.0_linux-x64.tar.gz
.migration/zongsoft(migrate)@1.0.0_linux-x64.sh
```

Containerizer never compiles hosts or generates migrations; it consumes these artifacts.

### Step 2: Declare shared service defaults

Service defaults live in `output/.settings` and are shared by every delivery of the project. The hosting repository keeps its versioned defaults in [.containerized/.settings](https://github.com/Zongsoft/hosting/blob/main/.containerized/.settings); the entries used by this delivery are:

```ini
[mysql]
tag=8.4.11
settings=storage=persistent;root-password=${mysql:root_password}

[redis]
tag=8.10.2
settings=storage=persistent;persistence=both;password=${redis:password}

[rustfs]
tag=1.0.1
settings=storage=persistent;access-key=${rustfs:access_key};secret-key=${rustfs:secret_key}

[nginx]
tag=1.30.5
```

The `${___}` references read the hosting root's [.env](https://github.com/Zongsoft/hosting/blob/main/.env) through the shared variable pipeline, so no credential is written into `.settings`. See [Service defaults and variables](#service-defaults-and-variables) for the full rules.

### Step 3: Plan the manifest

Run the following from the hosting root. A bare name that matches a built-in service selects that service; a file or directory is treated as an application package input:

```cmd
dotnet-containerize plan ^
	daemon web/default ^
	redis mysql rustfs nginx ^
	--name:zongsoft ^
	--version:1.0 ^
	--distribution:debian@13 ^
	--architecture:x64 ^
	--migration:.migration ^
	--output:.containerized
```

| Argument or option | Effect |
| --- | --- |
| `daemon`, `web/default` | Host directories; each is searched for a package matching the delivery name, distribution and architecture. |
| `redis mysql rustfs nginx` | Built-in infrastructure and ingress services. `nginx` is optional here: an application with complete `.web/nginx` handoff assets adds it automatically, and declaring it explicitly pins its tag and port mapping. |
| `--name:zongsoft` | Installation identity; it also prefixes the package search and the migration archive name. |
| `--version:1.0` | Release version of this delivery. Omitting it uses a date version, which is why the hosting project's own deliveries are named like `zongsoft@26.10.6-x64.tar.gz`. |
| `--distribution:debian@13` | Target distribution; `debian` alone means `debian@13`. |
| `--migration:.migration` | Directory searched for `zongsoft(migrate)@<version>_linux-<arch>` archives with their launchers. |
| `--output:.containerized` | Destination for the manifest and, later, for the delivery archive. |

### Step 4: Review the manifest

`plan` writes `.containerized/zongsoft@<version>-x64.container` and stops there — no engine contact, no image download. The draft for this delivery looks like:

```ini
name=zongsoft
version=1.0
distribution=debian@13
architecture=x64
source=..
output=.containerized
bootstrap=offline
imaging=offline
migration#1=.migration\zongsoft(migrate)@1.0.0_linux-x64.tar.gz

[redis]
tag=8.10.2
repository=docker.io/library/redis
settings=storage=persistent;persistence=both;password=${redis:password}

[mysql]
tag=8.4.11
repository=docker.io/library/mysql
settings=storage=persistent;root-password=${mysql:root_password}

[rustfs]
tag=1.0.1
repository=docker.io/rustfs/rustfs
settings=storage=persistent;access-key=${rustfs:access_key};secret-key=${rustfs:secret_key}

[zongsoft.daemon]
package=daemon\.packages\zongsoft.daemon@1.0.0-x64.deb

[zongsoft.web]
package=web\default\.packages\zongsoft.web@1.0.0-x64.deb

[nginx]
tag=1.30.5
settings=port=80:18080,443:none
```

`source` and `output` are stored relative to each other. Missing required service parameters appear as warnings while the draft is still saved; the delivery is edited here, not regenerated. See [Manifest fields](#manifest-fields) and [Web handoff and publication](#web-handoff-and-publication).

### Step 5: Make the delivery

```cmd
dotnet-containerize make .containerized/zongsoft@1.0-x64.container
```

`make` resolves and pins every image, builds the application images, collects bootstrap packages, migrations and configuration, verifies checksums and publishes:

```text
.containerized/zongsoft@1.0-x64.container
.containerized/zongsoft@1.0-x64.tar.gz
```

The archive contains the same completed manifest, so what ships is exactly what was reviewed. Existing deliveries are never overwritten; build the next release with `make FILE.container --version:1.1`. Use `--refresh` when the shared application runtime environments must be rebuilt from updated operating-system bases or runtime patches.

### Step 6: Rehearse locally

```cmd
dotnet-containerize run .containerized/zongsoft@1.0-x64.tar.gz
```

`run` verifies the archive, executes its original `install.sh` inside a disposable Linux container, and prints the addresses that actually answered. Keep the window open and press **Ctrl+C** to remove the environment and its test data. See [Local preview](#local-preview) for what is checked and what the exit codes mean.

### Step 7: Install on the host

Copy the archive to a matching Linux host, extract it into its own directory and run the launcher from a root session:

```sh
mkdir zongsoft-delivery
tar -xzf zongsoft@1.0-x64.tar.gz -C zongsoft-delivery
cd zongsoft-delivery
./install.sh
containerizer status --name zongsoft
```

From this point on, status and lifecycle commands work independently of the extraction directory. See [Target operations](#target-operations).

## Command reference

### Syntax

```text
dotnet containerize <components...> [options]
dotnet containerize plan <components... | file.container> [options]
dotnet containerize make <file.container> [options]
dotnet containerize      <file.container> [options]     # shorthand for make
dotnet containerize run  <archive.tar.gz> [--engine:auto|docker|podman]
```

`dotnet-containerize` is an equivalent entry point for every form. Write options as `--option:value`; maker commands exit with `0` on success, `2` for invalid input, and `130` when cancelled. Other codes identify the failing phase — see the [exit categories](docs/implementation.md#platform-and-verification-boundaries).

### Components

Positional components are resolved in the order they are written:

| Component | Interpretation |
| --- | --- |
| `service[@tag]` | A built-in service name (see [Built-in services](#built-in-services)), optionally with the image tag to pin. A value containing `/` or `\` is never a service name. |
| Package file | An application package: `.deb`, `.rpm`, or `.tar.gz` together with its `.sh` launcher. |
| Directory | Searched directly and in its `.packages/` subdirectory for a package matching the delivery's name prefix, distribution and architecture; several candidates prompt for selection, and file dates never pick a version. |
| `FILE.container` | Must be the only argument; plans or builds that manifest instead of a component list. |

### Options

All options below belong to the component-list forms; with a `.container` input only `version`, `source`, `output`, `engine` and `refresh` may be overridden, and everything else is edited in the manifest.

| Option | Default | Meaning |
| --- | --- | --- |
| `--name:<name>` | **Required** | Installation identity. It starts with an ASCII letter or digit, may continue with ASCII letters, digits, dots, underscores or hyphens, and cannot contain two consecutive dots. |
| `--distribution:<dist[@version]>` | **Required** | Target distribution. `ubuntu` means `ubuntu@22.04` and `debian` means `debian@13`; `debian@12`, `rhel@9`, `rocky@9` and `almalinux@9` are also accepted, and `redhat` resolves to `rhel`. |
| `--version:<version>` | Date version | Nonzero numeric release version with 2–4 parts. An omitted version uses the date and increments a fourth part when the output name is taken. |
| `--tag:<tag>` | empty | Optional delivery label, following the name rules; it is independent of service image tags. |
| `--architecture:<x64\|arm64>` | `x64` | Target architecture. |
| `--source:<directory>` | invocation directory | Working source directory. |
| `--output:<directory>` | final source | Directory receiving the manifest and the archive. |
| `--engine:<auto\|docker\|podman>` | `auto` | Container engine used by the maker and by `run`. |
| `--imaging:<offline\|online>` | `offline` | Infrastructure images: carried in the delivery, or fetched on the target from pinned digests. Application images are always carried. |
| `--bootstrap:<offline\|online>` | `offline` | System dependencies: carried in the delivery, or downloaded on the target — both install the versions, sources and checksums recorded at build time. |
| `--migration:<directory>` | none | Directory searched for Migrator archives and their launchers; omitting it adds no migrations. |
| `--refresh[:boolean]` | `false` | Rebuild the shared application runtime environments needed by this build and resolve their operating-system bases again. |
| `--title:<text>`, `--description:<text>` | empty | Descriptive manifest metadata. |

With a `.container` input, `plan` does not refresh, and `--refresh` never changes infrastructure image tags, bootstrap packages or preview caches.

### Paths and output names

`--source` itself resolves against the invocation directory. Every managed local input and output, including `./` and `../`, resolves against the **final source**; absolute paths keep their meaning. A manifest's own `source` resolves against the file that declares it, and a template's companion files resolve against the template directory. `run` has no source option and resolves its archive against the invocation directory.

Deliveries are named `name[-tag]@version-architecture.container` and `name[-tag]@version-architecture.tar.gz`. An existing archive is never overwritten, a failed build keeps the reviewed draft, and a successful build replaces its own draft in place. See [Paths, variables and output](docs/implementation.md#profiles-variables-and-paths) for the complete path contract.

### Examples

Plan and build an infrastructure-only delivery, then rehearse it:

```console
dotnet-containerize plan redis --name:example --distribution:debian --version:1.0 --output:.containerized
dotnet-containerize make .containerized/example@1.0-x64.container
dotnet-containerize run .containerized/example@1.0-x64.tar.gz
```

Build a delivery in one step instead of planning first, pinning the image tag inline (the output defaults to the source directory):

```console
dotnet-containerize redis@8.10.2 --name:example --distribution:debian --version:1.0
```

Deliver a Web application with its certificate, keeping HTTPS internal:

```ini
[example-web]
package=./packages/example-web.deb
probe-host!tenant=demo.example.com

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/example/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/example/key.pem=./certs/key.pem
```

## Configuration

### Service defaults and variables

The tool explicitly enables Core variable fallback: named references search their namespace, its parents and global, querying every source in priority order at each level before allowing declared option defaults. Unqualified references remain `${name}`, and a found null or empty value stops fallback.

Core `Variables.Environments()` reads the default namespace live without rewriting environment names or values. Environment names ignore case on Windows and are case-sensitive on Unix/Linux; configuration and command-option names ignore case.

`output/.settings` holds shared defaults, with one section per component accepting only `tag`, `repository` and `settings`:

```ini
[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both;password=${redis:password}

[mysql]
tag=latest
settings=root-password=${mysql:root_password};database=example
```

A section never selects its service; only the component list or the manifest does that. Values merge per parameter, and explicit component values win over `.settings`, which wins over template defaults. A default image tag is the literal `latest` — the tool does not search for the newest stable release.

`plan`, failed builds and manifest replay never rewrite `.settings`. Only a **successful complete build from a component list** fills in missing service tags; existing tags, repositories and settings are left alone.

Variables come from the environment, the `.env` files along the path from the filesystem root down to the source, and command options. References use `${name}`; `\${name}` keep a literal reference. Names that merely match a parameter are not bound automatically — the only built-in bindings are MySQL's `mysql:root_password` and RustFS's `rustfs:access_key` and `rustfs:secret_key`. Any other value must be set explicitly, as the hosting example does.

Service parameters use a single-line connection string:

```ini
settings=port=16379;password="a;b=""c"""
```

Parameters are split before individual values are evaluated, so a semicolon inside a password variable never starts a new parameter. A bare `password=` is an explicit empty value that blocks fallback; `false` and `0` are valid values; an empty whole `settings=` means "no explicit parameters", leaving template defaults and variable bindings in charge.

> 🚨 **Warning:** A completed manifest records evaluated values, which may include credentials. Treat it as configuration with secrets and manage its access and version control accordingly.

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
migration#1=.migration\example(migrate)@1.0.0_linux-x64.tar.gz

[redis]
tag=latest
settings=port=16379;storage=persistent;persistence=both
```

Root fields are `name`, `tag`, `version`, `engine`, `distribution`, `architecture`, `bootstrap`, `imaging`, `source`, `output`, `title` and `description`, plus the tool-maintained `stage=plan|complete` and the ordered `migration#1`, `migration#2`, … archive paths. A handwritten manifest may omit `stage`.

| Component field | Use |
| --- | --- |
| `package` | Application package path; its presence marks the section as an application. |
| `tag`, `repository`, `imaging` | Infrastructure image selection and an optional per-service delivery mode. |
| `settings` | Infrastructure parameters, listed under [Built-in services](#built-in-services). |
| `template` | Built-in template name or custom template path; defaults to the component name. See the [template reference](docs/templates.md). |
| `environment!NAME` | Explicit container environment value. An application may override package service values; an infrastructure component must not contradict a parameter mapping. |
| `dependences` | Application declarations of `nginx[@tag]` or `runtime-*`; runtimes must match the package metadata. This is not a general service dependency list. |
| `probe-host!SITE` | Concrete probe hostname for a Web site that declares only wildcard names. |
| `file!/absolute/container/path` | Extra resource consumed by the nginx component; the value is a maker-side file path. |
| `digest`, `identity`, `timestamp`, `size` | Image identity recorded by the tool, plus optional display metadata, as described in [Image identities and caches](docs/implementation.md#image-identities-and-caches). |

Applications do not use `template`, `settings` or service image-selection fields: their entrypoint, working directory, runtime and basic health check come from — or are inferred from — the package metadata. To change an application's own configuration, edit the Packager scheme and rebuild the package.

### Web handoff and publication

An application that ships Packager's complete `.web/nginx` handoff assets (`.bindings`, `<package-name>.conf` and `<package-name>.conf.template`) joins a managed nginx ingress automatically, and several applications can share that ingress. Without hosting assets, the application's valid `Listen` ports are published directly on host loopback.

Nginx ports come from the packaged bindings and are published on the same host port by default. Remap or disable them with a `container-port:host-port` list:

```ini
[zongsoft.web]
package=web\default\.packages\zongsoft.web@1.0.0-x64.deb

[nginx]
settings=port=80:18080,443:none
file!/etc/ssl/zongsoft/fullchain.pem=./certs/fullchain.pem
file!/etc/ssl/zongsoft/key.pem=./certs/key.pem
```

Here container port 80 prefers host port 18080 and container port 443 stays internal. A published wildcard-only site also needs a matching concrete `probe-host!SITE` name, and the site name and certificate paths must match the package content. External file declarations belong to the nginx component that uses them, and the ports are fixed when the manifest is written — editing `.settings` later does not change a saved delivery. Containerizer does not guess sites, issue certificates, or rewrite application callbacks and redirects.

### Migrations

Migrations come from [Migrator](../migrator/README.md) as `<name>(migrate)@<version>_linux-<architecture>.tar.gz` archives with matching `.sh` launchers, selected by `--migration:<directory>` while planning or listed as `migration#N` fields in a manifest. Selected versions must be unique and run in ascending order.

Building only collects the files; target installation and local preview execute them. Rebuild the migration package after changing its connection settings.

## Built-in services

Every service provides `port`; services with data mounts also provide `storage=persistent|temporary`, defaulting to persistent. Ports bind to `127.0.0.1` by default. An asterisk marks a required parameter; parameters without a stated default are optional.

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

Except for nginx mapping lists, a port parameter accepts `16379` (loopback), `192.0.2.10:16379`, `[::1]:16379` or `none`. It changes host publication only, never the container listener; `none` keeps the port reachable from inside the delivery only. Auxiliary ports stay unpublished by default, except the RustFS and TDengine consoles. nginx, haproxy, memcached and otel have no data mounts and reject `storage`.

`temporary` storage uses anonymous volumes: data survives stopping, starting or restarting the same container and is removed when the tool deletes that container. Starting infrastructure never implicitly recreates existing containers. Persistent directories survive an ordinary uninstall and require an explicit purge; changing settings does not migrate old data.

Redis and Valkey accept `both`, `rdb`, `aof` or `none` for `persistence`; AOF syncs every second and RDB uses the default snapshot schedule. `password` defaults to empty and, when set, configures both authentication and the matching health check. `maxmemory` and `maxmemory-policy` use the syntax accepted by the selected image, and storage is independent of persistence.

Parameters do not activate management features that an image lacks, and they never rewrite credentials in an existing database. SQL Server's `accept-eula` must be set by the operator according to its license. Caddy and haproxy are standalone ingress templates that do not consume applications' nginx assets, so their configuration must come from the selected image or a custom template. See the [template reference](docs/templates.md) for repositories, auxiliary ports and parameter mappings.

## Local preview

`run` validates a delivery and executes its original `install.sh` inside an isolated Linux container, covering bootstrap, image import, migrations, application startup and health checks. The selected Docker or Podman runs the outer container, an independent Docker runs inside it, and the delivery must match the outer engine's native architecture. Outer Compose is not required.

- Every session starts from fresh installation state and test data; clean base environments and verified infrastructure and ingress images can be reused.
- Requested ports are preferred, free ports are allocated on conflicts, and the addresses that actually answered are printed. Web entries publish on all IPv4 interfaces while ordinary TCP services publish on loopback, so LAN reachability still depends on firewalls and virtual-machine networking.
- A required Web probe failure fails the preview. A failure limited to local forwarding for an infrastructure service is reported as a warning, while installation or service health failures still prevent readiness.
- Domain probes connect to the local port while preserving Host and SNI, and HTTPS validates certificates. Hosts files, DNS and trust stores are left untouched, so opening a domain in a browser still needs suitable name resolution. Redirect targets are reported without being followed.
- **Ctrl+C** after readiness returns `0` when cleanup succeeds; cancelling during startup returns `130`. An installation or probe failure keeps the established environment until you press Ctrl+C, and the failing status is retained.
- A pre-existing preview environment for the same application blocks a second session. Force-terminating the process can leave resources behind; inspect the reported ownership before removing them.

> 🚨 **Warning:** A preview really executes the packaged applications and migrations, so use test-appropriate configuration. Containers share the host kernel and never replace acceptance on a real target machine.

## Target operations

Copy the delivery to a matching Linux host and extract it into its own directory. Run the following from a root session:

```sh
mkdir example-delivery
tar -xzf example@1.0-x64.tar.gz -C example-delivery
cd example-delivery
./install.sh
containerizer status --name example
```

The installer saves every required asset into managed locations and installs or reuses `/usr/local/bin/containerizer`. Status, lifecycle, recovery and removal then work independently of the extraction directory.

Target options are space-separated, and `BUNDLE` means a delivery archive or an extracted directory:

| Command | Effect |
| --- | --- |
| `containerizer install BUNDLE [--name NAME] [--no-start]` | Installs an archive or extracted directory. An existing deployment with the same name goes through the upgrade checks. |
| `containerizer prepare BUNDLE --name NAME` | Saves assets and prepares the engine and images, stopping at `Prepared`; it neither stops applications nor runs migrations. |
| `containerizer upgrade BUNDLE --name NAME [--no-start]` | Upgrades an existing deployment. |
| `containerizer list` | Lists installation records. |
| `containerizer status --name NAME` | Prints the installation state as JSON. |
| `containerizer logs [COMPONENT] --name NAME [--tail 100] [--follow]` | Shows or follows container logs. |
| `containerizer stop --name NAME` | Enters maintenance and stops applications and ingress; infrastructure keeps running. |
| `containerizer start --name NAME` | Starts prepared applications and ingress, and leaves maintenance once health checks pass. |
| `containerizer restart [COMPONENT] --name NAME` | Restarts applications and ingress, or the named component; rejected during maintenance or a pending transaction. |
| `containerizer recover --name NAME [--retry-migration VERSION]` | Continues the original failed or interrupted transaction and leaves the installation ready for an explicit start. |
| `containerizer uninstall --name NAME [--purge]` | Uninstalls; `./uninstall.sh [--purge]` identifies the same installation from a delivery directory. |

`--no-start` still starts infrastructure and runs migrations, leaving applications and ingress at `ReadyToStart`. Being in maintenance before the command also suppresses automatic application startup.

### What the archive contains

| Path | Content |
| --- | --- |
| `install.sh`, `uninstall.sh` | Thin root launchers for the bundled executor. |
| `containerizer`, `zh-Hans/` | Native AOT executor and its localized resources. |
| `name[-tag]@version-arch.container` | The completed manifest identical to the external copy. |
| `containerizer.json`, `checksums.sha256` | Execution plan and file checksums; the plan's hash identifies the release content. |
| `compose.yaml`, `config/` | Generated runtime configuration and read-only runtime assets. |
| `images/`, `packages/`, `migration/` | Offline images, bootstrap packages and migration archives, present when used. |
| `README.md`, `README.zh-Hans.md` | Delivery-specific instructions, including the recommended migration order. |

### Files on the host

| Location | Content |
| --- | --- |
| `/var/lib/containerizer/apps/NAME/` | Installation records, release assets and migration state. |
| `/var/lib/containerizer/data/NAME/` | Persistent service data. |
| `/var/log/containerizer/NAME/` | Migration attempt result logs. |
| `/var/cache/containerizer/NAME/` | Bootstrap downloads and staging. |

An ordinary uninstall removes this application's containers, anonymous volumes and networks while retaining data, release assets, migration records and images for diagnosis, reinstallation or a later purge. A purge additionally removes verified, owned local assets and exclusive images, deleting the registration last; an interrupted cleanup can simply be repeated. The shared executor, Docker and Compose, the original delivery media and remote data stay outside the purge scope.

### Upgrades and migrations

An upgrade accepts application, application-configuration and ingress changes. It rejects changes to infrastructure image identities, effective configuration or the bootstrap package set — an ordinary uninstall followed by a reinstall cannot bypass those checks.

A failure keeps the maintenance state and the diagnostics without automatically starting the previous release or re-running failed SQL. Investigate, then run `recover`; specify a failed migration version from the current transaction only when a retry is genuinely appropriate, and finally start explicitly.

Operators sequence multi-host maintenance: prepare and stop every host, upgrade the designated hosts with `--no-start`, then start each host once all required migrations have succeeded.

## Registry mirrors and caches

Put an optional `.mirrors` file in the final output directory. `run` reads the one beside the selected archive, while `plan` does not read it at all:

```ini
docker.io=mirror.example.com/docker.io
mcr.microsoft.com=mirror.example.com/mcr
```

Values are trusted `host[:port][/path-prefix]` endpoints, optionally separated by semicolons; they must not include a scheme, tag, digest, credentials or a trailing slash. The tool reuses a verified cache first, then tries the mirrors in order and finally the original registry, and no fallback can change a pinned digest or platform. These rules never enter the delivery: an ordinary target installation uses the engine's own configuration. See the [hosting registry mirrors](https://github.com/Zongsoft/hosting/blob/main/README.md#registry-mirrors) for a full list of public mirror entries. The complete resolution and cache rules are in [Registry mirrors](docs/implementation.md#registry-mirrors).

Shared application runtime environments are cached under `%LOCALAPPDATA%/Zongsoft/containerizer` on Windows or `~/.cache/Zongsoft/containerizer` on Linux, where an absolute `XDG_CACHE_HOME` replaces the Linux cache base. They contain neither application packages nor application data. Use `make --refresh` to update operating-system bases or runtime patches; [Image identities and caches](docs/implementation.md#image-identities-and-caches) describes the cache layers and the boundaries for manual cleanup.

## Reference examples

- The Zongsoft hosting project
	- [Container delivery](https://github.com/Zongsoft/hosting/blob/main/README.md#container-delivery) — script workflow, image delivery options and Nginx ports
	- [`containerize.cmd`](https://github.com/Zongsoft/hosting/blob/main/containerize.cmd) — interactive plan/make/run wrapper
	- [`.containerized/.settings`](https://github.com/Zongsoft/hosting/blob/main/.containerized/.settings) — shared service defaults for every delivery
	- [`.containerized/.mirrors`](https://github.com/Zongsoft/hosting/blob/main/.containerized/.mirrors) — registry mirrors used by builds and preview
	- [`.env`](https://github.com/Zongsoft/hosting/blob/main/.env) — variables referenced by service defaults
	- [`migrate.cmd`](https://github.com/Zongsoft/hosting/blob/main/migrate.cmd) — migration archive generation
	- [`daemon/pack.cmd`](https://github.com/Zongsoft/hosting/blob/main/daemon/pack.cmd) and [`web/default/pack.cmd`](https://github.com/Zongsoft/hosting/blob/main/web/default/pack.cmd) — application packages
	- [The Web host](https://github.com/Zongsoft/hosting/tree/main/web/default) — `.web/nginx` handoff assets and `web.profile`

- Related tools
	- [Packager](../packager/README.md) — application packages and their [Web hosting configuration](../packager/docs/web.md)
	- [Migrator](../migrator/README.md) — migration archives and launchers
	- [Deployer](../deployer/README.md) — plugin deployment into the hosts these packages carry

## Related documents

- [Implementation](docs/implementation.md): maker data flow, Web handoff, delivery protocol, installation state, caches and verification boundaries.
- [Template reference](docs/templates.md): built-in repositories, ports, parameter mappings and the custom template format.
- [Developer guide](SKILL.md): code maintenance, builds, tests and isolated validation.
