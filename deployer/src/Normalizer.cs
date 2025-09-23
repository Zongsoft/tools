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
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Deployer
{
	public static class Normalizer
	{
		#region 常量定义
		//变量解析的正则组名称
		private const string REGEX_VARIABLE_NAME = "name";
		//变量解析的正则表达式（变量包括两种语法：$(variable) 或 %variable%）
		private static readonly Regex _variableRegex = new(@"(?<opt>\$\((?<name>\w+)\))|(?<env>\%(?<name>\w+)\%)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
		#endregion

		#region 公共方法
		public static string Normalize(string text, IDictionary<string, string> variables, Action<string> failure = null)
		{
			if(string.IsNullOrWhiteSpace(text))
				return string.Empty;

			return _variableRegex.Replace(text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), match =>
			{
				if(match.Success && match.Groups.TryGetValue(REGEX_VARIABLE_NAME, out var group))
				{
					if(variables.TryGetValue(group.Value, out var value))
						return value;

					failure?.Invoke(group.Value);
				}

				return null;
			});
		}
		#endregion
	}
}