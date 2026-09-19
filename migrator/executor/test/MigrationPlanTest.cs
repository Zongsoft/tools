using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

using Xunit;

namespace Zongsoft.Tools.Migrator.Migration.Tests;

public sealed class MigrationPlanTest
{
	#region 常量定义
	private const string CANONICAL_JSON = "{\"FormatVersion\":1,\"Package\":\"zongsoft.web\",\"Version\":\"1.1.0\",\"Runtime\":\"linux-x64\",\"Tasks\":[{\"Id\":\"0001-sqlite\",\"Provider\":\"sqlite\",\"Parameters\":{\"Database\":\"/var/lib/zongsoft/hosting.db\"},\"Scripts\":[{\"Path\":\".migration/.artifacts/schema.sql\",\"Checksum\":\"17DB4FD369EDB9244B9F91D9AEED145C3D04AD8BA6E95D06247F07A63527D11A\"}],\"Buckets\":[]}]}";
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
		Assert.Equal(database, Assert.Single(plan.Tasks).Parameters["Database"]);
	}

	[Fact]
	public void Load_CanonicalJsonPreservesFingerprintAndHasNoSourceMember()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write(".migration/migration.json", CANONICAL_JSON);

		var loaded = MigrationPlan.Load(file);

		Assert.Equal("0001-sqlite", Assert.Single(loaded.Tasks).Id);
		Assert.Equal(".migration/.artifacts/schema.sql", Assert.Single(loaded.Tasks[0].Scripts).Path);
		Assert.Equal("82F5AF8E6B7FBC471E5CBB60B784C1D6A74107AF78BBB9A4D08DE81E69710BED", loaded.Fingerprint());
		Assert.Null(typeof(MigrationPlan.Script).GetProperty("Source"));
		Assert.DoesNotContain(typeof(MigrationPlan.Script).GetFields(), field => field.Name.Contains("Source", StringComparison.Ordinal));
	}
	#endregion
}
