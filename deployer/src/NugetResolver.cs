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

namespace Zongsoft.Tools.Deployer;

/// <summary>解析 NuGet 部署条目，处理显式包内路径、包内描述文件或延迟展开的普通包资产。</summary>
/// <remarks>普通根包先登记请求，统一求解依赖后再展开文件；解析阶段不执行目标复制。</remarks>
public class NugetResolver : DeploymentResolverBase
{
	#region 单例字段
	public static readonly NugetResolver Instance = new();
	#endregion

	#region 私有构造
	private NugetResolver() : base("Nuget") { }
	#endregion

	#region 重写方法
	protected override async Task<IEnumerable<DeploymentUtility.PathToken>> GetSourcesAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation)
	{
		if(!Utility.TryGetTargetFramework(context.Variables, out var framework))
			throw new FormatException(string.Format(Properties.Resources.Review_UndefinedVariable, "Framework"));

		if(NuGet.Frameworks.NuGetFramework.Parse(framework).IsUnsupported)
			throw new FormatException(string.Format(Properties.Resources.Review_InvalidOption, "Framework", framework));

		var argument = Argument.Parse(deployment.Source.Name);

		if(argument.IsEmpty)
			throw new FormatException(string.Format(Properties.Resources.Review_Missing, deployment.Source.Name));

		var version = argument.Version;

		if(context.Deployer.Session.LockedPlan != null && (string.IsNullOrEmpty(version) || string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase)))
			version = context.Deployer.Session.LockedPlan.Packages.Find(package => StringComparer.OrdinalIgnoreCase.Equals(package.Id, argument.Name))?.Version
				?? throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, argument.Name));

		var metadata = await NugetUtility.GetPackageMetadataAsync(context.Variables, argument.Name, version, cancellation)
			?? throw new InvalidOperationException(string.Format(Properties.Resources.Review_Missing, argument.ToString()));
		var path = await NugetUtility.DownloadPackageAsync(context.Variables, argument.Name, metadata.Identity.Version, cancellation)
			?? throw new InvalidOperationException(string.Format(Properties.Resources.Review_Missing, argument.ToString()));
		context.Deployer.Session.Requested[metadata.Identity.ToString()] = metadata;

		if(!string.IsNullOrEmpty(argument.Path))
		{
			var requested = Path.GetFullPath(Path.Combine(path, argument.Path));
			DeploymentPath.ValidateSource(path, requested);

			return DeploymentUtility.GetFiles(requested, context.Variables, cancellation: cancellation);
		}

		if(File.Exists(Path.Combine(path, Deployer.DEFAULT_DEPLOYMENT_FILENAME)))
			return DeploymentUtility.GetFiles(Path.Combine(path, Deployer.DEFAULT_DEPLOYMENT_FILENAME), context.Variables, cancellation: cancellation);

		// 保留清单中的占位位置，待所有根请求完成统一求解后再展开。
		context.Deployer.Session.Roots.Add((metadata, framework));
		context.Deployer.Session.Add(new DeploymentOperation
		{
			Kind = "Package",
			Package = metadata.Identity.ToString(),
			Manifest = deployment.Profile.FilePath,
			Expand = token => ExpandPackageAsync(context, deployment, metadata.Identity.Id, framework, token),
		});

		return [];
	}
	#endregion

	#region 资产展开
	/// <summary>收集根包及已求解依赖的文件，再统一登记源操作；不在遍历过程中重新选择版本。</summary>
	private async Task ExpandPackageAsync(DeploymentContext context, DeploymentEntry deployment, string root, string framework, CancellationToken cancellation)
	{
		var files = new List<DeploymentUtility.PathToken>();
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string>();
		pending.Push(root);

		// 保留根包优先、依赖深度优先的输出顺序；这里只消费已经求解的版本，不再做版本选择。
		while(pending.TryPop(out var id))
		{
			cancellation.ThrowIfCancellationRequested();
			if(!visited.Add(id))
				continue;

			var package = context.Deployer.Session.Packages[id];
			var path = await NugetUtility.DownloadPackageAsync(context.Variables, id, package.Identity.Version, cancellation)
				?? throw new InvalidOperationException(string.Format(Properties.Resources.Review_Missing, package.Identity));

			foreach(var file in NugetAssets.GetPackageFiles(path, framework, context.Variables, cancellation))
			{
				file.Package = package.Identity.ToString();
				files.Add(file);
			}

			// 栈后进先出，反向入栈以保持依赖声明的访问顺序。
			foreach(var dependency in NugetGraph.GetDependencies(package, framework).Reverse())
			{
				if(!NugetGraph.ShouldIgnoreDependency(context.Variables, dependency.Id))
					pending.Push(dependency.Id);
			}
		}

		await PlanSourcesAsync(context, deployment, files, cancellation);
	}
	#endregion

	#region 嵌套结构
	/// <summary>表示包名、可选版本及可选包内路径组成的 NuGet 部署参数。</summary>
	public readonly struct Argument
	{
		#region 静态常量
		static readonly char[] SEPARATORS = new char[] { '/', '\\'};
		#endregion

		#region 构造函数
		private Argument(string name, string version = null, string path = null)
		{
			this.Name = name;
			this.Version = version?.Trim();
			this.Path = path?.Trim();
		}
		#endregion

		#region 公共字段
		public readonly string Name;
		public readonly string Version;
		public readonly string Path;
		#endregion

		#region 公共属性
		public bool IsEmpty => string.IsNullOrEmpty(this.Name);
		#endregion

		#region 重写方法
		public override string ToString()
		{
			if(string.IsNullOrEmpty(this.Version))
				return string.IsNullOrEmpty(this.Path) ? this.Name : $"{this.Name}/{this.Path}";
			else
				return string.IsNullOrEmpty(this.Path) ? $"{this.Name}@{this.Version}" : $"{this.Name}@{this.Version}/{this.Path}";
		}
		#endregion

		#region 解析方法
		public static Argument Parse(ReadOnlySpan<char> text)
		{
			if(text.IsEmpty)
				return default;

			var index = text.IndexOfAny(SEPARATORS);

			if(index == 0)
				return default;

			if(index < 0)
			{
				(var name, var version) = ParseIdentity(text);

				return string.IsNullOrEmpty(name) ? default : new Argument(name, version);
			}
			else
			{
				(var name, var version) = ParseIdentity(text[..index]);

				return string.IsNullOrEmpty(name) ? default : new Argument(name, version, text[(index + 1)..].ToString());
			}
		}

		private static (string name, string version) ParseIdentity(ReadOnlySpan<char> text)
		{
			if(text.IsEmpty)
				return default;

			var index = text.IndexOf('@');

			if(index == 0)
				return default;

			if(index < 0)
				return (text.ToString(), null);

			return (text[..index].ToString(), text[(index + 1)..].ToString());
		}
		#endregion
	}
	#endregion
}
