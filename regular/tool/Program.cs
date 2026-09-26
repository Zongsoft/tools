/*
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
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;

namespace Zongsoft.Regular.Tool;

/// <summary>Locates and starts the bundled Windows Forms application.</summary>
internal static class Program
{
	public static int Main(string[] args)
	{
		if(!OperatingSystem.IsWindows())
		{
			Console.Error.WriteLine("Zongsoft Regular requires Windows.");
			return 1;
		}

		var executable = Path.Combine(AppContext.BaseDirectory, "gui", "Zongsoft.Tools.Regular.exe");

		if(!File.Exists(executable))
		{
			Console.Error.WriteLine($"Regular GUI executable was not found: {executable}");
			return 1;
		}

		var startInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			WorkingDirectory = Environment.CurrentDirectory,
		};

		foreach(var argument in args)
			startInfo.ArgumentList.Add(argument);

		try
		{
			using var process = Process.Start(startInfo);

			if(process is null)
			{
				Console.Error.WriteLine("Regular GUI could not be started.");
				return 1;
			}

			return 0;
		}
		catch(Exception exception) when(exception is Win32Exception or InvalidOperationException or IOException)
		{
			Console.Error.WriteLine($"Regular GUI could not be started: {exception.Message}");
			return 1;
		}
	}
}
