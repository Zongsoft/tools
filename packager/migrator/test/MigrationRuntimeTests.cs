using System;
using System.IO;
using System.Linq;
using System.Data.Common;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Packager.Migration.Tests;

public sealed class MigrationRuntimeTests
{
	#region 测试方法
	[Theory]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	public async Task Database_NewFileAndRepeat_ReexecutesIdempotentSqlOnEveryAttempt(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "data", "hosting.db");
		var logs = new List<string>();
		var migration = new MigrationPlan.Step
		{
			Id = "0001-" + provider,
			Provider = provider,
			Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = database },
			Scripts = [directory.Script(".migration/.artifacts/schema.sql", "CREATE TABLE IF NOT EXISTS samples (id INTEGER PRIMARY KEY, title VARCHAR(100));\nINSERT INTO samples SELECT 1, 'hosting;ready' WHERE NOT EXISTS (SELECT 1 FROM samples WHERE id=1);\nCREATE TABLE IF NOT EXISTS migration_audit (id INTEGER); INSERT INTO migration_audit VALUES (1);")],
		};
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"), logs.Add);
		var migrator = Migrator.Create(migration.Provider);
		try
		{
			await migrator.MigrateAsync(migration, context, TestContext.Current.CancellationToken);
			await migrator.MigrateAsync(migration, context, TestContext.Current.CancellationToken);

			Assert.True(File.Exists(database));
			await using var connection = Connection(provider, database);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM samples";
			Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
			command.CommandText = "SELECT title FROM samples WHERE id=1";
			Assert.Equal("hosting;ready", await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
			Assert.Contains(logs, message => message.Contains("schema.sql", StringComparison.Ordinal));
			command.CommandText = "SELECT COUNT(*) FROM migration_audit";
			Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
		}
		finally { ClearSqlitePool(provider); }
	}

	[Theory]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	public async Task Database_MiddleStatementFailure_StopsLaterStatementsAndReportsFailure(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "hosting.db");
		var migration = new MigrationPlan.Step
		{
			Id = "0001-" + provider,
			Provider = provider,
			Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = database },
			Scripts = [directory.Script(".migration/.artifacts/failure.sql", "CREATE TABLE samples (id INTEGER); INSERT INTO absent_table VALUES (1); INSERT INTO samples VALUES (2);")],
		};
		try
		{
			await Assert.ThrowsAnyAsync<DbException>(() => Migrator.Create(migration.Provider).MigrateAsync(migration, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

			await using var connection = Connection(provider, database);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM samples";
			Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
		}
		finally { ClearSqlitePool(provider); }
	}

	[Fact]
	public void Context_ModifiedOrEscapingSql_IsRejectedBeforeExecution()
	{
		using var directory = new MigrationTestDirectory();
		var script = directory.Script(".migration/.artifacts/schema.sql", "SELECT 1;");
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		Assert.Equal(Path.Combine(directory.Path, ".migration", ".artifacts", "schema.sql"), context.GetScriptPath(script));
		directory.Write(".migration/.artifacts/schema.sql", "SELECT 2;");

		Assert.Contains(script.Path, Assert.Throws<InvalidDataException>(() => context.GetScriptPath(script)).Message);
		script.Path = "../outside.sql";
		Assert.Throws<InvalidDataException>(() => context.GetScriptPath(script));
	}

	[Fact]
	public async Task Sqlite_TriggerBodyWithMultipleStatements_ExecutesAsCompleteScript()
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "hosting.db");
		var migration = new MigrationPlan.Step
		{
			Id = "0001-sqlite", Provider = "sqlite", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = database },
			Scripts = [directory.Script(".migration/.artifacts/trigger.sql", "CREATE TABLE samples (id INTEGER); CREATE TABLE audit (message TEXT); CREATE TRIGGER sample_added AFTER INSERT ON samples BEGIN INSERT INTO audit VALUES ('first;part'); INSERT INTO audit VALUES ('second'); END; INSERT INTO samples VALUES (1);")],
		};
		try
		{
			await Migrator.Create(migration.Provider).MigrateAsync(migration, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);
			await using var connection = Connection("sqlite", database);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM audit";
			Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
			command.CommandText = "SELECT message FROM audit ORDER BY rowid LIMIT 1";
			Assert.Equal("first;part", await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
		}
		finally { ClearSqlitePool("sqlite"); }
	}

	[Fact]
	public async Task Database_FailedLaterFile_RetryReexecutesEarlierSuccessfulFile()
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "hosting.db");
		var migration = new MigrationPlan.Step
		{
			Id = "0001-sqlite", Provider = "sqlite", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = database },
			Scripts =
			[
				directory.Script(".migration/.artifacts/first.sql", "CREATE TABLE IF NOT EXISTS migration_audit (id INTEGER); INSERT INTO migration_audit VALUES (1);"),
				directory.Script(".migration/.artifacts/second.sql", "INSERT INTO absent_table VALUES (2);"),
			],
		};
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		try
		{
			await Assert.ThrowsAnyAsync<DbException>(() => Migrator.Create(migration.Provider).MigrateAsync(migration, context, TestContext.Current.CancellationToken));
			Assert.Equal(1L, await CountAudits());
			migration.Scripts[1] = directory.Script(".migration/.artifacts/second.sql", "CREATE TABLE IF NOT EXISTS repaired (id INTEGER); INSERT INTO repaired VALUES (2);");

			await Migrator.Create(migration.Provider).MigrateAsync(migration, context, TestContext.Current.CancellationToken);

			Assert.Equal(2L, await CountAudits());
			await using var connection = Connection("sqlite", database);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT id FROM repaired";
			Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
		}
		finally { ClearSqlitePool("sqlite"); }

		async Task<long> CountAudits()
		{
			await using var connection = Connection("sqlite", database);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM migration_audit";
			return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
		}
	}

	[Theory]
	[InlineData("migration/schema.sql")]
	[InlineData("../outside.sql")]
	[InlineData(".migration/private.sql")]
	[InlineData(".migration/.artifacts/../outside.sql")]
	[InlineData(".migration/.artifacts-neighbor/schema.sql")]
	public void Context_ScriptOutsideMigrationDirectory_IsRejected(string relative)
	{
		using var directory = new MigrationTestDirectory();
		var script = directory.Script(".migration/.artifacts/schema.sql", "SELECT 1;");
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		Assert.Equal(Path.Combine(directory.Path, ".migration", ".artifacts", "schema.sql"), context.GetScriptPath(script));
		script.Path = relative;

		Assert.Throws<InvalidDataException>(() => context.GetScriptPath(script));
	}
	#endregion

	#region 辅助方法
	private static DbConnection Connection(string provider, string database) => provider switch
	{
		"sqlite" => new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = database }.ConnectionString),
		"duckdb" => new DuckDB.NET.Data.DuckDBConnection("DataSource=" + database),
		_ => throw new ArgumentOutOfRangeException(nameof(provider)),
	};

	private static void ClearSqlitePool(string provider)
	{
		if(provider == "sqlite") Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
	}
	#endregion
}
