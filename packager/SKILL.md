---
name: zongsoft-tools-packager
description: 修改打包命令、应用版本管理、载荷路径、三种包格式和安装生命周期。
---

# Packager 开发流程

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[实现说明](docs/implementation.md)。

- 命令与版本：PackCommand*、Variables、Normalizer。
- 包模型与编码：Package*、Generator*；长度、对齐、校验和、字节序为精确契约。
- 安装及服务：Scriptor.Systemd；推演安装、升级、覆盖和最终卸载，保留 Debian/RPM 阶段差异。
- 外部升迁：Migrator.cs；按名称和最终应用身份定位原样产物，安装调用其脚本。变量展开后，裸名称从 source 逐级查到根；带 / 或 \ 的路径只查显式目录，./ 可限定源目录。两文件都缺失才向上，半套或元数据无效即失败，不跨目录拼配；测试覆盖查找起点、最近目录、终止条件和三格式原样收录。生成/执行逻辑归独立 [migrator](../migrator/SKILL.md)。
- 搜索与文本：Utility.Search 调用 Core Searcher；TextSource 解释 file:/text:，pre/post 为文件列表。

变更先定位职责，保留用户修改。新增诊断用双语资源并运行 ResXFileCodeGenerator。遵守 CRLF/Tab、版权头和区域分类规范；测试不编写本地化断言。

运行本工具回归、全 TFM 严格构建和 IDE0049 verify。使用临时输入检查三格式路径、权限、来源元数据与启动门禁；不执行真实安装脚本。独立 migrator 以产物为边界，不增加项目引用、共享源码或原生产物收集。更新 README 与实现说明。
