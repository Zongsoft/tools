using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationPlanTest
{
	#region 常量定义
	// This literal is the portable wire representation, independent of serializer options and platform newlines.
	private const string CANONICAL_JSON = "{\"Name\":\"zongsoft.web\",\"Version\":\"1.1.0\",\"Runtime\":\"linux-x64\",\"Steps\":[{\"Provider\":\"sqlite\",\"DatabaseIndex\":0,\"Settings\":{},\"Scripts\":[{\"Path\":\".migration/.artifacts/schema.sql\",\"Checksum\":\"17DB4FD369EDB9244B9F91D9AEED145C3D04AD8BA6E95D06247F07A63527D11A\"}],\"Buckets\":[]}],\"Databases\":[{\"Name\":\"hosting\",\"Provider\":\"sqlite\",\"Settings\":{\"CommandTimeout\":\"300s\"},\"Options\":{\"Path\":\"/var/lib/zongsoft/hosting.db\",\"CommandTimeout\":\"300s\",\"Charset\":\"UTF-8\"},\"Users\":[]}]}";
	#endregion

	#region 测试方法
	[Fact]
	public async Task MainPlan_RuntimeMismatch_RejectsBeforeCreatingStateOrExecutingSqlAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);
		plan.Runtime = MigrationTestDirectory.CurrentRuntime == "win-x64" ? "linux-x64" : "win-x64";

		if(plan.Runtime == "win-x64")
			plan.Databases[0].Options["Path"] = "C:/Zongsoft/must-not-run.db";
		var file = directory.Write(".migration/migration.json", plan.Serialize());
		var state = Path.Combine(directory.Path, "state");

		var error = await RunAsync("apply", file, state, 1);

		Assert.Contains(plan.Runtime, error);
		Assert.Contains(MigrationTestDirectory.CurrentRuntime, error);
		Assert.False(Directory.Exists(state));
	}

	[Fact]
	public void Fingerprint_FixedSqlitePlan_MatchesPortableCompactJsonGoldenHash()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);

		var fingerprint = plan.Fingerprint();

		Assert.Equal("4D98AFD0485B59EC20EEC62A25999249C4B5A17BBAA5BB6DE4000F911DB06C1B", fingerprint);
		Assert.Equal("SELECT 1;", File.ReadAllText(plan.Steps[0].Scripts[0].Source));
		Assert.DoesNotContain("\r", CANONICAL_JSON);
		Assert.DoesNotContain("\n", CANONICAL_JSON);
	}

	[Fact]
	public void Fingerprint_LoadedCrLfAndLfPlans_EqualTheSamePortableGoldenHash()
	{
		using var directory = new MigrationTestDirectory();
		var json = Plan(directory).Serialize().Replace("\r\n", "\n");
		var windows = Path.Combine(directory.Path, "windows.json");
		var linux = Path.Combine(directory.Path, "linux.json");
		File.WriteAllText(windows, json.Replace("\n", "\r\n"), new UTF8Encoding(false));
		File.WriteAllText(linux, json, new UTF8Encoding(false));
		Assert.Contains("\r\n", File.ReadAllText(windows));
		Assert.DoesNotContain("\r", File.ReadAllText(linux));
		var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CANONICAL_JSON)));

		Assert.Equal(expected, MigrationPlan.Load(windows).Fingerprint());
		Assert.Equal(expected, MigrationPlan.Load(linux).Fingerprint());
	}

	[Fact]
	public void Fingerprint_LiteralCredentialNewline_IsPreservedRatherThanNormalized()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);
		var task = plan.Steps[0];
		task.Provider = "postgres";
		plan.Databases[0] = new() { Provider = "postgres", Name = "hosting", Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "localhost", ["UserName"] = "operator", ["Password"] = "first\r\nsecond" } };
		plan.Validate();
		var file = directory.Write(".migration/migration.json", plan.Serialize());
		var loaded = MigrationPlan.Load(file);
		var original = loaded.Fingerprint();

		Assert.Equal("first\r\nsecond", loaded.Databases[0].Settings["Password"]);
		loaded.Databases[0].Settings["Password"] = "first\nsecond";
		Assert.NotEqual(original, loaded.Fingerprint());
		Assert.Equal(original, plan.Fingerprint());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Serialize_RefinedContract_OmitsNullMembersAndRetainsExplicitValues(bool metadata)
	{
		var plan = new MigrationPlan
		{
			Name = "test",
			Version = "1.0.0",
			Runtime = "linux-x64",
			Title = metadata ? "" : null,
			Summary = metadata ? "summary" : null,
			Description = metadata ? "description" : null,
			Databases = [new() { Provider = "postgres", Name = "hosting", Settings = new() { ["Server"] = "localhost", ["Password"] = "operator" }, Users = [new() { Name = "application", Password = "initial" }] }],
			Steps =
			[
				new() { Provider = "postgres", DatabaseIndex = 0 },
				new() { Provider = "amazon.s3", Settings = new() { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" }, Buckets = [new() { Name = "attachments", Public = false }] },
			],
		};
		plan.Validate();

		using var document = JsonDocument.Parse(plan.Serialize());

		var root = document.RootElement;
		Assert.Equal(plan.Name, root.GetProperty("Name").GetString());
		Assert.False(root.TryGetProperty("Package", out _));
		Assert.Null(typeof(MigrationPlan).GetProperty("Package"));
		Assert.False(root.TryGetProperty("Tasks", out _));
		Assert.Null(typeof(MigrationPlan.Step).GetProperty("Database"));
		var steps = root.GetProperty("Steps").EnumerateArray().ToArray();
		Assert.Equal(2, steps.Length);
		Assert.All(steps, step =>
		{
			Assert.False(step.TryGetProperty("Id", out _));
			Assert.False(step.TryGetProperty("Database", out _));
			Assert.False(step.TryGetProperty("Parameters", out _));
			Assert.Equal(JsonValueKind.Object, step.GetProperty("Settings").ValueKind);
			Assert.Equal(0, step.GetProperty("Scripts").GetArrayLength());
		});
		Assert.Equal(0, steps[0].GetProperty("DatabaseIndex").GetInt32());
		Assert.Empty(steps[0].GetProperty("Settings").EnumerateObject());
		Assert.False(steps[1].TryGetProperty("DatabaseIndex", out _));
		Assert.False(steps[1].GetProperty("Buckets")[0].GetProperty("Public").GetBoolean());
		var database = root.GetProperty("Databases")[0];
		Assert.False(database.TryGetProperty("Id", out _));
		Assert.Null(typeof(MigrationPlan.Database).GetProperty("Id"));
		Assert.False(database.TryGetProperty("Parameters", out _));
		Assert.Equal("operator", database.GetProperty("Settings").GetProperty("Password").GetString());
		var user = database.GetProperty("Users")[0];
		Assert.False(user.TryGetProperty("Host", out _));
		Assert.Equal(0, user.GetProperty("Roles").GetArrayLength());
		Assert.Equal(5, user.GetProperty("Privileges").GetArrayLength());
		foreach(var property in new[] { "Title", "Summary", "Description" })
			Assert.Equal(metadata, root.TryGetProperty(property, out _));

		if(metadata)
		{
			Assert.Equal("", root.GetProperty("Title").GetString());
			Assert.Equal("summary", root.GetProperty("Summary").GetString());
			Assert.Equal("description", root.GetProperty("Description").GetString());
		}
		Assert.Equal(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(root))), plan.Fingerprint());
	}

	[Theory]
	[InlineData(null, "sqlite")]
	[InlineData(-1, "sqlite")]
	[InlineData(1, "sqlite")]
	[InlineData(int.MaxValue, "sqlite")]
	[InlineData(0, "duckdb")]
	[InlineData(0, "amazon.s3")]
	public void Validate_InvalidDatabaseIndex_RejectsBeforeExecution(int? index, string provider)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);
		plan.Steps[0].DatabaseIndex = index;
		plan.Steps[0].Provider = provider;

		if(provider == "amazon.s3")
		{
			plan.Steps.Add(new() { Provider = "sqlite", DatabaseIndex = 0 });
			plan.Steps[0].Scripts.Clear();
			plan.Steps[0].Settings = new() { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" };
		}

		Assert.Throws<InvalidDataException>(plan.Validate);
	}

	[Fact]
	public async Task MainPlan_RuntimeChecksFingerprintAndRejectsReorderedTasksAsync()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);
		plan.Runtime = MigrationTestDirectory.CurrentRuntime;
		plan.Databases[0].Options["Path"] = Path.Combine(directory.Path, "hosting.db");
		plan.Steps[0].Scripts.Add(directory.Script(".migration/.artifacts/seed.sql", "INSERT INTO samples VALUES (1);"));
		plan.Steps.Add(new()
		{
			Provider = "amazon.s3",
			Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test;value=retained" },
			Buckets = [new() { Name = "attachments", Public = true }, new() { Name = "archives", Public = false }],
		});
		var file = directory.Write(".migration/migration.json", plan.Serialize());

		var state = Path.Combine(directory.Path, "state");
		directory.Write("state/ready", plan.Fingerprint());

		await RunAsync("check", file, state, 0);

		Assert.DoesNotContain("Source", File.ReadAllText(file));
		plan.Steps.Reverse();
		directory.Write(".migration/migration.json", plan.Serialize());
		await RunAsync("check", file, state, 1);
	}

	[Fact]
	public void MainAssembly_DoesNotReferenceRuntimeOrDatabaseDrivers()
	{
		var assembly = typeof(MigrationLoader).Assembly;
		var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

		Assert.Equal(assembly, typeof(MigrationPlan).Assembly);
		Assert.DoesNotContain(references, name => name.StartsWith("Zongsoft.Tools.Migrator.Executor", StringComparison.Ordinal) || name.StartsWith("Zongsoft.Tools.Packager", StringComparison.Ordinal));
		Assert.DoesNotContain(references, name => name.StartsWith("AWSSDK.", StringComparison.Ordinal));
		foreach(var driver in new[] { "Microsoft.Data.SqlClient", "MySqlConnector", "Npgsql", "Microsoft.Data.Sqlite", "DuckDB.NET.Data", "TDengine" })
			Assert.DoesNotContain(driver, references);
	}

	[Fact]
	public async Task MainPlan_RuntimeAppliesPreparedSqliteScriptsInOrderAndRejectsTamperingAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "hosting.db");
		directory.Write("input/db.migration", "[sqlite]\n./sql/*.sql\n");
		directory.Write("input/sqlite.ini", "[sqlite]\nDatabase=" + database + "\n");
		directory.Write("input/sql/020-seed.sql", "INSERT INTO samples VALUES (2, '附件;ready');");
		directory.Write("input/sql/010-schema.sql", "CREATE TABLE IF NOT EXISTS samples (id INTEGER PRIMARY KEY, title TEXT); DELETE FROM samples; INSERT INTO samples VALUES (1, 'first');");
		directory.Write("input/sql/030-verify.sql", "CREATE TABLE IF NOT EXISTS verification (value INTEGER CHECK (value=1)); INSERT INTO verification SELECT CASE WHEN (SELECT COUNT(*) FROM samples)=2 AND (SELECT title FROM samples WHERE id=2)='附件;ready' THEN 1 ELSE 0 END;");
		var other = Path.Combine(directory.Path, "other.db");
		directory.Write("input/other.migration", "[sqlite]\n./other.sql\n");
		directory.Write("input/other.ini", "[sqlite]\nDatabase=" + other + "\n");
		directory.Write("input/other.sql", "CREATE TABLE IF NOT EXISTS independent (id INTEGER);");
		var plan = new MigrationLoader(null).Load("input/db.migration;input/other.migration", directory.Path, "zongsoft.daemon", "1.1.0", MigrationTestDirectory.CurrentRuntime);
		Assert.Equal(2, plan.Steps.Count);
		var scripts = plan.Steps[0].Scripts;
		Assert.Equal(".migration/.artifacts/sqlite/4.sql", Assert.Single(plan.Steps[1].Scripts).Path);
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/1.sql", ".migration/.artifacts/sqlite/2.sql", ".migration/.artifacts/sqlite/3.sql" }, scripts.Select(script => script.Path));
		foreach(var script in plan.Steps.SelectMany(task => task.Scripts))
			directory.Write(script.Path, script.Content);
		var file = directory.Write(".migration/migration.json", plan.Serialize());
		var state = Path.Combine(directory.Path, "state");

		await RunAsync("apply", file, state, 0);
		await RunAsync("check", file, state, 0);

		Assert.True(File.Exists(database));
		Assert.True(File.Exists(other));
		Assert.Equal(plan.Fingerprint(), File.ReadAllText(Path.Combine(state, "ready")));
		Assert.DoesNotContain("Content", File.ReadAllText(file));
		Assert.DoesNotContain("Source", File.ReadAllText(file));
		directory.Write(scripts[1].Path, "INSERT INTO samples VALUES (3, 'tampered');");

		await RunAsync("apply", file, state, 1);
		await RunAsync("check", file, state, 1);

		Assert.False(File.Exists(Path.Combine(state, "ready")));
		using var status = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "status.json")));
		Assert.Equal("failed", status.RootElement.GetProperty("status").GetString());
	}

	[Fact]
	public async Task MainPlan_UnpaddedArtifactsBeyondNine_ExecutorPreservesPlanOrderAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[sqlite]\nsql/*.sql\n");
		directory.Write("db.ini", "[sqlite]\nDatabase=" + Path.Combine(directory.Path, "ordered.db") + "\n");
		for(var number = 1; number <= 11; number++)
		{
			var schema = number == 1 ? "CREATE TABLE sequence (value INTEGER, previous INTEGER CHECK (previous = value - 1)); " : "";
			var verify = number == 11 ? " CREATE TABLE verified (total INTEGER CHECK(total = 11)); INSERT INTO verified SELECT COUNT(*) FROM sequence;" : "";
			directory.Write($"sql/{number:D2}.sql", schema + $"INSERT INTO sequence SELECT {number}, COUNT(*) FROM sequence;" + verify);
		}
		var plan = new MigrationLoader(null).Load("db.migration", directory.Path, "test", "1.0.0", MigrationTestDirectory.CurrentRuntime);
		var scripts = Assert.Single(plan.Steps).Scripts;
		Assert.Equal(Enumerable.Range(1, 11).Select(number => $".migration/.artifacts/sqlite/{number}.sql"), scripts.Select(script => script.Path));
		Assert.Single(plan.Databases);
		Assert.Equal(0, Assert.Single(plan.Steps).DatabaseIndex);
		Assert.NotEqual(scripts.Select(script => script.Path), scripts.Select(script => script.Path).Order(StringComparer.Ordinal));
		foreach(var script in scripts)
			directory.Write(script.Path, script.Content);
		var file = directory.Write(".migration/migration.json", plan.Serialize());
		var state = Path.Combine(directory.Path, "state");

		await RunAsync("apply", file, state, 0);
		await RunAsync("check", file, state, 0);

		Assert.Equal(plan.Fingerprint(), File.ReadAllText(Path.Combine(state, "ready")));
		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "status.json")));
		Assert.Equal("complete", status.RootElement.GetProperty("phase").GetString());
		Assert.False(status.RootElement.TryGetProperty("task", out _));
	}
	#endregion

	#region 辅助方法
	private static async Task<string> RunAsync(string command, string plan, string state, int expectedExitCode)
	{
		using var process = new Process
		{
			StartInfo = new()
			{
				FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(plan),
			},
		};
		process.StartInfo.ArgumentList.Add(Path.Combine(MigrationTestDirectory.GetRuntimeDirectory(), "Zongsoft.Tools.Migrator.Executor.dll"));
		process.StartInfo.ArgumentList.Add(command);
		process.StartInfo.ArgumentList.Add(plan);
		process.StartInfo.ArgumentList.Add(state);
		Assert.True(process.Start());
		var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		try
		{
			await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
			Assert.True(process.ExitCode == expectedExitCode, $"Expected exit code {expectedExitCode}, got {process.ExitCode}: {await error}");
			return await error;
		}
		finally
		{
			if(!process.HasExited)
			{
				process.Kill(true);
				await process.WaitForExitAsync();
			}
		}
	}

	private static MigrationPlan Plan(MigrationTestDirectory directory) => new()
	{
		Name = "zongsoft.web",
		Version = "1.1.0",
		Runtime = "linux-x64",
		Steps = [new() { Provider = "sqlite", DatabaseIndex = 0, Scripts = [directory.Script(".migration/.artifacts/schema.sql", "SELECT 1;")] }],
		Databases = [new() { Provider = "sqlite", Name = "hosting", Settings = new() { ["CommandTimeout"] = "300s" }, Options = new() { ["Path"] = "/var/lib/zongsoft/hosting.db", ["CommandTimeout"] = "300s", ["Charset"] = "UTF-8" } }],
	};
	#endregion
}
