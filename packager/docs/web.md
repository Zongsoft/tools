# Web Hosting Configuration Guide

[English](web.md) | [简体中文](web.zh-Hans.md)

This guide covers the complete `web.profile` syntax, examples, Nginx mapping, and installation behavior. See the [README](../README.md) for general packaging options and the [implementation guide](implementation.md) for internal types and package adapters.

## Contents

- [Capabilities and quick start](#overview)
- [Command option and input file](#command)
- [Profile syntax, scopes, and imports](#profile)
- [Sites, host names, and bindings](#bindings)
- [Frontend HTTPS and certificates](#certificates)
- [Paths and matching](#routes)
- [Backend addresses, application address, and weights](#servers)
- [Backend replacement and inheritance examples](#inheritance)
- [Backend HTTPS and certificate verification](#tls)
- [Balancing, failures, retries, and affinity](#policies)
- [Active health checks](#health)
- [Request headers and WebSocket](#headers)
- [Native hoster settings](#native)
- [Variables and escaping](#variables)
- [Complete configuration example](#example)
- [Payload selection and output layout](#delivery)
- [Bare-metal installation, container builds, and lifecycle](#installation)
- [Troubleshooting and capability boundaries](#troubleshooting)
- [IIS extension boundaries](#iis)

<a id="overview"></a>
## Capabilities and quick start

`--web` converts abstract sites, bindings, routes, and backends into configuration for the selected Web hoster. Nginx is implemented; `iis` is recognized but selecting it reports unimplemented.

Each package generates one `.web/nginx/<PackageName>.conf` containing its sites, routes, application-specific pools, and health match blocks. It is a fragment for an existing Nginx `http` include, with no outer `http { ... }`. It is not a full nginx.conf. Instance settings such as worker_processes/events and arbitrary native http directives/blocks are unsupported.

For the application and Nginx on one machine, create:

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = ~
```

With Example.Web.dll and its runtime files in publish, run this single-line command from Windows or Linux. The application version is an example:

```shell
dotnet-pack deb --name:Example.Web --version:1.0.0 --platform:linux --architecture:x64 --framework:net10.0 --source:publish --output:../packages --daemon:example.web --listen:8069 --web:nginx --exclude:*.profile
```

The generated application service listens on http://127.0.0.1:8069, reused by `~`. Nginx listens on IPv4/IPv6 HTTP port 80. bind controls Nginx; --listen controls the application.

For separate containers, use the application service name in the deployment network:

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
server = http://app:8069
```

The application must accept connections from other containers, for example with --listen:http://0.0.0.0:8069 when generating its service. Deployment provides networking and name resolution. Container builds disable Web activation and read the configuration from the known installation root; see [installation](#installation).

Packager does not start Nginx on the build machine, probe backends, issue certificates, or maintain runtime balancing state. Static-site hosting, a general rewrite language, certificate installation, and IIS/MSI are outside current common capabilities.

<a id="command"></a>
## Command option and input file

Syntax: `--web:<hoster[:filepath]>`. Hoster names are case-insensitive. Only the explicit option enables generation; a source web.profile or environment variable web=nginx alone does not.

| Value | Behavior |
| --- | --- |
| Omitted, empty, whitespace only, `none`, `none:` | Disabled; no input lookup or parsing. |
| `nginx`, `nginx:` | Read final source/web.profile. |
| `nginx:config/web.profile` | Relative to final source. |
| `nginx:../shared/web.production.profile` | May be outside source; no ancestor search. |
| `nginx:D:\config\web.profile` | Absolute path on the build platform; only the first colon is split. |
| `:web.profile`, `none:web.profile` | Error: a nonempty path needs an enabled hoster. |
| `iis`, `iis:...` | Unimplemented, with no Nginx fallback. |
| Other name | Unknown hoster error. |

An explicit path must identify one file, not a directory, wildcard, or list. Terminal quoting supports spaces in the top-level path:

```shell
dotnet-pack deb "--web:nginx:config files/web.profile"
```

The entire option is evaluated with final command variables before trimming, splitting, and validation. Relative paths use source, not the initial working directory. Missing/unreadable files or imports, parsing failures, and generation errors abort this attempt without publishing partial artifacts or saving source .version because of that failed attempt.

Short commands below assume source .version or other options provide required identity. --web does not replace general packaging parameters.

<a id="profile"></a>
## Profile syntax, scopes, and imports

### Text and sections

Zongsoft.Core Profile loads the input. UTF-8 is recommended:

```ini
# Common root defaults
forwarded = true
server = http://app:8069

[api]
host = api.example.com
bind!legacy = http://*

[api hub.devices]
path = /hub/devices
websocket = true
```

- Section part one identifies a site; part two identifies a route. Separate them with spaces or a Tab. Dots are literal: hub.devices does not imply /hub/devices.
- At most two levels are allowed; [api hub devices] fails. Section names cannot contain /, :, or !; [api /hub/devices] is invalid, so put the URL in path. Site identifiers do not infer domains, directories, or service names.
- Fields, section identifiers, and named-entry identifiers are case-insensitive. Values retain their field-specific semantics, including path case.
- Root defaults precede the first section. They do not create sites; at least one valid site is required.
- Lines beginning with # or ; are comments. #@import is a directive. Inline comments are not stripped and become part of the value.
- The first = separates name and value; later equals signs remain, as in weight=3.
- Profile does not strip quotes, support multiline values, or interpret general backslash escapes. `header!X-Name='hello'` includes the single quotes.
- `key` and `key=` both explicitly declare empty; they are not omission. Empty behavior is field-specific.
- Duplicate full keys in one file and section fail. Imports override in read order. Mixing scalar server and named members has additional [rules](#inheritance).

### Field scope reference

Root → site → route means an omitted child field inherits; an explicit declaration follows its field-specific override rules.

| Field | Scope | Default or rule |
| --- | --- | --- |
| `host` | Site | Optional comma-separated request hosts or IP literals. |
| `bind!name` | Site | A common binding or selected hoster's native listener is required. |
| `certificate`, `certificate-key` | Site | Frontend HTTPS certificate/key references. |
| `path` | Route | Missing/empty means / for prefix/exact; required for regex. |
| `match` | Route | prefix; also exact/regex; explicit empty fails. |
| `server`, `server!name` | Root → site → route | Group inheritance/replacement; ~ if absent everywhere. |
| `server-balance`, `server-failure-*`, `server-retry*`, `server-affinity*` | Root → site → route | Individual [policy fields](#policies). |
| `server-tls-*` | Root → site → route | Only HTTPS backends consume [TLS settings](#tls). |
| `server-health`, `server-health-*` | Root → site → route | Only an effective nonempty path enables [checks](#health). |
| `forwarded`, `websocket` | Root → site → route | Defaults true and false, respectively. |
| `header!name`, `server-health-header!name` | Root → site → route | Separate header collections, each merged by name. |
| `nginx:...`, `iis:...` | Site or route | Native entries; forbidden at root. |

Unknown fields, wrong scopes, or missing named suffixes such as bind=... and header!=... fail.

### Imports and order

Shared `shared/common.profile`:

```ini
forwarded = true
header!X-Application = example-api

[api]
bind!legacy = http://*
server = http://shared:8069
```

Host `web.profile`:

```ini
#@import shared/common.profile

[api]
host = api.example.com
server = http://local:8069
```

The result uses local and retains the shared binding/header. Imports run at their read position: usually import first, then override locally. Later imports can override earlier local values.

Paths are relative to **the declaring file**. An import of backend.profile from shared/common.profile means shared/backend.profile. Imports merge at the Profile root, not inside the current section, and never automatically rename sites.

Separate multiple import targets with whitespace, Tabs, or |:

```ini
#@import shared/common.profile|shared/sites.profile
```

Import paths do not support variable evaluation, enclosing quotes, wildcards, or filenames containing spaces. Use a path without spaces; top-level --web quoting does not change this rule. Prefer / in cross-platform imports.

All direct and recursive imports must exist and be readable. Cycles fail; default maximum depth is 64 including the root. Reimporting a completed file is allowed. Diagnostics preserve source file, section, entry, line, and underlying details.

<a id="bindings"></a>
## Sites, host names, and bindings

host describes the request host, while bind describes the local listener:

```ini
[api]
host = api.example.com,192.0.2.10
bind!legacy = http://*:80
bind!secure = https://*:443
server = http://app:8069
```

legacy/secure are user-chosen group names. HTTP 80 and HTTPS 443 may be omitted:

```ini
[api]
host = api.example.com
bind!legacy = http://*,http://[::]
bind!secure = https://*,https://[::]
server = http://app:8069
```

| Address | Meaning |
| --- | --- |
| `http://*`, `http://0.0.0.0:80` | IPv4 wildcard HTTP 80. |
| `http://[::]` | IPv6 wildcard HTTP 80. |
| `https://*`, `https://[::]:443` | IPv4 and IPv6 HTTPS 443, respectively. |
| `http://127.0.0.1:8080` | IPv4 loopback 8080. |
| `https://[2001:db8::10]:8443` | Specific IPv6 address and port. |

Every comma element must be complete: http://*,[::] fails. Whitespace around commas is accepted; empty elements and trailing commas fail. Ports are 1–65535; IPv6 needs brackets. DNS names, paths, queries, and fragments are invalid bindings. * means IPv4 only, not a portable dual-stack shortcut.

Only http/https are supported. WebSocket uses websocket=true on routes, not ws/wss bind schemes. host=192.0.2.10 does not restrict the listening interface. Omitted host adds no common host-name restriction or inferred domain; Nginx virtual-host selection still applies.

host trims spaces around commas and deduplicates names case-insensitively. Explicit empty, empty list elements, and names containing whitespace fail. Supply host names or IPs without scheme, path, or port; listening ports belong in bind. Use native settings for more complex Nginx server_name expressions.

A local same-name binding group replaces the whole imported group. Replacing `bind!legacy=http://*,http://[::]` with `bind!legacy=http://127.0.0.1:8080` leaves one listener. Different groups are combined and normalized duplicates within a site emitted once.

Every site needs a common binding or the selected hoster's native listener; iis entries cannot satisfy Nginx. Static cross-site listener/Host duplicates fail; distinct Hosts can share a listener. Other installed configuration conflicts require target Nginx validation.

<a id="certificates"></a>
## Frontend HTTPS and certificates

These configure client → Nginx TLS, separately from [backend TLS](#tls). One pair serves all HTTPS bindings in a site; HTTP does not consume it.

| Field | Omitted | Explicitly empty |
| --- | --- | --- |
| `certificate` | `.certificates/<PackageName>.pem` under actual installation root. | Error. |
| `certificate-key` | Final effective certificate file. | Clear inherited separate key; use final certificate. |

### Default combined PEM

```ini
[api]
host = api.example.com
bind!secure = https://*
server = http://app:8069
```

For zongsoft.web installed at /opt/zongsoft/web, the generated site contains:

```nginx
ssl_certificate "/opt/zongsoft/web/.certificates/zongsoft.web.pem";
ssl_certificate_key "/opt/zongsoft/web/.certificates/zongsoft.web.pem";
```

That PEM contains the certificate chain and matching private key. Packager does not create it, convert PFX, or install system certificates. Sites omitting both fields share the default file, whose certificate must cover their domains.

### Custom combined and separate files

Combined PEM:

```ini
[api]
host = api.example.com
bind!secure = https://*
certificate = file:/etc/tls/api/combined.pem
server = http://app:8069
```

Separate certificate and key:

```ini
[api]
host = api.example.com
bind!secure = https://*
certificate = file:/etc/tls/api/fullchain.pem
certificate-key = file:/etc/tls/api/private.key
server = http://app:8069
```

Setting only certificate-key pairs it with the default certificate. Common fields need not be declared together; native certificate directives have a [pair replacement rule](#native).

### Clearing an inherited separate key

`shared/site.profile`:

```ini
[api]
bind!secure = https://*
certificate = file:/etc/tls/shared/fullchain.pem
certificate-key = file:/etc/tls/shared/private.key
server = http://app:8069
```

`web.profile`:

```ini
#@import shared/site.profile

[api]
certificate = file:/etc/tls/api/combined.pem
certificate-key =
```

Both directives now reference combined.pem. Changing only certificate retains the inherited private.key.

file: references a Linux **target absolute path**, not a Windows build path. It is not read, required to exist, or automatically packaged at build time. Supply files as ordinary payload, through hooks, or deployment mounts. Enabled nginx -t detects missing files/keys and key mismatches.

Tar default references follow actual INSTALL_PATH relocation and exclude DESTDIR. Explicit absolute references remain unchanged. Disabling activation still resolves default paths. When moving configuration across containers, make its referenced paths valid in the Nginx container.

<a id="routes"></a>
## Paths and matching

### Default route and explicit fallback

A site without route sections gets an automatic / prefix route using its effective backend. Once route sections exist, only those routes are generated, with no extra fallback.

```ini
[api]
bind!legacy = http://*
server = http://app:8069

[api root]

[api devices]
path = /hub/devices
websocket = true
```

[api root] explicitly retains the root fallback while omitting path/match. These alternatives are equivalent in prefix/exact mode; choose one:

```ini
[api root]
```

```ini
[api root]
path =
```

```ini
[api root]
path = /
```

root has no special identifier semantics. Different identifiers resolving to the same match/path still fail as duplicates.

Nonempty prefix/exact paths must begin with / and cannot contain queries, fragments, or control characters. Query parameters belong to requests, not the matching field. Packager does not add a slash or convert identifier dots into path separators.

### Three matching modes

match values are case-insensitive. Omission means prefix; explicit empty and unknown values fail.

| match | Meaning | Nginx structure |
| --- | --- | --- |
| `prefix` | Case-sensitive prefix. | `location /api { ... }` |
| `exact` | Case-sensitive exact path. | `location = /health { ... }` |
| `regex` | Case-insensitive regular expression. | <code>location ~* "^/orders/[0-9]+&#36;" { ... }</code> |

Prefix example:

```ini
[api products]
path = /api
```

Matches /api, /api/orders, and /apix, but not /API. Use /api/ or a regex for a segment boundary. Trailing slashes matter; certain slash-ending Nginx proxy locations redirect requests missing that slash. See [Nginx location](https://nginx.org/en/docs/http/ngx_http_core_module.html#location).

Exact example:

```ini
[api health]
path = /health
match = exact
```

Matches /health and /health?full=true, but not /health/, /health/live, or /Health. Queries do not participate in path matching.

Regex examples:

```ini
[api orders]
path = ^/orders/[0-9]+$
match = regex

[api segment]
path = ^/api(/|$)
match = regex

[api json]
path = ^/files/.*\.json$
match = regex
```

These select numeric order IDs, the api segment boundary, and JSON paths. /Orders/123 and /FILES/a.JSON also match. Regex requires an explicit nonempty path; Packager adds no anchors and does not infer match from path contents. Write one backslash for `\.`, no Markdown `\_` escapes, and no Nginx ~* prefix in path. A suffix pattern such as <code>\.json&#36;</code> is valid; regex need not start with /.

### Precedence and overlap

For generated flat routes, exact matches win. Otherwise Nginx remembers the longest prefix, checks regex routes in declaration order, and uses the first matching regex. The remembered prefix is used only when no regex matches.

```ini
[api]
bind!legacy = http://*
server = http://app:8069

[api root]

[api resources]
path = /resources/

[api json]
path = \.json$
match = regex

[api health]
path = /health
match = exact
```

| Request | Selected rule |
| --- | --- |
| /health?full=true | Exact health. |
| /resources/a.json | Regex json, ahead of the resources prefix. |
| /resources/a.txt | Longest prefix resources. |
| /other | Root prefix. |

Put narrow overlapping regex rules before broad ones:

```ini
[api order-number]
match = regex
path = ^/orders/[0-9]+$

[api order-any]
match = regex
path = ^/orders/.*$
```

Reversing them lets the broad rule capture numeric orders first. Imports preserve first appearance: shared A/B followed by overriding A and adding C remains A/B/C, not B/A/C.

Identical match/path fails. Packager does not prove equivalence or overlap between different regexes. The target engine performs matching, including URI normalization; preserving an expression does not promise bytewise matching of the original request line. Packager does not additionally decode, concatenate, or rewrite it.

### Forwarded path and portability

Common server settings generate a target without a replacement URI:

```text
Client:  /api/orders/123?detail=true
Backend: http://app:8069/api/orders/123?detail=true
```

The prefix is not removed; the query is preserved. Regex selection adds no capture substitution or business-path rewrite. This does not bypass the hoster's own URI processing.

Common syntax has no ^~, user-defined named/nested locations, or capture rewrite language. Regex is not a file wildcard. Nginx uses PCRE/PCRE2; future IIS support must adapt URL Rewrite's ECMAScript regex and relative-path input. .NET Regex validation cannot replace the target engine, and arbitrary expressions are not guaranteed equivalent across engines. See [IIS URL Rewrite](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/url-rewrite-module-configuration-reference).

<a id="servers"></a>
## Backend addresses, application address, and weights

### Single backend and URI

server accepts an HTTP/HTTPS scheme, host, and optional port. A sole trailing / is removed rather than used as a business path:

| Value | Result |
| --- | --- |
| `http://app`, `http://app:80/` | The same HTTP 80 backend. |
| `https://api.internal` | HTTPS 443. |
| `http://192.0.2.20:8069` | IPv4 address and port. |
| `https://[2001:db8::20]:8443` | IPv6 address and port. |
| `http://app/api` | Invalid business path. |
| `http://app?q=1`, `http://app#part` | Invalid query or fragment. |
| `http://user:password@app` | Invalid user information. |
| `http://*:8069`, `http://0.0.0.0:8069`, `http://[::]:8069` | Invalid: wildcard listeners are not connection targets. |

Names, IPs, and default ports are normalized; build-time reachability is not tested. server= is invalid. Omission inherits; omission everywhere defaults to ~.

### Resolving ~

~ means the connection address of the application service generated by this command:

| Application service | server=~ |
| --- | --- |
| Generated systemd service with --listen:8069 | http://127.0.0.1:8069. |
| Generated service with one concrete HTTP/HTTPS listen address | Use that address. |
| Existing .service file | Error; do not parse ExecStart to guess. |
| --daemon:none, or no host found/service generated | Error. |
| Missing listen, multiple addresses, wildcard listener | Error: no unique connection target. |

A service file is a systemd .service, possibly containing ExecStart=dotnet ... . Existing files may use scripts, environment variables, or other launch methods and are not inference sources. Specify server=http://127.0.0.1:8069 instead; there is no requirement to delete the service file.

No application port is inferred from bind or a guessed application default. A route overridden by native nginx:proxy_pass no longer consumes its common server; other routes using ~ still need valid inference.

### Pools and weights

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = round-robin
```

app1/app2 are identifiers. Omitted weight is 1, yielding relative weights 3:1. This does not promise a fixed order in each group of four requests; runtime state and algorithm affect selection.

- Single-member pools are valid. Members use concrete addresses, not ~.
- weight is a positive integer; zero, negatives, and fractions fail. No arbitrary common upper bound is imposed, but values beyond the target hoster's range fail without truncation.
- All members need the same scheme; ports may differ. HTTP/HTTPS mixing fails.
- Normalized duplicates fail even with different names, such as http://app and http://APP:80/.
- One input file cannot mix server and server!name at the same scope. Cross-file replacement is allowed.
- Only used application-specific upstreams are emitted, with names isolated across packages. They share the existing http include; arbitrary native upstream definitions remain unsupported.

<a id="inheritance"></a>
## Backend replacement and inheritance examples

**Targets replace as a whole group; policies inherit per field.** A local scalar or any local named members replace the entire imported target group at that scope, without merging members by name. Root → site → route uses the same rule.

### Example 1: scalar replaces shared pool

shared/site.profile:

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
server-balance = least-connections
```

web.profile:

```ini
#@import shared/site.profile

[api]
server = http://single:8069
```

Only single remains. The separate server-balance field still inherits least-connections.

### Example 2: local pool replaces shared scalar

shared/site.profile:

```ini
[api]
bind!legacy = http://*
server = ~
```

web.profile:

```ini
#@import shared/site.profile

[api]
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
```

Only app1/app2 remain. The replaced ~ no longer requires --listen or a generated service. A shared concrete scalar address behaves identically.

### Example 3: local pool replaces shared pool

shared/site.profile:

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
```

web.profile:

```ini
#@import shared/site.profile

[api]
server!app3 = http://app3:8069
server!app4 = http://app4:8069
```

Only app3/app4 remain, not all four. Declaring just app3 leaves only app3. To change app1's weight while keeping app2, restate the entire pool:

```ini
#@import shared/site.profile

[api]
server!app1 = http://app1:8069 weight=5
server!app2 = http://app2:8069
```

Replaced addresses, weights, and variables are not evaluated. Syntax, unknown fields, wrong scopes, mixed server forms in one file/scope, and missing imports still fail.

### Example 4: root, site, and route

```ini
server = http://shared:8069
server-retry-count = 2

[api]
bind!legacy = http://*
server = http://api:8070

[api root]

[api reports]
path = /reports
server = http://reports:8080

[admin]
bind!legacy = http://*:8081
```

api root uses api:8070, reports uses reports:8080, and admin's automatic root uses shared:8069. All inherit retry-count=2.

Pool inheritance also replaces whole groups:

```ini
server!shared1 = http://shared1:8069
server!shared2 = http://shared2:8069

[api]
bind!legacy = http://*
server!app3 = http://app3:8069
server!app4 = http://app4:8069

[api root]

[api reports]
path = /reports
server = http://reports:8080

[admin]
bind!legacy = http://*:8081
```

api root uses app3/app4 only; reports uses reports only; admin inherits shared1/shared2. Other levels' members are not added.

<a id="tls"></a>
## Backend HTTPS and certificate verification

certificate controls client → Nginx; server-tls-* controls Nginx → HTTPS backend. The two connections are independent: frontend HTTPS may proxy HTTP, and frontend HTTP may proxy HTTPS.

| Field | Default | Rules |
| --- | --- | --- |
| `server-tls-verify` | false | true/false or 1/0; text is case-insensitive; empty fails. |
| `server-tls-trust` | Unspecified | Optional file:/target/absolute/path referencing PEM CA certificates; empty clears Profile inheritance. |
| `server-tls-name` | Inferred | Optional SNI/verification name; empty clears inheritance and restores inference. |

These inherit individually and apply to the effective scalar or every pool member. HTTP backends do not consume TLS fields.

### Verification and trust

Default false explicitly emits proxy_ssl_verify off so an outer enabled setting cannot unexpectedly change it. Encryption alone does not verify backend identity. With true, the target must provide usable CA trust and a certificate name.

```ini
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem

[api]
bind!legacy = http://*
server = https://api.internal:8443

[api root]

[api health]
path = /health
match = exact
server-tls-verify = false
```

The root verifies; health explicitly disables verification. internal-ca.pem contains trust certificates, not the frontend private key.

server-tls-trust is optional: omission relies on effective outer Nginx trust configuration. It does not mean trust-all or promise automatic system CA discovery. Verification without effective proxy_ssl_trusted_certificate fails Nginx validation. Packager does not find CA files on the build machine. See [Nginx backend verification](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_ssl_verify).

Clear a shared CA reference for a different backend:

```ini
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem

[api]
bind!legacy = http://*
server = https://api.internal:8443

[api root]

[api external]
path = /external
server = https://external.example.com
server-tls-trust =
```

external emits no internal CA reference but still verifies; outer configuration must provide appropriate trust. Empty neither restores the Profile parent value nor disables verification.

### SNI and verification name

Inference uses only effective backend DNS names, not frontend host, site/member identifiers, or generated upstream names:

| Backend | Missing/empty server-tls-name |
| --- | --- |
| Single DNS backend, including a one-member DNS pool | Use that DNS name. |
| All members share one DNS name but use different ports | Use the common DNS name. |
| IP backend or different DNS names | No inferred name; with verification off, SNI is off; verification on requires an explicit name. |

Same DNS name, different ports:

```ini
[api]
bind!legacy = http://*
server!a = https://api.internal:8443
server!b = https://api.internal:9443
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
```

Two IP targets sharing one verified name:

```ini
[api]
bind!legacy = http://*
server!a = https://192.0.2.20:8443
server!b = https://192.0.2.21:8443
server-tls-name = api.internal
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
```

An explicit name enables SNI and supplies the verification name when verification is on. It does not enable verification itself, change connection addresses, or rewrite HTTP Host. All certificates must satisfy that common name. An IP pool whose servers need neither SNI nor verification can omit all TLS fields.

server-tls-name takes one host name or IP literal, not a full URL, port, or path; invalid explicit names fail. Changing backends does not clear an inherited name: explicitly empty it to restore inference.

Clear an inherited name to infer from a new backend:

```ini
server-tls-name = api.internal

[api]
bind!legacy = http://*
server = https://192.0.2.20:8443

[api root]

[api external]
path = /external
server = https://external.example.com
server-tls-name =
```

Root uses api.internal; external uses external.example.com rather than the inherited name.

<a id="policies"></a>
## Balancing, failures, retries, and affinity

Policies inherit individually from root → site → route. Changing server does not reset policies. Lists replace rather than union with parents. Enumerations are case-insensitive; empty/unknown values fail unless clearing is explicitly supported.

| Field | Default | Constraints and Nginx behavior |
| --- | --- | --- |
| `server-balance` | round-robin | Weighted round robin, or least-connections. least-requests has no exact mapping and fails. |
| `server-failure-count` | 3 | Nonnegative per-member integer; 0 disables passive failure accounting/isolation; maps to max_fails. |
| `server-failure-timeout` | 30s | Positive duration; fail_timeout accounting window and exclusion period. |
| `server-retry` | error,timeout | Retry condition list, or off alone. |
| `server-retry-count` | 1 | Nonnegative number of additional attempts after the first; 0 disables. |
| `server-affinity` | none | none or cookie. |
| `server-affinity-cookie` | Generated name | Optional valid cookie name; empty restores generation and does not enable affinity itself. |

### Durations

Core TimeSpanUtility.Parse/TryParse defines the syntax. Packager then enforces positivity, target precision, and range rather than passing the original text through.

| Input | Meaning/result |
| --- | --- |
| 30s, 30S | 30 seconds. |
| 500ms, 500MS | 500 milliseconds. |
| 1.5s | 1.5 seconds. |
| 1.5m | 90 seconds. |
| 2h, 1d | 2 hours, 1 day. |
| 00:01:30 | Standard TimeSpan: 90 seconds. |
| 1.02:03:04 | 1 day, 2 hours, 3 minutes, 4 seconds. |
| 1m30s | Unsupported compound units; error. |
| 30 | Standard TimeSpan interprets this as 30 days, not 30 seconds. |
| Empty, zero, negative | Fails these fields' positive-duration requirement, even if Core parses it. |

Use explicit units. Nginx failure-timeout requires exact whole seconds; health intervals and connect/send/read timeouts require exact whole milliseconds. Thus server-failure-timeout=1.5s fails, while server-health-interval=1.5s can emit 1500ms. Values are never rounded/truncated; target range overflow fails.

### Balancing and passive failures

round-robin and least-connections retain member weights. Active connection count is not request count; least-requests cannot silently become least-connections.

```ini
[api]
bind!legacy = http://*
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = least-connections
server-failure-count = 3
server-failure-timeout = 30s
```

Failures are counted per member, not across the pool. Nginx temporarily excludes a member when the window's failure condition is met; this differs from active checks' consecutive-failure threshold. Single-member upstreams retain Nginx's native max_fails/fail_timeout limitation, so passive exclusion is not promised. failure-count=0 does not disable active checks. See [Nginx upstream members](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#server).

### Retries

Conditions are error, timeout, invalid_header, http_500, http_502, http_503, http_504, http_403, http_404, and http_429. Separate with commas. off must appear alone.

```ini
server-retry = error,timeout,http_502,http_503
server-retry-count = 2
```

This permits the initial attempt plus two additional attempts, mapped to Nginx total tries=3. Actual eligibility remains subject to hoster behavior. Packager does not add non_idempotent or otherwise relax retry conditions for requests already sent.

Either retry=off or retry-count=0 disables retries. Zero does not map to unlimited Nginx attempts. Disabling one field does not erase the other: inherited off plus a child count=2 remains disabled until the child also supplies enabled conditions. off,error, negative/fractional counts, and empty values fail; another disabling field does not hide an invalid effective value. See [proxy_next_upstream](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_next_upstream).

### Cookie affinity

```ini
[api]
host = api.example.com
bind!secure = https://*
server!app1 = http://app1:8069
server!app2 = http://app2:8069
server-affinity = cookie
server-affinity-cookie = API_ROUTE
```

Missing/empty names generate `HOSTER_ROUTE_<identifier>`, without directly embedding the application version. The same effective pool reuses its cookie; distinct pools are isolated. Statically detectable custom-name conflicts in the same host scope fail.

Cookies use Path=/, HttpOnly, SameSite=Lax, no Domain, and no persistent expiration. Secure is added only when every site frontend is HTTPS; mixed HTTP/HTTPS omits it. Affinity does not replicate application sessions or substitute client-IP hashing.

The target needs [sticky cookie](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#sticky): open-source Nginx at least 1.29.6, or Nginx Plus with the feature. Packager does not inspect the target version at build time.

<a id="health"></a>
## Active health checks

### Enablement and inheritance

**Only an effective nonempty server-health path enables checks.** There is no server-health-enabled field.

| server-health | Behavior |
| --- | --- |
| Omitted at this scope | Inherit; omitted everywhere means disabled. |
| / | Explicit root-path probe. |
| /health?ready=true | Probe that path and query. |
| off, OFF, etc. | Disable, overriding the parent. |
| Empty or without = | Disable; do not default to / or inherit again. |

Minimal configuration with a local disable:

```ini
[api]
bind!legacy = http://*
server!a = http://app1:8069
server!b = http://app2:8069
server-health = /health

[api root]

[api reports]
path = /reports
server-health =
```

Root enables checks; reports disables them. off is equivalent to empty. A more specific scope may enable a path again. Interval/status/headers/timeouts alone do not enable checks, but their effective declarations must still be valid.

### All probe fields

| Field | Default | Rules |
| --- | --- | --- |
| `server-health` | Disabled | Target-relative URI beginning with /; query allowed; full URLs, leading //, fragments, and whitespace forbidden. |
| `server-health-interval` | 10s | Positive probe interval. |
| `server-health-connect-timeout` | 5s | Positive connection timeout. |
| `server-health-send-timeout` | 5s | Positive send timeout. |
| `server-health-read-timeout` | 5s | Positive read timeout. |
| `server-health-failure-count` | 3 | Positive consecutive failure threshold. |
| `server-health-recovery-count` | 2 | Positive consecutive success threshold. |
| `server-health-status` | 2xx | Nonempty healthy response status set. |
| `server-health-header!name` | Unspecified | Fixed probe header, merged by name; empty suppresses sending. |

Complete probe policy:

```ini
[api]
host = api.example.com
bind!legacy = http://*
server!a = https://192.0.2.20:8443
server!b = https://192.0.2.21:8443
server-tls-name = api.internal
server-tls-verify = true
server-tls-trust = file:/etc/tls/internal-ca.pem
server-health = /health?ready=true
server-health-interval = 10s
server-health-connect-timeout = 2s
server-health-send-timeout = 5s
server-health-read-timeout = 5s
server-health-failure-count = 3
server-health-recovery-count = 2
server-health-status = 200-299,3xx
server-health-header!Host = probe.internal
server-health-header!X-Probe = readiness
```

Each member is probed with GET using its own scheme/address/port. No business-route prefix or client query is appended. HTTPS reuses effective server-tls settings; HTTP Host and TLS name are independent: probe.internal versus api.internal above.

### Healthy status set

| Value | Accepted statuses |
| --- | --- |
| 200 | Only 200. |
| 200,204 | Only 200 and 204. |
| 200-299, or 2xx | 200–299. |
| 200-299,3xx | 200–399. |
| 2xx,301,302 | 200–299 plus 301/302. |

Single codes and range endpoints must be 100–599. Classes are 1xx–5xx, case-insensitive. Commas form a union; overlaps/duplicates are normalized. Empty values/elements, reversed ranges, 99, 600, and 6xx fail.

Accepting 3xx accepts that response itself; it **does not follow Location redirects**. Network, timeout, and TLS failures still fail. Default 2xx emits a 200–299 predicate.

Sets replace wholly: root 2xx overridden with route 200,204 leaves only two statuses. The probe path, interval, and other fields continue to inherit individually.

### Counts, timeouts, and header isolation

Default threshold examples:

| Sequence | Result |
| --- | --- |
| Failure, failure, failure | Unhealthy at failure-count=3. |
| Failure, failure, success, failure | Success breaks the streak; only one consecutive failure remains. |
| Unhealthy, then success, success | Recover at recovery-count=2. |
| Unhealthy, then success, failure, success | Failure breaks recovery; two consecutive successes have not occurred. |

Counts belong to each member's active probes, not business failures, retries, or pool-wide totals. Startup/reload/native state remains the hoster's responsibility; Packager adds no probe process or state database.

Connect/send/read timeouts cover independent phases, not total probe duration. Two seconds connecting plus four seconds reading does not exceed a nonexistent five-second total timeout. Send/read follow Nginx's consecutive-wait semantics.

All three are optional. Setting only server-health-read-timeout=2s leaves connect/send inherited or at 5s. A business nginx:proxy_read_timeout=60s does not change the probe default to 60s; a 2s probe timeout does not change business timeouts.

Probe headers form an independent collection. They do not inherit business header, native proxy_set_header, forwarded, or WebSocket headers. Probe headers themselves merge case-insensitively from root → site → route. Omitted Host uses the hoster's native proxy default; explicitly empty Host suppresses it without restoring that default.

### Nginx module and generated structure

Active checks require the [Nginx active health check module](https://nginx.org/en/docs/http/ngx_http_upstream_hc_module.html) (Nginx Plus). Open-source passive accounting is not this module, and Packager does not simulate active checks with passive behavior.

Generation creates application-specific upstream/shared-zone/match settings and an internal named location isolating probe headers/timeouts, without exposing a new external route. Within one site, identical effective backends and related policies share a pool/probe definition. Different check enablement, paths, headers, TLS, or related policies create separate groups; sites do not share groups. Generated names are implementation details, not names to guess in native entries.

<a id="headers"></a>
## Request headers and WebSocket

### Automatic forwarding headers

forwarded defaults to true:

| Header | Nginx value |
| --- | --- |
| Host | <code>&#36;http_host</code>, preserving the received port. |
| X-Real-IP | <code>&#36;remote_addr</code>. |
| X-Forwarded-For | <code>&#36;proxy_add_x_forwarded_for</code>. |
| X-Forwarded-Proto | <code>&#36;scheme</code>, the client-facing scheme. |

HTTPS frontend with HTTP backend still forwards Proto=https. forwarded=false stops automatic generation; it does not remove existing client/outer headers. Use explicit empty headers to suppress them.

The application still needs forwarding/trusted-proxy configuration appropriate to the deployment topology.

### Fixed common headers and precedence

```ini
header!X-Application = example-api

[api]
bind!legacy = http://*
server = http://app:8069
header!X-Service = api

[api root]

[api hub]
path = /hub
header!X-Service = hub
header!X-Application =
```

Root sends X-Application=example-api and X-Service=api. hub sends X-Service=hub and suppresses X-Application. Omission inherits, explicit empty suppresses, and header names are case-insensitive.

Common values are fixed text after variable evaluation, not Nginx expressions. Both header and server-health-header names must be valid HTTP field names without spaces, colons, or other separators. Do not add quotes to ordinary literal text. Use native entries for dynamic values:

```ini
[api hub]
nginx:proxy_set_header!X-Remote = $remote_addr
```

Precedence from lowest to highest:

```text
Automatic → root common → site common → site native → route common → route native
```

A route common header overrides a site native header; same-scope native overrides common. Every route emits its complete effective set, avoiding Nginx group-inheritance surprises when only one header changes. Explicit overrides of forwarding/WebSocket automatic headers follow the same rule, with source diagnostics.

### WebSocket

websocket defaults false. Both websocket and forwarded accept true/false or 1/0, text case-insensitive; empty fails. They inherit root → site → route, and false overrides inherited true.

```ini
[api]
bind!legacy = http://*
bind!secure = https://*
server = http://app:8069

[api root]

[api devices]
path = /hub/devices
websocket = true
```

Use ws://.../hub/devices or wss://.../hub/devices on the same route; ordinary HTTP requests also work. Nginx generation uses HTTP/1.1 proxying and Upgrade/Connection settings, without a global map. Explicit header overrides take responsibility for their resulting behavior. See [Nginx WebSocket proxying](https://nginx.org/en/docs/http/websocket.html).

<a id="native"></a>
## Native hoster settings

### Syntax, scope, and empty values

hoster:directive=arguments describes a native leaf directive; a ! suffix is its first argument:

```ini
[api]
nginx:listen!80
nginx:listen![::]:80
nginx:server_name = api.example.com
server = http://app:8069

[api root]
nginx:proxy_set_header!Upgrade = $http_upgrade
nginx:proxy_set_header!Connection = 'upgrade'
nginx:proxy_set_header!Host = $host
nginx:proxy_set_header!X-Real-IP = $remote_addr
nginx:proxy_set_header!X-Forwarded-For = $proxy_add_x_forwarded_for
nginx:proxy_set_header!X-Forwarded-Proto = $scheme
nginx:proxy_read_timeout = 300s
```

nginx:listen!80 and nginx:listen!80= both emit listen 80;. Do not declare both in one section. nginx:listen!443=ssl emits listen 443 ssl;. Prefer websocket=true for complete automatic WebSocket setup.

In common bind!legacy/server!app1 the suffix is an identifier; in native nginx:listen!80 it is an actual parameter. An empty nginx:proxy_set_header!X-Optional, with or without =, emits `proxy_set_header X-Optional "";` to suppress that header, not an incomplete directive.

Site entries belong to server context; route entries belong to location context. Arbitrary site directives are not copied into every route; only explicitly modeled behavior such as header merging does that. Root native entries and dotted nesting such as nginx:http.server.listen are unsupported.

Known directives have context/cardinality checks. Unknown leaf directives may be passed to target Nginx/modules for validation, without guaranteeing support. Instance settings and user-injected http/server/location/upstream/match/map/if/limit_except blocks are forbidden; structural blocks are generated by the configurator.

For example, listen/server_name belong only to a site, and proxy_pass only to a route. Known single-value directives such as proxy_pass/ssl_certificate cannot use different ! suffixes to bypass duplicate or argument-count checks. Empty native values mean no additional arguments, not an implicit boolean true; the directive's syntax determines whether zero arguments are allowed.

Native parameters may preserve Nginx quotes and expressions, but **omit the final semicolon**. Unbalanced quotes, newline, NUL, or unquoted semicolon/comment/block delimiters fail. Profile does not strip inline comments. Do not paste whole Nginx configuration blocks into a value.

### Native overrides

| Native entry | Replaced common output |
| --- | --- |
| Any site nginx:listen, including named entries | All bind groups, not just one. |
| Site nginx:server_name | Entire host list. |
| Route nginx:proxy_pass | Effective scalar/pool target. |
| Site nginx:ssl_certificate and nginx:ssl_certificate_key | Common certificate/key as a pair; both native entries required. |

Listener replacement:

```ini
[api]
host = api.example.com
bind!legacy = http://*
bind!secure = https://*
nginx:listen!8080
server = http://app:8069
```

Only 8080 remains, not 80/443. Native server_name likewise replaces rather than appends to host.

Replacing application-address inference:

```ini
[api]
bind!legacy = http://*
server = ~

[api root]
nginx:proxy_pass = http://remote:8069
```

This route no longer resolves ~ or requires --listen. Another unoverridden route would still require inference. Native proxy_pass URI rewriting follows Nginx semantics; regex routes reject a static proxy_pass containing a URI part. Common server always disallows URI replacement.

Certificate pair replacement:

```ini
[api]
bind!secure = https://*
certificate = file:/unused/public.pem
nginx:ssl_certificate = /etc/tls/native/fullchain.pem
nginx:ssl_certificate_key = /etc/tls/native/private.key
server = http://app:8069
```

Only the native pair is used. Supplying only one fails even if common configuration could supply the other half. Pair completeness is checked after import merging.

Completely overridden common values with no other consumers are not evaluated or used for dependencies/default references; diagnostics identify the sources. Structural validation still applies. Native entries can change common semantics, so inspect the result; they are not a way to bypass structural checks.

<a id="variables"></a>
## Variables and escaping

Values accept &#36;(name) and %name% using this command's fixed variable view: defaults, environment, .env files from filesystem root to final source, then explicit options. Source and identity follow general packaging rules; Web input cannot redefine them.

```ini
[api]
host = $(Environment).api.example.com
bind!legacy = http://*
server = http://%BackendHost%:8069
header!X-Environment = $(Environment)
```

Only effective values need evaluation. Missing effective variables fail; replaced backends are not evaluated. Known unselected iis values are not evaluated, but a misspelled prefix such as ngnix fails.

Section names, common field names, member identifiers, and import paths are not evaluated. Native ! suffixes represent actual parameters and are evaluated as native parameters; this does not enable variables in common keys.

| Text | Behavior |
| --- | --- |
| <code>&#36;(name)</code>, `%name%` | Packaging variables. |
| <code>&#36;&#36;(name)</code>, `%%name%%` | Literal &#36;(name)/%name%; remain protected during recursive expansion. |
| <code>&#36;host</code>, <code>&#36;{host}</code>, <code>&#36;remote_addr</code> | Not packaging variable forms; retained for Nginx in native settings. |

Profile quotes do not prevent evaluation. Escaping protects variable evaluation, not target encoding. Common fixed headers containing &#36; cannot currently be safely represented by the Nginx writer and explicitly fail; header!X-Template=&#36;&#36;(name) is therefore not a valid fixed Nginx header example. header!X-Template=%%Environment%% can represent literal %Environment%.

Choose an environment-specific top-level file through the command. Bash/PowerShell single quotes prevent shell interpretation of &#36;():

```shell
dotnet-pack deb '--web:nginx:web.$(Environment).profile'
```

Windows cmd can use double quotes. The selected file imports shared content using fixed relative paths; do not write #@import web.&#36;(Environment).profile.

Build-time #@import reads Profile files; runtime nginx:include merely emits a Nginx reference and does not copy its target.

<a id="example"></a>
## Complete configuration example

Shared shared/web.profile:

```ini
forwarded = true
websocket = false
header!X-Application = example-api
```

Host web.profile, with app as the application service name in the deployment network:

```ini
#@import shared/web.profile

[api]
host = api.example.com
bind!legacy = http://*,http://[::]
bind!secure = https://*,https://[::]
certificate = file:/etc/tls/api/fullchain.pem
certificate-key = file:/etc/tls/api/private.key
server = http://app:8069

[api root]

[api hub.devices]
path = /hub/devices
websocket = true
nginx:proxy_read_timeout = 300s
```

root provides a prefix fallback; hub.devices supports WS/WSS with a longer business read timeout. The frontend determines the scheme without duplicate route definitions. Deployment supplies certificate files. For weighted balancing, replace the site's single server line with:

```ini
server!app1 = http://app1:8069 weight=3
server!app2 = http://app2:8069
server-balance = least-connections
```

For active probes, explicitly add server-health=/health and ensure the target has the required module. Sites, effective pools, and match blocks still occupy one application configuration file.

<a id="delivery"></a>
## Payload selection and output layout

### Ordinary arguments determine Profile payload

--web reads input and generates configuration. Positional arguments and --exclude determine ordinary payload, including Profile files.

These commands use the current directory as source and, with no positional arguments, recursively collect its files:

```shell
dotnet-pack deb --web:nginx
```

Read web.profile/imports and generate Nginx configuration. Ordinary collection also includes web.profile. Removing --web still includes that source file but stops configuration generation.

```shell
dotnet-pack deb --web:nginx --exclude:web.profile
```

Read and convert web.profile, but exclude that source file from payload. Generated configuration remains included.

```shell
dotnet-pack deb --web:nginx --exclude:*.profile
```

Read input/imports while excluding Profile payload according to ordinary rules. **Payload exclusion does not forbid build-time reading.**

Explicit positional arguments select only their ordinary payload. Importing an outside-source file does not automatically package it; certificate/include references likewise do not copy files. Ordinary exclude rules do not silently discard explicitly enabled generated configuration.

### Files, names, and conflicts

```text
<installation-root>/.web/nginx/zongsoft.web.conf
/etc/nginx/conf.d/zongsoft.web.conf
    -> <installation-root>/.web/nginx/zongsoft.web.conf
```

The first is the real generated file; the second is the link created on the target when activation is enabled. Container tooling reads the real file without relying on links or .hoster metadata. `<hoster>` means the hosting component, such as nginx or a future iis.

- One configuration per package combines all sites. Final PackageName includes Edition but not version: zongsoft.web-prod produces zongsoft.web-prod.conf.
- The filename stays stable when upgrading the same package name. Path characters and other invalid identifiers are rejected, not silently sanitized.
- The file is installation-root-relative payload, mode 0644, directories 0755, UTF-8 without BOM, CRLF, Tab indentation. Generated Shell scripts use LF.
- No temporary .conf is created in source, Profiles are not rewritten, and private-key contents are not copied into generated configuration. Identical effective input and installation paths produce deterministic content without timestamps or build-machine paths.
- Any ordinary payload/alias claiming the same normalized target causes a packaging error identifying both sources, regardless of insertion order.

For example, manual.conf aliased to .web/nginx/zongsoft.web.conf conflicts with automatic generation. Exclude/move that payload to use generated configuration, or disable --web to use your own. This differs from installation-time replacement of a system loading entry.

<a id="installation"></a>
## Bare-metal installation, container builds, and lifecycle

### Activation switch

`HOSTER_WEB_ACTIVATION` is read by the **installation or uninstallation process**, not captured from the build environment:

| Value | Install/upgrade behavior |
| --- | --- |
| Unset, 1, true | Create/update loading link, validate Nginx, reload if running. |
| 0, false | Deliver configuration but skip new link creation/update and all Nginx operations. |
| Explicit empty or anything else | Error. |

true/false are case-insensitive. Packager does not infer this setting from container markers, PID 1, or Nginx presence. Disabled Web activation needs no Nginx/systemd for Web actions, but does not disable application daemon actions, migrations, or user hooks.

Container build example, replacing application with the actual package name:

```dockerfile
RUN HOSTER_WEB_ACTIVATION=0 dpkg -i /tmp/application.deb
```

RPM and Tar equivalents:

```sh
HOSTER_WEB_ACTIVATION=false rpm -Uvh /tmp/application.rpm
HOSTER_WEB_ACTIVATION=0 sh /tmp/application.sh
```

After installation, discover direct files in .web/nginx/*.conf under the known installation root and transfer them to the Nginx image or mount. Do not require /etc/nginx/conf.d links. Deployment makes backend addresses, certificates, and includes valid in that container. Collect before uninstalling because uninstall removes .web.

### Bare-metal automatic activation

| Item | Fixed convention |
| --- | --- |
| Service | systemd nginx.service. |
| Main configuration | /etc/nginx/nginx.conf. |
| Loading entry | `/etc/nginx/conf.d/<PackageName>.conf`. |
| Validation | nginx -t -c /etc/nginx/nginx.conf. |
| Loaded-file confirmation | nginx -T confirms this package's entry is included. |
| Active service | After validation, systemctl reload nginx.service. |
| Explicitly stopped (inactive/failed) | Validate and leave stopped; do not start. |

The main http context must include the fragments, for example via an existing include /etc/nginx/conf.d/*.conf;. Packager does not rewrite the main file. Do not test a site fragment as the standalone -c main configuration, or regard unrelated configuration validation as proof this package is loaded.

Missing nginx/systemctl, an unloaded service, unknown state, absent include relationship, validation failure, or reload failure fails install/upgrade. Validation failure sends no reload. Packager does not install Nginx, inject distribution dependencies, or start/restart it. States such as activating are not treated as definitely stopped.

Non-systemd environments, custom main files, multiple instances, or custom service names use HOSTER_WEB_ACTIVATION=0 and existing user hooks to manage their links, checks, reloads, and uninstall cleanup. Default activation does not infer custom parameters from launch scripts.

nginx -t opens referenced files such as certificates. Container builds lacking dependencies can disable activation and validate in the target later. Reload success is not an application health check or a cross-format package rollback guarantee. See [Nginx switches](https://nginx.org/en/docs/switches.html) and [reload behavior](https://nginx.org/en/docs/control.html).

### Existing system loading entry

When activation is enabled:

| Existing entry | Action |
| --- | --- |
| Missing | Create link to this package's real configuration. |
| Correct link | Reuse it. |
| Regular file | Replace it with the package link. |
| Link elsewhere, including dangling links and links to directories | Replace only the link; leave its former target untouched. |
| Actual directory | Report conflict; do not recursively remove it. |

Links are created by target installation, not delivered as unconditional link payload, and require no real link on the Windows build machine.

Disabling activation skips **this invocation's** activation; it does not revoke past activation. If an old link exists and the new package still delivers the same filename, a disabled upgrade updates the real file while retaining the link. A later administrator reload may load the new contents. If no link exists, disabled activation does not create one.

### Install and upgrade ordering

The pre-install lifecycle remains preinstalling → installing → postinstalling, followed by payload delivery. Delivery resolves default paths and prunes obsolete configuration before:

| Order | Action |
| --- | --- |
| 1 | preinstalled user hook; may prepare certificates/dependencies. |
| 2 | Migration preparation/application if selected. |
| 3 | installed main hook: default application service installation/start, or its custom replacement. |
| 4 | Web activation: switch-controlled links, validation, conditional reload. |
| 5 | postinstalled user hook; may perform business validation. |

Failed earlier steps prevent subsequent activation/hooks. Packager follows return statuses without waiting for application health. Custom main hooks do not remove independent Web steps, and disabling daemon does not disable Web.

Activation failure preserves diagnostics and returns failure without an extra custom rollback. Payload may already be installed. Package formats retain their own installation-state and retry behavior.

### Updates, obsolete files, and uninstall

.web is package-owned generated output. Upgrades overwrite same-name files, including local edits, without backups or DEB conffile/RPM config preservation. Make durable changes in build-side Profiles and regenerate.

Files no longer delivered by a new package and package links still pointing to them are pruned **regardless of activation**. Canceling --web removes old .conf and matching links; disabling activation only prevents Nginx operations. When the same filename is still delivered, the file is updated and an existing link is retained while activation is off.

Normal uninstall ordering:

| Order | Action |
| --- | --- |
| 1 | preuninstalling user hook. |
| 2 | Remove links still targeting this package's expected files; validate/reload running Nginx according to the switch. |
| 3 | uninstalling main hook; default stops/disables the application service. |
| 4 | postuninstalling user hook. |
| 5 | Package adapter removes application payload. |
| 6 | preuninstalled → uninstalled main hook → remove remaining entire .web directory. |
| 7 | postuninstalled user hook. |

Disabled activation still removes owned links and .web, including local edits, without dangling links. It does not call Nginx or promise immediate removal of a running site. Target uninstall does not change source files on the build machine.

Cleanup removes only links whose target still matches this package. Same-name regular files, actual directories, and links elsewhere are retained. This ownership rule differs from installation's replacement of regular files/other links.

| Normal uninstall failure | Handling |
| --- | --- |
| Nginx missing, validation fails, state unknown, reload fails | Preserve diagnostics, warn, and continue cleanup; do not claim successful site deactivation. |
| Validation failure | No reload; continue file cleanup. |
| Owned link/file removal failure | Uninstall error, outside the warn-and-continue rule. |
| Invalid activation value | Error. |

For example, another site's invalid configuration may fail nginx -t after this package's link removal; package files are still removed, and an administrator handles the running old configuration later. Install/upgrade retain strict failure behavior.

### Package formats and relocation

| Format/scenario | Behavior |
| --- | --- |
| DEB | Install Web steps only in postinst configure, preserving Debian upgrade/removal semantics. |
| RPM | New package post updates; final removal cleans; old-version upgrade uninstall does not remove new configuration. |
| Normal Tar install | Delivery, lifecycle, and activation apply. |
| Tar INSTALL_PATH | Default certificate references/link targets follow actual root; explicit absolute references stay unchanged. |
| Tar DESTDIR staging | Deliver staged payload and resolve paths; skip lifecycle and real system loading-directory changes. |

DESTDIR is excluded from default references. Path resolution still occurs with activation/lifecycle disabled. New-package delivery determines obsolete cleanup, not old-version normal uninstall. No current Web output and no old owned configuration to remove means no hoster activation work.

<a id="troubleshooting"></a>
## Troubleshooting and capability boundaries

| Symptom | Check/action |
| --- | --- |
| web.profile missing | Default lookup is directly under final source; correct source or use an explicit file. |
| Missing/incorrect import | Resolve from the declaring file; import paths cannot contain variables, quotes, or spaces. |
| Mixed server forms | Pick scalar or named members per file/scope; changing forms is allowed across imported files. |
| ~ cannot resolve | Require a generated service and one usable listen address, or specify server explicitly. |
| Route not selected | Check match, case, slash, regex order, and explicit root fallback. |
| Input Profiles are packaged | Ordinary payload behavior; use --exclude:*.profile. |
| Generated target conflict | Check source payload and aliases for the same .web target. |
| least-requests capability error | Select a supported algorithm; Nginx has no exact current mapping. |
| Unknown sticky/health_check | Check target version/modules; generation does not install them. |
| HTTPS load error | Distinguish frontend certificate/key from backend CA/name; check target files. |
| Backend verification still fails | Verification requires usable CA trust and a matching name; it is not unconditional trust. |
| &#36; in common header fails | Common headers are fixed text; use native proxy_set_header for Nginx expressions. |
| Native parse error | Remove final semicolon/inline comments; check quotes/context; no blocks or dotted nesting. |
| Link exists but activation fails | Main configuration must actually include it; check nginx -T and nginx.service. |
| Site remains after activation=0 | The switch skips this activation, without revoking retained links or loaded runtime state. |
| Local edits disappear on upgrade | Generated output is package-owned; make durable edits in source Profiles. |

Build diagnostics retain file/section/entry/line and related sources where available. The generator checks model, format, scopes, static conflicts, and known capabilities, not build-time certificate contents, DNS, backend reachability, or target modules.

Inspect archives/generated files and validate the main configuration in an Nginx environment with real dependencies. Package inspection is not installation validation. Runtime state, certificate renewal, DNS changes, application sessions, and business health remain deployment/hoster/application responsibilities.

<a id="iis"></a>
## IIS extension boundaries

--web:iis currently reports unimplemented. These are extension boundaries, not delivered features:

- Sites, bindings, pools, and certificate associations require IIS server configuration; application web.config alone cannot create them. See [IIS bindings](https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/sites/site/bindings/binding).
- ANCM may host the local application. Explicit remote backends and weighted pools need ARR/URL Rewrite server farms, not an assumed ANCM mapping. ARR supports member weights, but each balance/retry/affinity/health field needs semantic validation, without approximate substitution. See [ARR server farms](https://learn.microsoft.com/en-us/iis/extensions/configuring-application-request-routing-arr/define-and-configure-an-application-request-routing-server-farm).
- Regex routes need URL Rewrite case/order/relative-path adaptation. Arbitrary Nginx native entries cannot automatically convert.
- `.web/<hoster>/` defines delivery/discovery, not the target runtime location. ANCM web.config still belongs at the application content root; merely storing it in .web/iis/ does not configure IIS. See [IIS web.config location](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#webconfig-file-location).
- Windows certificate stores, IIS resource ownership, installation transactions, and MSI/WiX adaptation need their own implementation; Linux PEM paths and Nginx lifecycle cannot be mechanically reused.
