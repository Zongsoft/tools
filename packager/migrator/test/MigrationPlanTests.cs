using System;
using System.Text;
using System.Security.Cryptography;

using Xunit;

namespace Zongsoft.Tools.Packager.Migration.Tests;

public sealed class MigrationPlanTests
{
	#region 常量定义
	private const string CANONICAL_JSON = "{\"FormatVersion\":1,\"Package\":\"zongsoft.web\",\"Version\":\"1.1.0\",\"Tasks\":[{\"Id\":\"0001-sqlite\",\"Provider\":\"sqlite\",\"Parameters\":{\"Database\":\"/var/lib/zongsoft/hosting.db\"},\"Scripts\":[{\"Path\":\".migration/.artifacts/schema.sql\",\"Checksum\":\"17DB4FD369EDB9244B9F91D9AEED145C3D04AD8BA6E95D06247F07A63527D11A\"}],\"Buckets\":[]}]}";
	#endregion

	#region 测试方法
	[Fact]
	public void Load_CanonicalJsonPreservesFingerprintAndHasNoSourceMember()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write(".migration/migration.json", CANONICAL_JSON);

		var loaded = MigrationPlan.Load(file);

		Assert.Equal("0001-sqlite", Assert.Single(loaded.Tasks).Id);
		Assert.Equal(".migration/.artifacts/schema.sql", Assert.Single(loaded.Tasks[0].Scripts).Path);
		Assert.Equal("41E02CE79354DB657EEFB817FD21A06A4A306457FB95BD3C1D5BC493EB3C459F", loaded.Fingerprint());
		Assert.Null(typeof(MigrationPlan.Script).GetProperty("Source"));
		Assert.DoesNotContain(typeof(MigrationPlan.Script).GetFields(), field => field.Name.Contains("Source", StringComparison.Ordinal));
	}
	#endregion
}
