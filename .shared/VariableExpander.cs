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
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools;

/// <summary>递归展开变量值，不修改原始变量集合。</summary>
internal static class VariableExpander
{
	#region 常量定义
	internal const int MAXIMUM_DEPTH = 64;
	private static readonly Regex _pattern = new(@"(?<opt>\$\((?<name>[\w.\[\]-]+)\))|(?<env>\%(?<name>[\w.\[\]-]+)\%)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	#endregion

	#region 公共方法
	internal static Result Expand(string text, IReadOnlyDictionary<string, string> variables, Action<string> missing = null)
	{
		if(string.IsNullOrWhiteSpace(text))
			return Result.Success(string.Empty);

		ArgumentNullException.ThrowIfNull(variables);

		if(variables is not Dictionary<string, string> dictionary || !dictionary.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
		{
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach(var variable in variables)
				values[variable.Key] = variable.Value;

			variables = values;
		}

		var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			return Result.Success(ExpandValue(text));
		}
		catch(ResolutionException exception)
		{
			return Result.Failure(exception.Name, exception.Reason);
		}

		string ExpandValue(string value) => _pattern.Replace(value ?? string.Empty, match =>
		{
			var name = match.Groups["name"].Value;

			if(!variables.TryGetValue(name, out var replacement))
			{
				missing?.Invoke(name);
				throw new ResolutionException(name, FailureReason.Missing);
			}

			if(active.Count >= MAXIMUM_DEPTH)
				throw new ResolutionException(name, FailureReason.Depth);

			if(!active.Add(name))
				throw new ResolutionException(name, FailureReason.Cycle);

			try
			{
				return ExpandValue(replacement);
			}
			finally
			{
				active.Remove(name);
			}
		});
	}
	#endregion

	#region 嵌套类型
	internal enum FailureReason { None, Missing, Cycle, Depth }

	internal readonly struct Result
	{
		private Result(string value, string variable, FailureReason reason)
		{
			this.Value = value;
			this.Variable = variable;
			this.Reason = reason;
		}

		internal string Value { get; }
		internal string Variable { get; }
		internal FailureReason Reason { get; }
		internal bool Succeed => this.Reason == FailureReason.None;

		internal static Result Success(string value) => new(value, null, FailureReason.None);
		internal static Result Failure(string name, FailureReason reason) => new(null, name, reason);
	}

	private sealed class ResolutionException(string name, FailureReason reason) : Exception
	{
		internal string Name { get; } = name;
		internal FailureReason Reason { get; } = reason;
	}
	#endregion
}

/// <summary>提供可变变量集合，读取时根据当前原始值展开。</summary>
internal sealed class VariableMap : IDictionary<string, string>
{
	#region 成员字段
	private readonly Dictionary<string, string> _raw;
	private readonly Func<string, string> _resolve;
	#endregion

	#region 构造函数
	internal VariableMap(Dictionary<string, string> raw, Func<string, string> resolve)
	{
		_raw = raw ?? throw new ArgumentNullException(nameof(raw));
		_resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
	}
	#endregion

	#region 公共属性
	public int Count => _raw.Count;
	public bool IsReadOnly => false;
	public ICollection<string> Keys => _raw.Keys;
	public ICollection<string> Values
	{
		get
		{
			var values = new List<string>(_raw.Count);

			foreach(var value in _raw.Values)
				values.Add(this.Resolve(value));

			return values;
		}
	}

	public string this[string key]
	{
		get => this.Resolve(_raw[key]);
		set => _raw[key] = value;
	}
	#endregion

	#region 内部属性
	internal IReadOnlyDictionary<string, string> Raw => _raw;
	#endregion

	#region 公共方法
	public void Add(string key, string value) => _raw.Add(key, value);
	public bool ContainsKey(string key) => _raw.ContainsKey(key);
	public bool Remove(string key) => _raw.Remove(key);
	public void Clear() => _raw.Clear();

	public bool TryGetValue(string key, out string value)
	{
		if(_raw.TryGetValue(key, out var raw))
		{
			value = this.Resolve(raw);
			return true;
		}

		value = null;
		return false;
	}

	public void Add(KeyValuePair<string, string> item) => ((IDictionary<string, string>)_raw).Add(item);
	public bool Contains(KeyValuePair<string, string> item) => this.TryGetValue(item.Key, out var value) && EqualityComparer<string>.Default.Equals(value, item.Value);
	public bool Remove(KeyValuePair<string, string> item) => this.Contains(item) && _raw.Remove(item.Key);
	public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
	{
		foreach(var item in this)
			array[arrayIndex++] = item;
	}
	#endregion

	#region 枚举遍历
	IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
	public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
	{
		foreach(var item in _raw)
			yield return new(item.Key, this.Resolve(item.Value));
	}
	#endregion

	#region 私有方法
	private string Resolve(string text)
	{
		if(text == null)
			return null;

		return _resolve(text);
	}
	#endregion
}
