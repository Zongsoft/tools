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
 * Copyright (C) 2015-2025 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer;

/// <summary>为文件源解析器提供源展开、嵌套描述文件处理和部署操作登记的公共流程。</summary>
public abstract class DeploymentResolverBase : IDeploymentResolver
{
	#region 构造函数
	protected DeploymentResolverBase(string name) => this.Name = name ?? string.Empty;
	#endregion

	#region 公共属性
	public string Name { get; }
	#endregion

	#region 公共方法
	public async Task ResolveAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation)
	{
		var sources = await this.GetSourcesAsync(context, deployment, cancellation);
		await PlanSourcesAsync(context, deployment, sources, cancellation);
	}
	#endregion

	#region 内部方法
	internal static async Task PlanSourcesAsync(DeploymentContext context, DeploymentEntry deployment, IEnumerable<DeploymentUtility.PathToken> sources, CancellationToken cancellation)
	{
		foreach(var source in sources)
		{
			cancellation.ThrowIfCancellationRequested();

			// 缺失的可选源不进入计划，避免阻止其他有效文件部署。
			if(DeploymentPath.IsMissing(source.Path))
			{
				context.Counter.Skip();

				var warning = string.Format(Properties.Resources.Review_MissingFile, source.Path);
				context.Deployer.Plan.Diagnostics.Add(warning);
				context.Deployer.Output.WriteLine(warning);

				continue;
			}

			if(!source.Exists())
				throw new FileNotFoundException(string.Format(Properties.Resources.Review_Missing, source.Path));

			var directory = string.IsNullOrEmpty(source.Suffix) ? deployment.Destination.Path : Path.Combine(deployment.Destination.Path, source.Suffix);
			context.Deployer.Session.Validate(directory);

			if(Utility.IsDeploymentFile(source.Path) && !Deployer.Flag(context.Variables, Deployer.IGNOREDEPLOYMENTFILE_OPTION))
			{
				await context.Deployer.PlanManifestAsync(source.Path, directory, cancellation);
				continue;
			}

			if(Path.GetFileName(source.Path) == "_._" && new FileInfo(source.Path).Length == 0)
				continue;

			var name = string.IsNullOrEmpty(deployment.Destination.Name) ? Path.GetFileName(source.Path) : deployment.Destination.Name;

			if(Utility.IsDirectory(name))
				name = Path.Combine(name, Path.GetFileName(source.Path));

			var destination = context.Deployer.Session.Validate(Path.Combine(directory, name));
			context.Deployer.Session.Add(new DeploymentOperation

			{
				Kind = "Copy",
				Source = source.Path,
				Destination = destination,
				Manifest = deployment.Profile.FilePath,
				Package = source.Package,
				Framework = Utility.GetTargetFramework(context.Variables),
				Runtime = context.Variables.TryGetValue("platform", out var platform) && context.Variables.TryGetValue("architecture", out var architecture) ? $"{platform}-{architecture}" : null,
			});
		}
	}
	#endregion

	#region 虚拟方法
	protected virtual Task<IEnumerable<DeploymentUtility.PathToken>> GetSourcesAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation) => Task.FromResult(DeploymentUtility.GetFiles(deployment.Source.FullPath, context.Variables, true, cancellation, Path.GetDirectoryName(deployment.Profile.FilePath)));
	#endregion
}
