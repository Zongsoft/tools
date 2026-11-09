/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Migrator.Migration;

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
		this.Name = name;
		this.Aliases = Array.AsReadOnly(new[] { name }.Concat(aliases).ToArray());
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
		{
			if(provider.Aliases.Contains(name?.Trim(), StringComparer.OrdinalIgnoreCase))
				return provider;
		}

		throw new InvalidDataException(string.Format(MigrationResources.MigratorUnknown_Message, name));
	}

	public abstract void Validate(IReadOnlyDictionary<string, string> parameters, string runtime = "linux-x64");
	public virtual void Prepare(MigrationPlan.Database database, string runtime = "linux-x64") => throw new InvalidDataException(MigrationResources.PlanInvalid_Message);
	#endregion

	#region 保护方法
	protected void Validate(IReadOnlyDictionary<string, string> parameters, string[] allowed)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		foreach(var key in parameters.Keys)
		{
			if(!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException(string.Format(MigrationResources.ParameterUnknown_Message, key, this.Name));
		}

		parameters.Seconds("Timeout", 30);
	}

	protected void Require(IReadOnlyDictionary<string, string> parameters, string key)
	{
		if(parameters.Get(key) == null)
			throw new InvalidDataException(string.Format(MigrationResources.ParameterMissing_Message, key, this.Name));
	}
	#endregion
}
