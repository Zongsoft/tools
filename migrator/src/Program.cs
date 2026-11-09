/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.Threading.Tasks;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Migrator;

internal class Program
{
	#region 公共属性
	public static ITerminalExecutor Executor => Terminal.Console.Executor;
	#endregion

	#region 入口方法
	public static async Task<int> Main(string[] args)
	{
		if(args is ["--help" or "-h"])
		{
			Utility.WriteOutput(Console.Out, Utility.FormatHelp("dotnet-migrate", "<input.migration> ... --name:<name> --platform:linux|windows [options]",
				"[--version:<version-or-path>] [--edition:<edition>]",
				"[--architecture:x64|arm64] [--output:<directory>] [--overwrite:true|false]",
				"[--title:<text>] [--summary:<text>] [--description:<text>]",
				"[--<variable>:<value>]"));
			return 0;
		}

		if(args == null || args.Length == 0)
		{
			Terminal.WriteLine(CommandOutletColor.DarkRed, Properties.Resources.CommandLineEmpty_Message);
			return 2;
		}

		//初始化
		Executor.Root.Children.Clear();
		Executor.Root.Children.Add(new MigrateCommand());
		var failed = false;
		Executor.Failed += OnFailed;

		try
		{
			//执行命令
			return await Executor.ExecuteAsync(GetCommandLine(args)) == null || failed ? 1 : 0;
		}
		catch(Exception ex)
		{
			//打印异常消息
			Terminal.WriteLine(Utility.FormatError(ex, includeStackTrace: true));
			return 1;
		}
		finally { Executor.Failed -= OnFailed; }

		void OnFailed(object sender, CommandExecutorFailureEventArgs e)
		{
			failed = true;
			Utility.HandleFailure(e);
		}
	}
	#endregion

	#region 内部方法
	internal static string GetCommandLine(string[] arguments) => Utility.FormatCommand("migrate", arguments);
	#endregion
}
