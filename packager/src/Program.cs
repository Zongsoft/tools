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
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

internal class Program
{
	public static ITerminalExecutor Executor => Terminal.Console.Executor;

	public static async Task<int> Main(string[] args)
	{
		if(args is ["--help" or "-h"] or ["tar" or "deb" or "rpm", "--help" or "-h"])
		{
			Utility.WriteOutput(Console.Out, Utility.FormatHelp("dotnet-pack", "<tar|deb|rpm> [<input> ...] --platform:linux [options]",
				"[--source:<directory>] [--output:<directory>]",
				"[--name:<name>] [--edition:<edition>] [--version:<version>]",
				"[--compilation:Release|Debug] [--framework:<framework>] [--architecture:x64|arm64]",
				"[--exclude:<patterns>] [--overwrite:true|false]",
				"[--daemon:<name>] [--install-path:<directory>]",
				"[--web:nginx] [--migration:<name-or-path>]",
				"[--title:<text>] [--summary:<text>] [--description:<text>]",
				"[--dependencies:<dependencies>] [--<variable>:<value>]"));
			return 0;
		}

		if(args == null || args.Length == 0)
		{
			Terminal.WriteLine(CommandOutletColor.DarkRed, Properties.Resources.CommandLineEmpty_Message);
			return 2;
		}

		//初始化
		Executor.Root.Children.Clear();
		Executor.Root.Children.Add(new TarCommand());
		Executor.Root.Children.Add(new DebCommand());
		Executor.Root.Children.Add(new RpmCommand());
		var failed = false;
		Executor.Failed += OnFailed;

		try
		{
			//执行命令
			return await Executor.ExecuteAsync(Utility.FormatCommand(args[0], args.AsSpan(1))) == null || failed ? 1 : 0;
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
}
