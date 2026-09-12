# 打包器实现改进任务清单

本清单对应 implementation.md「当前实现边界」审查。每项实现、聚焦验证通过后勾选；最终同步双语使用说明和实现文档。保留既有应用版本、升迁幂等、失败门禁、三格式安装行为。

## 本轮任务

- [x] 1. 变量：显式选项优先于环境与默认值；按使用展开；未知变量及循环明确失败；保留最终身份覆盖。
- [x] 2. 文本来源：统一基于源目录读取；支持 file:/text:；避免脚本文本被再次当成路径；pre/post 文件列表严格校验。
- [x] 3. 目录条目：三格式保留空目录和目录权限；自动父目录默认 0755；检查文件/目录冲突及 tar 安装卸载。
- [x] 4. 文件匹配：统一支持路径段 *、?、**，每个输入位置按 Ordinal 展开；保留升迁去重、任务和批次语义；避免符号链接递归。
- [x] 5. 制包内存：Debian/RPM 压缩载荷以受控临时流组织，增量摘要，避免完整原始载荷和包体数组；小内存条目保留。
- [x] 6. Debian 关系：格式专属 Provides/Replaces/Breaks/Conflicts/Recommends/Suggests，验证关系语法及拒绝换行。
- [x] 7. 文档：同步中英文 README、升迁指南、implementation.md、AGENTS/SKILL 和本清单的验证证据。
- [x] 8. 验收：两套回归、打包器 .NET 8/9/10 构建，hosting 临时目录三格式只读归档检查。

## 后续事项（本轮不实施）

- [ ] xz/zstd 压缩：在完成流式载荷后按目标发行版需求增加算法与元数据支持。
- [ ] 发布签名：设计独立签名及验签流程，不重写 RPM 生成架构。
- [ ] 其他服务管理器：按实际需求拆分通用生命周期与服务操作，再支持 OpenRC 等。
- [ ] 自定义目标用户/组：保留 root 默认，不继承构建机 UID/GID。

RPM 直接生成格式且不使用 spec/rpmbuild 的设计保持不变。本轮不发布公共 NuGet、不更新全局工具、不安装系统包或操作已有服务，不重新发布未改动的 AOT 运行器。

## 验证记录

变量和文本：PackageInputTests 全部 24 项通过（net10.0），包含真实 CommandContext 优先级、懒展开、缺失/循环及文件/文本边界。
打包器回归 241/241 通过，包含三格式目录、递归匹配、关系字段、每种 Debian/RPM 64 MiB 载荷制包线程分配小于 32 MiB、临时文件清理和 RPM 完整摘要检查。

运行器回归 33/33 通过（net10.0）。完整解决方案 net8.0/net9.0/net10.0 构建通过，零警告、零错误；还原使用既有 Core 7.59.0 本地增强包与隔离缓存，不修改 Core 或全局缓存。

从 `D:/Zongsoft/hosting/daemon`、`D:/Zongsoft/hosting/web/default` 复制真实宿主 DLL 与直属 `.version` 到临时目录，增加测试空目录。使用本地 net10.0 命令分别生成 tar.gz/deb/rpm，共六包；关闭服务生成，只读检查 DLL 原始字节、唯一版本路径/内容、0644、空目录0755、归档路径唯一性及 RPM 文件 SHA-256。没有复制连接凭据，未安装包或运行生命周期脚本。Windows 执行的权限断言核对归档中的模式，未执行 Linux 安装后的权限验收。

本轮本机证据（位于已忽略的构建输出内）：

- `src/bin/improvements-tests.log`：打包器完整回归。
- `src/bin/improvements-artifacts-tests.log`：最后一次 tar 父目录权限保护修正后的归档聚焦回归。
- `src/bin/improvements-migrator-tests.log`：运行器回归。
- `src/bin/improvements-build.log`：全部目标框架构建。
- `src/bin/improvements/hosting/packages/`：六个包及 tar 附属入口。
- `src/bin/improvements/hosting/archive-checks.json`：六包只读检查记录。

双语 README、升迁指南、implementation.md、AGENTS.md、SKILL.md 已同步；相对链接、CRLF 和 git diff --check 检查通过。资源访问类由 ResGen 的强类型资源生成流程更新，保留项目 ResXFileCodeGenerator 声明，不编写本地化测试。
