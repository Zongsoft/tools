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

namespace Zongsoft.Tools.Packager;

public sealed class Variables(IEnumerable<KeyValuePair<string, string>> variables = null) : IReadOnlyDictionary<string, string>, IReadOnlyCollection<KeyValuePair<string, string>>
{
	#region 常量定义
	internal const string URL = "url";
	internal const string NAME = "name";
	internal const string TITLE = "title";
	internal const string LICENSE = "license";
	internal const string CATEGORY = "category";
	internal const string MAINTAINER = "maintainer";
	internal const string DEPENDENCIES = "dependencies";
	internal const string SUMMARY = "summary";
	internal const string DESCRIPTION = "description";
	internal const string SOURCE = "source";
	internal const string OUTPUT = "output";
	internal const string EXCLUDE = "exclude";
	internal const string EDITION = "edition";
	internal const string VERSION = "version";
	internal const string PLATFORM = "platform";
	internal const string FRAMEWORK = "framework";
	internal const string COMPILATION = "compilation";
	internal const string ARCHITECTURE = "architecture";
	#endregion

	#region 成员字段
	private readonly Dictionary<string, string> _variables = new(variables ?? [], StringComparer.OrdinalIgnoreCase)
	{
	};
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
				throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed, result.Value));

			return value == null ? null : result.Value;
		}
		set => _variables[name] = value;
	}

	public string Url => this[URL];
	public string Name => this[NAME];
	public string Title => this[TITLE];
	public string License => this[LICENSE];
	public string Category => this[CATEGORY];
	public string Maintainer => this[MAINTAINER];
	public string Summary => TextSource.Read(this.Source, GetRaw(SUMMARY));
	public string Description => TextSource.Read(this.Source, GetRaw(DESCRIPTION));
	public string Source => this[SOURCE];
	public string Output => this[OUTPUT];
	public string Exclude => this[EXCLUDE];
	public string Edition => this[EDITION];
	public Version Version => _variables.TryGetValue(VERSION, out var value) ? Version.Parse(this[VERSION]) : null;
	public Platform Platform => _variables.TryGetValue(PLATFORM, out var value) ? Enum.Parse<Platform>(this[PLATFORM], true) : Platform.Unknown;
	public Architecture Architecture => _variables.TryGetValue(ARCHITECTURE, out var value) ? Enum.Parse<Architecture>(this[ARCHITECTURE], true) : Architecture.X64;
	public string Framework => this[FRAMEWORK];
	public string Compilation => _variables.TryGetValue(COMPILATION, out var value) ? this[COMPILATION] : "Release";
	public string RuntimeIdentifier => _variables.TryGetValue(nameof(RuntimeIdentifier), out var value) ? this[nameof(RuntimeIdentifier)] : Utility.GetRuntimeIdentifier(this.Platform, this.Architecture);
	public string[] Dependencies => _variables.TryGetValue(DEPENDENCIES, out var value) && value != null ? this[DEPENDENCIES].Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

	public DaemonVariable Daemon => new
	(
		this[DaemonVariable.DAEMON],
		this[DaemonVariable.DAEMON_BIND],
		this[DaemonVariable.DAEMON_ENVIRONMENTS]
	);

	public ScriptVariable Script => new
	(
		GetRaw(ScriptVariable.INSTALLING),
		GetRaw(ScriptVariable.INSTALLED),
		GetRaw(ScriptVariable.UNINSTALLING),
		GetRaw(ScriptVariable.UNINSTALLED),
		GetRaw(ScriptVariable.PREINSTALLING),
		GetRaw(ScriptVariable.POSTINSTALLING),
		GetRaw(ScriptVariable.PREINSTALLED),
		GetRaw(ScriptVariable.POSTINSTALLED),
		GetRaw(ScriptVariable.PREUNINSTALLING),
		GetRaw(ScriptVariable.POSTUNINSTALLING),
		GetRaw(ScriptVariable.PREUNINSTALLED),
		GetRaw(ScriptVariable.POSTUNINSTALLED)
	);
	#endregion

	#region 公共方法
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
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _variables.GetEnumerator();
	#endregion

	public readonly struct DaemonVariable
	{
		internal const string DAEMON = "daemon";
		internal const string DAEMON_BIND = "daemon-bind";
		internal const string DAEMON_ENVIRONMENTS = "daemon-environments";

		public DaemonVariable(string identifier, string bind, string environments)
		{
			this.Identifier = identifier;
			this.Bind = bind;
			this.Environments = string.IsNullOrEmpty(environments) ? [] : environments.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		}

		public readonly string Identifier;
		public readonly string Bind;
		public readonly string[] Environments;

		public bool Disabled =>
			string.Equals(this.Identifier, "none", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(this.Identifier, "disable", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(this.Identifier, "disabled", StringComparison.OrdinalIgnoreCase);
	}

	public readonly struct ScriptVariable
	{
		internal const string INSTALLING = "installing";
		internal const string INSTALLED = "installed";
		internal const string UNINSTALLING = "uninstalling";
		internal const string UNINSTALLED = "uninstalled";

		internal const string PREINSTALLING = "preinstalling";
		internal const string POSTINSTALLING = "postinstalling";
		internal const string PREINSTALLED = "preinstalled";
		internal const string POSTINSTALLED = "postinstalled";
		internal const string PREUNINSTALLING = "preuninstalling";
		internal const string POSTUNINSTALLING = "postuninstalling";
		internal const string PREUNINSTALLED = "preuninstalled";
		internal const string POSTUNINSTALLED = "postuninstalled";

		public ScriptVariable(
			string installing,
			string installed,
			string uninstalling,
			string uninstalled,
			string preinstalling,
			string postinstalling,
			string preinstalled,
			string postinstalled,
			string preuninstalling,
			string postuninstalling,
			string preuninstalled,
			string postuninstalled)
		{
			this.Installing = installing;
			this.Installed = installed;
			this.Uninstalling = uninstalling;
			this.Uninstalled = uninstalled;
			this.PreInstalling = preinstalling;
			this.PostInstalling = postinstalling;
			this.PreInstalled = preinstalled;
			this.PostInstalled = postinstalled;
			this.PreUninstalling = preuninstalling;
			this.PostUninstalling = postuninstalling;
			this.PreUninstalled = preuninstalled;
			this.PostUninstalled = postuninstalled;
		}

		public readonly string Installing;
		public readonly string Installed;
		public readonly string Uninstalling;
		public readonly string Uninstalled;
		public readonly string PreInstalling;
		public readonly string PostInstalling;
		public readonly string PreInstalled;
		public readonly string PostInstalled;
		public readonly string PreUninstalling;
		public readonly string PostUninstalling;
		public readonly string PreUninstalled;
		public readonly string PostUninstalled;
	}
}
