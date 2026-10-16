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

using Zongsoft.Components;

namespace Zongsoft.Tools.Deployer;

/// <summary>为文件源解析器提供源展开、嵌套描述文件处理和部署操作登记的公共流程。</summary>
public abstract class DeploymentResolverBase : IDeploymentResolver
{
	#region 构造函数
	/// <summary>创建指定名称的文件源解析器。</summary>
	/// <param name="name">解析器名称；空值表示默认路径解析器。</param>
	protected DeploymentResolverBase(string name) => this.Name = name ?? string.Empty;
	#endregion

	#region 公共属性
	/// <summary>获取解析器名称。</summary>
	public string Name { get; }
	#endregion

	#region 公共方法
	/// <summary>展开源文件，在计划阶段准备并登记部署操作。</summary>
	/// <param name="context">本次部署上下文。</param>
	/// <param name="deployment">源和目标已经求值的部署条目。</param>
	/// <param name="cancellation">用于取消源展开及准备工作的令牌。</param>
	/// <returns>表示计划准备工作的任务；此阶段不写入输出目录。</returns>
	public async Task ResolveAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation)
	{
		var sources = await this.GetSourcesAsync(context, deployment, cancellation);
		await PlanSourcesAsync(context, deployment, sources, cancellation, this);
	}
	#endregion

	#region 内部方法
	internal static async Task PlanSourcesAsync(DeploymentContext context, DeploymentEntry deployment, IEnumerable<DeploymentUtility.PathToken> sources, CancellationToken cancellation, DeploymentResolverBase resolver = null)
	{
		foreach(var source in sources)
		{
			cancellation.ThrowIfCancellationRequested();

			//普通路径允许缺少可选文件，模板源缺失则阻止执行。
			if(DeploymentPath.IsMissing(source.Path))
			{
				if(resolver is TemplateResolver)
					throw new FileNotFoundException(string.Format(Properties.Resources.Review_Missing, source.Path));

				context.Counter.Skip();

				var warning = string.Format(Properties.Resources.Review_MissingFile, source.Path);
				context.Deployer.Plan.Diagnostics.Add(warning);
				context.Deployer.Write(Utility.FormatOutput(Properties.Resources.Review_MissingFile, CommandOutletColor.DarkYellow, source.Path));

				continue;
			}

			if(!source.Exists())
				throw new FileNotFoundException(string.Format(Properties.Resources.Review_Missing, source.Path));

			var directory = string.IsNullOrEmpty(source.Suffix) ? deployment.Output.Path : Path.Combine(deployment.Output.Path, source.Suffix);
			context.Deployer.Session.Validate(directory);

			//.env 是变量输入，不随目录或通配符复制到部署输出。
			if(string.Equals(Path.GetFileName(source.Path), ".env", StringComparison.OrdinalIgnoreCase))
				continue;

			if(resolver is not TemplateResolver && Utility.IsDeploymentFile(source.Path) && !Deployer.Flag(context.Evaluator, Deployer.IGNOREDEPLOYMENTFILE_OPTION))
			{
				await context.Deployer.PlanManifestAsync(source.Path, directory, cancellation);
				continue;
			}

			if(Path.GetFileName(source.Path) == "_._" && new FileInfo(source.Path).Length == 0)
				continue;

			var sourceName = resolver?.GetFileName(source.Path) ?? Path.GetFileName(source.Path);
			var name = string.IsNullOrEmpty(deployment.Output.Name) ? sourceName : deployment.Output.Name;

			if(Utility.IsDirectory(name))
				name = Path.Combine(name, sourceName);

			var output = context.Deployer.Session.Validate(Path.Combine(directory, name));
			var operation = new DeploymentPlan.Operation
			{
				Kind = "Copy",
				Source = source.Path,
				Output = output,
				Manifest = deployment.Profile.FilePath,
				Package = source.Package,
				Framework = Utility.GetTargetFramework(context.Evaluator),
				Runtime = context.Evaluator.TryGetOption("platform", out var platform) && context.Evaluator.TryGetOption("architecture", out var architecture) ? $"{platform}-{architecture}" : null,
			};

			if(resolver != null)
				await resolver.PrepareAsync(context, operation, cancellation);

			context.Deployer.Session.Add(operation);
		}
	}
	#endregion

	#region 虚拟方法
	/// <summary>确定没有显式重命名的源文件对应的输出文件名。</summary>
	/// <param name="path">源文件路径。</param>
	/// <returns>默认输出文件名。</returns>
	protected virtual string GetFileName(string path) => Path.GetFileName(path);

	/// <summary>在计划阶段准备文件操作；不得写入部署目标。</summary>
	/// <param name="context">本次部署上下文。</param>
	/// <param name="operation">已确定源和目标的文件操作。</param>
	/// <param name="cancellation">用于取消准备工作的令牌。</param>
	/// <returns>表示准备工作的任务；默认不修改操作。</returns>
	protected virtual Task PrepareAsync(DeploymentContext context, DeploymentPlan.Operation operation, CancellationToken cancellation) => Task.CompletedTask;

	/// <summary>枚举部署项已经解析的源路径所匹配的文件。</summary>
	/// <param name="context">包含当前模板评估器与来源清单的部署上下文。</param>
	/// <param name="deployment">源和目标均已完成模板求值的部署项。</param>
	/// <param name="cancellation">用于取消文件枚举的令牌。</param>
	/// <returns>返回匹配文件序列的任务；序列在枚举时执行文件系统搜索。</returns>
	protected virtual Task<IEnumerable<DeploymentUtility.PathToken>> GetSourcesAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation) => Task.FromResult(DeploymentUtility.GetFiles(deployment.Source.FullPath, context.Evaluator, true, cancellation, Path.GetDirectoryName(deployment.Profile.FilePath)));
	#endregion
}
