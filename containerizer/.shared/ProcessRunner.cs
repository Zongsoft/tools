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
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed class ProcessRunner : IProcessRunner
{
	#region 公共方法
	public async Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation)
	{
		var startInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			WorkingDirectory = directory ?? Environment.CurrentDirectory,
		};

		foreach(var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process = new Process { StartInfo = startInfo };
		process.Start();

		try
		{
			var output = ForwardLinesAsync(process.StandardOutput, Console.Out);
			var error = ForwardLinesAsync(process.StandardError, Console.Error);

			await process.WaitForExitAsync(cancellation);
			await Task.WhenAll(output, error);

			return process.ExitCode;
		}
		finally
		{
			if(!process.HasExited)
				process.Kill(true);
		}

		async Task ForwardLinesAsync(StreamReader input, TextWriter output)
		{
			while(await input.ReadLineAsync(cancellation) is { } line)
				await output.WriteLineAsync(line.AsMemory(), cancellation);
		}
	}

	public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

		var startInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			WorkingDirectory = directory ?? Environment.CurrentDirectory,
		};

		foreach(var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process = new Process { StartInfo = startInfo };

		try
		{
			process.Start();

			var output = ReadOutputAsync(process.StandardOutput, timeout.Token);
			var error = ReadOutputAsync(process.StandardError, timeout.Token);
			await process.WaitForExitAsync(timeout.Token);

			return new(process.ExitCode, await output, await error);
		}
		catch(OperationCanceledException)
		{
			try
			{
				if(!process.HasExited)
					process.Kill(true);
			}
			catch(InvalidOperationException) { }

			throw;
		}
		catch(System.ComponentModel.Win32Exception exception)
		{
			throw new ContainerizationException(3, string.Format(Properties.Resources.ProcessRunner_1_Message, executable), exception);
		}
	}
	#endregion

	#region 私有方法
	private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellation)
	{
		var text = new StringBuilder();
		var buffer = new char[8192];
		int count;

		while((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
		{
			// Drain both pipes even after the bounded diagnostic buffer fills.
			if(text.Length < 8 * 1024 * 1024)
				text.Append(buffer, 0, Math.Min(count, 8 * 1024 * 1024 - text.Length));
		}

		return text.ToString();
	}
	#endregion
}
