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

internal readonly struct Dependency
{
	#region 构造函数
	private Dependency(string name, string minimum = null, bool minimumIncluded = false, string maximum = null, bool maximumIncluded = false)
	{
		this.Name = name;
		this.Minimum = minimum;
		this.Maximum = maximum;
		this.MinimumIncluded = minimumIncluded;
		this.MaximumIncluded = maximumIncluded;
	}
	#endregion

	#region 公共属性
	public string Name { get; }
	public string Minimum { get; }
	public string Maximum { get; }
	public bool MinimumIncluded { get; }
	public bool MaximumIncluded { get; }
	public bool IsExact => this.Minimum != null && this.MinimumIncluded && this.MaximumIncluded && this.Minimum == this.Maximum;
	#endregion

	#region 解析方法
	public static Dependency[][] Parse(IEnumerable<string> values)
	{
		var groups = new List<Dependency[]>();

		foreach(var value in values ?? [])
		{
			foreach(var group in Split(value))
			{
				var alternatives = group.Split('|');
				var dependencies = new Dependency[alternatives.Length];

				for(int i = 0; i < alternatives.Length; i++)
					dependencies[i] = ParseItem(alternatives[i].Trim());

				groups.Add(dependencies);
			}
		}

		return groups.ToArray();
	}

	public static string[] Split(string value)
	{
		if(value == null)
			return [];

		var groups = new List<string>();
		var depth = 0;
		var start = 0;

		for(int i = 0; i < value.Length; i++)
		{
			var character = value[i];
			if(char.IsControl(character) && character != '\t')
				throw Invalid(value);

			if(character is '[' or '(')
				depth++;
			else if(character is ']' or ')')
			{
				if(--depth < 0)
					throw Invalid(value);
			}
			else if(depth == 0 && character is ',' or ';')
			{
				Add(value[start..i]);
				start = i + 1;
			}
		}

		if(depth != 0)
			throw Invalid(value);

		Add(value[start..]);
		return groups.ToArray();

		void Add(string text)
		{
			if(!string.IsNullOrWhiteSpace(text))
				groups.Add(text.Trim());
		}
	}

	private static Dependency ParseItem(string text)
	{
		var separator = -1;

		for(int i = 0; i < text.Length; i++)
		{
			if(text[i] != ':')
				continue;

			var suffix = text.AsSpan(i + 1).TrimStart();
			if(!suffix.IsEmpty && (suffix[0] is '[' or '(' || char.IsAsciiDigit(suffix[0])))
			{
				separator = i;
				break;
			}
		}

		var name = separator < 0 ? text : text[..separator].TrimEnd();
		if(!IsName(name))
			throw Invalid(text);

		if(separator < 0)
			return new(name);

		var range = text[(separator + 1)..].Trim();
		if(range[0] is not ('[' or '('))
		{
			ValidateVersion(range, text);
			return new(name, range, true);
		}

		if(range.Length < 3 || range[^1] is not (']' or ')'))
			throw Invalid(text);

		var body = range[1..^1];
		var comma = body.IndexOf(',');
		if(comma < 0)
		{
			var version = body.Trim();
			ValidateVersion(version, text);
			if(range[0] != '[')
				throw Invalid(text);

			return range[^1] == ']' ? new(name, version, true, version, true) : new(name, version, true);
		}

		var minimum = body[..comma].Trim();
		var maximum = body[(comma + 1)..].Trim();
		if(minimum.Length == 0)
		{
			if(range[0] != '(')
				throw Invalid(text);
		}
		else
			ValidateVersion(minimum, text);

		if(maximum.Length == 0)
		{
			if(range[^1] != ')')
				throw Invalid(text);
		}
		else
			ValidateVersion(maximum, text);

		return new(name, minimum.Length == 0 ? null : minimum, range[0] == '[', maximum.Length == 0 ? null : maximum, range[^1] == ']');
	}
	#endregion

	#region 校验方法
	private static bool IsName(string name)
	{
		if(string.IsNullOrEmpty(name) || !(char.IsAsciiLetterOrDigit(name[0]) || name[0] == '/') || name[^1] == ':')
			return false;

		var depth = 0;
		foreach(var character in name)
		{
			if(character == '(')
				depth++;
			else if(character == ')')
			{
				if(--depth < 0)
					return false;
			}
			else if(!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.' or '_' or '/' or ':'))
				return false;
		}

		return depth == 0;
	}

	private static void ValidateVersion(string version, string expression)
	{
		if(string.IsNullOrEmpty(version))
			throw Invalid(expression);

		foreach(var character in version)
		{
			if(!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '+' or '-' or '~' or '^' or '_' or ':'))
				throw Invalid(expression);
		}
	}

	private static InvalidDataException Invalid(string expression) => new(string.Format(Properties.Resources.DependencyInvalid_Message, expression));
	#endregion
}
