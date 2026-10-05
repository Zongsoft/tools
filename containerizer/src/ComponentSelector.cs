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
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Zongsoft.Terminals;
using Zongsoft.Components;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ComponentSelector
{
	#region 公共方法
	public static string[] Choose(string[] candidates, bool multiple = false)
	{
		if(candidates.Length < 2)
			return candidates;

		for(int index = 0; index < candidates.Length; index++)
			Terminal.WriteLine(CommandOutletContent.Create(CommandOutletColor.DarkGray, $"{index + 1}: ").Append(CommandOutletStyles.Bold, CommandOutletColor.Cyan, candidates[index]));

		if(Console.IsInputRedirected)
			throw new ContainerizationException(2, Properties.Resources.MigrationInput_6_Message);

		Terminal.Write(CommandOutletStyles.Bold, CommandOutletColor.Yellow, multiple ? Properties.Resources.Selection_MigrationPrompt : Properties.Resources.Selection_PackagePrompt);
		var choices = (Terminal.Default.Input.ReadLine() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		if(choices.Length == 0 || !multiple && choices.Length != 1 || choices.Any(value => !int.TryParse(value, out var index) || index < 1 || index > candidates.Length))
			throw new ContainerizationException(2, Properties.Resources.MigrationInput_7_Message);

		return choices.Select(int.Parse).Distinct().Order().Select(index => candidates[index - 1]).ToArray();
	}
	#endregion
}
