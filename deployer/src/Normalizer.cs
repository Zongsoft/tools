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
 * Copyright (C) 2015-2025 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer;

/// <summary>展开部署文本中的 $(name) 和 %name% 变量引用。</summary>
public static class Normalizer
{
	#region 公共方法
	public static string Normalize(string text, IDictionary<string, string> variables, Action<string> failure = null)
	{
		if(string.IsNullOrWhiteSpace(text))
			return string.Empty;

		ArgumentNullException.ThrowIfNull(variables);

		var raw = variables switch
		{
			VariableMap map => map.Raw,
			Dictionary<string, string> dictionary when dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase) => dictionary,
			_ => new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase),
		};
		var result = VariableEvaluator.Evaluate(text, raw, failure);

		if(!result.Succeed)
			throw new FormatException(string.Format(Properties.Resources.Review_UndefinedVariable, result.Variable));

		return result.Value;
	}
	#endregion
}
