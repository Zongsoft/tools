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
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Components;

namespace Zongsoft.Tools.Migrator;

public sealed class Variables(IEnumerable<KeyValuePair<string, string>> variables = null) : IReadOnlyDictionary<string, string>, IReadOnlyCollection<KeyValuePair<string, string>>
{
	#region 常量定义
	internal const string NAME = "name";
	internal const string TITLE = "title";
	internal const string SUMMARY = "summary";
	internal const string DESCRIPTION = "description";
	internal const string SOURCE = "source";
	internal const string OUTPUT = "output";
	internal const string EDITION = "edition";
	internal const string VERSION = "version";
	internal const string PLATFORM = "platform";
	internal const string ARCHITECTURE = "architecture";
	#endregion

	#region 成员字段
	private readonly Dictionary<string, string> _variables = new(variables ?? [], StringComparer.OrdinalIgnoreCase);
	#endregion

	#region 公共属性
	public string this[string name]
	{
		get
		{
			if(!_variables.TryGetValue(name, out var value))
				return null;

			var result = Normalizer.Normalize(value, _variables);
			if(!result.Succeed)
				throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed_Message, result.Value));

			return value == null ? null : result.Value;
		}
		set => _variables[name] = value;
	}

	public string Name => this[NAME];
	public string Title => this[TITLE];
	public string Summary => TextSource.Read(this.Source, this.GetRaw(SUMMARY));
	public string Description => TextSource.Read(this.Source, this.GetRaw(DESCRIPTION));
	public string Source => this[SOURCE];
	public string Output => this[OUTPUT];
	public string Edition => this[EDITION];
	public Version Version => _variables.TryGetValue(VERSION, out var value) ? Version.Parse(this[VERSION]) : null;
	public Architecture Architecture => _variables.TryGetValue(ARCHITECTURE, out var value) ? Enum.Parse<Architecture>(this[ARCHITECTURE], true) : Architecture.X64;
	#endregion

	#region 公共方法
	internal static Dictionary<string, string> From(CommandContext context)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach(var option in context.Descriptor.Options)
			variables[option.Name] = option.DefaultValue?.ToString();

		foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
			variables[variable.Key.ToString()] = variable.Value?.ToString();

		foreach(var option in context.Options)
			variables[option.Key] = option.Value?.ToString();

		return variables;
	}

	public bool Contains(string name) => name != null && _variables.ContainsKey(name);
	public bool TryGetValue(string name, out string value)
	{
		value = name == null ? null : this[name];
		return name != null && _variables.ContainsKey(name);
	}
	#endregion

	#region 内部方法
	internal IReadOnlyDictionary<string, string> Raw => _variables;
	private string GetRaw(string name) => _variables.GetValueOrDefault(name);
	#endregion

	#region 显式实现
	int IReadOnlyCollection<KeyValuePair<string, string>>.Count => _variables.Count;
	IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => _variables.Keys;
	IEnumerable<string> IReadOnlyDictionary<string, string>.Values => _variables.Values;
	bool IReadOnlyDictionary<string, string>.ContainsKey(string key) => key != null && _variables.ContainsKey(key);
	#endregion

	#region 枚举遍历
	IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
	public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _variables.GetEnumerator();
	#endregion
}
