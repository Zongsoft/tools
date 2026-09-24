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

namespace Zongsoft.Tools.Deployer;

/// <summary>提供本地部署源展开、NuGet 缓存路径适配及文件覆盖操作。</summary>
/// <remarks>搜索复用 Core Searcher，部署适配层保留逻辑源名称与目标目录后缀。</remarks>
public static class DeploymentUtility
{
	#region 文件操作
	public static IEnumerable<PathToken> GetFiles(string filePath, IDictionary<string, string> variables, bool resolveLibrary = true, CancellationToken cancellation = default) => GetFiles(filePath, variables, resolveLibrary, cancellation, null);
	internal static IEnumerable<PathToken> GetFiles(string filePath, IDictionary<string, string> variables, bool resolveLibrary, CancellationToken cancellation, string sourceDirectory)
	{
		if(string.IsNullOrEmpty(filePath))
			yield break;

		filePath = Path.GetFullPath(filePath);
		if(Directory.Exists(filePath))
		{
			// 显式目录是选中的载荷根；Core 从该根递归，但不进入内部目录链接。
			foreach(var match in Zongsoft.IO.Searcher.Search(new DirectoryInfo(filePath), "**/*", Zongsoft.IO.Searcher.Target.Files, cancellation))
			{
				var suffix = Path.GetDirectoryName(Path.GetRelativePath(filePath, match.Path));
				yield return new PathToken(match.Path, suffix);
			}

			yield break;
		}

		var fileName = Path.GetFileName(filePath);
		if(string.IsNullOrEmpty(fileName))
			yield break;

		var directoryName = Path.GetDirectoryName(filePath);
		var expansion = Deployer.Flag(variables, Deployer.EXPANSION_OPTION);
		var selected = new HashSet<string>(DeploymentPath.Comparer);
		var expanded = new List<string>();

		// 先定位包目录，再调整框架，避免原框架不存在时通配搜索提前返回空集。
		foreach(var candidate in GetLibraryDirectories(directoryName, variables, resolveLibrary, expansion, cancellation))
		{
			foreach(var directory in GetDirectories(candidate.Path, expansion, cancellation, sourceDirectory))
			{
				cancellation.ThrowIfCancellationRequested();
				if(expanded.Any(parent => IsWithin(parent, directory.Path)))
					continue;

				if(filePath.IndexOfAny(['*', '?']) >= 0 && HasDirectoryLink(directory.Path, sourceDirectory))
					continue;

				var suffix = CombineSuffix(candidate.Suffix, directory.Suffix);
				if(fileName.IndexOfAny(['*', '?']) < 0)
				{
					// 精确请求保留缺失项，由计划阶段给出文件不存在诊断。
					var token = new PathToken(Path.Combine(directory.Path, fileName), suffix);
					if(selected.Add(token.ToString()))
						yield return token;
					continue;
				}

				foreach(var match in Zongsoft.IO.Searcher.Search(new DirectoryInfo(directory.Path), fileName, cancellation: cancellation))
				{
					if(expanded.Any(parent => IsWithin(parent, match.Path)))
						continue;

					if(match.IsDirectory(out _))
					{
						expanded.Add(match.Path);
						var prefix = CombineSuffix(suffix, Path.GetFileName(match.Path));
						foreach(var child in Zongsoft.IO.Searcher.Search(new DirectoryInfo(match.Path), "**/*", Zongsoft.IO.Searcher.Target.Files, cancellation))
						{
							var relative = Path.GetDirectoryName(Path.GetRelativePath(match.Path, child.Path));
							var token = new PathToken(child.Path, CombineSuffix(prefix, relative));
							if(selected.Add(token.ToString()))
								yield return token;
						}
					}
					else
					{
						var token = new PathToken(match.Path, suffix);
						if(selected.Add(token.ToString()))
							yield return token;
					}
				}
			}
		}
	}

	public static IEnumerable<PathToken> GetDirectories(string directory, bool expansion, CancellationToken cancellation = default) => GetDirectories(directory, expansion, cancellation, null);
	private static IEnumerable<PathToken> GetDirectories(string directory, bool expansion, CancellationToken cancellation, string sourceDirectory)
	{
		if(string.IsNullOrEmpty(directory))
			return [];

		directory = Path.GetFullPath(directory);
		if(directory.IndexOfAny(['*', '?']) < 0)
			return [new PathToken(directory)];

		var root = sourceDirectory ?? Path.GetPathRoot(directory);
		return Zongsoft.IO.Searcher.Search(new DirectoryInfo(root), Path.GetRelativePath(root, directory), Zongsoft.IO.Searcher.Target.Directories, cancellation)
			.Select(match => new PathToken(match.Path, expansion ? Path.GetRelativePath(match.Origin.FullName, match.Path) : Path.Combine(match.Captures.ToArray())));
	}

	private static IEnumerable<PathToken> GetLibraryDirectories(string directory, IDictionary<string, string> variables, bool resolveLibrary, bool expansion, CancellationToken cancellation)
	{
		if(!resolveLibrary)
			return [new PathToken(directory)];

		var cache = Path.GetFullPath(NugetUtility.GetPackagesDirectory(variables));
		var relative = Path.GetRelativePath(cache, directory);
		if(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			return [new PathToken(directory)];

		var parts = relative.Split(Utility.PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
		var index = Array.IndexOf(parts, "lib");
		if(index < 0)
			return [new PathToken(directory)];

		// 框架段本身含通配时先按名称展开，不能把 net* 当作框架标识交给 NuGet。
		var prefixLength = index + 1 < parts.Length && parts[index + 1].IndexOfAny(['*', '?']) >= 0 ? index + 2 : index;
		var package = Path.Combine([cache, .. parts.Take(prefixLength)]);
		return GetDirectories(package, expansion, cancellation).Select(item =>
		{
			var requested = Path.Combine([item.Path, .. parts.Skip(prefixLength)]);
			var resolved = NugetAssets.ResolveLibraryPath(requested, variables);
			var suffix = item.Suffix;

			// 包目录已消耗通配捕获；expansion 还需保留其后的固定段。
			if(expansion && package.IndexOfAny(['*', '?']) >= 0)
			{
				var tail = parts.Skip(prefixLength).TakeWhile(part => part.IndexOfAny(['*', '?']) < 0).ToArray();
				suffix = CombineSuffix(suffix, Path.Combine(tail));
			}

			return new PathToken(resolved, suffix);
		});
	}

	private static bool IsWithin(string directory, string path)
	{
		var relative = Path.GetRelativePath(directory, path);
		return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	}

	private static bool HasDirectoryLink(string path, string origin)
	{
		for(var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
		{
			if(origin != null && DeploymentPath.Comparer.Equals(directory.FullName, Path.GetFullPath(origin)))
				break;

			if(directory.LinkTarget != null)
				return true;
		}

		return false;
	}

	private static string CombineSuffix(string first, string second) => string.IsNullOrEmpty(first) ? second : string.IsNullOrEmpty(second) ? first : Path.Combine(first, second);

	public static bool CopyFile(string source, string destination, Overwrite overwrite)
	{
		if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
			return false;

		var copyRequired = true;

		if(File.Exists(destination))
		{
			copyRequired = overwrite switch
			{
				Overwrite.Alway => true,
				Overwrite.Never => false,
				Overwrite.Newest => File.GetLastWriteTime(source) >= File.GetLastWriteTime(destination),
				_ => true,
			};
		}

		if(copyRequired)
		{
			var directory = Path.GetDirectoryName(destination);

			if(!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			ArtifactPublisher.Copy(source, destination);
		}

		return copyRequired;
	}
	#endregion

	#region 嵌套子类
	/// <summary>保存逻辑源文件路径、部署目标的相对目录后缀及可选包来源。</summary>
	public class PathToken
	{
		public PathToken(string path, string suffix = null)
		{
			this.Path = path;
			this.Suffix = string.IsNullOrEmpty(suffix) || suffix == "." ? null : suffix.Trim(Utility.PATH_SEPARATORS);
		}

		public string Package;
		public string Path;
		public string Suffix;

		public bool Exists() => !string.IsNullOrEmpty(this.Path) && File.Exists(this.Path);
		public override string ToString() => string.IsNullOrEmpty(this.Suffix) ? this.Path : $"{this.Path}?{this.Suffix}";
	}
	#endregion
}
