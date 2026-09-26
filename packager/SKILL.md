---
name: zongsoft-tools-packager
description: 修改打包命令、应用版本管理、载荷路径、三种包格式和安装生命周期。
---

# Packager 开发流程

先阅读 [AGENTS.md](AGENTS.md)、[README](README.zh-Hans.md)、[实现说明](docs/implementation.zh-Hans.md)。

- 命令与版本：PackCommand*、Variables、Normalizer。
- 变量依次加载默认值、系统环境、从根到 source 的直属 `.env`、显式选项；共用 Utility/Profile.Load，多级段落与条目以下划线拼名。先用环境和选项固定 source，再加载 `.env`；不搜索子目录、不以 `.env` 重定位 source，保留显式身份与源版本文件规则。
- 包模型与编码：Package*、Generator*；长度、对齐、校验和、字节序为精确契约。
- 安装及服务：ApplicationHost 固定共享的宿主/listen 结果，Scriptor.Systemd 组合生命周期；推演安装、升级、覆盖和最终卸载，保留 Debian/RPM 阶段差异。
- Web 托管：Web/Definition、Configurator、Installation；严格导入、后端整组替换和有效值求值顺序不可颠倒。生成 .web/nginx 配置，Delivered 独立于生命周期和激活开关；测试采用临时目录与 Nginx/systemctl 替身，不运行真实安装。字段、模块要求和容器化约定见 [Web 指南](docs/web.zh-Hans.md)。
- 外部升迁：Migrator.cs；按名称和最终应用身份定位原样产物，安装调用其脚本。变量展开后，裸名称从 source 逐级查到根，每层先查目录本身再查直属 `.migration/`；带 / 或 \ 的路径只查显式目录，./ 可限定源目录。两文件都缺失才查下一位置，半套或元数据无效即失败，不跨目录拼配；测试覆盖查找起点、最近目录、终止条件和三格式原样收录。生成/执行逻辑归独立 [migrator](../migrator/SKILL.md)。
- 搜索与文本：Utility.Search 调用 Core Searcher；TextSource 解释 file:/text:，pre/post 为文件列表。

变更先定位职责，保留用户修改。通用变量解析通过 tools/.shared 源码链接，升迁协议仍仅通过产物交接。新增诊断用双语资源并运行 ResXFileCodeGenerator。遵守 CRLF/Tab、版权头和区域分类规范；测试不编写本地化断言。

运行本工具回归、全 TFM 严格构建和 IDE0049 verify。使用临时输入检查三格式路径、权限、来源元数据与启动门禁；不执行真实安装脚本。独立 migrator 以产物为边界，不增加项目引用、升迁协议共享源码或原生产物收集。更新 README 与实现说明。
