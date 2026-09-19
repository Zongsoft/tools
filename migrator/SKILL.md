---
name: zongsoft-tools-migrator
description: 修改独立升迁输入、SQL 批次、原生执行、产物命名和 AOT 构建。
---

# Migrator 开发流程

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[升迁指南](docs/migration.zh-Hans.md)和[实现说明](docs/implementation.zh-Hans.md)。

命令身份先由 MigrateCommand.Version.cs 的私有 VersionSource 解析：--version 可为版本号、文件或现有目录，省略/空白只读当前目录直属 .version，不能由环境 version 代替。ApplicationVersion 选择 Edition，保留文件拼写；name 保持独立必填。最终值回填后再初始化变量，源版本文件始终不写入，失败不能修改已有产物。

输入处理在 src/MigrationLoader* 与 MigrationProfile，生成文件集在 MigrationBundle，归档和脚本在 Generator。协议及参数描述在 .shared。数据库/S3/锁/状态在 executor/src。保持这些边界；packager 只消费产物。

核对两端名称契约：缺少既定后缀时补 -migrate，Edition 可省略，版本与 RID 明确。脚本和归档必须同前缀，check 不解压，状态跨版本保留，外部调用可覆盖 state。已有输入无效不能按缺失跳过。

先运行针对性测试，再运行 test、executor/test 全回归、全 TFM 严格构建及 IDE0049 verify。真实执行使用隔离 SQLite/DuckDB 或测试服务，不能使用真实连接参数。资源经 ResXFileCodeGenerator 生成，不为本地化写测试。

Native AOT 显式发布 Linux x64/arm64 与 Windows x64 到 `executor/src/bin/<配置>/net10.0/<RID>/publish/`；生成端从发布目录链接文件，发行布局为 .migrator/<RID>/。检查 ELF/PE、原生库、资源、权限和第三方警告，日志与符号保留在 RID 下的独立目录。Pod 使用相对 YAML 路径，继承根中央包和公共构建属性；Linux 发布禁用只读规范配置的同步。DNS 或挂载修改需重建专用 Pod；保留缓存，不修改其他容器。普通 dotnet build/test 不启动环境。文档集中维护命令用法、输入协议和实现机制；临时验证文件在结束后清理。
