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
using System.Globalization;
using System.Text.RegularExpressions;

using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer;

internal static partial class Output
{
	#region 公共方法
	public static CommandOutletContent Message(string format, params string[] arguments) => Message(format, null, arguments);
	public static CommandOutletContent Message(string format, CommandOutletColor? color, params string[] arguments)
	{
		CommandOutletContent content = null;
		var position = 0;

		foreach(Match match in ParameterRegex().Matches(format))
		{
			content = Append(content, format[position..match.Index], color);
			content = Append(content, arguments[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)],
				color == CommandOutletColor.Magenta ? CommandOutletColor.Yellow : CommandOutletColor.Cyan, CommandOutletStyles.Bold);
			position = match.Index + match.Length;
		}

		return Append(content, format[position..], color)?.First ?? CommandOutletContent.Create();
	}

	public static CommandOutletContent Syntax(string text)
	{
		CommandOutletContent content = null;
		var position = 0;

		foreach(Match match in SyntaxRegex().Matches(text))
		{
			content = Append(content, text[position..match.Index]);
			content = Append(content, match.Value, match.Value.StartsWith("--", StringComparison.Ordinal) ? CommandOutletColor.Cyan : CommandOutletColor.Green);
			position = match.Index + match.Length;
		}

		return Append(content, text[position..])?.First ?? CommandOutletContent.Create();
	}
	#endregion

	#region 私有方法
	private static CommandOutletContent Append(CommandOutletContent content, string text, CommandOutletColor? color = null, CommandOutletStyles style = CommandOutletStyles.None)
	{
		if(string.IsNullOrEmpty(text))
			return content;

		content = content == null ? CommandOutletContent.Create(style, text) : content.Append(style, text);
		content.ForegroundColor = color;
		return content;
	}

	[GeneratedRegex(@"\{(\d+)\}")]
	private static partial Regex ParameterRegex();
	[GeneratedRegex(@"--[\w-]+|<[^>]+>|(?<=:)[a-z0-9|]+")]
	private static partial Regex SyntaxRegex();
	#endregion
}
