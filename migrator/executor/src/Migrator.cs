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

namespace Zongsoft.Tools.Migrator.Migration;

public abstract partial class Migrator
{
	#region 公共方法
	public abstract Task MigrateAsync(MigrationPlan.Step step, MigrationContext context, CancellationToken cancellation = default);
	public static Migrator Create(string provider) => MigrationProvider.Get(provider).Name switch
	{
		"mssql" => new Database.MsSql(),
		"mysql" => new Database.MySql(),
		"postgres" => new Database.Postgres(),
		"sqlite" => new Database.Sqlite(),
		"duckdb" => new Database.DuckDB(),
		"tdengine" => new Database.TDengine(),
		"amazon.s3" => new AmazonS3(),
		_ => throw new InvalidDataException(Properties.Resources.DatabaseMigratorUnknown_Message),
	};
	#endregion
}
