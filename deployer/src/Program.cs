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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Common;
using Zongsoft.Services;

namespace Zongsoft.Tools.Deployer
{
	internal class Program
	{
		public static async Task Main(string[] args)
		{
			try
			{
				//使用当前命令行参数构造一个命令表达式
				var expression = CommandExpression.Parse("deployer " + string.Join(" ", args ?? Array.Empty<string>()));

				//如果没有指定命令行参数并且当前目录下也没有默认部署文件则退出
				if(expression.Arguments.Length == 0 && !HasDefaultDeploymentFile())
					return;

				//创建部署器类的实例
				var deployer = new Deployer(Zongsoft.Terminals.ConsoleTerminal.Instance, expression.Options);

				//创建一个部署文件路径的列表
				var paths = expression.Arguments.Length > 0 ? expression.Arguments : new[] { Deployer.DEFAULT_DEPLOYMENT_FILENAME };

				//修整部署文件的路径
				for(int i = 0; i < paths.Length; i++)
					paths[i] = Normalizer.Normalize(paths[i], deployer.Variables);

				//剔除重复的部署文件
				if(paths.Length > 1)
					paths = paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();

				//打印开始部署信息
				deployer.StartDeployment(expression, paths);

				//依次部署指定的部署文件
				for(int i = 0; i < paths.Length; i++)
				{
					//部署指定的文件
					var counter = await deployer.DeployAsync(paths[i]);

					//打印部署的结果信息
					deployer.Terminal.CompleteDeployment(counter.FilePath, counter, i >= paths.Length - 1);
				}
			}
			catch(Terminals.TerminalCommandExecutor.ExitException ex)
			{
				//设置控制台前景色为“红色”
				Console.ForegroundColor = ConsoleColor.Red;

				//打印异常消息
				if(!string.IsNullOrEmpty(ex.Message))
					Console.Error.WriteLine(ex.Message);

				//重置控制台的前景色
				Console.ResetColor();
			}
			catch(Exception ex)
			{
				//设置控制台前景色为“红色”
				Console.ForegroundColor = ConsoleColor.Red;

				//打印异常消息
				Console.Error.WriteLine(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace);

				//重置控制台的前景色
				Console.ResetColor();

				throw;
			}
		}

		private static bool HasDefaultDeploymentFile()
		{
			//判断当前目录下是否存在默认部署文件，如果不存在则打印错误信息并退出
			if(File.Exists(Path.Combine(Environment.CurrentDirectory, Deployer.DEFAULT_DEPLOYMENT_FILENAME)))
				return true;

			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine(Properties.Resources.MissingArguments_Message);
			Console.ResetColor();

			return false;
		}
	}
}
