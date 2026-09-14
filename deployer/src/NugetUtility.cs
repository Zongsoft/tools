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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Zongsoft.Tools.Deployer;

/// <summary>提供 NuGet 包源、缓存目录、元数据、版本及下载操作，并计算部署锁定所需的包内容摘要。</summary>
/// <remarks>包访问缓存按变量字典隔离；依赖求解、资产选择与 RID 回退分别由独立类型负责。</remarks>
public static class NugetUtility
{
	#region 常量定义
	private const string NUGET_SERVER_URL = @"https://api.nuget.org/v3/index.json";

	private const string USERPROFILE_ENVIRONMENT = "USERPROFILE";
	private const string NUGET_SERVER_ENVIRONMENT = "NuGet_Server";
	private const string NUGET_PACKAGES_ENVIRONMENT = "NuGet_Packages";
	#endregion

	#region 私有变量
	private static readonly ConditionalWeakTable<IDictionary<string, string>, PackageCache> _caches = new();
	#endregion

	#region 静态属性
	private static string DEFAULT_PACKAGES_DIRECTORY => Path.Combine(NuGetEnvironment.GetFolderPath(NuGetFolderPath.NuGetHome), "packages");
	#endregion

	#region 初始方法
	/// <summary>清除指定变量上下文的包访问缓存，供新的部署调用重新读取包信息。</summary>
	internal static void ResetCache(IDictionary<string, string> variables) => _caches.Remove(variables);

	/// <summary>为部署变量补入缺少的包源、用户目录和包缓存目录。</summary>
	public static void Initialize(IDictionary<string, string> variables)
	{
		if(!variables.ContainsKey(NUGET_SERVER_ENVIRONMENT))
			variables[NUGET_SERVER_ENVIRONMENT] = NUGET_SERVER_URL;

		if(!variables.ContainsKey(USERPROFILE_ENVIRONMENT))
			variables[USERPROFILE_ENVIRONMENT] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if(!variables.TryGetValue(NUGET_PACKAGES_ENVIRONMENT, out var directory) || string.IsNullOrWhiteSpace(directory))
			variables[NUGET_PACKAGES_ENVIRONMENT] = DEFAULT_PACKAGES_DIRECTORY;
	}
	#endregion

	#region 包源路径
	/// <summary>获取配置的 NuGet 包源；未指定时使用官方 V3 包源。</summary>
	public static string GetNugetServer(IDictionary<string, string> variables)
	{
		if(!variables.TryGetValue(NUGET_SERVER_ENVIRONMENT, out var server) || string.IsNullOrWhiteSpace(server))
			server = NUGET_SERVER_URL;

		return server;
	}

	/// <summary>获取配置的包缓存目录；未指定时使用 NuGet 用户目录下的 packages 目录。</summary>
	public static string GetPackagesDirectory(IDictionary<string, string> variables)
	{
		return variables.TryGetValue(NUGET_PACKAGES_ENVIRONMENT, out var directory) && !string.IsNullOrEmpty(directory) ? directory : DEFAULT_PACKAGES_DIRECTORY;
	}

	/// <summary>按 NuGet 缓存布局生成绝对目录；未指定版本时返回该包的版本列表目录，不执行文件系统访问。</summary>
	public static string GetFolderPath(string packagesDirectory, string name, NuGetVersion version = null)
	{
		var folder = new VersionFolderPathResolver(Path.GetFullPath(packagesDirectory));
		return version == null ? folder.GetVersionListPath(name) : folder.GetInstallPath(name, version);
	}
	#endregion

	#region 包访问
	/// <summary>优先从本地读取包元数据，必要时访问包源；空版本或 latest 按预发布策略选择最高可用版本。</summary>
	/// <returns>找到的包元数据；版本格式无效或找不到对应包时返回空。</returns>
	public static async Task<PackageMetadata> GetPackageMetadataAsync(IDictionary<string, string> variables, string name, string version, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();

		if(string.IsNullOrWhiteSpace(name) || !NuGet.Packaging.PackageIdValidator.IsValidPackageId(name))
			throw new FormatException(string.Format(Properties.Resources.Review_Missing, name));

		var state = _caches.GetOrCreateValue(variables);
		var key = ContextKey(variables) + "|" + GetCacheKey(name, version);

		if(state.Metadata.TryGetValue(key, out var cached))
			return cached;

		var latest = string.IsNullOrEmpty(version) || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase);

		if(latest)
		{
			var versions = await GetVersionsAsync(variables, name, cancellation);
			version = versions.Where(item => Deployer.Flag(variables, "prerelease") || !item.IsPrerelease).Max()?.ToNormalizedString();

			if(version == null)
				return null;
		}

		if(!NuGetVersion.TryParse(version, out var parsed))
			return null;

		var result = GetLocalPackageMetadata(GetPackagesDirectory(variables), name, parsed);

		if(result == null)
		{
			if(Deployer.Flag(variables, "offline"))
				return null;

			using var cache = new SourceCacheContext();
			var resource = await GetRepository(variables).GetResourceAsync<PackageMetadataResource>(cancellation);
			result = PackageMetadata.Create(await resource.GetMetadataAsync(new PackageIdentity(name, parsed), cache, NullLogger.Instance, cancellation));
		}

		if(result != null)
			state.Metadata[key] = result;

		return result;

		static string GetCacheKey(string name, string version) => string.IsNullOrEmpty(version) ? name : $"{name}:{version}";
	}

	/// <summary>合并本地与包源中的版本并按升序返回；离线模式仅查询本地目录，预发布筛选由调用方决定。</summary>
	internal static async Task<NuGetVersion[]> GetVersionsAsync(IDictionary<string, string> variables, string name, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();

		var state = _caches.GetOrCreateValue(variables);
		var key = ContextKey(variables) + "|" + name;

		if(state.Versions.TryGetValue(key, out var cached))
			return cached;

		var versions = new HashSet<NuGetVersion>();
		var path = GetFolderPath(GetPackagesDirectory(variables), name);

		if(Directory.Exists(path))
			foreach(var directory in Directory.EnumerateDirectories(path))
				if(NuGetVersion.TryParse(Path.GetFileName(directory), out var version))
					versions.Add(version);

		if(!Deployer.Flag(variables, "offline"))
		{
			using var cache = new SourceCacheContext();
			var resource = await GetRepository(variables).GetResourceAsync<FindPackageByIdResource>(cancellation);
			versions.UnionWith(await resource.GetAllVersionsAsync(name, cache, NullLogger.Instance, cancellation));
		}

		return state.Versions[key] = [.. versions.Order()];
	}

	/// <summary>复用本地包或下载指定版本并返回缓存目录；离线缺包时报错。</summary>
	/// <returns>包的本地目录；未提供包名或版本，或者下载结果不可用时返回空。</returns>
	public static async Task<string> DownloadPackageAsync(IDictionary<string, string> variables, string name, NuGetVersion version, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();

		if(string.IsNullOrEmpty(name) || version == null)
			return null;

		var directory = GetPackagesDirectory(variables);
		var path = GetFolderPath(directory, name, version);

		if(GetLocalPackageMetadata(directory, name, version) != null)
			return path;

		if(Deployer.Flag(variables, "offline"))
			throw new InvalidOperationException(string.Format(Properties.Resources.Review_Offline, $"{name}@{version}"));

		using var cache = new SourceCacheContext();
		var context = new PackageDownloadContext(cache);
		var resource = await GetRepository(variables).GetResourceAsync<DownloadResource>(cancellation);
		using var result = await resource.GetDownloadResourceResultAsync(new PackageIdentity(name, version), context, directory, NullLogger.Instance, cancellation);

		return result.Status == DownloadResourceResultStatus.Available || result.Status == DownloadResourceResultStatus.AvailableWithoutStream ? path : null;
	}
	#endregion

	#region 包校验
	/// <summary>按固定顺序汇总包内相对路径和文件内容摘要，排除链接项、包归档和缓存记账文件。/summary>
	internal static string PackageHash(IDictionary<string, string> variables, PackageMetadata metadata)
	{
		var root = GetFolderPath(GetPackagesDirectory(variables), metadata.Identity.Id, metadata.Identity.Version);
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

		foreach(var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
		}).Order(StringComparer.Ordinal))
		{
			if(path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".nupkg.sha512", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) == ".nupkg.metadata")
				continue;

			hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace('\\', '/') + "\0"));
			hash.AppendData(Convert.FromHexString(DeploymentSession.Hash(path)));
		}

		return Convert.ToHexString(hash.GetHashAndReset());
	}
	#endregion

	#region 私有方法
	private static PackageMetadata GetLocalPackageMetadata(string packagesDirectory, string name, NuGetVersion version)
	{
		var path = GetFolderPath(packagesDirectory, name, version);

		if(!Directory.Exists(path))
			return null;

		var nuspec = Directory.EnumerateFiles(path, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();

		if(!string.IsNullOrEmpty(nuspec))
		{
			using var stream = File.OpenRead(nuspec);

			return PackageMetadata.Create(new NuspecReader(stream));
		}

		var nupkg = Directory.EnumerateFiles(path, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();

		if(string.IsNullOrEmpty(nupkg))
			return null;

		using var reader = new PackageArchiveReader(nupkg);

		return PackageMetadata.Create(reader.NuspecReader);
	}

	private static string ContextKey(IDictionary<string, string> variables)
	{
		// 变量字典可在两次查询间修改；缓存键需同时区分包源、目录和解析策略。
		var directory = Path.GetFullPath(GetPackagesDirectory(variables));
		var offline = Deployer.Flag(variables, "offline");
		var prerelease = Deployer.Flag(variables, "prerelease");

		return $"{GetNugetServer(variables)}|{directory}|{offline}|{prerelease}";
	}

	private static SourceRepository GetRepository(IDictionary<string, string> variables) => Repository.Factory.GetCoreV3(NugetUtility.GetNugetServer(variables));
	#endregion

	#region 嵌套子类
	/// <summary>
	/// 保存一个变量字典对应的元数据与版本查询缓存，不延长该字典的生命周期。
	/// </summary>
	private sealed class PackageCache
	{
		public readonly Dictionary<string, PackageMetadata> Metadata = new(StringComparer.OrdinalIgnoreCase);
		public readonly Dictionary<string, NuGetVersion[]> Versions = new(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 保存依赖求解所需的包身份和框架依赖组，使调用方不依赖元数据来自包源、nuspec 或包归档。
	/// </summary>
	public sealed class PackageMetadata
	{
		private PackageMetadata(PackageIdentity identity, IEnumerable<PackageDependencyGroup> dependencySets)
		{
			this.Identity = identity;
			this.DependencySets = dependencySets?.ToArray() ?? [];
		}

		public PackageIdentity Identity { get; }
		public IEnumerable<PackageDependencyGroup> DependencySets { get; }

		public static PackageMetadata Create(IPackageSearchMetadata metadata) => metadata == null ? null : new PackageMetadata(metadata.Identity, metadata.DependencySets);
		public static PackageMetadata Create(NuspecReader reader) => reader == null ? null : new PackageMetadata(reader.GetIdentity(), reader.GetDependencyGroups());
	}
	#endregion
}
