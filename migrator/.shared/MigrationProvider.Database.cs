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
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationProvider
{
	private sealed class Database(string name, params string[] aliases) : MigrationProvider(name, aliases)
	{
		#region 公共方法
		public override void Validate(IReadOnlyDictionary<string, string> parameters, string runtime = "linux-x64")
		{
			base.Validate(parameters, this.Name is "sqlite" or "duckdb" ?
				["Database", "CommandTimeout"] :
				["Server", "Port", "Database", "UserName", "Password", "Bootstrap", "Timeout", "CommandTimeout", "Secured", "TrustServerCertificate"]);

			parameters.Seconds("CommandTimeout", 300);

			if(this.Name is "sqlite" or "duckdb")
				return;

			this.Require(parameters, "Server");

			if(!parameters.ContainsKey("Password") || parameters["Password"] == null)
				throw Invalid("Password");
			if(parameters.TryGetValue("Port", out var port) && (!ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0))
				throw Invalid("Port");

			foreach(var key in new[] { "Secured", "TrustServerCertificate" })
			{
				if(parameters.TryGetValue(key, out var value) && !bool.TryParse(value, out _))
					throw Invalid(key);
			}

			if(this.Name != "mssql" && parameters.ContainsKey("TrustServerCertificate"))
				throw Invalid("TrustServerCertificate");
		}

		public override void Prepare(MigrationPlan.Database database, string runtime = "linux-x64")
		{
			if(database == null || database.Provider != this.Name || !Identifier(database.Name) ||
				database.Settings == null || database.Options == null || database.Users == null)
				throw Invalid("Database");

			database.Settings = new(database.Settings, StringComparer.OrdinalIgnoreCase);
			database.Options = new(database.Options, StringComparer.OrdinalIgnoreCase);
			database.WindowsPath = runtime == "win-x64";

			var parameters = database.Settings;
			var options = database.Options;
			this.Validate(parameters, runtime);
			parameters.TryAdd("CommandTimeout", "300s");

			if(this.Name is not ("sqlite" or "duckdb"))
			{
				parameters.TryAdd("UserName", this.Name switch { "mssql" => "sa", "postgres" => "postgres", _ => "root" });
				parameters.TryAdd("Port", this.Name switch { "mssql" => "1433", "postgres" => "5432", "mysql" => "3306", _ => "6041" });
				parameters["Port"] = ushort.Parse(parameters["Port"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
				parameters.TryAdd("Timeout", "30s");
				parameters.TryAdd("Bootstrap", this.Name switch { "mssql" => "master", "postgres" => "postgres", "mysql" => "mysql", _ => "" });

				this.Require(parameters, "UserName");

				if(this.Name == "mssql")
				{
					parameters.TryAdd("Secured", "true");
					parameters.TryAdd("TrustServerCertificate", "false");
				}
				else if(this.Name == "tdengine")
					parameters.TryAdd("Secured", "false");
			}

			string[] allowed = this.Name switch
			{
				"mysql" => ["CommandTimeout", "Charset", "Collation"],
				"postgres" => ["CommandTimeout", "Charset", "Collation", "CType", "Template", "Timezone", "ConnectionLimit"],
				"mssql" => ["CommandTimeout", "Collation"],
				"tdengine" => ["CommandTimeout", "Precision", "Keep", "Duration", "Replica"],
				"sqlite" => ["CommandTimeout", "Path", "Charset"],
				_ => ["CommandTimeout", "Path"],
			};

			foreach(var option in options)
			{
				if(!allowed.Contains(option.Key, StringComparer.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(option.Value) || option.Value.Contains('\0'))
					throw Invalid(option.Key);
			}

			options.TryAdd("CommandTimeout", parameters.Get("CommandTimeout"));
			options.Seconds("CommandTimeout", 300);

			switch(this.Name)
			{
				case "mysql":
					if(!options.ContainsKey("Charset") && !options.ContainsKey("Collation"))
					{
						options.Add("Charset", "utf8mb4");
						options.Add("Collation", "utf8mb4_0900_ai_ci");
					}
					else if(options.Get("Charset")?.Equals("utf8mb4", StringComparison.OrdinalIgnoreCase) == true)
						options.TryAdd("Collation", "utf8mb4_0900_ai_ci");

					foreach(var key in new[] { "Charset", "Collation" })
						if(options.TryGetValue(key, out var value) && !Regex.IsMatch(value, @"^[a-zA-Z0-9_]+$"))
							throw Invalid(key);
					break;
				case "postgres":
					options.TryAdd("Charset", "UTF8");
					options.TryAdd("Template", "template0");
					options.TryAdd("ConnectionLimit", "-1");

					if(!int.TryParse(options["ConnectionLimit"], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var limit) || limit != -1 && limit <= 0)
						throw Invalid("ConnectionLimit");

					break;
				case "tdengine":
					options.TryAdd("Precision", "ms");
					options["Precision"] = options["Precision"].ToLowerInvariant();

					if(options["Precision"] is not ("ms" or "us" or "ns"))
						throw Invalid("Precision");

					options.TryAdd("Keep", "3650");
					var keep = Positive(options, "Keep");
					options.TryAdd("Duration", Math.Min(10, keep).ToString(CultureInfo.InvariantCulture));

					if(Positive(options, "Duration") > keep)
						throw Invalid("Duration");

					options.TryAdd("Replica", "1");

					if(options["Replica"] is not ("1" or "3"))
						throw Invalid("Replica");

					break;
				case "sqlite":
					options.TryAdd("Charset", "UTF-8");
					options["Charset"] = options["Charset"].ToUpperInvariant() switch
					{
						"UTF-8" => "UTF-8",
						"UTF-16LE" => "UTF-16le",
						"UTF-16BE" => "UTF-16be",
						_ => throw Invalid("Charset"),
					};

					goto case "duckdb";
				case "duckdb":
					if(!MigrationRuntime.IsDatabasePath(options.Get("Path"), runtime) || database.Users.Count > 0)
						throw Invalid(database.Users.Count > 0 ? "Users" : "Path");

					break;
			}

			var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach(var user in database.Users)
			{
				if(user == null || !Identifier(user.Name) || string.IsNullOrWhiteSpace(user.Password) || user.Password.Contains('\0') ||
					user.Privileges == null || user.Roles == null || !users.Add(user.Name))
					throw Invalid("Users");

				user.Permission = user.Permission?.ToLowerInvariant();
				if(user.Permission is not ("none" or "readonly" or "readwrite" or "admin"))
					throw Invalid("Permission");

				if(this.Name == "mysql")
				{
					user.Host ??= "%";
					if(!Identifier(user.Host))
						throw Invalid("Host");

				}
				else if(user.Host != null)
					throw Invalid("Host");

				foreach(var role in user.Roles)
					if(!Identifier(role))
						throw Invalid("Roles");

				MigrationPrivileges.Validate(database, user);
			}
		}
		#endregion

		#region 私有方法
		private static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
		private static InvalidDataException Invalid(string key) => new(string.Format(MigrationResources.ParameterValueInvalid_Message, key));
		private static int Positive(IReadOnlyDictionary<string, string> options, string key) =>
			int.TryParse(options.Get(key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : throw Invalid(key);
		#endregion
	}
}
