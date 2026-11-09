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
