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
		public sealed class DuckDB : Database
		{
			#region 公共方法
			public override async Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				var path = database.Options.Get("Path");
				if(File.Exists(path))
					return;

				Directory.CreateDirectory(Path.GetDirectoryName(path));
				await using var connection = this.Connect(database, path);
				await connection.OpenAsync(cancellation);
			}
			#endregion

			#region 连接方法
			protected override DbConnection CreateConnection(MigrationPlan.Database database, string name) => new global::DuckDB.NET.Data.DuckDBConnection(new DbConnectionStringBuilder { ["Data Source"] = name }.ConnectionString);
			#endregion
		}
	}
}
