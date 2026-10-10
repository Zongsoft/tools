using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Components;
using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class EnvironmentVariablesTest
{
	[Fact]
	public void CreateEvaluator_EnvironmentViewPreservesLivePlatformNamesAndDefaultNamespace()
	{
		var name = "ZongsoftToolsLive_" + Guid.NewGuid().ToString("N");
		var rawName = name + ".path-value";
		var scopedName = name + ":value";
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("tar")[0], new TarCommand(), null);

		try
		{
			Environment.SetEnvironmentVariable(name, "first");
			Environment.SetEnvironmentVariable(rawName, "raw-name");
			Environment.SetEnvironmentVariable(scopedName, "must-not-be-a-namespace");
			var evaluator = PackCommand<Package.Tar>.CreateEvaluator(context);

			Assert.Equal("first", evaluator.Evaluate("${" + name + "}"));
			Assert.Equal(OperatingSystem.IsWindows(), evaluator.TryGetVariable(name.ToLowerInvariant(), out var differentlyCased));
			Assert.Equal(OperatingSystem.IsWindows() ? "first" : null, differentlyCased);
			Assert.Equal("raw-name", evaluator.GetVariable(rawName));
			Assert.False(evaluator.TryGetVariable(name + "_path_value", out _));
			Assert.False(evaluator.TryGetVariable(scopedName, out _));

			Environment.SetEnvironmentVariable(name, "updated");

			Assert.Equal("updated", evaluator.Evaluate("${" + name + "}"));

			Environment.SetEnvironmentVariable(name, null);

			Assert.False(evaluator.TryGetVariable(name, out var removed));
			Assert.Null(removed);
		}
		finally
		{
			Environment.SetEnvironmentVariable(name, null);
			Environment.SetEnvironmentVariable(rawName, null);
			Environment.SetEnvironmentVariable(scopedName, null);
		}
	}

	[Fact]
	public void CreateEvaluator_TypedAliasesAndDefaultsRespectProviderPriority()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "retry_count=17\nc=18\nenabled=true\ne=true\nlayer=file\n");
		var names = new[] { "retry_count", "c", "enabled", "e", "layer", "limit" };
		var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);

		try
		{
			foreach(var name in names)
				Environment.SetEnvironmentVariable(name, name == "limit" ? null : "environment");

			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("options -c:7 -e:false")[0], new VariableOptionsCommand(), null);
			var evaluator = Utility.CreateEvaluator(context, directory.Path);

			Assert.Equal(7, Assert.IsType<int>(evaluator.GetVariable("retry_count")));
			Assert.Equal(7, Assert.IsType<int>(evaluator.GetVariable("c")));
			Assert.False(Assert.IsType<bool>(evaluator.GetVariable("enabled")));
			Assert.False(Assert.IsType<bool>(evaluator.GetVariable("e")));
			Assert.Equal("7/7/False/False", evaluator.Evaluate("${retry_count}/${c}/${enabled}/${e}"));
			Assert.Equal("file", evaluator.GetVariable("layer"));
			Assert.Equal(42, Assert.IsType<int>(evaluator.GetVariable("limit")));

			var withoutFile = Utility.CreateEvaluator(context);

			Assert.Equal("environment", withoutFile.GetVariable("layer"));

			Environment.SetEnvironmentVariable("layer", null);

			Assert.Equal("descriptor", withoutFile.GetVariable("layer"));
			Assert.Equal("file", evaluator.GetVariable("layer"));
		}
		finally
		{
			foreach(var pair in previous)
				Environment.SetEnvironmentVariable(pair.Key, pair.Value);
		}
	}

	[Fact]
	public void CreateEvaluator_DefaultsSupportImportsAndRecursiveValuesAfterConfiguration()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "#@import ${deployment:zongsoft_default_file}\nzongsoft_default_result=${deployment.child:zongsoft_default_template}\n[default]\nprofile_only=ordinary namespace\nzongsoft_default_layer=ordinary layer\n");
		directory.Write("selected.env", "zongsoft_default_layer=imported\n[deployment]\nzongsoft_default_layer=scoped\n");
		var names = new[] { "zongsoft_default_file", "zongsoft_default_template", "zongsoft_default_layer", "zongsoft_default_suffix" };
		var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);

		try
		{
			foreach(var name in names)
				Environment.SetEnvironmentVariable(name, null);

			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("defaults")[0], new DefaultOptionsCommand(), null);
			var evaluator = Utility.CreateEvaluator(context, directory.Path);

			Assert.Equal("imported", evaluator.GetVariable("zongsoft_default_layer"));
			Assert.True(evaluator.Options.Fallback);
			Assert.Equal("ordinary layer", evaluator.GetVariable("default:zongsoft_default_layer"));
			Assert.Equal("scoped", evaluator.Evaluate("${deployment.child:zongsoft_default_layer}"));
			Assert.Equal("imported/tail", evaluator.Evaluate("${zongsoft_default_result}"));
			Assert.Equal("ordinary namespace", evaluator.Evaluate("${default:profile_only}"));
			Assert.False(evaluator.TryGetVariable("profile_only", out var missing));
			Assert.Null(missing);
			Assert.True(evaluator.TryGetVariable("other:zongsoft_default_suffix", out var suffix));
			Assert.Equal("tail", suffix);
			var explicitContext = new CommandContext(new CommandExecutor(), CommandLine.Parse("defaults --zongsoft-default-layer:explicit")[0], new DefaultOptionsCommand(), null);
			var explicitEvaluator = Utility.CreateEvaluator(explicitContext, directory.Path);

			Assert.Equal("explicit", explicitEvaluator.GetVariable("zongsoft_default_layer"));
			Assert.Equal("scoped", explicitEvaluator.Evaluate("${deployment.child:zongsoft_default_layer}"));
			Assert.Equal("explicit/tail", explicitEvaluator.Evaluate("${zongsoft_default_result}"));
			var strict = Utility.CreateEvaluator(context);
			strict.Options.Fallback = false;

			var error = Assert.Throws<TemplateEvaluationException>(() => Utility.LoadEnvironmentProfiles(strict, directory.Path));

			Assert.Equal("MissingVariable", error.Code);
			Assert.Equal("deployment:zongsoft_default_file", error.Expression);
		}
		finally
		{
			foreach(var pair in previous)
				Environment.SetEnvironmentVariable(pair.Key, pair.Value);
		}
	}

	[Theory]
	[InlineData("tar", "File Manufacturer", "https://file.example/product")]
	[InlineData("tar --manufacturer --homepage:https://option.example/product", "Zongsoft", "https://option.example/product")]
	[InlineData("tar --manufacturer=\"\"", "Zongsoft", "https://file.example/product")]
	[InlineData("tar --manufacturer=\"   \"", "   ", "https://file.example/product")]
	[InlineData("tar --manufacturer:${company} --homepage:${site}", "Referenced Manufacturer", "https://reference.example/product")]
	public void CreateEvaluator_ManufacturerAndHomepageRespectPriorityAndEmptyBoundary(string commandLine, string expectedManufacturer, string expectedHomepage)
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
			var options = new PackageOptions(PackCommand<Package.Tar>.CreateEvaluator(context, directory.Path));

			Assert.Equal(expectedManufacturer, options.Manufacturer);
			Assert.Equal(expectedHomepage, options.Homepage);
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
	[InlineData("tar --framework", false, null)]
	[InlineData("tar --framework=\"\"", false, "")]
	[InlineData("tar", true, "net9.0")]
	[InlineData("tar --FRAMEWORK:", true, null)]
	[InlineData("tar --framework=\"\"", true, "")]
	[InlineData("tar --framework:net10.0", true, "net10.0")]
	[InlineData("tar --framework=\"   \"", true, "   ")]
	public void CreateEvaluator_FrameworkUsesFirstFoundValueIncludingExplicitEmpty(string commandLine, bool environmentFile, string expected)
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
			var evaluator = PackCommand<Package.Tar>.CreateEvaluator(context, Path.Combine(directory.Path, "source"));

			Assert.Equal(expected, evaluator.GetVariable("framework"));
			Assert.Equal(expected, new PackageOptions(evaluator).Framework);
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
	public void CreateEvaluator_ExplicitEmptyFrameworkBlocksConfiguration(string commandLine, string expected)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "framework=net8.0\n");
		directory.Write("source/.env", "framework=net9.0\n");
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(commandLine)[0], new TarCommand(), null);
		var evaluator = PackCommand<Package.Tar>.CreateEvaluator(context, Path.Combine(directory.Path, "source"));

		Assert.True(evaluator.TryGetVariable("framework", out _));
		Assert.Equal(expected, evaluator.GetVariable("framework"));
		Assert.Equal(expected, new PackageOptions(evaluator).Framework);
	}

	[Fact]
	public void CreateEvaluator_EnvironmentHierarchyRespectsDefaultsEnvironmentAndExplicitOptions()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "compilation=ancestor\nzongsoft_env_parent=retained\nzongsoft_env_reference=${compilation}/${zongsoft_env_parent}\n[io rustfs]\nsecret_key=import-secret\n");
		directory.Write("source/missing/leaf/.env", "COMPILATION=source\narchitecture=Arm64\nzongsoft_env_empty=inherited\nzongsoft_env_blank=\nzongsoft_env_flag\n");
		directory.Write("source/missing/leaf/child/.env", "compilation=child\n");
		var previous = Environment.GetEnvironmentVariable("compilation");

		try
		{
			Environment.SetEnvironmentVariable("compilation", "process");
			var source = Path.Combine(directory.Path, "source", "missing", "leaf");
			var command = new TarCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("tar --architecture:X64 --zongsoft_env_empty:")[0], command, null);
			var withoutProfile = new PackageOptions(PackCommand<Package.Tar>.CreateEvaluator(context));
			Assert.Equal("process", withoutProfile.Compilation);
			var evaluator = PackCommand<Package.Tar>.CreateEvaluator(context, source);
			var options = new PackageOptions(evaluator);
			Assert.Equal("source", options.Compilation);
			Assert.Equal("source/retained", options["zongsoft_env_reference"]);
			Assert.Equal("import-secret", options["IO.RUSTFS:SECRET_KEY"]);
			Assert.Equal("X64", options["architecture"]);
			Assert.Null(evaluator.GetVariable("zongsoft_env_empty"));
			Assert.Equal(string.Empty, evaluator.GetVariable("zongsoft_env_blank"));
			Assert.Null(evaluator.GetVariable("zongsoft_env_flag"));
			Assert.Equal("process", Environment.GetEnvironmentVariable("compilation"));

			directory.Write("source/missing/leaf/.env", "compilation=changed\n");
			var reloaded = new PackageOptions(PackCommand<Package.Tar>.CreateEvaluator(context, source));
			Assert.Equal("changed/retained", reloaded["zongsoft_env_reference"]);
			Assert.False(reloaded.Contains("zongsoft_env_flag"));
			Assert.Equal("source/retained", options["zongsoft_env_reference"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable("compilation", previous);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CreateEvaluator_EnvironmentIdentityRequiresExplicitOptions(bool explicitIdentity)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "name=incorrect\nversion=9.9.9\nedition=incorrect\n[zongsoft identity]\nname=zongsoft.daemon\nversion=2.3.4\nedition=Community\n");
		var command = new TarCommand();
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(explicitIdentity ? "tar --name:${zongsoft.identity:name} --version:${zongsoft.identity:version} --edition:${zongsoft.identity:edition}" : "tar")[0], command, null);

		var options = new PackageOptions(PackCommand<Package.Tar>.CreateEvaluator(context, directory.Path));

		Assert.Equal(explicitIdentity ? "zongsoft.daemon" : null, options.Name);
		Assert.Equal(explicitIdentity ? "2.3.4" : null, options["version"]);
		Assert.Equal(explicitIdentity ? "Community" : null, options.Edition);
		Assert.Equal("zongsoft.daemon", options["zongsoft.identity:name"]);
	}

	[CommandOption("zongsoft-default-file", typeof(string), "selected.env")]
	[CommandOption("zongsoft-default-template", typeof(string), "${zongsoft_default_layer}/${zongsoft_default_suffix}")]
	[CommandOption("zongsoft-default-layer", typeof(string), "descriptor")]
	[CommandOption("zongsoft-default-suffix", typeof(string), "tail")]
	private sealed class DefaultOptionsCommand : CommandBase
	{
		protected override ValueTask<object> OnExecuteAsync(object argument, CancellationToken cancellation) => ValueTask.FromResult<object>(null);
	}

	[CommandOption("retry-count", 'c', typeof(int), 3)]
	[CommandOption("enabled", 'e', typeof(bool), true)]
	[CommandOption("layer", typeof(string), "descriptor")]
	[CommandOption("limit", typeof(int), 42)]
	private sealed class VariableOptionsCommand : CommandBase
	{
		protected override ValueTask<object> OnExecuteAsync(object argument, CancellationToken cancellation) => ValueTask.FromResult<object>(null);
	}
}
