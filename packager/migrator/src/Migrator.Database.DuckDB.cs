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
	partial class Database
	{
		public sealed class DuckDB : Database
		{
			#region 公共方法
			public override async Task MigrateAsync(MigrationPlan.Step task, MigrationContext context, CancellationToken cancellation = default)
			{
				var database = task.Parameters.Get("Database");
				Directory.CreateDirectory(Path.GetDirectoryName(database));
				await ExecuteScriptsAsync(this.CreateConnection(task.Parameters, database), task, context, cancellation);
			}
			#endregion

			#region 私有方法
			private DbConnection CreateConnection(IReadOnlyDictionary<string, string> parameters, string database)
			{
				var builder = new DbConnectionStringBuilder
				{
					["Data Source"] = database,
				};

				return new global::DuckDB.NET.Data.DuckDBConnection(builder.ConnectionString);
			}
			#endregion
		}
	}
}
