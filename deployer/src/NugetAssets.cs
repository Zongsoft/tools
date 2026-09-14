/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2015-2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Zongsoft.Tools.Deployer;

/// <summary>根据目标框架、RID 和内容规则选择包内部署文件，并适配显式 NuGet 缓存路径。</summary>
/// <remarks>只负责源文件及目标后缀的选择，不求解依赖版本，也不向部署目标写入文件。</remarks>
internal static class NugetAssets
{
	#region 框架路径
	/// <summary>调整包缓存中的库目录；无需调整或没有适用框架时返回原路径。</summary>
	internal static string ResolveLibraryPath(string directory, IDictionary<string, string> variables)
	{
		if(string.IsNullOrEmpty(directory) || !Utility.TryGetTargetFramework(variables, out var framework))
			return directory;

		var packagesDirectory = Path.GetFullPath(NugetUtility.GetPackagesDirectory(variables));
		var relative = Path.GetRelativePath(packagesDirectory, Path.GetFullPath(directory));

		if(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			return directory;

		//只检查缓存根以内的路径，避免把上级同名目录误认为包的库目录。
		var parts = relative.Split(Utility.PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
		var index = Array.IndexOf(parts, "lib");

		if(index < 0)
			return directory;

		//路径中指定的框架优先；只有 lib 目录时使用部署目标框架。
		if(index + 1 < parts.Length)
			framework = parts[index + 1];

		var packagePath = Path.Combine([packagesDirectory, .. parts.Take(index)]);
		var libraryPath = GetNearestFrameworkPath(Path.Combine(packagePath, "lib"), NuGetFramework.Parse(framework));

		return string.IsNullOrEmpty(libraryPath) ? directory : Path.Combine([libraryPath, .. parts.Skip(index + 2)]);
	}

	private static string GetNearestFrameworkPath(string path, NuGetFramework framework)
	{
		var directory = new DirectoryInfo(path);

		if(!directory.Exists)
			return null;

		var frameworks = directory.GetDirectories()
			.Select(dir => new { Directory = dir.FullName, Framework = TryParseFramework(dir.Name) })
			.Where(item => !item.Framework.IsUnsupported);

		return NuGetFrameworkUtility.GetNearest(frameworks, framework, item => item.Framework)?.Directory;
	}
	#endregion

	#region 资产选择
	/// <summary>展开已选资产组并应用内容复制规则，按目标相对路径合并同一包的文件。</summary>
	internal static IEnumerable<DeploymentUtility.PathToken> GetPackageFiles(string path, string framework, IDictionary<string, string> variables, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();

		var result = new Dictionary<string, DeploymentUtility.PathToken>(DeploymentPath.Comparer);
		var nuspec = Directory.EnumerateFiles(path, "*.nuspec").FirstOrDefault();
		var contentRules = nuspec == null ? [] : new NuspecReader(nuspec).GetContentFiles().ToArray();

		foreach(var asset in GetAssetPaths(path, framework, variables))
		{
			//资产目录已按目标框架选定，直接展开文件。
			foreach(var file in DeploymentUtility.GetFiles(Path.Combine(asset, "*"), variables, resolveLibrary: false, cancellation: cancellation))
			{
				var relative = Path.GetRelativePath(path, file.Path).Replace('\\', '/');

				if(relative.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase))
				{
					if(!CopyContent(contentRules, relative, out var flatten))
						continue;

					if(flatten)
						file.Suffix = null;
				}

				var key = Path.Combine(file.Suffix ?? "", Path.GetFileName(file.Path));

				// 同包的目标相对路径只保留一个来源；后选资产覆盖先选资产。
				result[key] = file;
			}
		}

		return result.Values;
	}
	#endregion

	#region 资产目录
	private static IEnumerable<string> GetAssetPaths(string path, string framework, IDictionary<string, string> variables)
	{
		var target = NuGetFramework.Parse(framework);
		string library = null;
		string native = null;

		// 托管和原生资产独立选择最先适用的 RID，不能因其中一组命中而停止另一组回退。
		foreach(var runtime in NugetRuntime.GetIdentifiers(variables))
		{
			var runtimePath = Path.Combine(path, "runtimes", runtime);
			library ??= GetNearestFrameworkPath(Path.Combine(runtimePath, "lib"), target);

			if(native == null)
			{
				var directory = Path.Combine(runtimePath, "native");
				if(Directory.Exists(directory))
					native = directory;
			}

			if(library != null && native != null)
				break;
		}

		library ??= GetNearestFrameworkPath(Path.Combine(path, "lib"), target);

		if(library != null)
			yield return library;
		if(native != null)
			yield return native;

		foreach(var content in GetContentPaths(path, target))
			yield return content;
	}

	private static IEnumerable<string> GetContentPaths(string path, NuGetFramework framework)
	{
		var contentFiles = Path.Combine(path, "contentFiles", "any");
		var content = GetNearestFrameworkPath(contentFiles, framework);

		if(!string.IsNullOrEmpty(content))
		{
			yield return Path.Combine(content, "**");
			yield break;
		}

		content = Path.Combine(path, "content");

		if(Directory.Exists(content))
			yield return Path.Combine(content, "**");
	}

	private static NuGetFramework TryParseFramework(string name)
	{
		if(string.Equals(name, "any", StringComparison.OrdinalIgnoreCase))
			return NuGetFramework.AnyFramework;

		try
		{
			return NuGetFramework.Parse(name);
		}
		catch(ArgumentException)
		{
			return NuGetFramework.UnsupportedFramework;
		}
	}
	#endregion

	#region 内容筛选
	private static bool CopyContent(IEnumerable<ContentFilesEntry> rules, string relative, out bool flatten)
	{
		flatten = false;

		// 未声明复制的内容不输出；后续匹配规则只覆盖显式指定的属性。
		var copy = false;
		var file = relative["contentFiles/".Length..];

		foreach(var rule in rules)
		{
			if(!Match(rule.Include, file) || Match(rule.Exclude, file))
				continue;

			if(rule.CopyToOutput.HasValue)
				copy = rule.CopyToOutput.Value;

			if(rule.Flatten.HasValue)
				flatten = rule.Flatten.Value;
		}

		return copy;
	}

	private static bool Match(string patterns, string path)
	{
		if(string.IsNullOrWhiteSpace(patterns))
			return false;

		//这里匹配 nuspec 中的包内逻辑路径，不进行本地文件系统搜索。
		foreach(var pattern in patterns.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var escaped = System.Text.RegularExpressions.Regex.Escape(pattern.Replace('\\', '/'))
				.Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");

			if(System.Text.RegularExpressions.Regex.IsMatch(path, "^" + escaped + "$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
				return true;
		}

		return false;
	}
	#endregion
}
