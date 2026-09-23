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

namespace Zongsoft.Tools.Migrator.Migration;

internal static class MigrationPrivileges
{
	#region 静态字段
	private static readonly string[] _names =
	[
		"Select", "Insert", "Update", "Delete", "Execute",
		"CreateTable", "CreateIndex", "CreateView", "CreateProcedure", "CreateFunction",
		"AlterTable", "AlterIndex", "AlterView", "AlterProcedure", "AlterFunction",
		"DropTable", "DropIndex", "DropView", "DropProcedure", "DropFunction",
	];
	#endregion

	#region 公共方法
	public static string[] Resolve(MigrationPlan.User user)
	{
		var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach(var value in user.Privileges)
		{
			var name = value?.Trim();
			if(name == null || !_names.Contains(name, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Privileges"));

			requested.Add(name);
		}

		foreach(var name in user.Permission.ToLowerInvariant() switch
		{
			"none" => Array.Empty<string>(),
			"admin" => _names,
			"readonly" => ["Select"],
			"readwrite" => ["Select", "Insert", "Update", "Delete", "Execute"],
			_ => throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Permission")),
		})
		{
			requested.Add(name);
		}

		return _names.Where(requested.Contains).ToArray();
	}

	public static bool UsesSequences(IEnumerable<string> privileges) => privileges.Any(name => name is "Select" or "Insert" or "Update" or "Delete");
	public static void Validate(MigrationPlan.Database database, MigrationPlan.User user)
	{
		user.Privileges = Resolve(user);

		if(database.Provider == "tdengine")
		{
			foreach(var privilege in user.Privileges)
			{
				if(privilege is not ("Select" or "Insert" or "Delete"))
					throw new MigrationPrivilegeException(database, privilege, "UnsupportedOperation");
			}

			if(user.Roles.Length > 0)
				throw new MigrationPrivilegeException(database, "Roles", "UnverifiedRoleScope");
		}
	}
	#endregion
}

public sealed class MigrationPrivilegeException : NotSupportedException
{
	#region 构造函数
	internal MigrationPrivilegeException(MigrationPlan.Database database, string privilege, string reason) :
		base(string.Format(MigrationResources.ParameterValueInvalid_Message, $"Privileges.{privilege} ({database.Provider}/{database.Name}: {reason})"))
	{
		this.Provider = database.Provider;
		this.Database = database.Name;
		this.Privilege = privilege;
		this.Reason = reason;
	}
	#endregion

	#region 公共属性
	public string Provider { get; }
	public string Database { get; }
	public string Privilege { get; }
	public string Reason { get; }
	#endregion
}
