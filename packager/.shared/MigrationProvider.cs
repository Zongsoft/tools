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
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Migration;

public abstract partial class MigrationProvider
{
	#region 静态字段
	private static readonly MigrationProvider[] _providers =
	[
		new Database("mssql"), new Database("mysql"), new Database("postgres", "postgresql"),
		new Database("sqlite"), new Database("duckdb"), new Database("tdengine"), new AmazonS3(),
	];
	#endregion

	#region 构造函数
	private MigrationProvider(string name, params string[] aliases)
	{
		Name = name;
		Aliases = Array.AsReadOnly(new[] { name }.Concat(aliases).ToArray());
	}
	#endregion

	#region 公共属性
	public string Name { get; }
	public IReadOnlyList<string> Aliases { get; }
	#endregion

	#region 公共方法
	public static MigrationProvider Get(string name)
	{
		foreach(var provider in _providers)
			if(provider.Aliases.Contains(name?.Trim(), StringComparer.OrdinalIgnoreCase))
				return provider;

		throw new InvalidDataException(string.Format(MigrationResources.MigratorUnknown, name));
	}

	public abstract void Validate(IReadOnlyDictionary<string, string> parameters);
	#endregion

	#region 保护方法
	protected void Validate(IReadOnlyDictionary<string, string> parameters, string[] allowed)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		foreach(var key in parameters.Keys)
			if(!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException(string.Format(MigrationResources.ParameterUnknown, key, Name));

		parameters.Seconds("Timeout", 30);
	}

	protected void Require(IReadOnlyDictionary<string, string> parameters, string key)
	{
		if(parameters.Get(key) == null)
			throw new InvalidDataException(string.Format(MigrationResources.ParameterMissing, key, Name));
	}
	#endregion
}
