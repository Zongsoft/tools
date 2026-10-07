[English](templates.md) | [简体中文](templates.zh-Hans.md)

# 服务模板参考

日常参数用法见 [README](../README.zh-Hans.md#内置服务)。本文补充仓库、端口、参数映射及自定义模板格式。内置定义位于 [templates](../templates)，由 [TemplateCatalog](../src/TemplateCatalog.cs)、[ServiceOptions](../src/ServiceOptions.cs) 和 [ServiceImagePreparer](../src/ServiceImagePreparer.cs) 消费。模板声明不代表所有镜像 tag 和平台组合均已通过实际运行验证。

## 内置仓库与存储

仓库不含 tag；tag 来自组件选择或默认配置，缺省为字面 latest。下表数据路径位于容器内；宿主持久目录为 `/var/lib/containerizer/data/<name>/<service>/`，多数据挂载时追加稳定挂载 ID。

| 服务 | 默认仓库 | 声明架构 | 容器数据路径 |
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

nginx 根据应用托管资产规划入口；caddy/haproxy 使用其自身模板，不读取 nginx 的 .bindings。应用本身不使用基础服务模板。

## 参数映射

下表是模板声明的全部环境/命令参数。未设置的可选参数不输出，沿用镜像行为；显式空值保留，必填空值在 make 拒绝。值均为字符串，镜像负责密码策略、单位及参数组合约束。变量绑定列之外的参数不会仅因同名 .env 变量存在而自动取值。

| 服务 | 参数 | 环境变量或命令参数 | 默认值 | 变量绑定 | 要求 |
| --- | --- | --- | --- | --- | --- |
| `elasticsearch` | `password` | `ELASTIC_PASSWORD` | — | — | 必填 |
| `grafana` | `admin-password` | `GF_SECURITY_ADMIN_PASSWORD` | — | — | 必填 |
| `mariadb` | `root-password` | `MARIADB_ROOT_PASSWORD` | — | — | 必填 |
| `mariadb` | `database` | `MARIADB_DATABASE` | — | — | 可选 |
| `mariadb` | `user` | `MARIADB_USER` | — | — | 可选 |
| `mariadb` | `password` | `MARIADB_PASSWORD` | — | — | 可选 |
| `mongodb` | `root-user` | `MONGO_INITDB_ROOT_USERNAME` | — | — | 必填 |
| `mongodb` | `root-password` | `MONGO_INITDB_ROOT_PASSWORD` | — | — | 必填 |
| `mysql` | `root-password` | `MYSQL_ROOT_PASSWORD` | — | `mysql_root_password` | 必填 |
| `mysql` | `database` | `MYSQL_DATABASE` | — | — | 可选 |
| `mysql` | `user` | `MYSQL_USER` | — | — | 可选 |
| `mysql` | `password` | `MYSQL_PASSWORD` | — | — | 可选 |
| `nacos` | `mode` | `MODE` | `standalone` | — | 可选 |
| `nacos` | `auth-token` | `NACOS_AUTH_TOKEN` | — | — | 必填 |
| `nacos` | `identity-key` | `NACOS_AUTH_IDENTITY_KEY` | — | — | 必填 |
| `nacos` | `identity-value` | `NACOS_AUTH_IDENTITY_VALUE` | — | — | 必填 |
| `opensearch` | `password` | `OPENSEARCH_INITIAL_ADMIN_PASSWORD` | — | — | 必填 |
| `postgresql` | `password` | `POSTGRES_PASSWORD` | — | — | 必填 |
| `postgresql` | `user` | `POSTGRES_USER` | `postgres` | — | 可选 |
| `postgresql` | `database` | `POSTGRES_DB` | `postgres` | — | 可选 |
| `rabbitmq` | `user` | `RABBITMQ_DEFAULT_USER` | — | — | 必填 |
| `rabbitmq` | `password` | `RABBITMQ_DEFAULT_PASS` | — | — | 必填 |
| `redis` | `maxmemory` | `--maxmemory` | — | — | 可选 |
| `redis` | `maxmemory-policy` | `--maxmemory-policy` | — | — | 可选 |
| `rustfs` | `access-key` | `RUSTFS_ACCESS_KEY` | — | `rustfs_access_key` | 必填 |
| `rustfs` | `secret-key` | `RUSTFS_SECRET_KEY` | — | `rustfs_secret_key` | 必填 |
| `sqlserver` | `password` | `MSSQL_SA_PASSWORD` | — | — | 必填 |
| `sqlserver` | `accept-eula` | `ACCEPT_EULA` | — | — | 必填 |
| `valkey` | `maxmemory` | `--maxmemory` | — | — | 可选 |
| `valkey` | `maxmemory-policy` | `--maxmemory-policy` | — | — | 可选 |

通用参数及专用转换：

| 参数 | 语义 |
| --- | --- |
| port | 默认端口发布；除 nginx 外接受整数、IPv4:端口、[IPv6]:端口或 none |
| storage | 有数据挂载时提供 persistent/temporary；默认 persistent，作用于全部数据挂载 |
| Redis/Valkey persistence | both（默认）、rdb、aof、none；RDB 周期为 `3600 1 300 100 60 10000`，AOF 使用 everysec |
| Redis/Valkey password | 默认空/无认证；非空设置 requirepass，并同步带认证的健康检查 |
| nginx port | 逗号分隔的原监听端口:宿主端口，如 `80:18080,443:none`；未覆盖的端口保持原号 |

persistence=none 关闭自动快照/AOF，不删除旧文件或禁止手动保存。password 只处理简单密码认证，不生成 ACL 多用户配置。maxmemory 及其策略按所选镜像的语法填写。SQL Server accept-eula 须由操作者根据许可设置；数据库初始化参数不会自动更新已有数据中的凭据。

辅助端口如下，发布本身不会启用镜像缺少的功能：

| 服务 | 参数 | 容器端口 | 默认宿主发布 |
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

RabbitMQ 管理入口需要启用管理功能的镜像；Nacos 控制台端口针对其相应镜像布局，客户端 gRPC 的端口偏移仍由操作者配置。TDengine 6041 同时承载 REST 和 `/rest/ws`，6060 控制台要求镜像包含并启动 Explorer。RustFS 控制台使用 `/rustfs/console/`，根路径可能返回 S3 响应。自动端口避让不改写客户端配置。

## 自定义模板格式

在基础服务组件中使用 `template=路径`，路径相对于最终 source；也可使用内置模板名作为别名组件的模板。模板采用 Core Profile，固定 `version=1`，不导入其他文件、不提供继承或可执行生命周期钩子。

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

此示例展示模板格式；内置 Redis/Valkey 的特殊持久化和认证处理以模板身份识别，自定义文件不会仅因 image 相同而自动获得这些转换。

### 根字段

| 字段 | 含义 / 默认值 |
| --- | --- |
| version | 模板协议，必须为 1 |
| image | 完整默认仓库；可由组件 repository 覆盖 |
| kind | infrastructure（默认）或 ingress；不能为 application |
| platforms | 分号分隔的 x64/arm64，默认两者 |
| entrypoint、command、health | JSON 字符串数组，保留参数边界 |
| workdir、user | 容器工作目录、运行用户 |
| data-owner | 数据目录账号或数字 UID:GID；从固定镜像解析账号/组 |
| restart | 正常运行策略，默认 unless-stopped；维护策略由执行器控制 |
| stop-signal | 默认 SIGTERM |
| health-timeout | 秒数，默认 5 |
| dependencies | 分号分隔的本节点服务 ID，由服务规划校验 |

### 段落

| 段落 | 内容 |
| --- | --- |
| [environment] | 默认环境项 |
| [ports] | 命名的 `[IPv4:]宿主端口:容器端口[/tcp\|udp]`；省略地址时为 0.0.0.0 |
| [data] | 稳定挂载 ID → Linux 容器绝对路径 |
| [configuration] | 容器文件绝对路径 → 相对于模板的本地文件，原样复制并只读绑定 |
| [settings 参数名] | environment 和/或 argument 映射，可带 variable、default、required=true、分号分隔的 choices |

default 命名端口产生 port 参数，其余命名端口产生 名称-port，后者默认不发布，除非 settings 声明默认值。有 data 时提供 storage，未知参数/字段报错。

参数值依次选择：显式 settings、对应的显式 environment!、variable 绑定、default。显式空值阻止回退；变量存在但为空仍为空。参数与显式环境同时提供且值冲突时报错；不由参数管理的环境项可直接覆盖。工具派生环境不重复写入完成清单，避免回放覆盖编辑后的 settings。

make 对模板值使用清单变量视图求值，支持 $(name)、%name% 及字面转义；固定元数据使用普通字面值。JSON 数组先解析再逐项求值，变量中的引号不会改变参数边界。配置文件只对输入路径求值，正文原样复制。现场不再次求值。

应用启动、运行时及 Web 交接不属于模板扩展，见 [实现说明](implementation.zh-Hans.md#应用web-与服务准备)。不支持把原始 Compose/Pod YAML 作为服务输入，也没有通用 config 参数、模板继承或资源限制配置入口。
