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

namespace Zongsoft.Tools.Packager.Migration;

partial class Migrator
{
	public abstract partial class Database : Migrator
	{
		#region 保护方法
		protected static async Task EnsureDatabaseAsync(DbConnection connection, string database, string catalog, string creation, int timeout, CancellationToken cancellation)
		{
			await using(connection)
			{
				await connection.OpenAsync(cancellation);

				if(await ExistsAsync(connection, database, catalog, timeout, cancellation))
					return;

				try
				{
					await ExecuteAsync(connection, creation, timeout, cancellation);
				}
				catch(DbException)
				{
					// Another installer may have created it between the check and CREATE.
					if(!await ExistsAsync(connection, database, catalog, timeout, cancellation))
						throw;
				}
			}
		}

		protected static async Task ExecuteScriptsAsync(DbConnection connection, MigrationPlan.Step task, MigrationContext context, CancellationToken cancellation)
		{
			await using(connection)
			{
				await connection.OpenAsync(cancellation);
				var timeout = task.Parameters.Seconds("CommandTimeout", 300);

				foreach(var script in task.Scripts)
				{
					context.Log(string.Format(Properties.Resources.ScriptExecuting, task.Id, script.Path));
					var text = await File.ReadAllTextAsync(context.GetScriptPath(script), cancellation);
					await ExecuteAsync(connection, text, timeout, cancellation);
				}
			}
		}
		#endregion

		#region 私有方法
		private static async Task<bool> ExistsAsync(DbConnection connection, string database, string catalog, int timeout, CancellationToken cancellation)
		{
			await using var command = connection.CreateCommand();
			command.CommandTimeout = timeout;
			command.CommandText = catalog;

			// Catalog names are returned as values; no user input is interpolated into a query.
			await using var reader = await command.ExecuteReaderAsync(cancellation);
			while(await reader.ReadAsync(cancellation))
			{
				if(string.Equals(reader.GetString(0), database, StringComparison.Ordinal))
					return true;
			}

			return false;
		}

		private static async Task ExecuteAsync(DbConnection connection, string sql, int timeout, CancellationToken cancellation)
		{
			await using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.CommandTimeout = timeout;
			await command.ExecuteNonQueryAsync(cancellation);
		}
		#endregion
	}
}
