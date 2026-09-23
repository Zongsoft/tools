using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

using Xunit;

namespace Zongsoft.Tools.Migrator.Migration.Tests;

public sealed class MigrationPlanTest
{
	#region 常量定义
	private const string CANONICAL_JSON = "{\"Name\":\"zongsoft.web\",\"Version\":\"1.1.0\",\"Runtime\":\"linux-x64\",\"Steps\":[{\"Provider\":\"sqlite\",\"DatabaseIndex\":0,\"Settings\":{},\"Scripts\":[{\"Path\":\".migration/.artifacts/schema.sql\",\"Checksum\":\"17DB4FD369EDB9244B9F91D9AEED145C3D04AD8BA6E95D06247F07A63527D11A\"}],\"Buckets\":[]}],\"Databases\":[{\"Name\":\"hosting\",\"Provider\":\"sqlite\",\"Settings\":{\"CommandTimeout\":\"300s\"},\"Options\":{\"Path\":\"/var/lib/zongsoft/hosting.db\",\"CommandTimeout\":\"300s\",\"Charset\":\"UTF-8\"},\"Users\":[]}]}";
	#endregion

	#region 测试方法
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("win-arm64")]
	[InlineData("osx-x64")]
	[InlineData("linux-x86")]
	public void Load_MissingOrUnsupportedRuntime_RejectsPlan(string runtime)
	{
		using var directory = new MigrationTestDirectory();
		var json = runtime == null ? CANONICAL_JSON.Replace("\"Runtime\":\"linux-x64\",", "") : CANONICAL_JSON.Replace("linux-x64", runtime);

		Assert.Throws<InvalidDataException>(() => MigrationPlan.Load(directory.Write(".migration/migration.json", json)));
	}

	[Theory]
	[InlineData("linux-x64", "/data/hosting.db")]
	[InlineData("linux-arm64", "/data/hosting.db")]
	[InlineData("win-x64", "C:/Zongsoft/hosting.db")]
	public void Load_SupportedRuntime_RetainsTargetAndDatabasePath(string runtime, string database)
	{
		using var directory = new MigrationTestDirectory();
		var json = CANONICAL_JSON.Replace("linux-x64", runtime).Replace("/var/lib/zongsoft/hosting.db", database);

		var plan = MigrationPlan.Load(directory.Write(".migration/migration.json", json));

		Assert.Equal(runtime, plan.Runtime);
		Assert.Equal(database, Assert.Single(plan.Databases).Options["Path"]);
	}

	[Fact]
	public void Load_CanonicalJsonPreservesFingerprintAndHasNoSourceMember()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write(".migration/migration.json", CANONICAL_JSON);

		var loaded = MigrationPlan.Load(file);

		Assert.Equal(0, Assert.Single(loaded.Steps).DatabaseIndex);
		Assert.Equal(".migration/.artifacts/schema.sql", Assert.Single(loaded.Steps[0].Scripts).Path);
		Assert.Equal("4D98AFD0485B59EC20EEC62A25999249C4B5A17BBAA5BB6DE4000F911DB06C1B", loaded.Fingerprint());
		Assert.Null(typeof(MigrationPlan.Script).GetProperty("Source"));
		Assert.DoesNotContain(typeof(MigrationPlan.Script).GetFields(), field => field.Name.Contains("Source", StringComparison.Ordinal));
	}
	[Fact]
	public void Serialize_StepsAndSettings_ExcludesLegacyNamesAndStepIdentifier()
	{
		using var directory = new MigrationTestDirectory();
		var plan = MigrationPlan.Load(directory.Write(".migration/migration.json", CANONICAL_JSON));

		using var document = JsonDocument.Parse(plan.Serialize());

		var root = document.RootElement;
		Assert.Equal(plan.Name, root.GetProperty("Name").GetString());
		Assert.False(root.TryGetProperty("Package", out _));
		Assert.Null(typeof(MigrationPlan).GetProperty("Package"));
		var steps = root.GetProperty("Steps");
		Assert.Equal(1, steps.GetArrayLength());
		Assert.Equal("sqlite", steps[0].GetProperty("Provider").GetString());
		Assert.Equal(0, steps[0].GetProperty("DatabaseIndex").GetInt32());
		Assert.Equal(JsonValueKind.Object, steps[0].GetProperty("Settings").ValueKind);
		Assert.False(steps[0].TryGetProperty("Id", out _));
		Assert.False(root.TryGetProperty("Tasks", out _));
		Assert.False(steps[0].TryGetProperty("Parameters", out _));
		var database = root.GetProperty("Databases")[0];
		Assert.False(database.TryGetProperty("Id", out _));
		Assert.Equal("300s", database.GetProperty("Settings").GetProperty("CommandTimeout").GetString());
		Assert.False(database.TryGetProperty("Parameters", out _));
		Assert.Null(typeof(MigrationPlan.Step).GetProperty("Id"));
		Assert.Null(typeof(MigrationPlan.Step).GetProperty("Database"));
		Assert.Null(typeof(MigrationPlan.Database).GetProperty("Id"));
		Assert.False(steps[0].TryGetProperty("Database", out _));
	}
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Serialize_NullMembersOmittedButValuesRetained_MatchesFingerprint(bool hasMetadata)
	{
		var plan = new MigrationPlan
		{
			Name = "test",
			Version = "1.0.0",
			Runtime = "linux-x64",
			Title = hasMetadata ? "" : null,
			Summary = hasMetadata ? "summary" : null,
			Description = hasMetadata ? "description" : null,
			Databases = [new()
			{
				Provider = "postgres",
				Name = "hosting",
				Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "localhost", ["Password"] = "test" },
				Users = [new() { Name = "application", Password = "test" }],
			}],
			Steps =
			[
				new() { Provider = "postgres", DatabaseIndex = 0 },
				new()
				{
					Provider = "amazon.s3",
					Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" },
					Buckets = [new() { Name = "attachments", Public = false }],
				},
			],
		};
		plan.Validate();

		using var document = JsonDocument.Parse(plan.Serialize());
		var root = document.RootElement;
		Assert.Equal(plan.Name, root.GetProperty("Name").GetString());
		Assert.False(root.TryGetProperty("Package", out _));
		Assert.Null(typeof(MigrationPlan).GetProperty("Package"));

		Assert.Equal(hasMetadata, root.TryGetProperty("Title", out var title));
		Assert.Equal(hasMetadata, root.TryGetProperty("Summary", out var summary));
		Assert.Equal(hasMetadata, root.TryGetProperty("Description", out var description));
		if(hasMetadata)
		{
			Assert.Equal("", title.GetString());
			Assert.Equal("summary", summary.GetString());
			Assert.Equal("description", description.GetString());
		}
		var user = root.GetProperty("Databases")[0].GetProperty("Users")[0];
		Assert.False(user.TryGetProperty("Host", out _));
		Assert.Equal(0, user.GetProperty("Roles").GetArrayLength());
		Assert.Equal(5, user.GetProperty("Privileges").GetArrayLength());
		var steps = root.GetProperty("Steps");
		Assert.Equal(0, steps[0].GetProperty("DatabaseIndex").GetInt32());
		Assert.False(steps[1].TryGetProperty("DatabaseIndex", out _));
		Assert.Equal(0, steps[1].GetProperty("Scripts").GetArrayLength());
		Assert.False(steps[1].GetProperty("Buckets")[0].GetProperty("Public").GetBoolean());
		using var stream = new MemoryStream();
		using(var writer = new Utf8JsonWriter(stream))
			root.WriteTo(writer);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(stream.ToArray())), plan.Fingerprint());
	}
	[Theory]
	[InlineData("LOCALHOST", "localhost", "3306", "3306")]
	[InlineData("localhost", "localhost", "03306", "3306")]
	public void Validate_MySqlSameAccountWithHostOrPortSpellingDifferences_RejectsConflictingPasswords(string firstHost, string secondHost, string firstPort, string secondPort)
	{
		var first = Database("first", firstHost, firstPort, "first-password");
		var second = Database("second", secondHost, secondPort, "second-password");
		var plan = new MigrationPlan
		{
			Name = "test",
			Version = "1.0.0",
			Runtime = "linux-x64",
			Databases = [first, second],
			Steps = [new() { Provider = "mysql", DatabaseIndex = 0 }, new() { Provider = "mysql", DatabaseIndex = 1 }],
		};

		var error = Assert.Throws<InvalidDataException>(plan.Validate);

		Assert.DoesNotContain("first-password", error.ToString());
		Assert.DoesNotContain("second-password", error.ToString());

		static MigrationPlan.Database Database(string name, string host, string port, string password) => new()
		{
			Name = name,
			Provider = "mysql",
			Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "localhost", ["Port"] = port, ["Password"] = "administrator" },
			Users = [new() { Name = "application", Host = host, Password = password }],
		};
	}
	#endregion
}
