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
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class Program
{
	#region 公共方法
	public static async Task<int> Main(string[] arguments)
	{
		if(arguments.Length == 0 || arguments[0] is "--help" or "-h" || arguments.Length == 2 && arguments[1] is "--help" or "-h")
		{
			ContainerizeCommand.ShowHelp();

			return arguments.Length == 0 ? 2 : 0;
		}

		var executor = Terminal.Console.Executor;
		executor.Root.Children.Clear();
		executor.Root.Children.Add(new ContainerizeCommand());
		executor.Root.Children.Add(new PlanCommand());
		executor.Root.Children.Add(new MakeCommand());
		executor.Root.Children.Add(new RunCommand());

		var code = 0;
		executor.Failed += OnFailed;

		try
		{
			var command = arguments[0] is "plan" or "make" or "run" ? arguments[0] : "containerize";
			await executor.ExecuteAsync(Utility.FormatCommand(command, command == "containerize" ? arguments.AsSpan() : arguments.AsSpan(1)));
			return code;
		}
		catch(ContainerizationException failure) { Utility.WriteError(Utility.FormatError(failure)); return failure.Code; }
		catch(Exception exception) { Utility.WriteError(Utility.FormatError(exception)); return 2; }
		finally { executor.Failed -= OnFailed; }

		void OnFailed(object sender, CommandExecutorFailureEventArgs args)
		{
			code = args.Exception is ContainerizationException failure ? failure.Code : args.Exception is OperationCanceledException ? 130 : 2;
			Utility.HandleFailure(args);
		}
	}
	#endregion
}
