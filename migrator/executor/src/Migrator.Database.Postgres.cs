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
		public sealed class Postgres(Func<MigrationPlan.Database, string, DbConnection> factory = null) : Database(factory)
		{
			#region 公共方法
			public override Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) =>
				EnsureDatabaseAsync(database, context, this.Connect(database, database.Settings.Get("Bootstrap")), "SELECT datname FROM pg_database",
					async connection =>
					{
						var options = database.Options;
						var sql = "CREATE DATABASE " + Quote(database.Name) + " TEMPLATE " + Quote(options.Get("Template")) +
							" ENCODING " + Text(options.Get("Charset")) + " CONNECTION LIMIT " + options.Get("ConnectionLimit");

						if(options.ContainsKey("Collation") || options.ContainsKey("CType"))
						{
							var version = await QueryAsync(connection, "SHOW server_version_num", options.Seconds("CommandTimeout", 300), cancellation);
							if(version.Count > 0 && int.Parse(version[0][0], System.Globalization.CultureInfo.InvariantCulture) >= 150000)
								sql += " LOCALE_PROVIDER libc";
						}

						if(options.TryGetValue("Collation", out var collation))
							sql += " LC_COLLATE " + Text(collation);
						if(options.TryGetValue("CType", out var ctype))
							sql += " LC_CTYPE " + Text(ctype);

						return sql;
					}, cancellation, connection => database.Options.TryGetValue("Timezone", out var timezone) ?
						ExecuteAsync(connection, "ALTER DATABASE " + Quote(database.Name) + " SET timezone TO " + Text(timezone), database.Options.Seconds("CommandTimeout", 300), cancellation) : Task.CompletedTask);

			public override async Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				await using var connection = this.Connect(database, database.Settings.Get("Bootstrap"));
				await connection.OpenAsync(cancellation);

				var timeout = database.Options.Seconds("CommandTimeout", 300);
				foreach(var user in database.Users)
				{
					await EnsureUserAsync(connection, user.Name, "SELECT rolname FROM pg_roles",
						"CREATE ROLE " + Quote(user.Name) + " LOGIN PASSWORD " + Text(user.Password), timeout, cancellation);
				}
			}

			public override async Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				await using var connection = this.Connect(database, database.Name);
				await connection.OpenAsync(cancellation);

				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var denied = false;

				if(connection is Npgsql.NpgsqlConnection postgres)
					postgres.Notice += (_, args) => denied |= args.Notice.SqlState == "01007";

				var schemas = await QueryAsync(connection, "SELECT nspname FROM pg_namespace WHERE nspname <> 'information_schema' AND left(nspname, 3) <> 'pg_' ORDER BY nspname", timeout, cancellation);

				foreach(var user in database.Users)
				{
					var target = Quote(user.Name);
					var privileges = MigrationPrivileges.Resolve(user);
					foreach(var role in user.Roles)
					{
						var scope = await QueryAsync(connection, RoleScope(role), timeout, cancellation);
						if(scope.Count != 1 || scope[0][0] != "safe")
							throw new MigrationPrivilegeException(database, "Roles", "UnverifiedRoleScope");
					}

					foreach(var role in user.Roles)
						await ExecuteAsync(connection, "GRANT " + Quote(role) + " TO " + target, timeout, cancellation);

					if(privileges.Length == 0)
						continue;

					// 对已有对象的 DDL 必须具有所有者权限，不能用 GRANT ALL 代替。
					foreach(var privilege in privileges)
					{
						var catalog = Ownership(privilege, user.Name);
						if(catalog != null && (await QueryAsync(connection, catalog, timeout, cancellation)).Count > 0)
							throw new MigrationPrivilegeException(database, privilege, "ObjectOwnershipRequired");
					}

					await ExecuteAsync(connection, "GRANT CONNECT ON DATABASE " + Quote(database.Name) + " TO " + target, timeout, cancellation);
					var tables = privileges.Where(value => value is "Select" or "Insert" or "Update" or "Delete").Select(value => value.ToUpperInvariant()).ToList();
					if(privileges.Any(value => value is "CreateView" or "AlterView") && !tables.Contains("SELECT", StringComparer.Ordinal))
						tables.Add("SELECT");
					if(privileges.Any(value => value is "CreateTable" or "AlterTable"))
						tables.Add("REFERENCES");
					var create = privileges.Any(value => value.StartsWith("Create", StringComparison.Ordinal) || value is "AlterView" or "AlterProcedure" or "AlterFunction");
					foreach(var row in schemas)
					{
						var schema = Quote(row[0]);
						await ExecuteAsync(connection, "GRANT " + (create ? "USAGE, CREATE" : "USAGE") + " ON SCHEMA " + schema + " TO " + target, timeout, cancellation);
						if(tables.Count > 0)
							await GrantObjectsAsync("TABLES", string.Join(", ", tables), schema, target);
						if(MigrationPrivileges.UsesSequences(privileges) || privileges.Any(value => value is "CreateTable" or "CreateIndex"))
							await GrantObjectsAsync("SEQUENCES", "USAGE, SELECT", schema, target);
						if(privileges.Contains("Execute", StringComparer.Ordinal))
							await GrantObjectsAsync("FUNCTIONS", "EXECUTE", schema, target);
					}
				}

				if(denied)
					throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Privileges"));

				async Task GrantObjectsAsync(string objects, string privileges, string schema, string target)
				{
					// ROUTINES 包含现有过程；默认例程权限使用 PostgreSQL 的 FUNCTIONS 语法。
					await ExecuteAsync(connection, "GRANT " + privileges + " ON ALL " + (objects == "FUNCTIONS" ? "ROUTINES" : objects) + " IN SCHEMA " + schema + " TO " + target, timeout, cancellation);
					await ExecuteAsync(connection, "ALTER DEFAULT PRIVILEGES FOR ROLE " + Quote(database.Settings.Get("UserName")) + " IN SCHEMA " + schema + " GRANT " + privileges + " ON " + objects + " TO " + target, timeout, cancellation);
				}
			}
			#endregion

			#region 连接方法
			protected override DbConnection CreateConnection(MigrationPlan.Database database, string name)
			{
				var settings = database.Settings;
				var builder = new DbConnectionStringBuilder
				{
					["Host"] = settings.Get("Server"),
					["Username"] = settings.Get("UserName"),
					["Password"] = settings["Password"],
					["Timeout"] = settings.Seconds("Timeout", 30),
					["Port"] = settings.Get("Port"),
					["Database"] = name,
				};

				if(settings.TryGetValue("Secured", out var secured))
					builder["SSL Mode"] = bool.Parse(secured) ? "Require" : "Disable";

				return new Npgsql.NpgsqlConnection(builder.ConnectionString);
			}
			#endregion

			#region 私有方法
			private static string Ownership(string privilege, string user)
			{
				var kinds = privilege switch
				{
					"CreateIndex" => "'r', 'p', 'm'",
					"AlterTable" or "DropTable" => "'r', 'p', 'f'",
					"AlterIndex" or "DropIndex" => "'i', 'I'",
					"AlterView" or "DropView" => "'v', 'm'",
					_ => null,
				};
				if(kinds != null)
					return "SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname <> 'information_schema' AND left(n.nspname, 3) <> 'pg_' AND c.relkind IN (" + kinds + ") AND NOT pg_has_role(" + Text(user) + ", c.relowner, 'USAGE') LIMIT 1";
				if(privilege is "AlterProcedure" or "DropProcedure" or "AlterFunction" or "DropFunction")
					return "SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname <> 'information_schema' AND left(n.nspname, 3) <> 'pg_' AND p.prokind " +
						(privilege.EndsWith("Procedure", StringComparison.Ordinal) ? "= 'p'" : "<> 'p'") + " AND NOT pg_has_role(" + Text(user) + ", p.proowner, 'USAGE') LIMIT 1";
				return null;
			}

			private static string RoleScope(string role) =>
				"WITH RECURSIVE roles(oid) AS (SELECT oid FROM pg_roles WHERE rolname = " + Text(role) +
				" UNION SELECT m.roleid FROM pg_auth_members m JOIN roles r ON m.member = r.oid) SELECT CASE WHEN " +
				"EXISTS (SELECT 1 FROM pg_roles r JOIN roles s ON r.oid = s.oid WHERE r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls OR left(r.rolname, 3) = 'pg_') OR " +
				"EXISTS (SELECT 1 FROM pg_auth_members m JOIN roles r ON m.member = r.oid WHERE m.admin_option) OR " +
				"EXISTS (SELECT 1 FROM pg_shdepend d JOIN roles r ON r.oid = d.refobjid WHERE d.refclassid = 'pg_authid'::regclass AND " +
				"((d.dbid <> 0 AND d.dbid <> (SELECT oid FROM pg_database WHERE datname = current_database())) OR " +
				"(d.dbid = 0 AND NOT (d.classid = 'pg_database'::regclass AND d.objid = (SELECT oid FROM pg_database WHERE datname = current_database()))))) " +
				"THEN 'unsafe' ELSE 'safe' END WHERE EXISTS (SELECT 1 FROM roles)";

			private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
			private static string Text(string value) => "E" + Literal(value.Replace("\\", "\\\\"));
			#endregion
		}
	}
}
