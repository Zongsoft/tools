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

using System.Data.Common;

namespace Zongsoft.Tools.Migrator.Migration;

partial class Migrator
{
	partial class Database
	{
		public sealed class MySql(Func<MigrationPlan.Database, string, DbConnection> factory = null) : Database(factory)
		{
			#region 公共方法
			public override Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) =>
				EnsureDatabaseAsync(database, context, this.Connect(database, database.Settings.Get("Bootstrap")), "SHOW DATABASES",
					async connection =>
					{
						var timeout = database.Options.Seconds("CommandTimeout", 300);
						var charset = database.Options.Get("Charset");
						var collation = database.Options.Get("Collation");

						if(collation != null)
						{
							var rows = await QueryAsync(connection, "SELECT CHARACTER_SET_NAME FROM information_schema.COLLATIONS WHERE COLLATION_NAME = " + Literal(collation), timeout, cancellation);

							if(rows.Count != 1 || charset != null && !charset.Equals(rows[0][0], StringComparison.OrdinalIgnoreCase))
								throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Collation"));
						}

						return "CREATE DATABASE " + Quote(database.Name) +
							(charset == null ? "" : " CHARACTER SET " + Quote(charset)) +
							(collation == null ? "" : " COLLATE " + Quote(collation));
					}, cancellation);

			public override async Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				await using var connection = this.Connect(database, database.Settings.Get("Bootstrap"));
				await connection.OpenAsync(cancellation);

				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var quote = await GetLiteralAsync(connection, timeout, cancellation);

				foreach(var user in database.Users)
					await EnsureUserAsync(connection, user.Name,
						"SELECT User FROM mysql.user WHERE User = " + quote(user.Name) + " AND Host = " + quote(user.Host),
						"CREATE USER " + Account(user, quote) + " IDENTIFIED BY " + quote(user.Password), timeout, cancellation);
			}

			public override async Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				await using var connection = this.Connect(database, database.Name);
				await connection.OpenAsync(cancellation);
				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var quote = await GetLiteralAsync(connection, timeout, cancellation);
				var partial = await QueryAsync(connection, "SELECT @@GLOBAL.partial_revokes", timeout, cancellation);
				var literalScope = partial.Count > 0 && partial[0][0] is "1" or "ON";
				var scope = Quote(literalScope ? database.Name : database.Name.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%")) + ".*";

				foreach(var user in database.Users)
				{
					var permissions = MigrationPrivileges.Resolve(user).SelectMany(Expand).Distinct(StringComparer.Ordinal).ToArray();
					foreach(var role in user.Roles)
					{
						var grants = await QueryAsync(connection, "SHOW GRANTS FOR " + quote(role) + "@'%'", timeout, cancellation);
						if(grants.Count == 0 || grants.Any(row => !IsScopedGrant(row[0], scope, Quote(database.Name))))
							throw new MigrationPrivilegeException(database, "Roles", "UnverifiedRoleScope");
					}

					if(permissions.Length > 0)
						await ExecuteAsync(connection, "GRANT " + string.Join(", ", permissions) + " ON " + scope + " TO " + Account(user, quote), timeout, cancellation);

					if(user.Roles.Length == 0)
						continue;

					var defaults = await QueryAsync(connection, "SELECT DEFAULT_ROLE_USER, DEFAULT_ROLE_HOST FROM mysql.default_roles WHERE USER = " + quote(user.Name) + " AND HOST = " + quote(user.Host), timeout, cancellation);
					var roles = defaults.Select(row => quote(row[0]) + "@" + quote(row[1])).ToHashSet(StringComparer.Ordinal);

					foreach(var role in user.Roles)
					{
						var account = quote(role) + "@" + quote("%");
						await ExecuteAsync(connection, "GRANT " + account + " TO " + Account(user, quote), timeout, cancellation);
						roles.Add(account);
					}

					await ExecuteAsync(connection, "SET DEFAULT ROLE " + string.Join(", ", roles.Order(StringComparer.Ordinal)) + " TO " + Account(user, quote), timeout, cancellation);
				}
			}
			#endregion

			#region 连接方法
			protected override DbConnection CreateConnection(MigrationPlan.Database database, string name)
			{
				var settings = database.Settings;
				var builder = new DbConnectionStringBuilder
				{
					["Allow User Variables"] = true,
					["Host"] = settings.Get("Server"),
					["Username"] = settings.Get("UserName"),
					["Password"] = settings["Password"],
					["Connection Timeout"] = settings.Seconds("Timeout", 30),
					["Port"] = settings.Get("Port"),
				};

				if(!string.IsNullOrEmpty(name))
					builder["Database"] = name;
				if(settings.TryGetValue("Secured", out var secured))
					builder["SSL Mode"] = bool.Parse(secured) ? "Required" : "Disabled";

				return new MySqlConnector.MySqlConnection(builder.ConnectionString);
			}
			#endregion

			#region 私有方法
			private static string[] Expand(string privilege) => privilege switch
			{
				"Select" => ["SELECT"],
				"Insert" => ["INSERT"],
				"Update" => ["UPDATE"],
				"Delete" => ["DELETE"],
				"Execute" => ["EXECUTE"],
				"CreateTable" => ["CREATE", "REFERENCES"],
				"CreateIndex" or "DropIndex" => ["INDEX"],
				"CreateView" => ["CREATE VIEW", "SHOW VIEW", "SELECT"],
				"CreateProcedure" or "CreateFunction" => ["CREATE ROUTINE"],
				"AlterTable" => ["ALTER", "CREATE", "INSERT", "REFERENCES", "INDEX"],
				"AlterIndex" => ["ALTER", "CREATE", "INSERT", "INDEX"],
				"AlterView" => ["CREATE VIEW", "DROP", "SHOW VIEW", "SELECT"],
				"AlterProcedure" or "AlterFunction" => ["ALTER ROUTINE", "CREATE ROUTINE"],
				"DropTable" or "DropView" => ["DROP"],
				"DropProcedure" or "DropFunction" => ["ALTER ROUTINE"],
				_ => throw new InvalidDataException(MigrationResources.PlanInvalid_Message),
			};

			private static bool IsScopedGrant(string grant, string scope, string database)
			{
				// SHOW GRANTS 的角色继承、代理和全局权限不作为目标库授权接受。
				if(grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal) && !grant.Contains(" WITH ", StringComparison.Ordinal))
					return true;
				var start = grant.IndexOf(" ON ", StringComparison.Ordinal);
				var end = grant.IndexOf(" TO ", StringComparison.Ordinal);
				if(!grant.StartsWith("GRANT ", StringComparison.Ordinal) || start < 0 || end <= start)
					return false;
				var target = grant[(start + 4)..end];
				if(target.StartsWith("PROCEDURE ", StringComparison.Ordinal))
					target = target[10..];
				else if(target.StartsWith("FUNCTION ", StringComparison.Ordinal))
					target = target[9..];
				// 对象级授权中的数据库名没有通配符语义；数据库级授权必须精确匹配转义后的范围。
				return target.StartsWith(scope[..^1], StringComparison.Ordinal) || target.StartsWith(database + ".`", StringComparison.Ordinal);
			}

			private static string Quote(string name) => "`" + name.Replace("`", "``") + "`";
			private static string Account(MigrationPlan.User user, Func<string, string> quote) => quote(user.Name) + "@" + quote(user.Host);
			private static async Task<Func<string, string>> GetLiteralAsync(DbConnection connection, int timeout, CancellationToken cancellation)
			{
				var rows = await QueryAsync(connection, "SELECT @@SESSION.sql_mode", timeout, cancellation);
				var raw = rows.Count > 0 && (rows[0][0] ?? "").Split(',').Contains("NO_BACKSLASH_ESCAPES", StringComparer.OrdinalIgnoreCase);
				return value => Literal(raw ? value : value.Replace("\\", "\\\\"));
			}
			#endregion
		}
	}
}
