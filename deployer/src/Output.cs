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
using System.Collections.Generic;

using Zongsoft.Services;
using Zongsoft.Terminals;

namespace Zongsoft.Tools.Deployer
{
	internal static class Output
	{
		public static void FileDeploySucceed(this ITerminal terminal, string source, string destination)
		{
			terminal.Write(CommandOutletColor.Blue, Properties.Resources.Tips_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.FileDeploySucceed_Message, source, destination));
		}

		public static void FileDeployFailed(this ITerminal terminal, string source, string destination)
		{
			terminal.Write(CommandOutletColor.Magenta, Properties.Resources.Warn_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkYellow, string.Format(Properties.Resources.FileDeployFailed_Message, source, destination));
		}

		public static void FileDeployFailed(this ITerminal terminal, string source, string destination, Overwrite overwrite)
		{
			var message = overwrite switch
			{
				Overwrite.Never => string.Format(Properties.Resources.FileDeployFailed_Never_Message, source, destination, overwrite),
				Overwrite.Newest => string.Format(Properties.Resources.FileDeployFailed_Newer_Message, source, destination, overwrite),
				_ => string.Format(Properties.Resources.FileDeployFailed_Message, source, destination),
			};

			terminal.Write(CommandOutletColor.Magenta, Properties.Resources.Warn_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkYellow, message);
		}

		public static void FileDeletedSucceed(this ITerminal terminal, string filePath)
		{
			terminal.Write(CommandOutletColor.Blue, Properties.Resources.Tips_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.FileDeleteSucceed_Message, filePath));
		}

		public static void FileDeletedFailed(this ITerminal terminal, string filePath)
		{
			terminal.Write(CommandOutletColor.Magenta, Properties.Resources.Warn_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkYellow, string.Format(Properties.Resources.FileDeleteFailed_Message, filePath));
		}

		public static void FileNotExists(this ITerminal terminal, string filePath)
		{
			if(string.IsNullOrEmpty(filePath))
				return;

			terminal.Write(CommandOutletColor.Magenta, Properties.Resources.Warn_Prompt);

			if(Utility.IsDeploymentFile(filePath))
				terminal.WriteLine(CommandOutletColor.DarkMagenta, string.Format(Properties.Resources.DeploymentFileNotExists_Message, filePath));
			else
				terminal.WriteLine(CommandOutletColor.DarkYellow, string.Format(Properties.Resources.FileNotExists_Message, filePath));
		}

		public static void UnspecifiedVariable(this ITerminal terminal, string variable)
		{
			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);
			terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.UnspecifiedVariable_Message, variable));
		}

		public static void UndefinedVariable(this ITerminal terminal, string variable, string expression) => UndefinedVariable(terminal, variable, expression, null, -1);
		public static void UndefinedVariable(this ITerminal terminal, string variable, string expression, string filePath, int lineNumber = -1)
		{
			if(lineNumber >= 0)
				filePath = $"{filePath} (#{lineNumber})";

			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);

			if(string.IsNullOrEmpty(filePath))
				terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.VariableUndefined_Message, variable, expression));
			else
				terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.VariableUndefinedInFile_Message, variable, expression, filePath));
		}

		public static void UndefinedResolver(this ITerminal terminal, string resolver, string expression) => UndefinedVariable(terminal, resolver, expression, null, -1);
		public static void UndefinedResolver(this ITerminal terminal, string resolver, string expression, string filePath, int lineNumber = -1)
		{
			if(lineNumber >= 0)
				filePath = $"{filePath} (#{lineNumber})";

			terminal.Write(CommandOutletColor.Red, Properties.Resources.Error_Prompt);

			if(string.IsNullOrEmpty(filePath))
				terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.ResolverUndefined_Message, resolver, expression));
			else
				terminal.WriteLine(CommandOutletColor.DarkRed, string.Format(Properties.Resources.ResolverUndefinedInFile_Message, resolver, expression, filePath));
		}

		public static void StartDeployment(this Deployer deployer, CommandExpression expression, string[] filePaths)
		{
			const string splash = @"
     _____                                ___ __
    /_   /  ____  ____  ____  ____ ____  / __/ /_
      / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
     / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
    /____/\____/_/ /_/\__  /____/\____/_/  \__/
                     /____/
";

			deployer.Terminal.WriteLine(splash);

			var content = CommandOutletContent
				.Create(CommandOutletColor.DarkMagenta, Properties.Resources.Deployment_List_Label)
				.AppendLine();

			for(int i = 0; i < filePaths.Length; i++)
			{
				content.Append(CommandOutletColor.DarkGray, $"\t[");
				content.Append(CommandOutletColor.DarkYellow, $"{i + 1}");
				content.Append(CommandOutletColor.DarkGray, $"] ");
				content.AppendLine(CommandOutletColor.DarkGreen, filePaths[i]);
			}

			content.AppendLine(CommandOutletColor.DarkMagenta, Properties.Resources.Deployment_Options_Label);

			foreach(var option in expression.Options)
			{
				content.Append(CommandOutletColor.DarkCyan, $"\t{option.Key}");
				content.Append(CommandOutletColor.DarkGray, ":");
				content.AppendLine(CommandOutletColor.DarkGreen, Normalizer.Normalize(option.Value, deployer.Variables));
			}

			IDictionary<string, string> variables;

			if(deployer.IsVerbosity(Verbosity.Detail))
			{
				variables = deployer.Variables;
			}
			else
			{
				variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

				if(deployer.Variables.TryGetValue("application", out var value))
					variables["application"] = value;
				if(deployer.Variables.TryGetValue("environment", out value))
					variables["environment"] = value;
				if(deployer.Variables.TryGetValue(NugetUtility.NUGET_SERVER_ENVIRONMENT, out value))
					variables[NugetUtility.NUGET_SERVER_ENVIRONMENT] = value;
				if(deployer.Variables.TryGetValue(NugetUtility.NUGET_PACKAGES_ENVIRONMENT, out value))
					variables[NugetUtility.NUGET_PACKAGES_ENVIRONMENT] = value;
			}

			content.AppendLine(CommandOutletColor.DarkMagenta, Properties.Resources.Environment_Variables_Label);

			foreach(var variable in variables)
			{
				content.Append(CommandOutletColor.DarkCyan, $"\t{variable.Key}");
				content.Append(CommandOutletColor.DarkGray, ":");
				content.AppendLine(CommandOutletColor.DarkGreen, variable.Value);
			}

			deployer.Terminal.WriteLine(content);
		}

		public static void CompleteDeployment(this ITerminal terminal, string filePath, DeploymentCounter counter, bool final)
		{
			var content = CommandOutletContent
				.Create(counter.Successes > 0 ? CommandOutletColor.DarkGreen : CommandOutletColor.DarkYellow, string.Format(Properties.Resources.DeploymentComplete_Message, filePath, counter.Total))
				.Append(Properties.Resources.DeploymentComplete_CountBegin)
				.Append(CommandOutletColor.Green, string.Format(Properties.Resources.DeploymentComplete_SucceedCount, counter.Successes))
				.Append(Properties.Resources.DeploymentComplete_CountSparator)
				.Append(CommandOutletColor.DarkRed, string.Format(Properties.Resources.DeploymentComplete_FailedCount, counter.Failures))
				.Append(Properties.Resources.DeploymentComplete_CountEnd);

			var count = 0;
			var message = CommandOutletContent.GetFullText(content);

			for(int i = 0; i < message.Length; i++)
				count += char.IsAscii(message[i]) ? 1 : 2;

			terminal.WriteLine(new string('-', count));
			terminal.WriteLine(content);
			terminal.WriteLine(new string('-', count));

			if(!final)
				terminal.WriteLine();
		}
	}
}