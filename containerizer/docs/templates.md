[English](templates.md) | [简体中文](templates.zh-Hans.md)

# Service template reference

See the [README](../README.md#built-in-services) for everyday settings. This reference covers repositories, ports, parameter mappings and custom template syntax. Built-ins live in [templates](../templates), consumed by [TemplateCatalog](../src/TemplateCatalog.cs), [ServiceOptions](../src/ServiceOptions.cs) and [ServiceImagePreparer](../src/ServiceImagePreparer.cs). Declarations do not establish actual runtime acceptance for every image tag/platform combination.

## Built-in repositories and storage

Repositories exclude tags; tags come from component selection/defaults and otherwise use the literal latest. Data paths below are inside containers. Host persistent directories use `/var/lib/containerizer/data/<name>/<service>/`, adding stable mount IDs when there are multiple data mounts.

| Service | Default repository | Declared architectures | Container data paths |
| --- | --- | --- | --- |
| `caddy` | `docker.io/library/caddy` | `x64;arm64` | `/data` |
| `clickhouse` | `docker.io/clickhouse/clickhouse-server` | `x64;arm64` | `/var/lib/clickhouse` |
| `consul` | `docker.io/hashicorp/consul` | `x64;arm64` | `/consul/data` |
| `elasticsearch` | `docker.elastic.co/elasticsearch/elasticsearch` | `x64;arm64` | `/usr/share/elasticsearch/data` |
| `emqx` | `docker.io/emqx/emqx` | `x64;arm64` | `/opt/emqx/data` |
| `etcd` | `quay.io/coreos/etcd` | `x64;arm64` | `/etcd-data` |
| `grafana` | `docker.io/grafana/grafana` | `x64;arm64` | `/var/lib/grafana` |
| `haproxy` | `docker.io/library/haproxy` | `x64;arm64` | — |
| `influxdb` | `docker.io/library/influxdb` | `x64;arm64` | `/var/lib/influxdb2` |
| `kafka` | `docker.io/apache/kafka` | `x64;arm64` | `/var/lib/kafka/data` |
| `loki` | `docker.io/grafana/loki` | `x64;arm64` | `/loki` |
| `mariadb` | `docker.io/library/mariadb` | `x64;arm64` | `/var/lib/mysql` |
| `memcached` | `docker.io/library/memcached` | `x64;arm64` | — |
| `mongodb` | `docker.io/library/mongo` | `x64;arm64` | `/data/db` |
| `mosquitto` | `docker.io/library/eclipse-mosquitto` | `x64;arm64` | `/mosquitto/data` |
| `mysql` | `docker.io/library/mysql` | `x64;arm64` | `/var/lib/mysql` |
| `nacos` | `docker.io/nacos/nacos-server` | `x64;arm64` | `/home/nacos/data` |
| `nats` | `docker.io/library/nats` | `x64;arm64` | `/data` |
| `nginx` | `docker.io/library/nginx` | `x64;arm64` | — |
| `opensearch` | `docker.io/opensearchproject/opensearch` | `x64;arm64` | `/usr/share/opensearch/data` |
| `otel` | `docker.io/otel/opentelemetry-collector-contrib` | `x64;arm64` | — |
| `postgresql` | `docker.io/library/postgres` | `x64;arm64` | `/var/lib/postgresql` |
| `prometheus` | `docker.io/prom/prometheus` | `x64;arm64` | `/prometheus` |
| `rabbitmq` | `docker.io/library/rabbitmq` | `x64;arm64` | `/var/lib/rabbitmq` |
| `redis` | `docker.io/library/redis` | `x64;arm64` | `/data` |
| `rustfs` | `docker.io/rustfs/rustfs` | `x64;arm64` | `/data` |
| `sqlserver` | `mcr.microsoft.com/mssql/server` | `x64` | `/var/opt/mssql` |
| `tdengine` | `docker.io/tdengine/tsdb` | `x64;arm64` | `/var/lib/taos` |
| `valkey` | `docker.io/valkey/valkey` | `x64;arm64` | `/data` |
| `zookeeper` | `docker.io/library/zookeeper` | `x64;arm64` | `/data` |

Nginx plans ingress from application hosting assets. Caddy/haproxy use their own templates and do not read nginx .bindings. Applications themselves do not use infrastructure templates.

## Parameter mappings

These are all template-declared environment/command parameters. Unset optional parameters are omitted, leaving image behavior intact. Explicit empty values are retained; make rejects empty required values. Values are strings; images enforce password policies, units and combinations. Only the variable-binding column establishes automatic .env bindings.

| Service | Parameter | Environment variable or argument | Default | Variable binding | Requirement |
| --- | --- | --- | --- | --- | --- |
| `elasticsearch` | `password` | `ELASTIC_PASSWORD` | — | — | Required |
| `grafana` | `admin-password` | `GF_SECURITY_ADMIN_PASSWORD` | — | — | Required |
| `mariadb` | `root-password` | `MARIADB_ROOT_PASSWORD` | — | — | Required |
| `mariadb` | `database` | `MARIADB_DATABASE` | — | — | Optional |
| `mariadb` | `user` | `MARIADB_USER` | — | — | Optional |
| `mariadb` | `password` | `MARIADB_PASSWORD` | — | — | Optional |
| `mongodb` | `root-user` | `MONGO_INITDB_ROOT_USERNAME` | — | — | Required |
| `mongodb` | `root-password` | `MONGO_INITDB_ROOT_PASSWORD` | — | — | Required |
| `mysql` | `root-password` | `MYSQL_ROOT_PASSWORD` | — | `mysql_root_password` | Required |
| `mysql` | `database` | `MYSQL_DATABASE` | — | — | Optional |
| `mysql` | `user` | `MYSQL_USER` | — | — | Optional |
| `mysql` | `password` | `MYSQL_PASSWORD` | — | — | Optional |
| `nacos` | `mode` | `MODE` | `standalone` | — | Optional |
| `nacos` | `auth-token` | `NACOS_AUTH_TOKEN` | — | — | Required |
| `nacos` | `identity-key` | `NACOS_AUTH_IDENTITY_KEY` | — | — | Required |
| `nacos` | `identity-value` | `NACOS_AUTH_IDENTITY_VALUE` | — | — | Required |
| `opensearch` | `password` | `OPENSEARCH_INITIAL_ADMIN_PASSWORD` | — | — | Required |
| `postgresql` | `password` | `POSTGRES_PASSWORD` | — | — | Required |
| `postgresql` | `user` | `POSTGRES_USER` | `postgres` | — | Optional |
| `postgresql` | `database` | `POSTGRES_DB` | `postgres` | — | Optional |
| `rabbitmq` | `user` | `RABBITMQ_DEFAULT_USER` | — | — | Required |
| `rabbitmq` | `password` | `RABBITMQ_DEFAULT_PASS` | — | — | Required |
| `redis` | `maxmemory` | `--maxmemory` | — | — | Optional |
| `redis` | `maxmemory-policy` | `--maxmemory-policy` | — | — | Optional |
| `rustfs` | `access-key` | `RUSTFS_ACCESS_KEY` | — | `rustfs_access_key` | Required |
| `rustfs` | `secret-key` | `RUSTFS_SECRET_KEY` | — | `rustfs_secret_key` | Required |
| `sqlserver` | `password` | `MSSQL_SA_PASSWORD` | — | — | Required |
| `sqlserver` | `accept-eula` | `ACCEPT_EULA` | — | — | Required |
| `valkey` | `maxmemory` | `--maxmemory` | — | — | Optional |
| `valkey` | `maxmemory-policy` | `--maxmemory-policy` | — | — | Optional |

Common parameters and dedicated transformations:

| Parameter | Meaning |
| --- | --- |
| port | Default publication; except nginx, accepts an integer, IPv4:port, [IPv6]:port or none |
| storage | persistent/temporary when data mounts exist; defaults to persistent and affects all data mounts |
| Redis/Valkey persistence | both (default), rdb, aof or none; RDB schedule is `3600 1 300 100 60 10000` and AOF uses everysec |
| Redis/Valkey password | Empty/no authentication by default; a nonempty value sets requirepass and matching authenticated health checks |
| nginx port | Comma-separated original-listener-port:host-port pairs, such as `80:18080,443:none`; unspecified ports keep their original number |

Persistence none disables automatic snapshots/AOF without deleting old files or prohibiting manual saves. Password provides simple authentication, without generating multi-user ACL configuration. Maxmemory/policy use the chosen image's syntax. Operators set SQL Server accept-eula according to its license. Database initialization parameters do not automatically update credentials in existing data.

Auxiliary publications do not activate features absent from the selected image:

| Service | Parameter | Container port | Default host publication |
| --- | --- | --- | --- |
| `clickhouse` | `native-port` | `9000` | `none` |
| `emqx` | `dashboard-port` | `18083` | `none` |
| `emqx` | `websocket-port` | `8083` | `none` |
| `nacos` | `console-port` | `8080` | `none` |
| `nacos` | `grpc-port` | `9848` | `none` |
| `nats` | `monitoring-port` | `8222` | `none` |
| `otel` | `http-port` | `4318` | `none` |
| `rabbitmq` | `management-port` | `15672` | `none` |
| `rustfs` | `console-port` | `9001` | `127.0.0.1:9001` |
| `tdengine` | `console-port` | `6060` | `127.0.0.1:6060` |

RabbitMQ management requires an image with management enabled. Nacos console ports depend on the corresponding image layout; operators still configure client gRPC port offsets. TDengine 6041 carries both REST and `/rest/ws`; its 6060 console requires an image that includes and starts Explorer. RustFS's console uses `/rustfs/console/`, while the root may return an S3 response. Automatic port avoidance does not rewrite client configuration.

## Custom template format

Set template=PATH on an infrastructure component, relative to final source, or use a built-in template name for an alias component. Templates use Core Profile with version=1, without imports, inheritance or executable lifecycle hooks.

```ini
version=1
image=docker.io/library/redis
kind=infrastructure
platforms=x64;arm64
command=["redis-server"]
health=["CMD","redis-cli","ping"]
data-owner=redis

[ports]
default=127.0.0.1:6379:6379

[data]
data=/data

[settings maxmemory]
argument=--maxmemory
```

This illustrates the format. Built-in Redis/Valkey persistence/authentication transformations are selected by template identity; a custom file does not gain them merely by using the same image.

### Root fields

| Field | Meaning / default |
| --- | --- |
| version | Template protocol; must be 1 |
| image | Fully qualified default repository; component repository can override it |
| kind | infrastructure (default) or ingress; not application |
| platforms | Semicolon-separated x64/arm64; both by default |
| entrypoint, command, health | JSON string arrays preserving argument boundaries |
| workdir, user | Container working directory and execution user |
| data-owner | Data account or numeric UID:GID; accounts/groups resolve from the pinned image |
| restart | Normal runtime policy, default unless-stopped; executor controls maintenance policy |
| stop-signal | Defaults to SIGTERM |
| health-timeout | Seconds, default 5 |
| dependencies | Semicolon-separated local service IDs, checked by service planning |

### Sections

| Section | Contents |
| --- | --- |
| [environment] | Default environment values |
| [ports] | Named `[IPv4:]host-port:container-port[/tcp\|udp]` mappings; omitted address means 0.0.0.0 |
| [data] | Stable mount ID → absolute Linux container path |
| [configuration] | Absolute container file path → local file relative to the template, copied unchanged and mounted read-only |
| [settings NAME] | environment and/or argument mapping, with optional variable, default, required=true and semicolon-separated choices |

A port named default creates the port parameter. Other names create NAME-port parameters, unpublished unless a settings declaration provides a default. Data mounts provide storage. Unknown parameters/fields fail.

Values are selected from explicit settings, the corresponding explicit environment! value, variable binding, then default. Explicit emptiness blocks fallback; an existing empty variable stays empty. Conflicting simultaneous parameter/environment values fail. Environment values not managed by parameters can be overridden directly. Derived environment is not duplicated in completed manifests, preventing replay from overriding edited settings.

Make evaluates template values against the manifest variable view, supporting $(name), %name% and literal escapes; fixed metadata uses literal values. JSON arrays are parsed before evaluating individual elements, so quotes inside variables cannot change argument boundaries. Only configuration input paths are evaluated; file contents are copied unchanged. Targets do not evaluate values again.

Application startup, runtimes and Web handoff are outside template extensions; see [Implementation](implementation.md#applications-web-and-service-preparation). Raw Compose/Pod YAML is not accepted as service input. There is no generic config parameter, template inheritance or resource-limit configuration entry.
