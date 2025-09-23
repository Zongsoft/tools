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
 * Copyright (C) 2015-2017 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Threading.Tasks;
using System.Collections.Generic;

using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGet.Frameworks;

namespace Zongsoft.Tools.Deployer
{
	public static class NugetUtility
	{
		#region 常量定义
		private const string NUGET_SERVER_URL = @"https://api.nuget.org/v3/index.json";

		internal const string USERPROFILE_ENVIRONMENT = "USERPROFILE";
		internal const string NUGET_SERVER_ENVIRONMENT = "NuGet_Server";
		internal const string NUGET_PACKAGES_ENVIRONMENT = "NuGet_Packages";
		#endregion

		#region 私有变量
		private static VersionFolderPathResolver _folder = null;
		private static readonly Dictionary<string, IPackageSearchMetadata> _metadatas = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, string> _packages = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, ICollection<string>> _dependents = new(StringComparer.OrdinalIgnoreCase);
		#endregion

		#region 静态属性
		private static string DEFAULT_PACKAGES_DIRECTORY => Path.Combine(NuGetEnvironment.GetFolderPath(NuGet.Common.NuGetFolderPath.NuGetHome), "packages");
		#endregion

		#region 初始方法
		public static void Initialize(IDictionary<string, string> variables)
		{
			if(!variables.ContainsKey(NUGET_SERVER_ENVIRONMENT))
				variables[NUGET_SERVER_ENVIRONMENT] = NUGET_SERVER_URL;

			if(!variables.ContainsKey(USERPROFILE_ENVIRONMENT))
				variables[USERPROFILE_ENVIRONMENT] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			if(!variables.TryGetValue(NUGET_PACKAGES_ENVIRONMENT, out var directory) || string.IsNullOrWhiteSpace(directory))
				variables.TryAdd(NUGET_PACKAGES_ENVIRONMENT, DEFAULT_PACKAGES_DIRECTORY);
		}
		#endregion

		#region 公共方法
		public static string GetNugetServer(IDictionary<string, string> variables)
		{
			if(!variables.TryGetValue(NUGET_SERVER_ENVIRONMENT, out var server) || string.IsNullOrWhiteSpace(server))
				server = NUGET_SERVER_URL;

			return server;
		}

		public static string GetPackagesDirectory(IDictionary<string, string> variables)
		{
			return variables.TryGetValue(NUGET_PACKAGES_ENVIRONMENT, out var directory) && !string.IsNullOrEmpty(directory) ? directory : DEFAULT_PACKAGES_DIRECTORY;
		}

		public static string GetNearestLibraryPath(string path, string framework)
		{
			if(string.IsNullOrEmpty(path) || string.IsNullOrEmpty(framework))
				return null;

			var directory = new DirectoryInfo(Path.Combine(path, "lib"));
			if(!directory.Exists)
				return null;

			var frameworks = directory.GetDirectories().Select(dir => NuGetFramework.Parse(dir.Name));

			//从包的库目录中查找最适用的框架版本
			var nearest = NuGetFrameworkUtility.GetNearest(frameworks, NuGetFramework.Parse(framework), p => p);
			return nearest == null || nearest.IsUnsupported ? null : Path.Combine(path, "lib", nearest.GetShortFolderName());
		}

		public static string GetFolderPath(string packagesDirectory, string name, string version) => NuGetVersion.TryParse(version, out var ver) ? GetFolderPath(packagesDirectory, name, ver) : GetFolderPath(packagesDirectory, name);
		public static string GetFolderPath(string packagesDirectory, string name, NuGetVersion version = null)
		{
			_folder ??= new VersionFolderPathResolver(packagesDirectory);
			return version == null ? _folder.GetVersionListPath(name) : _folder.GetInstallPath(name, version);
		}

		public static async Task<IPackageSearchMetadata> GetPackageMetadataAsync(IDictionary<string, string> variables, string name, string version, CancellationToken cancellation)
		{
			var key = GetCacheKey(name, version);
			if(_metadatas.TryGetValue(key, out var metadata))
				return metadata;

			using var cache = new SourceCacheContext() { NoCache = true };
			var repository = GetRepository(variables);
			var resource = repository.GetResource<PackageMetadataResource>();

			if(string.IsNullOrEmpty(version) || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
			{
				var metadatas = await resource.GetMetadataAsync(name, false, false, cache, NullLogger.Instance, cancellation);
				var result = _metadatas[key] = metadatas.MaxBy(metadata => metadata.Identity.Version);

				if(result != null)
					_metadatas[GetCacheKey(name, result.Identity.Version)] = result;

				return result;
			}

			return _metadatas[key] = NuGetVersion.TryParse(version, out var nugetVersion) ?
				await resource.GetMetadataAsync(new PackageIdentity(name, nugetVersion), cache, NullLogger.Instance, cancellation) : null;
		}

		public static async Task<string> DownloadPackageAsync(IDictionary<string, string> variables, string name, NuGetVersion version, CancellationToken cancellation)
		{
			if(string.IsNullOrEmpty(name) || version == null)
				return null;

			var key = GetCacheKey(name, version);
			if(_packages.TryGetValue(name, out var package))
				return package;

			using var cache = new SourceCacheContext();
			var context = new PackageDownloadContext(cache);
			var directory = GetPackagesDirectory(variables);
			var resource = await GetRepository(variables).GetResourceAsync<DownloadResource>(cancellation);
			using var result = await resource.GetDownloadResourceResultAsync(new PackageIdentity(name, version), context, directory, NullLogger.Instance, cancellation);

			return _packages[key] = result.Status == DownloadResourceResultStatus.Available || result.Status == DownloadResourceResultStatus.AvailableWithoutStream ? GetFolderPath(directory, name, version) : null;
		}

		public static async Task<IEnumerable<string>> DownloadDependentPackageAsync(IDictionary<string, string> variables, IPackageSearchMetadata metadata, string framework, CancellationToken cancellation)
		{
			if(metadata == null || metadata.DependencySets == null)
				return Array.Empty<string>();

			var key = GetCacheKey(metadata.Identity);
			if(_dependents.TryGetValue(key, out var dependents))
				return dependents;

			var nearest = NuGetFrameworkExtensions.GetNearest(metadata.DependencySets, NuGetFramework.Parse(framework));
			if(nearest == null)
				return _dependents[key] = Array.Empty<string>();

			var result = new List<string>(nearest.Packages.Count());

			foreach(var package in nearest.Packages)
			{
				//忽略依赖中的系统包、框架内置包以及 Zongsoft 包
				if(package.Id.StartsWith("System.") || package.Id.StartsWith("Microsoft.Extensions.") || package.Id.StartsWith("Zongsoft."))
					continue;

				var path = await DownloadPackageAsync(variables, package.Id, package.VersionRange.MinVersion, cancellation);
				result.Add(path);
			}

			return _dependents[key] = result;
		}

		private static string GetCacheKey(string name, string version) => string.IsNullOrEmpty(version) ? name : $"{name}:{version}";
		private static string GetCacheKey(string name, NuGetVersion version) => $"{name}:{version}";
		private static string GetCacheKey(PackageIdentity identity) => $"{identity.Id}:{identity.Version}";
		private static SourceRepository GetRepository(IDictionary<string, string> variables) => Repository.Factory.GetCoreV3(NugetUtility.GetNugetServer(variables));
		#endregion
	}
}