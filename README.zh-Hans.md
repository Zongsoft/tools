[English](README.md) | [简体中文](README.zh-Hans.md)

-----

## 概述

提供了辅助开发的工具集，包括：

- [deployer](https://github.com/Zongsoft/tools/tree/main/deployer)
	> 部署工具：提供应用程序部署发布进行文件拷贝、包获取等功能。

- [packager](https://github.com/Zongsoft/tools/tree/main/packager)
	> 打包工具：提供应用程序安装包的制作 _(先使用部署工具准备好待打包的内容)_。

- [regular](https://github.com/Zongsoft/tools/tree/main/regular)
	> 正则工具：提供正则表达式匹配、替换等功能的一个 _GUI_ 程序。

## NuGet 发布

[Publish NuGet Packages](.github/workflows/publish-nuget.yml) 提供 `deployer`、`packager` 的手动选择，仅在 `main` 分支执行，并串行运行发布任务。`regular` 是 Windows GUI 应用，目前没有 NuGet 打包配置。

在 GitHub 配置 `release` 环境，并将 nuget.org 用户名（不是邮箱）保存为 `NUGET_USER` 环境机密。在 [NuGet 可信发布策略](https://www.nuget.org/account/trustedpublishing) 中设置所有者 `Zongsoft`、仓库 `tools`、工作流文件 `publish-nuget.yml`（仅文件名）和环境 `release`。工作流通过 `id-token: write` 与 `NuGet/login@v1` 在构建完成后取得临时密钥。

工作流安装 .NET 8/9/10，并从仓库工具清单还原 Cake 6.2.0。发布 `packager` 时，先准备 Rocky Linux 9 Podman 容器，将 GitHub 工作目录挂载到 `/workspace`，复用 Cake 构建流程生成 `linux-x64`、`linux-arm64` 两套 Native AOT 升迁运行器。发布阶段通过 Cake 的 `pack --exclusive` 仅推送已构建的包，并跳过已存在的版本。

首次发布前应验证本地包：在仓库根目录运行 `dotnet tool restore`，进入所选工具目录执行 `dotnet cake --edition Release --target build`，检查 `src/bin/Release/*.nupkg`。packager 构建需要其文档约定的 Podman 环境。需要单独收集包时，可在 deployer 目录执行 `dotnet pack src/Zongsoft.Tools.Deployer.csproj -c Release -o ./artifacts`；packager 则在准备好两套升迁运行器后对对应 Packager 项目执行同样命令。这些验证命令不会推送包。
