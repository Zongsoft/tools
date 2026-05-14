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
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Deployer;

internal class Program
{
	public static ITerminalExecutor Executor => Terminal.Console.Executor;

	public static async Task Main(string[] args)
	{
		//如果没有指定命令行参数并且当前目录下也没有默认部署文件则退出
		if(args == null || args.Length == 0)
		{
			if(HasDefaultDeploymentFile())
				args = [Deployer.DEFAULT_DEPLOYMENT_FILENAME];
			else
				return;
		}

		//初始化
		Executor.Root.Children.Clear();
		Executor.Root.Children.Add(new Deployer.DeployCommand());

		try
		{
			//执行命令
			await Executor.ExecuteAsync($"deploy {CommandLine.Get(args)}");
		}
		catch(Exception ex)
		{
			//打印异常消息
			Terminal.WriteLine(CommandOutletColor.DarkRed, ex.Message + Environment.NewLine + ex.StackTrace);
		}
	}

	private static bool HasDefaultDeploymentFile()
	{
		//判断当前目录下是否存在默认部署文件，如果不存在则打印错误信息并退出
		if(File.Exists(Deployer.DEFAULT_DEPLOYMENT_FILENAME))
			return true;

		Terminal.WriteLine(CommandOutletColor.DarkRed, Properties.Resources.MissingArguments_Message);
		return false;
	}
}
