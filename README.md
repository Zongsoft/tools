[English](README.md) | [简体中文](README.zh-Hans.md)

-----

## Overview

Provides a suite of tools to assist in development, including:

- [deployer](deployer/README.md)
	> Deployment Tool: Provides functions for application deployment and release, including file copying and package retrieval.

- [migrator](migrator/README.md)
	> Migration Tool: migration generation and native database/S3 execution.

- [packager](packager/README.md)
	> Packaging Tool: Creates application installation package. _(first use the deployment tool to prepare the content to be packaged)_

- [regular](regular/README.md)
	> Regular Expression Tool: A Windows GUI for matching expressions and inspecting matches, groups, and captures.

## NuGet publishing

The manual [Publish NuGet Packages](.github/workflows/publish-nuget.yml) workflow selects deployer, packager or migrator and runs only on main. Configure NUGET_USER in the release environment and a NuGet trusted publishing policy for this workflow; publishing obtains a temporary key after building.

Packager builds independently. Migrator builds Linux and Windows native artifacts in separate jobs, merges all three RIDs, then uses Cake compile to create the tool package. Ordinary build/test does not publish native executors. See each tool README for local packaging; Cake pack pushes packages and is not for local testing.

## Package versions

[Directory.Packages.props](Directory.Packages.props) centrally manages common dependencies: Zongsoft.Core, Zongsoft.CodeAnalysis and test packages. Each project declares its own references without versions for these packages. Specialized dependencies, such as deployer's NuGet SDK and the migrator executor's database drivers and AWS SDK, retain their versions in the owning project using `VersionOverride`. Shared language, author/company/copyright and NuGet metadata, tool package icons and the analyzer reference are defined in [Directory.Build.props](Directory.Build.props). Tool identities, versions, target frameworks, test settings and publishing modes stay in each project. Solutions and release workflows remain independent. AOT containers mount both root props files read-only at the container root.

## Code style synchronization

All tools share the root `.editorconfig`. `Directory.Build.props` sets `ZongsoftGuidelinesSynchronization` to the repository root. During build preparation, `Zongsoft.CodeAnalysis` copies the template from the referenced NuGet package to that file; no separate synchronization command or GitHub access is required.

Synchronization overwrites the root file without merging local edits. The template is maintained in guidelines; Directory.Packages.props defines the shared analyzer version. Restore, clean, design-time builds and builds skipped by Visual Studio's up-to-date check do not synchronize; use Rebuild when necessary. AOT containers mount the root configuration read-only; the Linux publish script passes `-p:ZongsoftGuidelinesSynchronization=` to disable synchronization.
