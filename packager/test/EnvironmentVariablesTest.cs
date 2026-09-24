using System;
using System.IO;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class EnvironmentVariablesTest
{
	[Fact]
	public void GetVariables_EnvironmentHierarchyRespectsDefaultsEnvironmentAndExplicitOptions()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "compilation=ancestor\nzongsoft_env_parent=retained\nzongsoft_env_reference=$(compilation)/%zongsoft_env_parent%\n[io rustfs]\nsecret_key=import-secret\n");
		directory.Write("source/missing/leaf/.env", "COMPILATION=source\narchitecture=Arm64\nzongsoft_env_empty=inherited\nzongsoft_env_blank=\nzongsoft_env_flag\n");
		directory.Write("source/missing/leaf/child/.env", "compilation=child\n");
		var previous = Environment.GetEnvironmentVariable("compilation");
		try
		{
			Environment.SetEnvironmentVariable("compilation", "process");
			var source = Path.Combine(directory.Path, "source", "missing", "leaf");
			var command = new TarCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("tar --architecture:X64 --zongsoft_env_empty:")[0], command, null);
			var defaults = new Variables(PackCommand<Package.Tar>.GetVariables(context));
			Assert.Equal("process", defaults.Compilation);
			var values = PackCommand<Package.Tar>.GetVariables(context, source);
			var variables = new Variables(values);
			Assert.Equal("source", variables.Compilation);
			Assert.Equal("source/retained", variables["zongsoft_env_reference"]);
			Assert.Equal("import-secret", variables["IO_RUSTFS_SECRET_KEY"]);
			Assert.Equal("X64", variables["architecture"]);
			Assert.Null(values["zongsoft_env_empty"]);
			Assert.Equal(string.Empty, values["zongsoft_env_blank"]);
			Assert.Null(values["zongsoft_env_flag"]);
			Assert.Equal("process", Environment.GetEnvironmentVariable("compilation"));

			directory.Write("source/missing/leaf/.env", "compilation=changed\n");
			var reloaded = new Variables(PackCommand<Package.Tar>.GetVariables(context, source));
			Assert.Equal("changed/retained", reloaded["zongsoft_env_reference"]);
			Assert.False(reloaded.Contains("zongsoft_env_flag"));
			Assert.Equal("source/retained", variables["zongsoft_env_reference"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable("compilation", previous);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void GetVariables_EnvironmentIdentityRequiresExplicitOptions(bool explicitIdentity)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "name=incorrect\nversion=9.9.9\nedition=incorrect\n[zongsoft identity]\nname=zongsoft.daemon\nversion=2.3.4\nedition=Community\n");
		var command = new TarCommand();
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(explicitIdentity ? "tar --name:$(zongsoft_identity_name) --version:$(zongsoft_identity_version) --edition:$(zongsoft_identity_edition)" : "tar")[0], command, null);

		var variables = new Variables(PackCommand<Package.Tar>.GetVariables(context, directory.Path));

		Assert.Equal(explicitIdentity ? "zongsoft.daemon" : null, variables.Name);
		Assert.Equal(explicitIdentity ? "2.3.4" : null, variables["version"]);
		Assert.Equal(explicitIdentity ? "Community" : null, variables.Edition);
		Assert.Equal("zongsoft.daemon", variables["zongsoft_identity_name"]);
	}
}
