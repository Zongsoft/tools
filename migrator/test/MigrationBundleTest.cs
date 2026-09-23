using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationBundleTest
{
	#region 文件集测试
	[Theory]
	[InlineData("linux-x64", "sh", "")]
	[InlineData("linux-arm64", "sh", "")]
	[InlineData("win-x64", "cmd", ".exe")]
	public void Build_StandalonePlan_ProducesPrivatePlanAndTargetSpecificEntryFiles(string runtime, string extension, string executableExtension)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan(directory, runtime);
		using var bundle = MigrationBundle.Build(plan, null, directory.CreateRuntime());
		var entries = bundle.Entries;
		var json = Assert.Single(entries, entry => entry.EntryName == ".migration/migration.json");
		Assert.Equal((UnixFileMode)384, json.Mode);
		Assert.Equal(plan.Fingerprint(), File.ReadAllText(Assert.Single(entries, entry => entry.EntryName == ".migration/id").Source));
		Assert.Equal(plan.Fingerprint(), MigrationPlan.Load(json.Source).Fingerprint());
		Assert.Equal(plan.Fingerprint(), bundle.Fingerprint);
		Assert.Equal(runtime, bundle.Runtime);
		Assert.DoesNotContain("Source", File.ReadAllText(json.Source));
		Assert.DoesNotContain("Content", File.ReadAllText(json.Source));
		Assert.DoesNotContain("\r", File.ReadAllText(json.Source));
		var executable = Assert.Single(entries, entry => entry.EntryName == ".migration/Zongsoft.Tools.Migrator.Executor" + executableExtension);
		Assert.Equal((UnixFileMode)493, executable.Mode);
		var launcher = Assert.Single(entries, entry => entry.EntryName == ".migration/migrate." + extension);
		Assert.Equal((UnixFileMode)493, launcher.Mode);
		var content = File.ReadAllText(launcher.Source);
		Assert.Contains("Zongsoft.Tools.Migrator.Executor", content);
		Assert.DoesNotContain("dotnet", content);
		if(runtime == "win-x64")
			Assert.Contains("\r\n", content);
		else
			Assert.DoesNotContain("\r", content);
		var sql = Assert.Single(entries, entry => entry.EntryName == plan.Steps[0].Scripts[0].Path);
		using var stream = sql.OpenRead();
		Assert.Equal(sql.FileSize, stream.Length);
		Assert.Equal(Encoding.UTF8.GetBytes(plan.Steps[0].Scripts[0].Content), File.ReadAllBytes(sql.Source));
		Assert.DoesNotContain(entries, entry => entry.EntryName is "install.sh" or "uninstall.sh" || entry.EntryName.EndsWith(".service", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, entry => entry.EntryName.Contains("Packager", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("mssql")]
	[InlineData("mysql")]
	[InlineData("postgres")]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	[InlineData("tdengine")]
	[InlineData("amazon.s3")]
	public void Build_ProviderAndArchitecture_CollectsCompleteSelectedNativeDirectory(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var runtimeRoot = directory.CreateRuntime();
		foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
		{
			var parameters = provider == "amazon.s3" ? new Dictionary<string, string> { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" } : provider is "sqlite" or "duckdb" ? new() { ["Database"] = "/var/lib/zongsoft/test.db" } : new() { ["Server"] = "localhost", ["Database"] = "hosting", ["UserName"] = "operator", ["Password"] = "" };
			var plan = new MigrationPlan { Name = "zongsoft.daemon", Version = "1.1.0", Runtime = runtime, Steps = [new() { Provider = provider }] };
			if(provider == "amazon.s3")
				plan.Steps[0].Settings = parameters;
			else
			{
				plan.Steps[0].DatabaseIndex = 0;
				plan.Databases.Add(new() { Provider = provider, Name = "hosting", Settings = provider is "sqlite" or "duckdb" ? new() : parameters, Options = provider is "sqlite" or "duckdb" ? new() { ["Path"] = "/var/lib/zongsoft/test.db" } : new() });
			}
			using var bundle = MigrationBundle.Build(plan, null, runtimeRoot);
			var native = Assert.Single(bundle.Entries, entry => entry.EntryName == ".migration/Zongsoft.Tools.Migrator.Executor");
			Assert.Equal(runtime == "linux-x64" ? (byte)62 : (byte)183, File.ReadAllBytes(native.Source)[18]);
			Assert.Equal("sqlite-" + runtime, File.ReadAllText(Assert.Single(bundle.Entries, entry => entry.EntryName == ".migration/libe_sqlite3.so").Source));
			Assert.Equal("duckdb-" + runtime, File.ReadAllText(Assert.Single(bundle.Entries, entry => entry.EntryName == ".migration/libduckdb.so").Source));
			Assert.Equal(runtime, File.ReadAllText(Assert.Single(bundle.Entries, entry => entry.EntryName == ".migration/assets/manifest.txt").Source));
			Assert.DoesNotContain(bundle.Entries, entry => entry.EntryName.EndsWith(".deps.json", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void Build_MissingNativeEntry_FailsWithExpectedExecutablePath()
	{
		using var directory = new MigrationTestDirectory();
		var runtime = directory.CreateRuntime();
		var executable = Path.Combine(runtime, "linux-x64", "Zongsoft.Tools.Migrator.Executor");
		File.Delete(executable);
		var error = Assert.Throws<FileNotFoundException>(() => MigrationBundle.Build(Plan(directory), null, runtime));
		Assert.Equal(executable, error.FileName);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Build_InvalidNativeEntry_RejectsWrongFormatOrArchitecture(bool wrongArchitecture)
	{
		using var directory = new MigrationTestDirectory();
		var runtime = directory.CreateRuntime();
		var executable = Path.Combine(runtime, "linux-x64", "Zongsoft.Tools.Migrator.Executor");
		var bytes = File.ReadAllBytes(executable);
		bytes[wrongArchitecture ? 18 : 0] = wrongArchitecture ? (byte)183 : (byte)'M';
		File.WriteAllBytes(executable, bytes);
		var error = Assert.Throws<InvalidDataException>(() => MigrationBundle.Build(Plan(directory), null, runtime));
		Assert.Contains("linux-x64", error.Message);
		Assert.Contains("Zongsoft.Tools.Migrator.Executor", error.Message);
	}

	[Fact]
	public void Build_PreprocessedSqlServerBatches_PreserveContentOrderAndChecksums()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[mssql]\n./sql/*.sql\n");
		directory.Write("mssql.env", "[mssql]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		directory.Write("sql/020-seed.sql", "INSERT INTO samples VALUES (N'附件');\r\nGO\r\n");
		directory.Write("sql/010-schema.sql", "CREATE TABLE samples (title NVARCHAR(100));\r\nGO\r\nSELECT N'GO';\r\n");
		directory.Write("second.migration", "[mssql]\n./other.sql\n");
		directory.Write("other.sql", "SELECT N'other task';");
		var plan = new MigrationLoader(null).Load("db.migration;second.migration", directory.Path, "zongsoft.daemon", "1.1.0");
		using var bundle = MigrationBundle.Build(plan, null, directory.CreateRuntime());
		using var json = JsonDocument.Parse(File.ReadAllBytes(Assert.Single(bundle.Entries, entry => entry.EntryName == ".migration/migration.json").Source));
		var tasks = json.RootElement.GetProperty("Steps").EnumerateArray().ToArray();
		Assert.Equal(2, tasks.Length);
		var scripts = tasks.SelectMany(task => task.GetProperty("Scripts").EnumerateArray()).ToArray();
		var expected = new[] { "CREATE TABLE samples (title NVARCHAR(100));", "SELECT N'GO';", "INSERT INTO samples VALUES (N'附件');", "SELECT N'other task';" };
		Assert.Equal(expected.Length, scripts.Length);
		Assert.Equal(expected.Length, bundle.Entries.Count(entry => entry.EntryName.EndsWith(".sql", StringComparison.Ordinal)));
		for(var index = 0; index < expected.Length; index++)
		{
			var path = $".migration/.artifacts/mssql/{index + 1}.sql";
			Assert.Equal(path, scripts[index].GetProperty("Path").GetString());
			var entry = Assert.Single(bundle.Entries, entry => entry.EntryName == path);
			Assert.Equal(expected[index], File.ReadAllText(entry.Source).Trim());
			Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Source))), scripts[index].GetProperty("Checksum").GetString());
			Assert.Equal((UnixFileMode)420, entry.Mode);
		}
	}
	#endregion

	#region 辅助方法
	private static MigrationPlan Plan(MigrationTestDirectory directory, string runtime = "linux-x64") => new()
	{
		Name = "zongsoft.daemon",
		Version = "1.1.0",
		Runtime = runtime,
		Steps = [new() { Provider = "sqlite", DatabaseIndex = 0, Scripts = [directory.Script(".migration/.artifacts/sqlite/1.sql", "CREATE TABLE samples (id INTEGER);")] }],
		Databases = [new() { Provider = "sqlite", Name = "hosting", Options = new() { ["Path"] = runtime == "win-x64" ? "C:/Zongsoft/hosting.db" : "/var/lib/zongsoft/hosting.db" } }],
	};
	#endregion
}
