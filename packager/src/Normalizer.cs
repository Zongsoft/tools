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
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Packager;

public static class Normalizer
{
	#region 公共方法
	public static string Normalize(string text, IReadOnlyDictionary<string, string> variables, string fallback)
	{
		if(string.IsNullOrWhiteSpace(text))
			return fallback;

		var result = Normalize(text, variables);
		if(!result.Succeed)
			throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed_Message, result.Value));

		return string.IsNullOrWhiteSpace(result.Value) ? fallback : result.Value.Trim();
	}

	public static Result Normalize(string text, IReadOnlyDictionary<string, string> variables)
	{
		if(string.IsNullOrWhiteSpace(text))
			return Result.Success(string.Empty);

		ArgumentNullException.ThrowIfNull(variables);
		if(variables is Variables collection)
			variables = collection.Raw;

		var result = VariableExpander.Expand(text, variables);
		return result.Succeed ? Result.Success(result.Value) : Result.Failure(result.Variable);
	}

	#endregion

	#region 嵌套结构
	public readonly struct Result
	{
		private Result(string value, bool succeed)
		{
			this.Value = value;
			this.Succeed = succeed;
		}

		public readonly string Value;
		public readonly bool Succeed;

		public override string ToString() => this.Value;
		public static implicit operator bool(Result result) => result.Succeed;
		public static implicit operator string(Result result) => result.Value;

		public static Result Failure(string value) => new(value, false);
		public static Result Success(string value) => new(value, true);
	}
	#endregion
}
