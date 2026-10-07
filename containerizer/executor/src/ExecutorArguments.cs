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
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal sealed class ExecutorArguments
{
	#region 公共属性
	public string Command { get; private set; }
	public string Name { get; private set; }
	public string BundlePath { get; private set; }
	public string Component { get; private set; }
	public string SourceBundlePath { get; private set; }
	public string RetryMigrationVersion { get; private set; }
	public bool NoStart { get; private set; }
	public bool Purge { get; private set; }
	public bool Follow { get; private set; }
	public int TailLines { get; private set; } = 100;
	#endregion

	#region 公共方法
	public static ExecutorArguments Parse(string[] arguments)
	{
		if(arguments.Length == 0)
			throw new ContainerizationException(2, Properties.Resources.Arguments_1_Message);

		var result = new ExecutorArguments { Command = arguments[0] };
		if(result.Command is not ("install" or "upgrade" or "prepare" or "uninstall" or "start" or "stop" or "restart" or "recover" or "list" or "status" or "logs"))
			throw new ContainerizationException(2, Properties.Resources.Arguments_2_Message);

		var positionals = new List<string>();
		var options = new HashSet<string>(StringComparer.Ordinal);

		for(int index = 1; index < arguments.Length; index++)
		{
			var argument = arguments[index];
			if(!argument.StartsWith('-'))
			{
				positionals.Add(argument);
				continue;
			}

			if(!options.Add(argument))
				throw new ContainerizationException(2, Properties.Resources.Arguments_3_Message);

			switch(argument)
			{
				case "--name":
					result.Name = ReadOptionValue();
					break;
				case "--from" when result.Command == "uninstall":
					result.SourceBundlePath = ReadOptionValue();
					break;
				case "--no-start" when result.Command is "install" or "upgrade":
					result.NoStart = true;
					break;
				case "--purge" when result.Command == "uninstall":
					result.Purge = true;
					break;
				case "--retry-migration" when result.Command == "recover":
					result.RetryMigrationVersion = ReadOptionValue();
					break;
				case "--follow" when result.Command == "logs":
					result.Follow = true;
					break;
				case "--tail" when result.Command == "logs":
					if(!int.TryParse(ReadOptionValue(), out var count) || count < 0)
						throw new ContainerizationException(2, Properties.Resources.Arguments_4_Message);
					result.TailLines = count;
					break;
				default:
					throw new ContainerizationException(2, Properties.Resources.Arguments_5_Message);
			}

			string ReadOptionValue()
			{
				if(++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith('-'))
					throw new ContainerizationException(2, Properties.Resources.Arguments_6_Message);

				return arguments[index];
			}
		}

		if(result.Command is "install" or "upgrade" or "prepare")
		{
			if(positionals.Count != 1)
				throw new ContainerizationException(2, Properties.Resources.Arguments_7_Message);
			result.BundlePath = positionals[0];
		}
		else if(result.Command is "logs" or "restart")
		{
			if(positionals.Count > 1)
				throw new ContainerizationException(2, Properties.Resources.Arguments_8_Message);
			result.Component = positionals.Count == 1 ? positionals[0] : null;
		}
		else if(positionals.Count != 0)
			throw new ContainerizationException(2, Properties.Resources.Arguments_9_Message);

		if(result.Command == "list" && result.Name != null)
			throw new ContainerizationException(2, Properties.Resources.Arguments_10_Message);
		if(result.Command is not ("list" or "install") && result.Name == null && result.SourceBundlePath == null)
			throw new ContainerizationException(2, Properties.Resources.Arguments_11_Message);
		if(result.Name != null)
			DeliveryBundle.ValidateIdentity(result.Name);

		return result;
	}
	#endregion
}
