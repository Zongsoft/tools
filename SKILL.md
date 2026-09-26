---
name: zongsoft-tools
description: 在 Zongsoft tools 仓库中修改、审查或排障时用于判断工作归属 deployer、packager、migrator 或 regular，并选择安全的最小验证范围；适用于仓库级定向，不替代 deployer 或 packager 的专项技能。
---

# Zongsoft Tools 仓库定向

开始工作前阅读 [AGENTS.md](AGENTS.md) 和目标子目录的就近文档。

## 选择工具

- `.deploy` 解析、变量/过滤条件、文件复制删除、NuGet 下载及最接近目标框架选择：进入 `deployer`，使用其专项技能。
- tar/deb/rpm 格式、打包项、Unix 权限、systemd、安装卸载生命周期脚本：进入 `packager`，使用其专项技能。
- 升迁输入、SQL 分段、S3 初始化、执行归档和 Native AOT：进入 `migrator`，使用其专项技能。
- 正则匹配 UI、捕获结果树、文件打开保存、WinForms 布局或 `dotnet-regular` 工具包：进入 `regular`。
- Framework 的部署清单格式、插件/映射产物或升级协议本身不归本仓库拥有；先确认应否改动相邻 Framework 或业务仓库。

## 通用流程

1. 读取目标 README、解决方案、项目文件、`build.cake` 和相关源码入口；公共构建属性读取根 `Directory.Build.props`，通用包版本读取根 `Directory.Packages.props`，专用包版本查看所属项目的 `VersionOverride`。
2. 搜索命令选项、资源文本、双语 README 和测试中的同名契约。
3. 将文件复制、删除、网络下载、包安装和服务管理视为副作用；验证时使用隔离目录和可控输入。
4. 修改命令行或公开行为时同步英文/中文 README、资源信息和包元数据。
5. 只构建或测试受影响工具；发布和全局工具安装必须由用户明确要求。

## 最小验证

- deployer：构建其 `.slnx`，再用临时部署目录验证解析分支。
- packager：运行测试项目，并静态检查生成包内容；不安装到宿主系统。
- regular：在 Windows 构建并手工验证受影响界面；工具包变化另执行 Cake `build`，检查包内容及隔离安装。
- 纯文档：只检查链接、CRLF 和 Git 差异。

规范检查遵循 [AGENTS.md](AGENTS.md#代码规范检查)：严格构建及独立 IDE0049 检查都要通过；不要把普通构建成功等同于规范检查通过。
