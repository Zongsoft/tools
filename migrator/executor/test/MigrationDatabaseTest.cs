using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Migrator.Migration.Tests;

public sealed class MigrationDatabaseTest
{
	[Theory]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	public async Task Apply_MultipleDatabasesAndEmptyTask_InitializesEveryReferencedDatabaseAsync(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var first = FileDatabase(directory, provider, "first");
		var second = FileDatabase(directory, provider, "second");
		var plan = Plan(first, second);
		plan.Steps[0].Scripts.Add(directory.Script(".migration/.artifacts/first.sql", "CREATE TABLE records(id INTEGER); INSERT INTO records VALUES(7);"));

		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		try
		{
			await new MigrationExecutor().ApplyAsync(plan, context, TestContext.Current.CancellationToken);

			Assert.True(File.Exists(first.Options["Path"]));
			Assert.True(File.Exists(second.Options["Path"]));
			Assert.True(MigrationExecutor.IsReady(plan, context.StateDirectory));
			await using var connection = provider == "sqlite" ? (System.Data.Common.DbConnection)new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + first.Options["Path"]) : new DuckDB.NET.Data.DuckDBConnection("DataSource=" + first.Options["Path"]);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT id FROM records";
			Assert.Equal(7L, Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
			await using var emptyConnection = provider == "sqlite" ? (System.Data.Common.DbConnection)new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + second.Options["Path"]) : new DuckDB.NET.Data.DuckDBConnection("DataSource=" + second.Options["Path"]);
			await emptyConnection.OpenAsync(TestContext.Current.CancellationToken);
			await using var emptyCommand = emptyConnection.CreateCommand();
			emptyCommand.CommandText = provider == "sqlite" ? "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='records'" : "SELECT COUNT(*) FROM information_schema.tables WHERE table_name='records'";
			Assert.Equal(0L, Convert.ToInt64(await emptyCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
		}
		finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
	}

	[Fact]
	public async Task Sqlite_NewEncodingAndExistingDatabase_PreservesInitialEncodingAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = FileDatabase(directory, "sqlite", "hosting");
		database.Options["Charset"] = "UTF-16le";
		var plan = Plan(database);
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		try
		{
			await new MigrationExecutor().ApplyAsync(plan, context, TestContext.Current.CancellationToken);
			Assert.Equal("UTF-16le", await Encoding());
			database.Options["Charset"] = "UTF-8";
			await new MigrationExecutor().ApplyAsync(plan, context, TestContext.Current.CancellationToken);
			Assert.Equal("UTF-16le", await Encoding());
		}
		finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }

		async Task<string> Encoding()
		{
			await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + database.Options["Path"]);
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			command.CommandText = "PRAGMA encoding";
			return (string)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
		}
	}

	[Fact]
	public async Task Apply_TwoDatabases_InitializesAndCreatesUsersBeforeSqlAndGrantsAfterAllSqlAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "first"), FileDatabase(directory, "sqlite", "second"));
		var calls = new List<string>();
		var driver = new RecordingDatabase(calls);

		await new MigrationExecutor(_ => driver).ApplyAsync(plan, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "initialize:first", "initialize:second", "users:first", "users:second", "sql:first", "sql:second", "grant:first", "grant:second" }, calls);
	}

	[Fact]
	public async Task Apply_GrantFailureAndRetry_InvalidatesReadyRepeatsSqlAndRedactsSecretsAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "hosting"));
		var calls = new List<string>();
		var logs = new List<string>();
		var driver = new RecordingDatabase(calls) { FailGrant = true };
		var executor = new MigrationExecutor(_ => driver);
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"), logs.Add);
		directory.Write("state/ready", plan.Fingerprint());

		var error = await Assert.ThrowsAsync<MigrationException>(() => executor.ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
		Assert.DoesNotContain("sensitive-password", error.ToString() + File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")) + string.Join("\n", logs));
		driver.FailGrant = false;
		await executor.ApplyAsync(plan, context, TestContext.Current.CancellationToken);
		Assert.True(MigrationExecutor.IsReady(plan, context.StateDirectory));
		Assert.Equal(2, calls.Count(call => call == "sql:hosting"));
		Assert.Equal(2, calls.Count(call => call == "grant:hosting"));
	}

	[Fact]
	public async Task Apply_TamperedLastSql_RejectsBeforeAnyDatabaseInitializationAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "first"), FileDatabase(directory, "sqlite", "second"));
		plan.Steps[1].Scripts.Add(directory.Script(".migration/.artifacts/schema.sql", "SELECT 1;"));
		directory.Write(".migration/.artifacts/schema.sql", "SELECT 2;");
		var calls = new List<string>();

		await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ => new RecordingDatabase(calls)).ApplyAsync(plan, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Empty(calls);
	}

	[Theory]
	[InlineData("databases")]
	[InlineData("users")]
	[InlineData("permissions")]
	public async Task Apply_DatabasePhaseFailure_RecordsPhaseAndTargetWithoutStepNumberAsync(string phase)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "hosting"));
		var driver = new RecordingDatabase([]) { FailPhase = phase };
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ => driver).ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("failed", status.RootElement.GetProperty("status").GetString());
		Assert.Equal(phase, status.RootElement.GetProperty("phase").GetString());
		Assert.Equal(0, status.RootElement.GetProperty("databaseIndex").GetInt32());
		Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("step").ValueKind);
		Assert.False(status.RootElement.TryGetProperty("task", out _));
		Assert.False(status.RootElement.TryGetProperty("database", out _));
		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
	}

	[Fact]
	public async Task Apply_SecondStepFailsOnSameDatabase_RecordsOneBasedStepWithoutIdentifierAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = FileDatabase(directory, "sqlite", "hosting");
		var plan = Plan(database);
		plan.Steps.Add(new() { Provider = "sqlite", DatabaseIndex = 0 });
		var driver = new RecordingDatabase([]) { FailStep = 2 };
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ => driver).ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("steps", status.RootElement.GetProperty("phase").GetString());
		Assert.Equal(2, status.RootElement.GetProperty("step").GetInt32());
		Assert.Equal(0, status.RootElement.GetProperty("databaseIndex").GetInt32());
		Assert.False(status.RootElement.TryGetProperty("task", out _));
		Assert.False(status.RootElement.TryGetProperty("database", out _));
		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
	}
	[Theory]
	[InlineData("sqlite", null)]
	[InlineData("sqlite", -1)]
	[InlineData("sqlite", 1)]
	[InlineData("sqlite", int.MaxValue)]
	[InlineData("duckdb", 0)]
	[InlineData("amazon.s3", 0)]
	public async Task Apply_InvalidDatabaseIndex_RejectsBeforeResolvingAnyDriverAsync(string provider, int? index)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "hosting"));
		var step = new MigrationPlan.Step { Provider = provider, DatabaseIndex = index };

		if(provider == "amazon.s3")
			step.Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" };
		plan.Steps.Add(step);
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		var resolutions = 0;

		await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ =>
		{
			resolutions++;
			return new RecordingDatabase([]);
		}).ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.Equal(0, resolutions);
		Assert.False(File.Exists(plan.Databases[0].Options["Path"]));
		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("validation", status.RootElement.GetProperty("phase").GetString());
		Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("step").ValueKind);
		Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("databaseIndex").ValueKind);
		Assert.False(status.RootElement.TryGetProperty("database", out _));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(-1)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void Context_InvalidDatabaseIndex_ThrowsInvalidDataException(int? index)
	{
		using var directory = new MigrationTestDirectory();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state")) { Databases = [FileDatabase(directory, "sqlite", "hosting")] };

		Assert.Throws<InvalidDataException>(() => context.GetDatabase(index));
	}

	[Fact]
	public void Context_ZeroAndLastDatabaseIndex_ReturnsExactArrayEntries()
	{
		using var directory = new MigrationTestDirectory();
		var first = FileDatabase(directory, "sqlite", "first");
		var last = FileDatabase(directory, "sqlite", "last");
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state")) { Databases = [first, last] };

		Assert.Same(first, context.GetDatabase(0));
		Assert.Same(last, context.GetDatabase(1));
	}

	[Fact]
	public async Task Apply_ReorderedRepeatedDatabaseIndexes_RoutesByIndexAndPreservesStepOrderAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(FileDatabase(directory, "sqlite", "first"), FileDatabase(directory, "sqlite", "second"));
		plan.Steps = [new() { Provider = "sqlite", DatabaseIndex = 1 }, new() { Provider = "sqlite", DatabaseIndex = 0 }, new() { Provider = "sqlite", DatabaseIndex = 1 }];
		var calls = new List<string>();

		await new MigrationExecutor(_ => new RecordingDatabase(calls)).ApplyAsync(plan, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "sql:second", "sql:first", "sql:second" }, calls.Where(call => call.StartsWith("sql:", StringComparison.Ordinal)));
		Assert.Equal(new[] { "initialize:first", "initialize:second" }, calls.Where(call => call.StartsWith("initialize:", StringComparison.Ordinal)));
	}
	private static MigrationPlan.Database FileDatabase(MigrationTestDirectory directory, string provider, string name) => new()
	{
		Provider = provider,
		Name = name,
		Options = new(StringComparer.OrdinalIgnoreCase) { ["Path"] = Path.Combine(directory.Path, name + ".db") },
	};

	private static MigrationPlan Plan(params MigrationPlan.Database[] databases) => new()
	{
		Name = "database-tests",
		Version = "1.0.0",
		Runtime = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
		Databases = databases.ToList(),
		Steps = databases.Select((database, index) => new MigrationPlan.Step { Provider = database.Provider, DatabaseIndex = index }).ToList(),
	};

	private sealed class RecordingDatabase(List<string> calls) : Migrator.Database
	{
		public bool FailGrant { get; set; }
		public string FailPhase { get; set; }
		public int FailStep { get; set; }

		public override Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
		{
			calls.Add("initialize:" + database.Name);
			if(this.FailPhase == "databases")
				throw new InvalidOperationException("initialization failed");
			return Task.CompletedTask;
		}

		public override Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
		{
			calls.Add("users:" + database.Name);
			if(this.FailPhase == "users")
				throw new InvalidOperationException("user creation failed");
			return Task.CompletedTask;
		}
		public override Task MigrateAsync(MigrationPlan.Step task, MigrationContext context, CancellationToken cancellation = default)
		{
			calls.Add("sql:" + context.GetDatabase(task.DatabaseIndex).Name);
			if(this.FailStep > 0 && context.StepNumber == this.FailStep)
				throw new InvalidOperationException("SQL failed");
			return Task.CompletedTask;
		}
		public override Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
		{
			calls.Add("grant:" + database.Name);
			if(this.FailGrant || this.FailPhase == "permissions")
				throw new InvalidOperationException("sensitive-password");
			return Task.CompletedTask;
		}
	}
}
