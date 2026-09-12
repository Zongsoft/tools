using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using Zongsoft.Tools.Packager.Migration;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class MigrationPlanTests
{
	#region 常量定义
	// This literal is the portable wire representation, independent of serializer options and platform newlines.
	private const string CANONICAL_JSON = "{\"FormatVersion\":1,\"Package\":\"zongsoft.web\",\"Version\":\"1.1.0\",\"Tasks\":[{\"Id\":\"0001-sqlite\",\"Provider\":\"sqlite\",\"Parameters\":{\"Database\":\"/var/lib/zongsoft/hosting.db\"},\"Scripts\":[{\"Path\":\".migration/.artifacts/schema.sql\",\"Checksum\":\"17DB4FD369EDB9244B9F91D9AEED145C3D04AD8BA6E95D06247F07A63527D11A\"}],\"Buckets\":[]}]}";
	#endregion

	#region 测试方法
	[Fact]
	public void Fingerprint_FixedSqlitePlan_MatchesPortableCompactJsonGoldenHash()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);

		var fingerprint = plan.Fingerprint();

		Assert.Equal("41E02CE79354DB657EEFB817FD21A06A4A306457FB95BD3C1D5BC493EB3C459F", fingerprint);
		Assert.Equal("SELECT 1;", File.ReadAllText(plan.Tasks[0].Scripts[0].Source));
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
		var task = plan.Tasks[0];
		task.Provider = "postgres";
		task.Id = "0001-postgres";
		task.Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Server"] = "localhost", ["Database"] = "hosting", ["UserName"] = "operator", ["Password"] = "first\r\nsecond" };
		var file = directory.Write(".migration/migration.json", plan.Serialize());
		var loaded = MigrationPlan.Load(file);
		var original = loaded.Fingerprint();

		Assert.Equal("first\r\nsecond", loaded.Tasks[0].Parameters["Password"]);
		loaded.Tasks[0].Parameters["Password"] = "first\nsecond";
		Assert.NotEqual(original, loaded.Fingerprint());
		Assert.Equal(original, plan.Fingerprint());
	}

	[Fact]
	public async Task MainPlan_RuntimeChecksFingerprintAndRejectsReorderedTasks()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory);
		plan.Tasks[0].Scripts.Add(directory.Script(".migration/.artifacts/seed.sql", "INSERT INTO samples VALUES (1);"));
		plan.Tasks.Add(new()
		{
			Id = "0002-amazon.s3", Provider = "amazon.s3",
			Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test;value=retained" },
			Buckets = [new() { Name = "attachments", Public = true }, new() { Name = "archives", Public = false }],
		});
		var file = directory.Write(".migration/migration.json", plan.Serialize());

		var state = Path.Combine(directory.Path, "state");
		directory.Write("state/ready", plan.Fingerprint());

		await RunAsync("check", file, state, 0);

		Assert.DoesNotContain("Source", File.ReadAllText(file));
		plan.Tasks.Reverse();
		directory.Write(".migration/migration.json", plan.Serialize());
		await RunAsync("check", file, state, 1);
	}

	[Fact]
	public void MainAssembly_DoesNotReferenceRuntimeOrDatabaseDrivers()
	{
		var assembly = typeof(MigrationLoader).Assembly;
		var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

		Assert.Equal(assembly, typeof(MigrationPlan).Assembly);
		Assert.DoesNotContain(references, name => name.StartsWith("Zongsoft.Tools.Packager.Migrat", StringComparison.Ordinal));
		Assert.DoesNotContain(references, name => name.StartsWith("AWSSDK.", StringComparison.Ordinal));
		foreach(var driver in new[] { "Microsoft.Data.SqlClient", "MySqlConnector", "Npgsql", "Microsoft.Data.Sqlite", "DuckDB.NET.Data", "TDengine" })
			Assert.DoesNotContain(driver, references);
	}

	[Fact]
	public async Task MainPlan_RuntimeAppliesPreparedSqliteScriptsInOrderAndRejectsTampering()
	{
		using var directory = new MigrationTestDirectory();
		var database = Path.Combine(directory.Path, "hosting.db");
		directory.Write("input/db.ini", "[sqlite]\n./sql/*.sql\n");
		directory.Write("input/sqlite.env", "Database=/" + database[Path.GetPathRoot(database).Length..].Replace('\\', '/') + "\n");
		directory.Write("input/sql/020-seed.sql", "INSERT INTO samples VALUES (2, '附件;ready');");
		directory.Write("input/sql/010-schema.sql", "CREATE TABLE IF NOT EXISTS samples (id INTEGER PRIMARY KEY, title TEXT); DELETE FROM samples; INSERT INTO samples VALUES (1, 'first');");
		directory.Write("input/sql/030-verify.sql", "CREATE TABLE IF NOT EXISTS verification (value INTEGER CHECK (value=1)); INSERT INTO verification SELECT CASE WHEN (SELECT COUNT(*) FROM samples)=2 AND (SELECT title FROM samples WHERE id=2)='附件;ready' THEN 1 ELSE 0 END;");
		var other = Path.Combine(directory.Path, "other.db");
		directory.Write("input/other.ini", "[sqlite]\n./other.sql\n");
		directory.Write("input/other.env", "[sqlite]\nDatabase=/" + other[Path.GetPathRoot(other).Length..].Replace('\\', '/') + "\n");
		directory.Write("input/other.sql", "CREATE TABLE IF NOT EXISTS independent (id INTEGER);");
		var plan = new MigrationLoader(null).Load("input/db.ini;input/other.ini", directory.Path, "zongsoft.daemon", "1.1.0");
		Assert.Equal(2, plan.Tasks.Count);
		var scripts = plan.Tasks[0].Scripts;
		Assert.Equal(".migration/.artifacts/sqlite/0004.sql", Assert.Single(plan.Tasks[1].Scripts).Path);
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/0001.sql", ".migration/.artifacts/sqlite/0002.sql", ".migration/.artifacts/sqlite/0003.sql" }, scripts.Select(script => script.Path));
		foreach(var script in plan.Tasks.SelectMany(task => task.Scripts)) directory.Write(script.Path, script.Content);
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
	#endregion

	#region 辅助方法
	private static async Task RunAsync(string command, string plan, string state, int expectedExitCode)
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
		process.StartInfo.ArgumentList.Add(Path.Combine(MigrationTestDirectory.GetRuntimeDirectory(), "Zongsoft.Tools.Packager.Migrator.dll"));
		process.StartInfo.ArgumentList.Add(command);
		process.StartInfo.ArgumentList.Add(plan);
		process.StartInfo.ArgumentList.Add(state);
		Assert.True(process.Start());
		var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		try
		{
			await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
			Assert.True(process.ExitCode == expectedExitCode, $"Expected exit code {expectedExitCode}, got {process.ExitCode}: {await error}");
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
		Package = "zongsoft.web", Version = "1.1.0",
		Tasks = [new() { Id = "0001-sqlite", Provider = "sqlite", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = "/var/lib/zongsoft/hosting.db" }, Scripts = [directory.Script(".migration/.artifacts/schema.sql", "SELECT 1;")] }],
	};
	#endregion
}
