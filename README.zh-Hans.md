[English](README.md) | [简体中文](README.zh-Hans.md)

-----

## 概述

提供了辅助开发的工具集，包括：

- [deployer](https://github.com/Zongsoft/tools/tree/main/deployer)
	> 部署工具：提供应用程序部署发布进行文件拷贝、包获取等功能。

- [migrator](migrator/README.zh-Hans.md)
	> 升迁工具：制作升迁归档与脚本，初始化数据库和 S3。

- [packager](https://github.com/Zongsoft/tools/tree/main/packager)
	> 打包工具：提供应用程序安装包的制作 _(先使用部署工具准备好待打包的内容)_。

- [regular](https://github.com/Zongsoft/tools/tree/main/regular)
	> 正则工具：提供正则表达式匹配、替换等功能的一个 _GUI_ 程序。

## NuGet 发布

手动工作流 [Publish NuGet Packages](.github/workflows/publish-nuget.yml) 支持 deployer、packager、migrator，仅 main 分支运行。release 环境配置 NUGET_USER，并为该工作流配置 NuGet Trusted Publishing；取得临时密钥后仅推送已构建的包。

packager 独立构建。migrator 分别由 Linux 和 Windows 作业准备三种原生产物，汇集后执行 Cake compile 制包。普通构建/回归不发布原生执行器。各工具本地制包方法见其 README；Cake pack 会推送 NuGet，不能用于仅本地测试。

## 包版本管理

[Directory.Packages.props](Directory.Packages.props) 只集中管理通用依赖：Zongsoft.Core、Zongsoft.CodeAnalysis 和测试包。各项目按需引用这些包，不单独填写版本。deployer 的 NuGet SDK、migrator 执行器的数据库驱动和 AWS SDK 等专用依赖，在所属项目使用 `VersionOverride` 指定版本。公共语言版本、作者/公司/版权、NuGet 元数据、工具包图标和分析器引用由 [Directory.Build.props](Directory.Build.props) 统一定义；工具名称、版本号、目标框架、测试属性和发布方式保留在各项目中。解决方案和发布流程仍各自独立。AOT 容器将这两个根 props 文件只读挂载到容器根目录。

## 代码规范同步

各工具共享根目录 `.editorconfig`。`Directory.Build.props` 设置 `ZongsoftGuidelinesSynchronization` 为仓库根目录，`Zongsoft.CodeAnalysis` 在实际构建的准备阶段将所引用 NuGet 包中的模板同步到该文件；无需独立同步命令或访问 GitHub。

同步会覆盖根文件，不合并本地修改。模板在 guidelines 维护，分析器包版本由 Directory.Packages.props 统一定义。升级包并构建后，检查并提交配置差异。仅还原、清理、设计时构建，以及被 Visual Studio 最新检查跳过的构建均不会同步；需要时使用“重新生成”。AOT 容器只读挂载根配置，Linux 发布脚本通过 `-p:ZongsoftGuidelinesSynchronization=` 禁用同步。
