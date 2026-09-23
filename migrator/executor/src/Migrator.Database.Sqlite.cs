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
