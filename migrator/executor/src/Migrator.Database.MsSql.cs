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

using System.Data.Common;

namespace Zongsoft.Tools.Migrator.Migration;

partial class Migrator
{
	partial class Database
	{
		public sealed class MsSql(Func<MigrationPlan.Database, string, DbConnection> factory = null) : Database(factory)
		{
			#region 公共方法
			public override Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) =>
				EnsureDatabaseAsync(database, context, this.Connect(database, database.Settings.Get("Bootstrap")), Catalog("sys.databases", database.Name),
					async connection =>
					{
						var sql = "CREATE DATABASE " + Quote(database.Name);
						if(database.Options.TryGetValue("Collation", out var collation))
						{
							if(!await ExistsAsync(connection, collation, "SELECT name FROM sys.fn_helpcollations()", database.Options.Seconds("CommandTimeout", 300), cancellation))
								throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Collation"));
							sql += " COLLATE " + Quote(collation);
						}

						return sql;
					}, cancellation);

			public override async Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				var timeout = database.Options.Seconds("CommandTimeout", 300);

				await using(var connection = this.Connect(database, database.Settings.Get("Bootstrap")))
				{
					await connection.OpenAsync(cancellation);
					foreach(var user in database.Users)
						await EnsureUserAsync(connection, user.Name, Catalog("sys.server_principals", user.Name),
							"CREATE LOGIN " + Quote(user.Name) + " WITH PASSWORD = N" + Literal(user.Password), timeout, cancellation);
				}

				await using(var connection = this.Connect(database, database.Name))
				{
					await connection.OpenAsync(cancellation);

					foreach(var user in database.Users)
					{
						await EnsureUserAsync(connection, user.Name, Catalog("sys.database_principals", user.Name),
							"CREATE USER " + Quote(user.Name) + " FOR LOGIN " + Quote(user.Name), timeout, cancellation);

						var mappings = await QueryAsync(connection,
							"SELECT dp.name FROM sys.database_principals dp JOIN sys.server_principals sp ON dp.sid = sp.sid WHERE dp.type = 'S' AND sp.type = 'S' AND dp.name = N" +
							Literal(user.Name) + " AND sp.name = N" + Literal(user.Name), timeout, cancellation);

						if(mappings.Count != 1)
							throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Users.Login"));
					}
				}
			}

			public override async Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				await using var connection = this.Connect(database, database.Name);
				await connection.OpenAsync(cancellation);
				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var schemas = await QueryAsync(connection, "SELECT name FROM sys.schemas WHERE name NOT IN ('sys', 'INFORMATION_SCHEMA') AND schema_id NOT BETWEEN 16384 AND 16393 ORDER BY name", timeout, cancellation);
				var sequences = await QueryAsync(connection, "SELECT SCHEMA_NAME(schema_id), name FROM sys.sequences WHERE is_ms_shipped = 0", timeout, cancellation);
				var functions = await QueryAsync(connection, "SELECT SCHEMA_NAME(schema_id), name FROM sys.objects WHERE type IN ('IF', 'TF', 'FT') AND is_ms_shipped = 0", timeout, cancellation);

				foreach(var user in database.Users)
				{
					var privileges = MigrationPrivileges.Resolve(user);
					foreach(var role in user.Roles.Distinct(StringComparer.Ordinal))
					{
						var members = await QueryAsync(connection,
							"SELECT member.name FROM sys.database_role_members membership JOIN sys.database_principals role ON membership.role_principal_id = role.principal_id JOIN sys.database_principals member ON membership.member_principal_id = member.principal_id WHERE role.name = N" +
							Literal(role) + " AND member.name = N" + Literal(user.Name), timeout, cancellation);

						if(members.Count == 0)
							await ExecuteAsync(connection, "ALTER ROLE " + Quote(role) + " ADD MEMBER " + Quote(user.Name), timeout, cancellation);
					}

					if(privileges.Length == 0)
						continue;

					var grants = new HashSet<string>(StringComparer.Ordinal) { "CONNECT" };
					var alter = false;
					var references = false;

					foreach(var privilege in privileges)
					{
						if(privilege is "Select" or "Insert" or "Update" or "Delete" or "Execute")
							grants.Add(privilege.ToUpperInvariant());
						else
						{
							alter = true;
							if(privilege.StartsWith("Create", StringComparison.Ordinal) && privilege != "CreateIndex")
								grants.Add("CREATE " + privilege[6..].ToUpperInvariant());
							references |= privilege is "CreateTable" or "AlterTable";
						}
					}

					if(privileges.Any(value => value is "CreateView" or "AlterView"))
						grants.Add("SELECT");

					foreach(var grant in grants)
						await ExecuteAsync(connection, "GRANT " + grant + " ON DATABASE::" + Quote(database.Name) + " TO " + Quote(user.Name), timeout, cancellation);

					foreach(var schema in schemas)
					{
						// ALTER ON SCHEMA 同时满足建表、建序列、索引维护和删除对象的依赖。
						if(alter)
							await ExecuteAsync(connection, "GRANT ALTER, VIEW DEFINITION ON SCHEMA::" + Quote(schema[0]) + " TO " + Quote(user.Name), timeout, cancellation);
						if(references)
							await ExecuteAsync(connection, "GRANT REFERENCES ON SCHEMA::" + Quote(schema[0]) + " TO " + Quote(user.Name), timeout, cancellation);
					}

					if(MigrationPrivileges.UsesSequences(privileges) || privileges.Any(value => value is "CreateTable" or "CreateIndex"))
					{
						foreach(var sequence in sequences)
							await ExecuteAsync(connection, "GRANT UPDATE ON OBJECT::" + Quote(sequence[0]) + "." + Quote(sequence[1]) + " TO " + Quote(user.Name), timeout, cancellation);
					}

					if(privileges.Contains("Execute", StringComparer.Ordinal))
					{
						foreach(var function in functions)
							await ExecuteAsync(connection, "GRANT SELECT ON OBJECT::" + Quote(function[0]) + "." + Quote(function[1]) + " TO " + Quote(user.Name), timeout, cancellation);
					}
				}
			}
			#endregion

			#region 连接方法
			protected override DbConnection CreateConnection(MigrationPlan.Database database, string name)
			{
				var settings = database.Settings;
				var builder = new DbConnectionStringBuilder
				{
					["Data Source"] = settings.Get("Server") + "," + settings.Get("Port"),
					["Initial Catalog"] = name,
					["User ID"] = settings.Get("UserName"),
					["Password"] = settings["Password"],
					["Connect Timeout"] = settings.Seconds("Timeout", 30),
					["Encrypt"] = bool.Parse(settings.Get("Secured", "true")),
					["TrustServerCertificate"] = bool.Parse(settings.Get("TrustServerCertificate", "false")),
				};

				return new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
			}
			#endregion

			#region 私有方法
			private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";
			private static string Catalog(string table, string name) => "SELECT N" + Literal(name) + " FROM " + table + " WHERE name = N" + Literal(name);
			#endregion
		}
	}
}
