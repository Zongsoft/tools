[English](README.md) | [简体中文](README.zh-Hans.md)

-----

## Overview

Provides a suite of tools to assist in development, including:

- [deployer](https://github.com/Zongsoft/tools/tree/main/deployer)
	> Deployment Tool: Provides functions for application deployment and release, including file copying and package retrieval.

- [packager](https://github.com/Zongsoft/tools/tree/main/packager)
	> Packaging Tool: Creates application installation package. _(first use the deployment tool to prepare the content to be packaged)_

- [regular](https://github.com/Zongsoft/tools/tree/main/regular)
	> Regular Expression Tool: A GUI program offering regular expression matching, replacement, and other features.

## NuGet publishing

[Publish NuGet Packages](.github/workflows/publish-nuget.yml) provides a manual project selector for `deployer` and `packager`, runs only on `main`, and serializes publishing runs. `regular` is a Windows GUI application without NuGet packaging configuration.

Configure the GitHub `release` environment and its `NUGET_USER` secret with the nuget.org profile name (not an email address). Add a [NuGet trusted publishing policy](https://www.nuget.org/account/trustedpublishing) for owner `Zongsoft`, repository `tools`, workflow file `publish-nuget.yml` (filename only), and environment `release`. The workflow uses `id-token: write` and `NuGet/login@v1` to obtain a temporary key after the build.

The workflow installs .NET 8/9/10 and restores Cake 6.2.0 from the repository tool manifest. For `packager`, it prepares the Rocky Linux 9 Podman container with the runner's checkout mounted at `/workspace`; the existing Cake build includes both `linux-x64` and `linux-arm64` Native AOT migrators. Publishing uses Cake's `pack --exclusive` target to push the already built packages and skip duplicate versions.

Before the first publish, verify local packages: run `dotnet tool restore` at the repository root, then `dotnet cake --edition Release --target build` in the selected tool directory and inspect `src/bin/Release/*.nupkg`. The packager build requires its documented Podman environment. To collect packages separately, use `dotnet pack src/Zongsoft.Tools.Deployer.csproj -c Release -o ./artifacts` in `deployer`, or the corresponding Packager project in `packager` after preparing both migrators. These verification commands do not push packages.
