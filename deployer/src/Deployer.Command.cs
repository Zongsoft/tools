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

using Zongsoft.Components;

namespace Zongsoft.Tools.Deployer;

partial class Deployer
{
	internal sealed class DeployCommand : CommandBase<CommandContext>
	{
		protected override async ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
		{
			//获取当前操作的变量集
			var variables = GetVariables(context);

			//创建一个部署文件路径的列表
			string[] paths = context.Arguments.IsEmpty ? [Deployer.DEFAULT_DEPLOYMENT_FILENAME] : [..context.Arguments];

			//修整部署文件的路径
			for(int i = 0; i < paths.Length; i++)
				paths[i] = Normalizer.Normalize(paths[i], variables);

			//剔除重复的部署文件
			if(paths.Length > 1)
				paths = paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();

			//创建部署器实例
			var deployer = new Deployer(variables);

			//打印开始部署信息
			deployer.StartDeployment(context.Options, paths);

			//创建一个计时器
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			//依次部署指定的部署文件
			for(int i = 0; i < paths.Length; i++)
			{
				//部署指定的文件
				var counter = await deployer.DeployAsync(paths[i], cancellation);

				//打印部署的结果信息
				deployer.Terminal.CompleteDeployment(counter.FilePath, counter, stopwatch.Elapsed, i >= paths.Length - 1);

				//重新计时
				stopwatch.Restart();
			}

			//停止计时器
			stopwatch.Stop();

			return paths;
		}

		static IDictionary<string, string> GetVariables(CommandContext context)
		{
			//加载系统环境变量
			var variables = Collections.DictionaryExtension.ToDictionary<string, string>(Environment.GetEnvironmentVariables(), StringComparer.OrdinalIgnoreCase);

			//将部署目录中的 appsettings.json 文件内容解析后加载到变量集
			AppSettingsUtility.Load(variables);
			//初始化 Nuget 相关的变量
			NugetUtility.Initialize(variables);

			//将命令选项添加到变量集
			foreach(var option in context.Options)
			{
				if(option.Value != null)
					variables[option.Key] = Normalizer.Normalize(option.Value.ToString(), variables);
			}

			return variables;
		}
	}
}
