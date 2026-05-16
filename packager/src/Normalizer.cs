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
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager;

public class Normalizer
{
	#region 常量定义
	//变量解析的正则组名称
	private const string REGEX_VARIABLE_NAME = "name";
	//变量解析的正则表达式（变量包括两种语法：$(variable) 或 %variable%）
	private static readonly Regex _variableRegex = new(@"(?<opt>\$\((?<name>\w+)\))|(?<env>\%(?<name>\w+)\%)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
	#endregion

	#region 公共属性
	private static Variables _variables;
	public static Variables Variables => _variables;
	#endregion

	#region 初始方法
	public static void Initialize(IReadOnlyDictionary<string, string> variables)
	{
		ArgumentNullException.ThrowIfNull(variables);

		_variables = new Variables();

		foreach(var variable in variables)
			_variables[variable.Key] = Normalize(variable.Value, variables);

		_variables[Variables.SUMMARY] = NormalizeFile(_variables[Variables.SUMMARY]);
		_variables[Variables.DESCRIPTION] = NormalizeFile(_variables[Variables.DESCRIPTION]);

		_variables[Variables.ScriptVariable.INSTALLING] = NormalizeFile(_variables[Variables.ScriptVariable.INSTALLING]);
		_variables[Variables.ScriptVariable.INSTALLED] = NormalizeFile(_variables[Variables.ScriptVariable.INSTALLED]);
		_variables[Variables.ScriptVariable.UNINSTALLING] = NormalizeFile(_variables[Variables.ScriptVariable.UNINSTALLING]);
		_variables[Variables.ScriptVariable.UNINSTALLED] = NormalizeFile(_variables[Variables.ScriptVariable.UNINSTALLED]);
	}
	#endregion

	#region 公共方法
	public static bool TryNormalize(string text, out string result)
	{
		var normalized = Normalize(text, _variables);
		if(normalized)
		{
			result = normalized.Value;
			return true;
		}

		Dumper.UndefinedVariable(normalized.Value, text);
		result = null;
		return false;
	}

	public static string Normalize(string text, string fallback = null)
	{
		if(string.IsNullOrWhiteSpace(text))
			return fallback;

		if(!TryNormalize(text, out var result))
			return fallback;

		return string.IsNullOrWhiteSpace(result) ? fallback : result.Trim();
	}

	public static Result Normalize(string text, IReadOnlyDictionary<string, string> variables)
	{
		if(string.IsNullOrWhiteSpace(text))
			return Result.Success(string.Empty);

		variables ??= _variables ?? throw new InvalidOperationException($"The Normalizer has not been initialized yet.");

		try
		{
			var value = _variableRegex.Replace(text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), match =>
			{
				if(match.Success && match.Groups.TryGetValue(REGEX_VARIABLE_NAME, out var group))
				{
					if(variables.TryGetValue(group.Value, out var value))
						return value;

					throw new NormalizerException(group.Value);
				}

				return null;
			});

			return Result.Success(value);
		}
		catch(NormalizerException ex)
		{
			return Result.Failure(ex.Variable);
		}
	}
	#endregion

	internal static string NormalizeFile(string text)
	{
		if(string.IsNullOrWhiteSpace(text))
			return null;

		if(!TryNormalize(text, out var result))
			return null;

		return File.Exists(result) ? File.ReadAllText(result) : result;
	}

	internal static string[] NormalizeList(string text)
	{
		if(string.IsNullOrWhiteSpace(text))
			return [];

		return TryNormalize(text, out var result) ?
			result.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
	}

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

	#region 私有子类
	private sealed class NormalizerException : Exception
	{
		public NormalizerException(string variable) => this.Variable = variable;
		public string Variable { get; }
	}
	#endregion
}
