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
using System.IO;
using System.Collections.Generic;

using Zongsoft.Collections;

namespace Zongsoft.Tools.Deployer;

partial class Deployer
{
	#region 变量加载
	public static IDictionary<string, string> CreateVariables(IDictionary<string, string> options, string currentDirectory = null)
	{
		currentDirectory ??= Environment.CurrentDirectory;

		var variables = Environment.GetEnvironmentVariables().ToDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var arguments = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);

		if(!arguments.TryGetValue(DESTINATION_OPTION, out var target) && !variables.TryGetValue(DESTINATION_OPTION, out target))
			target = currentDirectory;

		target = Normalizer.Normalize(target, variables, name => throw new FormatException(string.Format(Properties.Resources.Review_UndefinedVariable, name)));
		target = Path.GetFullPath(target, currentDirectory);
		AppSettingsUtility.Load(variables, target);
		NugetUtility.Initialize(variables);

		foreach(var option in arguments)
			variables[option.Key] = Normalizer.Normalize(option.Value ?? "", variables);

		variables[DESTINATION_OPTION] = target;

		return variables;
	}
	#endregion
}
