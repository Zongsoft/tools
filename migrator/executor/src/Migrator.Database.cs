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

using System.Text;
using System.Data.Common;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Migrator.Migration;

partial class Migrator
{
	public abstract partial class Database(Func<MigrationPlan.Database, string, DbConnection> factory = null) : Migrator
	{
		#region 公共方法
		public virtual Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) => Task.CompletedTask;
		public virtual Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) => Task.CompletedTask;
		public virtual Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default) => Task.CompletedTask;

		public override async Task MigrateAsync(MigrationPlan.Step step, MigrationContext context, CancellationToken cancellation = default)
		{
			var database = context.GetDatabase(step.DatabaseIndex);
			await using var connection = this.Connect(database, database.Provider is "sqlite" or "duckdb" ? database.Options.Get("Path") : database.Name);
			await connection.OpenAsync(cancellation);
			var timeout = database.Options.Seconds("CommandTimeout", 300);

			foreach(var script in step.Scripts)
			{
				context.Log(string.Format(Properties.Resources.ScriptExecuting, context.StepNumber, script.Path));
				await ExecuteAsync(connection, await File.ReadAllTextAsync(context.GetScriptPath(script), cancellation), timeout, cancellation);
			}
		}
		#endregion

		#region 连接方法
		protected DbConnection Connect(MigrationPlan.Database database, string name) => factory?.Invoke(database, name) ?? this.CreateConnection(database, name);
		protected virtual DbConnection CreateConnection(MigrationPlan.Database database, string name) => throw new NotSupportedException();
		#endregion

		#region 初始化方法
		protected static async Task EnsureDatabaseAsync(MigrationPlan.Database database, MigrationContext context, DbConnection connection,
			string catalog, Func<DbConnection, Task<string>> creation, CancellationToken cancellation,
			Func<DbConnection, Task> configure = null)
		{
			await using(connection)
			{
				await connection.OpenAsync(cancellation);
				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var exists = await ExistsAsync(connection, database.Name, catalog, timeout, cancellation);
				var journal = GetJournal(database, context);
				var signature = GetConfigurationSignature(database);

				if(exists && !File.Exists(journal))
					return;

				if(File.Exists(journal) && await File.ReadAllTextAsync(journal, cancellation) != signature)
					throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Database.Pending"));

				if(!exists)
				{
					var sql = await creation(connection);
					Directory.CreateDirectory(context.StateDirectory);
					await File.WriteAllTextAsync(journal, signature, cancellation);

					try
					{
						await ExecuteAsync(connection, sql, timeout, cancellation);
					}
					catch(DbException)
					{
						if(!await ExistsAsync(connection, database.Name, catalog, timeout, cancellation))
						{
							File.Delete(journal);
							throw;
						}

						// 创建结果不确定时保留记录；配置仅包含这个新库的初始化设置。
					}
				}

				if(configure != null)
					await configure(connection);

				File.Delete(journal);
			}
		}

		protected static string GetJournal(MigrationPlan.Database database, MigrationContext context) =>
			Path.Combine(context.StateDirectory, "database-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(database.TargetKey))) + ".pending");

		protected static string GetConfigurationSignature(MigrationPlan.Database database) =>
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0",
				database.Options.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).SelectMany(item => new[] { item.Key.ToLowerInvariant(), item.Value })))));
		#endregion

		#region 命令方法
		protected static async Task<bool> ExistsAsync(DbConnection connection, string name, string catalog, int timeout, CancellationToken cancellation)
		{
			var rows = await QueryAsync(connection, catalog, timeout, cancellation);
			return rows.Any(row => string.Equals(row[0], name, StringComparison.Ordinal));
		}

		protected static async Task<List<string[]>> QueryAsync(DbConnection connection, string sql, int timeout, CancellationToken cancellation)
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.CommandTimeout = timeout;

			await using var reader = await command.ExecuteReaderAsync(cancellation);
			var rows = new List<string[]>();

			while(await reader.ReadAsync(cancellation))
			{
				var values = new string[reader.FieldCount];

				for(var i = 0; i < values.Length; i++)
					values[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);

				rows.Add(values);
			}

			return rows;
		}

		protected static async Task ExecuteAsync(DbConnection connection, string sql, int timeout, CancellationToken cancellation)
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.CommandTimeout = timeout;
			await command.ExecuteNonQueryAsync(cancellation);
		}

		protected static async Task EnsureUserAsync(DbConnection connection, string name, string catalog, string creation, int timeout, CancellationToken cancellation)
		{
			if(await ExistsAsync(connection, name, catalog, timeout, cancellation))
				return;

			try
			{
				await ExecuteAsync(connection, creation, timeout, cancellation);
			}
			catch(DbException)
			{
				if(!await ExistsAsync(connection, name, catalog, timeout, cancellation))
					throw;
			}
		}

		protected static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
		#endregion
	}
}
