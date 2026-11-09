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
	public static string[] Resolve(MigrationPlan.Database.User user)
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
	public static void Validate(MigrationPlan.Database database, MigrationPlan.Database.User user)
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
