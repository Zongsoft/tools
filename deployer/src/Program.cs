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
		using var cancellation = new CancellationTokenSource();

		void Handler(object sender, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
			cancellation.Cancel();
		}

		Console.CancelKeyPress += Handler;

		try
		{
			var command = CommandLine.Parse($"deploy {CommandLine.Get(args)}")[0];
			var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach(var option in command.Options)
				options[option.Name] = option.Value ?? "";

			var variables = Deployer.CreateVariables(options);
			var deployer = new Deployer(variables);
			var paths = command.Arguments.Count == 0 ? new[] { ".deploy" } : [.. command.Arguments];
			var result = await deployer.DeployManyAsync(paths, cancellation: cancellation.Token);

			Console.WriteLine(string.Format(Properties.Resources.Review_Summary, result.Successes, result.Skipped, result.Deleted, result.Failures));
			return result.Failures == 0 ? 0 : 1;
		}
		catch(OperationCanceledException)
		{
			return 130;
		}
		catch(Exception exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
		finally
		{
			Console.CancelKeyPress -= Handler;
		}
	}
}
