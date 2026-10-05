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
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal static class Program
{
	#region 公共方法
	public static async Task<int> Main(string[] arguments)
	{
		if(arguments.Length == 1 && arguments[0] == "--protocol")
		{
			Console.WriteLine($"{{\"tool\":\"containerizer\",\"minimum\":{DeliveryPlan.ProtocolVersion},\"maximum\":{DeliveryPlan.ProtocolVersion}}}");
			return 0;
		}

		if(arguments.Length == 0 || arguments[0] is "--help" or "-h")
		{
			Console.WriteLine("containerizer install <directory|archive.tar.gz> [--name NAME] [--no-start]\ncontainerizer upgrade|prepare <archive.tar.gz> --name NAME\ncontainerizer uninstall --name NAME [--purge]\ncontainerizer start|stop|status --name NAME\ncontainerizer restart [COMPONENT] --name NAME\ncontainerizer recover --name NAME [--retry-migration VERSION]\ncontainerizer logs [COMPONENT] --name NAME [--tail COUNT] [--follow]\ncontainerizer list");
			return arguments.Length == 0 ? 2 : 0;
		}

		using var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += OnCancel;

		try
		{
			var options = ExecutorArguments.Parse(arguments);
			var store = new InstallationStore();
			await new InstallationManager(store, new DockerHost(store, new ProcessRunner())).ExecuteAsync(options, cancellation.Token);
			return 0;
		}
		catch(ContainerizationException failure) { Console.Error.WriteLine(failure.Message); return failure.Code; }
		catch(OperationCanceledException) { Console.Error.WriteLine(Properties.Resources.Executor_Interrupted_Message); return 7; }
		catch(Exception exception) { Console.Error.WriteLine(string.Format(Properties.Resources.Executor_Failure_Message, exception.GetType().Name)); return 4; }
		finally { Console.CancelKeyPress -= OnCancel; }

		void OnCancel(object sender, ConsoleCancelEventArgs args)
		{
			args.Cancel = true;
			cancellation.Cancel();
		}
	}
	#endregion
}
