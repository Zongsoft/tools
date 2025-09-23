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
 * Copyright (C) 2015-2024 Zongsoft Corporation <http://www.zongsoft.com>
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

using Zongsoft.Services;
using Zongsoft.Terminals;

namespace Zongsoft.Tools.Deployer
{
	internal static class NugetOutput
	{
		public static void IllegalArgument(this ITerminal terminal, string argument, string filePath, int lineNumber = -1)
		{
			if(lineNumber >= 0)
				filePath = $"{filePath} (#{lineNumber})";

			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.NuGet_IllegalArgument_Message, argument, filePath));
		}

		public static void NotFound(this ITerminal terminal, string package, string version)
		{
			if(string.IsNullOrEmpty(version))
				version = Properties.Resources.Latest;

			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.NuGet_NotFound_Message, package, version));
		}

		public static void UnmatchPackage(this ITerminal terminal, string package, string framework)
		{
			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.NoPackageForFramework, package, framework));
		}

		public static void DownloadFailed(this ITerminal terminal, string package, string version)
		{
			if(string.IsNullOrEmpty(version))
				version = Properties.Resources.Latest;

			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.NuGet_DownloadFailed_Message, package, version));
		}
	}
}