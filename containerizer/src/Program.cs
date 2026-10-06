/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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
			ContainerizeCommand.Help();

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
		catch(ContainerizationException failure) { Terminal.Default.Error.WriteLine(failure.Message); return failure.Code; }
		catch(Exception exception) { Terminal.Default.Error.WriteLine(exception.Message); return 2; }
		finally { executor.Failed -= OnFailed; }

		void OnFailed(object sender, CommandExecutorFailureEventArgs args) => code = args.Exception is ContainerizationException failure ? failure.Code : args.Exception is OperationCanceledException ? 130 : 2;
	}
	#endregion
}
