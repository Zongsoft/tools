[English](templates.md) | [简体中文](templates.zh-Hans.md)

# Infrastructure templates

Templates are Core Profile files with `version=1`. They describe infrastructure/ingress only and are build inputs, not executable hooks. Built-in definitions are in [templates](../templates); their presence does not establish a tested image-version/platform combination.

Root fields: `image` (default repository), `kind` (`infrastructure`, `ingress`), `platforms` (semicolon-separated `x64`/`arm64`), JSON string arrays `entrypoint`, `command`, `health`, `workdir`, `user`, numeric `data-owner` (`UID:GID`), `restart`, `stop-signal`, `health-timeout` (seconds), and `dependencies` (local service IDs separated by semicolons). `version` identifies the template protocol.

| Section | Contract |
| --- | --- |
| `[environment]` | Default environment values |
| `[ports]` | Named `[IPv4:]published:container[/tcp|udp]` declarations |
| `[data]` | Stable directory ID → absolute container path; a single data mount uses `/var/lib/containerizer/data/<name>/<service>/`; multiple data mounts each use a `<mount>/` child directory |
| `[configuration]` | Absolute container file path → file relative to the template; infrastructure only |
| `[settings parameter]` | `environment` and/or `argument` mapping; optional `variable`, `default`, `required=true`, semicolon-separated `choices` |

Unknown fields/settings fail. Explicit settings override shared/template defaults; explicit empty values suppress fallback. `environment!NAME` can override unmanaged environment defaults. A parameter and its explicitly supplied environment mapping must agree. When the parameter is absent, explicit environment can supply its value. Derived environment entries are not copied to the manifest, so they cannot override later parameter edits. Command arrays preserve argument boundaries without a host shell.

`variable` names the shared variable used for an omitted parameter; make uses default only when that variable is absent, retaining an existing empty value. MySQL root-password binds mysql_root_password; RustFS credentials bind rustfs_access_key and rustfs_secret_key. Plan retains references, warns in magenta for missing required values and saves its draft. Make validates before image operations. Other variables require explicit bindings/references; they are not exported wholesale.

Template values support `$(variable)` and `%variable%` through the maker's shared variable evaluator. They use the manifest's resolved source `.env` chain and CLI variables, with the final root fields available as variables. Section names and keys stay literal; literal values are not replaced merely because a same-named variable exists. JSON argument arrays are parsed before each string is expanded, so quotes and backslashes in variable values preserve argument boundaries. Missing/cyclic references fail. `$$(variable)` and `%%variable%%` preserve a literal reference. Configuration file contents are copied unchanged; only their declared input paths are expanded, still relative to the template directory. The executor does not reevaluate any value.

Keep fixed template metadata and defaults literal. Use variables for values that vary between builds, for example `default=$(redis_limit)` in `[settings maxmemory]` or `default=%redis_limit%`. Existing built-in settings mappings also accept variables in the manifest's `settings` value without modifying the template.

For nonroot images, the maker reads the locked image's user metadata and account files from a stopped inspection container to resolve numeric data ownership. `data-owner` may explicitly supply it. Configuration is protected on the host and bound read-only; writable paths are ownership-tracked. RHEL-family bindings request a private SELinux label.

Applications do not support templates, companion template discovery or template settings. Their foreground entrypoint, working directory and service environment come from the installation package. Application configuration selected by packager from hosting/.deploy/<scheme>/ stays in the application image; no extension-based extraction or application configuration mounts are generated. Explicit `environment!NAME` values can override service environment entries.

Effective package `Listen` metadata comes from tar PAX, Debian control or RPM tag `1000001`. Absent/empty metadata uses `["CMD-SHELL","kill -0 1"]`. Nonempty metadata must contain semicolon-separated HTTP/HTTPS URLs without credentials, nonroot paths, query strings or fragments. Invalid metadata fails. Infrastructure checks are unchanged.

The maker converts loopback/wildcard/DNS listener bindings to all container interfaces, preserving schemes and ports, and publishes each distinct port on host `127.0.0.1` at the same port number. It selects the first HTTP listener, otherwise the first HTTPS listener, and probes over container loopback with GET `/`. Any HTTP response establishes listener liveness, including 404; redirects are not followed. Connection, timeout and TLS verification failures remain unhealthy. This does not establish business readiness.

HTTPS uses the DNS identity declared in `Listen`, SNI and the image system trust store; an HTTPS-only IP/wildcard listener cannot supply a DNS identity and is rejected. No application template can supply overrides. curl/CA dependencies are installed even for self-contained hosts. Checks use a 10-second interval, 5-second timeout, 12 retries and 30-second start period.

Packages without unambiguous startup/runtime information must be corrected upstream. Runtime inference reads the entry DLL runtimeconfig; explicit `runtime-*` dependencies must agree.

For Ubuntu 22.04 and .NET 9 or later, application builds use the Ubuntu `ppa:dotnet/backports` feed described in the [official installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install). The installed runtime must match the required major/minor and meet its minimum patch version. APT/DNF install the requested runtime major/minor from the configured distribution feed without pinning an obsolete package patch or packaging revision. The image builder verifies that the selected runtime is at least the package minimum, and the delivery lock records the actual installed patch reported by the built image.

Raw Pod/Compose YAML inputs, source prefixes and service selectors are removed. Use `template` for custom runtime descriptions and `repository` for repository overrides. Generated Compose assets remain part of the delivery. Template `image` and protocol `version` remain distinct from `.container` fields; image software labels and ENV versions do not select or rewrite tags.

Every declared port exposes a setting: default maps to port; other names map to NAME-port and start unpublished. Templates with [data] expose storage=persistent|temporary. Temporary storage uses anonymous volumes, survives ordinary stop/start and is removed by tool uninstall. Redis/Valkey share one persistence/password transformation. See the [complete parameter reference](../README.md#built-in-service-parameter-reference). No template inheritance, generic config input or .settings template selector is added; detailed custom-template redesign is deferred.
