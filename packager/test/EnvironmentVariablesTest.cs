using System;
using System.IO;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class EnvironmentVariablesTest
{
	[Theory]
	[InlineData("tar", "File Manufacturer", "https://file.example/product")]
	[InlineData("tar --manufacturer --homepage:https://option.example/product", "Zongsoft", "https://option.example/product")]
	[InlineData("tar --manufacturer=\"\"", "Zongsoft", "https://file.example/product")]
	[InlineData("tar --manufacturer=\"   \"", "   ", "https://file.example/product")]
	[InlineData("tar --manufacturer:$(company) --homepage:%site%", "Referenced Manufacturer", "https://reference.example/product")]
	public void GetVariables_ManufacturerAndHomepageRespectPriorityAndEmptyBoundary(string commandLine, string expectedManufacturer, string expectedHomepage)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "manufacturer=File Manufacturer\nhomepage=https://file.example/product\ncompany=Referenced Manufacturer\nsite=https://reference.example/product\n");
		var previousManufacturer = Environment.GetEnvironmentVariable("manufacturer");
		var previousHomepage = Environment.GetEnvironmentVariable("homepage");
		try
		{
			Environment.SetEnvironmentVariable("manufacturer", "Process Manufacturer");
			Environment.SetEnvironmentVariable("homepage", "https://process.example/product");
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(commandLine)[0], new TarCommand(), null);
			var variables = new Variables(PackCommand<Package.Tar>.GetVariables(context, directory.Path));

			Assert.Equal(expectedManufacturer, variables.Manufacturer);
			Assert.Equal(expectedHomepage, variables.Homepage);
			Assert.Equal("Process Manufacturer", Environment.GetEnvironmentVariable("manufacturer"));
			Assert.Equal("https://process.example/product", Environment.GetEnvironmentVariable("homepage"));
		}
		finally
		{
			Environment.SetEnvironmentVariable("manufacturer", previousManufacturer);
			Environment.SetEnvironmentVariable("homepage", previousHomepage);
		}
	}

	[Theory]
	[InlineData("tar", false, "net8.0")]
	[InlineData("tar --framework", false, "net8.0")]
	[InlineData("tar --framework=\"\"", false, "net8.0")]
	[InlineData("tar", true, "net9.0")]
	[InlineData("tar --FRAMEWORK:", true, "net9.0")]
	[InlineData("tar --framework=\"\"", true, "net9.0")]
	[InlineData("tar --framework:net10.0", true, "net10.0")]
	[InlineData("tar --framework=\"   \"", true, "   ")]
	public void GetVariables_EmptyFrameworkUsesMergedVariables(string commandLine, bool environmentFile, string expected)
	{
		using var directory = new MigrationTestDirectory();
		var previous = Environment.GetEnvironmentVariable("framework");
		try
		{
			Environment.SetEnvironmentVariable("framework", "net8.0");
			if(environmentFile)
			{
				directory.Write(".env", "framework=net8.0\n");
				directory.Write("source/.env", "Framework=net9.0\n");
			}
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(commandLine)[0], new TarCommand(), null);
			var values = PackCommand<Package.Tar>.GetVariables(context, Path.Combine(directory.Path, "source"));

			Assert.Equal(expected, values["framework"]);
			Assert.Equal(string.IsNullOrWhiteSpace(expected) ? string.Empty : expected, new Variables(values).Framework);
			Assert.Equal("net8.0", Environment.GetEnvironmentVariable("framework"));
		}
		finally
		{
			Environment.SetEnvironmentVariable("framework", previous);
		}
	}

	[Theory]
	[InlineData("tar --framework", null)]
	[InlineData("tar --framework=\"\"", "")]
	public void GetVariables_EmptyFrameworkPreservesOptionWhenFallbackUnavailable(string commandLine, string expected)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "framework=net8.0\n");
		directory.Write("source/.env", "framework=\n");
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(commandLine)[0], new TarCommand(), null);
		var values = PackCommand<Package.Tar>.GetVariables(context, Path.Combine(directory.Path, "source"));

		Assert.True(values.ContainsKey("framework"));
		Assert.Equal(expected, values["framework"]);
		Assert.Equal(expected, new Variables(values).Framework);
	}

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
