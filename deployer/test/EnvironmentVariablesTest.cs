using Xunit;

using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Deployer.Tests;

public sealed class EnvironmentVariablesTest
{
	[Fact]
	public void CreateVariables_EnvironmentHierarchyFlattensSectionsAndIsolatesLoads()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "zongsoft_env_root=ancestor\nzongsoft_env_shared=far\nzongsoft_env_parent=retained\nzongsoft_env_reference=$(zongsoft_env_shared)/%zongsoft_env_root%\n[io rustfs]\naccess_key=ancestor-access\nsecret_key=RustFS@2025\n");
		fixture.Write("source/missing/leaf/.env", "ZONGSOFT_ENV_SHARED=near\nzongsoft_env_empty=\nzongsoft_env_unused=$(zongsoft_env_missing)\n[io rustfs]\naccess_key=local-access\n");
		fixture.Write("source/missing/leaf/child/.env", "zongsoft_env_shared=child\nzongsoft_env_child=must-not-load\n");
		var previous = Environment.GetEnvironmentVariable("zongsoft_env_shared");
		var first = Deployer.CreateVariables(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source", "missing", "leaf"));

		Assert.Equal("ancestor", first["zongsoft_env_root"]);
		Assert.Equal("retained", first["zongsoft_env_parent"]);
		Assert.Equal("near", first["zongsoft_env_shared"]);
		Assert.Equal("near/ancestor", first["zongsoft_env_reference"]);
		Assert.Equal("local-access", first["IO_RUSTFS_ACCESS_KEY"]);
		Assert.Equal("RustFS@2025", first["io_rustfs_secret_key"]);
		Assert.Equal(string.Empty, first["zongsoft_env_empty"]);
		Assert.False(first.ContainsKey("zongsoft_env_child"));
		Assert.Throws<FormatException>(() => first["zongsoft_env_unused"]);
		Assert.Equal(previous, Environment.GetEnvironmentVariable("zongsoft_env_shared"));

		fixture.Write("source/missing/leaf/.env", "zongsoft_env_shared=updated\n");
		var second = Deployer.CreateVariables(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source", "missing", "leaf"));
		Assert.Equal("updated/ancestor", second["zongsoft_env_reference"]);
		Assert.Equal("ancestor-access", second["io_rustfs_access_key"]);
		Assert.False(second.ContainsKey("zongsoft_env_empty"));
		Assert.Equal("near/ancestor", first["zongsoft_env_reference"]);
	}

	[Fact]
	public void CreateVariables_EnvironmentFilesRespectConfigurationAndExplicitOptionPriority()
	{
		using var fixture = new DeploymentFixture();
		const string KEY = "zongsoft_env_priority";
		var previous = Environment.GetEnvironmentVariable(KEY);
		try
		{
			Environment.SetEnvironmentVariable(KEY, "process");
			fixture.Write(".env", KEY + "=ancestor\nzongsoft_env_workspace=" + fixture.Root + "\n");
			fixture.Write("source/.env", KEY + "=source\nzongsoft_env_target=$(zongsoft_env_workspace)/$(zongsoft_env_stage)\n");
			fixture.Write("target/appsettings.json", "{\"zongsoft_env_priority\":\"configuration\",\"zongsoft_env_configuration\":\"target-only\"}");
			var options = new Dictionary<string, string>
			{
				["destination"] = "$(zongsoft_env_target)",
				["zongsoft_env_stage"] = "target",
			};
			var source = Path.Combine(fixture.Root, "source");
			var environment = Deployer.CreateVariables(new Dictionary<string, string>(), source);
			Assert.Equal("source", environment[KEY]);
			var configured = Deployer.CreateVariables(options, source);
			Assert.Equal(fixture.Destination, configured["destination"]);
			Assert.Equal("configuration", configured[KEY]);
			Assert.Equal("target-only", configured["zongsoft_env_configuration"]);

			options[KEY] = string.Empty;
			var explicitEmpty = Deployer.CreateVariables(options, source);
			Assert.Equal(string.Empty, explicitEmpty[KEY]);
			Assert.Equal("process", Environment.GetEnvironmentVariable(KEY));
		}
		finally
		{
			Environment.SetEnvironmentVariable(KEY, previous);
		}
	}

	[Fact]
	public void CreateVariables_EnvironmentImportPreservesCoreOrderingAndNullValues()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "#@import settings/defaults.ini\nzongsoft_env_value=local\nzongsoft_env_flag\n");
		fixture.Write("source/settings/defaults.ini", "#@import absent.ini\nzongsoft_env_value=imported\n[io rustfs]\nsecret_key=import-secret\n");

		var variables = Deployer.CreateVariables(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source"));

		Assert.Equal("local", variables["zongsoft_env_value"]);
		Assert.Equal("import-secret", variables["io_rustfs_secret_key"]);
		Assert.True(variables.ContainsKey("zongsoft_env_flag"));
		Assert.Null(variables["zongsoft_env_flag"]);
		using var exclusive = File.Open(Path.Combine(fixture.Root, "source", ".env"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		Assert.True(exclusive.CanWrite);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CreateVariables_InvalidEnvironmentFileFailsBeforeDeployment(bool unreadable)
	{
		using var fixture = new DeploymentFixture();
		var path = Path.Combine(fixture.Root, ".env");
		if(unreadable)
			Directory.CreateDirectory(path);
		else
			fixture.Write(".env", "#@import .env\n");

		if(unreadable)
			Assert.Throws<UnauthorizedAccessException>(() => Deployer.CreateVariables(new Dictionary<string, string>(), fixture.Root));
		else
			Assert.Throws<ProfileException>(() => Deployer.CreateVariables(new Dictionary<string, string>(), fixture.Root));
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}
}
