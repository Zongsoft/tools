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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Components;

namespace Zongsoft.Tools.Deployer;

internal class Program
{
	public static async Task<int> Main(string[] args)
	{
		if(args is ["--help" or "-h"])
		{
			Utility.WriteOutput(Console.Out, Utility.FormatHelp("dotnet deploy", "[<file.deploy> ...] [options]",
				"[--source:<directory>] [--output:<directory>]",
				"[--framework:<framework>] [--platform:<platform>] [--architecture:x64|arm64]",
				"[--overwrite:alway|never|newest] [--verbosity:quiet|normal|detail]",
				"[--dry-run] [--offline] [--explain] [--report:<file>]",
				"[--lockFile:<file>] [--locked] [--previous:<file>] [--prune]",
				"[--scheme:<name>] [--environment:<name>] [--network:<name>]",
				"[--<variable>:<value>]"));
			return 0;
		}

		using var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += Handler;

		try
		{
			var command = CommandLine.Parse(Utility.FormatCommand("deploy", args))[0];
			var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach(var option in command.Options)
				options[option.Name] = option.Value ?? "";

			var evaluator = Deployer.CreateEvaluator(options);
			var deployer = new Deployer(evaluator);
			using var timing = Utility.MeasureOutput(deployer.Write, Properties.Resources.Output_Elapsed, "dotnet-deploy");
			var paths = command.Arguments.Count == 0 ? new[] { ".deploy" } : [.. command.Arguments];
			var result = await deployer.DeployManyAsync(paths, cancellation: cancellation.Token);

			deployer.Write(Utility.FormatOutput(Properties.Resources.Review_Summary, result.Failures == 0 ? CommandOutletColor.DarkGreen : CommandOutletColor.DarkRed, result.Successes, result.Skipped, result.Deleted, result.Failures));
			return result.Failures == 0 ? 0 : 1;
		}
		catch(OperationCanceledException)
		{
			return 130;
		}
		catch(Exception exception)
		{
			Utility.WriteOutput(Console.Error, Utility.FormatError(exception));
			return 1;
		}
		finally
		{
			Console.CancelKeyPress -= Handler;
		}

		void Handler(object sender, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
			cancellation.Cancel();
		}
	}
}
