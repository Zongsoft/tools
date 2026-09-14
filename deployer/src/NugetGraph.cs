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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NuGet.Frameworks;
using NuGet.Versioning;
using NuGet.Packaging.Core;

namespace Zongsoft.Tools.Deployer;

/// <summary>在固定根请求及各目标框架的版本约束下，为一次部署求解完整依赖闭包。</summary>
/// <remarks>每次求解创建独立实例，按候选版本升序回溯；这里只执行部署所需的约束策略，不等同于完整的 NuGet restore。</remarks>
internal sealed class NugetGraph
{
	#region 私有字段
	private readonly IDictionary<string, string> _variables;
	private readonly (NugetUtility.PackageMetadata Metadata, NuGetFramework Framework)[] _roots;
	private readonly IReadOnlyList<PackageSelection> _locked;
	private readonly CancellationToken _cancellation;
	private int _attempts;
	private string _conflict = string.Empty;
	#endregion

	#region 构造函数
	private NugetGraph(IDictionary<string, string> variables,
		IEnumerable<(NugetUtility.PackageMetadata Metadata, string Framework)> requests,
		CancellationToken cancellation, IReadOnlyList<PackageSelection> locked)
	{
		_variables = variables;
		_roots = requests.Select(request => (request.Metadata, NuGetFramework.Parse(request.Framework))).ToArray();
		_cancellation = cancellation;
		_locked = locked;
	}
	#endregion

	#region 依赖查询
	/// <summary>取得最适合目标框架的依赖组；此处不应用部署忽略规则。</summary>
	public static IEnumerable<PackageDependency> GetDependencies(NugetUtility.PackageMetadata package, string framework) =>
		GetDependencies(package, NuGetFramework.Parse(framework));

	private static IEnumerable<PackageDependency> GetDependencies(NugetUtility.PackageMetadata package, NuGetFramework framework) =>
		NuGetFrameworkExtensions.GetNearest(package.DependencySets, framework)?.Packages ?? [];

	/// <summary>按默认和自定义前缀判断是否忽略传递依赖，不用于过滤显式根请求。</summary>
	public static bool ShouldIgnoreDependency(IDictionary<string, string> variables, string name)
	{
		var prefixes = new List<string> { "System.", "Microsoft.Extensions.", "Zongsoft." };

		if(variables.TryGetValue(Deployer.IGNOREDEPENDENTPREFIX_OPTION, out var value) && !string.IsNullOrWhiteSpace(value))
			prefixes.AddRange(value.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

		return prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
	}
	#endregion

	#region 求解入口
	/// <summary>固定根包版本并求解满足全部约束的依赖；指定锁定记录时仅使用锁内候选版本。</summary>
	public static Task<Dictionary<string, NugetUtility.PackageMetadata>> ResolveAsync(IDictionary<string, string> variables,
		IEnumerable<(NugetUtility.PackageMetadata Metadata, string Framework)> requests, CancellationToken cancellation, IReadOnlyList<PackageSelection> locked = null) =>
		new NugetGraph(variables, requests, cancellation, locked).ResolveAsync();

	private async Task<Dictionary<string, NugetUtility.PackageMetadata>> ResolveAsync()
	{
		var selected = new Dictionary<string, NugetUtility.PackageMetadata>(StringComparer.OrdinalIgnoreCase);

		foreach(var (metadata, _) in _roots)
		{
			if(selected.TryGetValue(metadata.Identity.Id, out var other) && other.Identity.Version != metadata.Identity.Version)
				throw Conflict($"{other.Identity} / {metadata.Identity}");

			selected[metadata.Identity.Id] = metadata;
		}

		return await SearchAsync(selected) ?? throw Conflict(_conflict);
	}
	#endregion

	#region 版本搜索
	/// <summary>尝试完成当前分支的版本选择；约束冲突或循环时返回空以便上层回溯。</summary>
	private async Task<Dictionary<string, NugetUtility.PackageMetadata>> SearchAsync(Dictionary<string, NugetUtility.PackageMetadata> selected)
	{
		_cancellation.ThrowIfCancellationRequested();

		if(++_attempts > 10000 || selected.Count > 512)
			throw Conflict("resolution limit");

		if(!TryGetRequirements(selected, out var requirements))
			return null;

		foreach(var pair in requirements)
		{
			if(selected.TryGetValue(pair.Key, out var package) && pair.Value.Any(request => !request.Range.Satisfies(package.Identity.Version)))
			{
				_conflict = $"{package.Identity}: {Describe(pair.Value)}";
				return null;
			}
		}

		var unresolved = requirements.FirstOrDefault(pair => !selected.ContainsKey(pair.Key));
		if(unresolved.Key == null)
			return selected;

		var versions = await GetVersionsAsync(unresolved.Key);
		var permitPreview = Deployer.Flag(_variables, "prerelease") || unresolved.Value.Any(item => item.Range.MinVersion?.IsPrerelease == true);

		// 按升序尝试满足全部约束的候选；分支拥有自己的选择表，失败后回溯到下一版本。
		foreach(var version in versions)
		{
			if((!permitPreview && version.IsPrerelease) || unresolved.Value.Any(item => !item.Range.Satisfies(version)))
				continue;

			var metadata = await NugetUtility.GetPackageMetadataAsync(_variables, unresolved.Key, version.ToNormalizedString(), _cancellation);
			if(metadata == null)
				continue;

			var next = new Dictionary<string, NugetUtility.PackageMetadata>(selected, StringComparer.OrdinalIgnoreCase)
			{
				[unresolved.Key] = metadata,
			};
			var solved = await SearchAsync(next);
			if(solved != null)
				return solved;
		}

		if(string.IsNullOrEmpty(_conflict))
			_conflict = $"{unresolved.Key}: {Describe(unresolved.Value)}";

		return null;
	}

	private Task<NuGetVersion[]> GetVersionsAsync(string name)
	{
		if(_locked == null)
			return NugetUtility.GetVersionsAsync(_variables, name, _cancellation);

		// 锁定模式只允许锁内版本，不查询新的候选。
		var pin = _locked.FirstOrDefault(package => StringComparer.OrdinalIgnoreCase.Equals(package.Id, name));
		return Task.FromResult<NuGetVersion[]>(pin == null ? [] : [NuGetVersion.Parse(pin.Version)]);
	}
	#endregion

	#region 约束收集
	/// <summary>根据当前已选版本重建各框架的依赖约束，并检测活动依赖链上的循环。</summary>
	private bool TryGetRequirements(Dictionary<string, NugetUtility.PackageMetadata> selected, out Dictionary<string, List<(VersionRange Range, string Parent)>> requirements)
	{
		var collected = new Dictionary<string, List<(VersionRange Range, string Parent)>>(StringComparer.OrdinalIgnoreCase);

		// 完成访问的节点可被菱形依赖复用；只有活动链重复才构成循环。
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var chain = new List<string>();
		requirements = collected;

		foreach(var (metadata, framework) in _roots)
		{
			if(!Visit(metadata.Identity.Id, framework))
				return false;
		}

		return true;

		bool Visit(string id, NuGetFramework framework)
		{
			_cancellation.ThrowIfCancellationRequested();
			// 同一包在不同目标框架下可能具有不同依赖，必须分别收集。
			var key = id + ":" + framework.GetShortFolderName();

			if(active.Contains(key))
			{
				_conflict = "cycle: " + string.Join(" -> ", chain.Append(key));
				return false;
			}

			if(!selected.TryGetValue(id, out var metadata) || !visited.Add(key))
				return true;

			active.Add(key);
			chain.Add(key);

			foreach(var dependency in GetDependencies(metadata, framework))
			{
				if(ShouldIgnoreDependency(_variables, dependency.Id))
					continue;

				if(!collected.TryGetValue(dependency.Id, out var ranges))
					collected[dependency.Id] = ranges = [];

				ranges.Add((dependency.VersionRange, metadata.Identity.ToString()));

				if(!Visit(dependency.Id, framework))
					return false;
			}

			active.Remove(key);
			chain.RemoveAt(chain.Count - 1);
			return true;
		}
	}
	#endregion

	#region 辅助方法
	private static string Describe(IEnumerable<(VersionRange Range, string Parent)> requirements) =>
		string.Join("; ", requirements.Select(item => $"{item.Parent} {item.Range}"));

	private static InvalidOperationException Conflict(string detail) =>
		new(string.Format(Properties.Resources.Review_DependencyConflict, detail));
	#endregion
}
