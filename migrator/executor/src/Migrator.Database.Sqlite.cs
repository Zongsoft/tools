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
		public sealed class Sqlite : Database
		{
			#region 公共方法
			public override async Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				var path = database.Options.Get("Path");
				var journal = GetJournal(database, context);
				var signature = GetConfigurationSignature(database);

				if(File.Exists(path) && !File.Exists(journal))
					return;
				if(File.Exists(journal) && await File.ReadAllTextAsync(journal, cancellation) != signature)
					throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Database.Pending"));

				Directory.CreateDirectory(Path.GetDirectoryName(path));
				Directory.CreateDirectory(context.StateDirectory);
				await File.WriteAllTextAsync(journal, signature, cancellation);

				await using var connection = this.Connect(database, path);
				await connection.OpenAsync(cancellation);

				var timeout = database.Options.Seconds("CommandTimeout", 300);
				await ExecuteAsync(connection, "PRAGMA encoding = " + Literal(database.Options.Get("Charset")), timeout, cancellation);

				// SQLite 只有写入主数据库 schema 后才保存编码，VACUUM 空库会回到 UTF-8。
				var marker = "\"__migrator_" + Guid.NewGuid().ToString("N") + "\"";
				await ExecuteAsync(connection, "BEGIN; CREATE TABLE " + marker + " (value INTEGER); DROP TABLE " + marker + "; COMMIT;", timeout, cancellation);
				await ExecuteAsync(connection, "VACUUM", timeout, cancellation);
				File.Delete(journal);
			}
			#endregion

			#region 连接方法
			protected override DbConnection CreateConnection(MigrationPlan.Database database, string name) => new Microsoft.Data.Sqlite.SqliteConnection(new DbConnectionStringBuilder
			{
				["Data Source"] = name,
				["Default Timeout"] = database.Options.Seconds("CommandTimeout", 300),
			}.ConnectionString);
			#endregion
		}
	}
}
