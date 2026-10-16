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

namespace Zongsoft.Tools.Deployer;

/// <summary>按名称选择本地路径、模板、NuGet 或删除解析器；空名称使用默认路径解析器。</summary>
public static class DeploymentResolverManager
{
	/// <summary>按不区分大小写的名称查询解析器。</summary>
	/// <param name="name">解析器名称；null、空字符串或纯空白选择默认路径解析器。</param>
	/// <returns>匹配的解析器；未知名称返回 null。</returns>
	public static IDeploymentResolver GetResolver(string name)
	{
		if(string.IsNullOrWhiteSpace(name))
			return DefaultResolver.Instance;

		return name?.ToLowerInvariant() switch
		{
			"nuget" => NugetResolver.Instance,
			"path" => DefaultResolver.Instance,
			"template" => TemplateResolver.Instance,
			"delete" or "remove" => DeleteResolver.Instance,
			_ => null,
		};
	}

	/// <summary>将删除条目登记为待执行操作，实际删除由部署执行阶段完成。</summary>
	public class DeleteResolver : IDeploymentResolver
	{
		#region 单例字段
		/// <summary>获取删除解析器实例。</summary>
		public static readonly DeleteResolver Instance = new();
		#endregion

		#region 私有构造
		private DeleteResolver() { }
		#endregion

		#region 公共属性
		/// <summary>获取删除解析器的名称。</summary>
		public string Name => "Delete";
		#endregion

		#region 公共方法
		/// <summary>校验删除目标并将删除操作加入部署计划。</summary>
		/// <param name="context">本次部署上下文。</param>
		/// <param name="deployment">已经求值的删除条目。</param>
		/// <param name="cancellation">部署取消令牌。</param>
		/// <returns>表示计划登记完成的任务，不立即删除文件。</returns>
		public Task ResolveAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation)
		{
			cancellation.ThrowIfCancellationRequested();

			if(!string.IsNullOrEmpty(deployment.Output.Name))
				throw new FormatException(string.Format(Properties.Resources.Review_InvalidOption, "delete", "output"));

			var path = context.Deployer.Session.Validate(Path.Combine(deployment.Output.Path, deployment.Source.Name));
			context.Deployer.Session.Add(new DeploymentPlan.Operation
			{
				Kind = "Delete",
				Output = path,
				Manifest = deployment.Profile.FilePath,
			});

			return Task.CompletedTask;
		}
		#endregion
	}

	/// <summary>使用本地文件和目录作为部署源的默认解析器。</summary>
	public class DefaultResolver : DeploymentResolverBase
	{
		#region 单例字段
		/// <summary>获取默认路径解析器实例。</summary>
		public static readonly DefaultResolver Instance = new();
		#endregion

		#region 私有构造
		private DefaultResolver() : base(string.Empty) { }
		#endregion
	}
}
