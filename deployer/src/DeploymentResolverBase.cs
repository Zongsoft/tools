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
 * Copyright (C) 2015-2024 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer
{
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

			//由于源路径中可能含有通配符，因此必须查找匹配的文件集
			foreach(var sourceFile in sources)
			{
				var destinationFile = Path.Combine(GetDestinationDirectory(deployment.Destination.Path, sourceFile.Suffix), string.IsNullOrEmpty(deployment.Destination.Name) ? Path.GetFileName(sourceFile.Path) : deployment.Destination.Name);

				if(!sourceFile.Exists())
				{
					//累加文件复制失败计数器
					context.Counter.Fail();

					//打印文件不存在的消息（如果是静默模式则不打印提示消息）
					if(!context.IsVerbosity(Verbosity.Quiet))
						context.Deployer.Terminal.FileNotExists(sourceFile.Path);

					continue;
				}

				//如果指定要拷贝的源文件是一个部署文件
				if(Utility.IsDeploymentFile(sourceFile.Path))
				{
					//如果没有指定忽略处理子部署文件，则进行子部署文件的递归处理
					if(!context.Variables.ContainsKey(Deployer.IGNOREDEPLOYMENTFILE_OPTION))
					{
						var counter = await context.Deployer.DeployAsync(sourceFile.Path, GetDestinationDirectory(deployment.Destination.Path, sourceFile.Suffix), cancellation);
						context.Count(counter);
						continue;
					}
				}

				//获取覆盖选项
				var overwrite = context.Variables.TryGetValue(Deployer.OVERWRITE_OPTION, out var variable) && Enum.TryParse<Overwrite>(variable, true, out var value) ? value : Overwrite.Alway;

				//执行文件复制
				if(DeploymentUtility.CopyFile(sourceFile.Path, destinationFile, overwrite))
				{
					context.Counter.Success();

					if(context.IsVerbosity(Verbosity.Detail))
						context.Deployer.Terminal.FileDeploySucceed(sourceFile.Path, destinationFile);
				}
				else
				{
					context.Counter.Fail();

					if(!context.IsVerbosity(Verbosity.Quiet))
						context.Deployer.Terminal.FileDeployFailed(sourceFile.Path, destinationFile, overwrite);
				}
			}

			static string GetDestinationDirectory(string root, string suffix) => string.IsNullOrEmpty(suffix) ? root : Path.Combine(root, suffix);
		}
		#endregion

		#region 虚拟方法
		protected virtual Task<IEnumerable<DeploymentUtility.PathToken>> GetSourcesAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation) => Task.FromResult(DeploymentUtility.GetFiles(deployment.Source.FullPath, context.Variables));
		#endregion
	}
}