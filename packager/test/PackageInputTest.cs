using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

using Zongsoft.Text.Templating;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageInputTest
{
	#region 变量展开
	[Fact]
	public void CreateEvaluator_ExplicitThenEnvironmentThenDefaults_PreservesPriority()
	{
		var architecture = Environment.GetEnvironmentVariable("architecture");
		var compilation = Environment.GetEnvironmentVariable("compilation");
		var homepage = Environment.GetEnvironmentVariable("homepage");
		var maintainer = Environment.GetEnvironmentVariable("maintainer");
		var manufacturer = Environment.GetEnvironmentVariable("manufacturer");

		try
		{
			Environment.SetEnvironmentVariable("architecture", "Arm64");
			Environment.SetEnvironmentVariable("compilation", "Debug");
			Environment.SetEnvironmentVariable("homepage", null);
			Environment.SetEnvironmentVariable("maintainer", null);
			Environment.SetEnvironmentVariable("manufacturer", null);
			var command = new DebCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("deb --architecture:X64 --platform:Linux --framework:net10.0")[0], command, null);

			var evaluator = PackCommand<Package.Deb>.CreateEvaluator(context);
			var options = new PackageOptions(evaluator);

			Assert.Equal("X64", evaluator.GetVariable("architecture"));
			Assert.Equal("Debug", evaluator.GetVariable("compilation"));
			Assert.Equal("https://github.com/Zongsoft", evaluator.GetVariable("homepage"));
			Assert.Equal("Zongsoft", options.Maintainer);
			Assert.Equal("Zongsoft", options.Manufacturer);
			Assert.Equal(System.Runtime.InteropServices.Architecture.X64, options.Architecture);
			Assert.Equal("Debug", options.Compilation);
		}
		finally
		{
			Environment.SetEnvironmentVariable("architecture", architecture);
			Environment.SetEnvironmentVariable("compilation", compilation);
			Environment.SetEnvironmentVariable("homepage", homepage);
			Environment.SetEnvironmentVariable("maintainer", maintainer);
			Environment.SetEnvironmentVariable("manufacturer", manufacturer);
		}
	}

	[Fact]
	public void Options_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "${name}/bin",
			["unused"] = "${missing}",
			["loop"] = "${loop}",
		}));

		Assert.Equal("zongsoft.daemon/bin", options["payload"]);
		Assert.Equal("zongsoft.daemon", options.Name);
		Assert.Throws<TemplateEvaluationException>(() => options["unused"]);
		Assert.Throws<TemplateEvaluationException>(() => options["loop"]);
	}

	[Fact]
	public void Evaluate_NestedAndRepeatedReferences_ExpandsTemplates()
	{
		var variables = new global::Zongsoft.Common.Variables()
		{
			["name"] = "zongsoft.daemon",
			["scheme"] = "default",
			["root"] = "${scheme}/${NAME}",
		};

		var result = Utility.CreateEvaluator(variables).Evaluate("${root}/${name}-${name}");
		Assert.Equal("default/zongsoft.daemon/zongsoft.daemon-zongsoft.daemon", result);
		Assert.Equal("${scheme}/${NAME}", variables["root"]);
	}

	[Fact]
	public void Evaluate_CoreVariablesIgnoreVariableNameCase()
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["Root"] = "${service_name}",
			["Service_Name"] = "worker",
		};

		var result = Utility.CreateEvaluator(variables).Evaluate("${ROOT}/${SERVICE_NAME}");
		Assert.Equal("worker/worker", result);
	}

	[Fact]
	public void Options_WinPlatformAliasMapsToWindows()
	{
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["platform"] = "win" }));
		Assert.Equal(Platform.Windows, options.Platform);
	}

	[Theory]
	[InlineData("0", (Architecture)0)]
	[InlineData("999", (Architecture)999)]
	public void Options_NumericArchitectureUsesCoreConversionAfterExpansion(string architecture, Architecture expected)
	{
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["architecture"] = "${target}",
			["target"] = architecture,
		}));
		Assert.Equal(expected, options.Architecture);
	}

	[Theory]
	[InlineData("${missing}", "resolved")]
	[InlineData("${first}", "resolved")]
	[InlineData("${second}", "${first}")]
	public void Options_InvalidReference_ThrowsWhenRead(string first, string second)
	{
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["first"] = first,
			["second"] = second,
			["valid"] = "unaffected",
		}));

		Assert.Throws<TemplateEvaluationException>(() => options["first"]);

		Assert.Equal("unaffected", options["valid"]);
	}

	[Theory]
	[InlineData("${missing}", "valid", "MissingVariable")]
	[InlineData("${first}", "valid", "DepthExceeded")]
	[InlineData("${second}", "${first}", "DepthExceeded")]
	public void Evaluate_InvalidReference_ReturnsFailure(string first, string second, string code)
	{
		var variables = new global::Zongsoft.Common.Variables()
		{
			["first"] = first,
			["second"] = second,
		};

		var evaluator = Utility.CreateEvaluator(variables);

		Assert.False(evaluator.TryEvaluate("${first}", out var result, out var error));
		Assert.Null(result);
		Assert.Equal(code, error.Code);
		Assert.Equal(first, variables["first"]);
	}

	[Fact]
	public void Options_ChangedDependency_UpdatesResolvedValue()
	{
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["version"] = "1.0.0",
			["migration"] = ".deploy/default/migration/${version}/*.migration",
		}));
		Assert.Equal(".deploy/default/migration/1.0.0/*.migration", options["migration"]);

		options["version"] = "1.1.0";

		Assert.Equal(".deploy/default/migration/1.1.0/*.migration", options["migration"]);
		Assert.Equal(new Version(1, 1, 0), options.Version);
	}

	[Fact]
	public void Evaluate_DepthLimit_AllowsSixtyFourLevelsIncludingRootAndRejectsNext()
	{
		var variables = new global::Zongsoft.Common.Variables();
		for(var index = 0; index < 62; index++)
			variables["step" + index] = "${step" + (index + 1) + "}";
		variables["step62"] = "resolved";

		var evaluator = Utility.CreateEvaluator(variables);
		Assert.Equal("resolved", evaluator.Evaluate("${step0}"));

		variables["step62"] = "${step63}";
		variables["step63"] = "resolved";
		Assert.False(evaluator.TryEvaluate("${step0}", out var result, out var error));
		Assert.Null(result);
		Assert.Equal("DepthExceeded", error.Code);
	}

	[Fact]
	public void Options_StructuredNamesAndSameKeysRemainInstanceScoped()
	{
		var first = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["channel_name"] = "alpha",
			["settings_0"] = "one",
			["profile_key"] = "primary",
			["route"] = "${channel_name}/${settings_0}/${profile_key}",
		}));
		var second = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["channel_name"] = "beta",
			["settings_0"] = "two",
			["profile_key"] = "secondary",
			["route"] = "${channel_name}/${settings_0}/${profile_key}",
		}));

		Assert.Equal("alpha/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
		first["channel_name"] = "updated";
		Assert.Equal("updated/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
	}
	#endregion

	#region 文本来源
	[Fact]
	public void Read_SameNameAsWorkingDirectoryFile_UsesOnlySourceFile()
	{
		using var directory = new MigrationTestDirectory();
		const string FILE_NAME = "install.sh";
		directory.Write("working/" + FILE_NAME, "working-directory-content");
		directory.Write("source/" + FILE_NAME, "source-only-content");
		var options = new PackageOptions();
		var previous = Environment.CurrentDirectory;

		try
		{
			Environment.CurrentDirectory = Path.Combine(directory.Path, "working");

			Assert.Equal("source-only-content", TextSource.Read(Path.Combine(directory.Path, "source"), FILE_NAME, options));
			Assert.Equal("working-directory-content", File.ReadAllText(FILE_NAME));
		}
		finally
		{
			Environment.CurrentDirectory = previous;
		}
	}

	[Theory]
	[InlineData("file:")]
	[InlineData("file:   ")]
	public void Read_EmptyExplicitFile_DoesNotBecomeLiteralText(string value)
	{
		using var directory = new MigrationTestDirectory();
		var options = new PackageOptions();

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value, options));
	}

	[Theory]
	[InlineData("scripts/setup script.sh")]
	[InlineData("file:scripts/setup script.sh")]
	public void Read_RelativeFile_UsesSourceDirectory(string value)
	{
		using var directory = new MigrationTestDirectory();
		const string CONTENT = "echo source-specific-content";
		directory.Write("scripts/setup script.sh", CONTENT);
		var options = new PackageOptions();

		Assert.NotEqual(Environment.CurrentDirectory, directory.Path);
		Assert.Equal(CONTENT, TextSource.Read(directory.Path, value, options));
	}

	[Fact]
	public void Read_AbsoluteFile_ReadsContents()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write("scripts/install.sh", "echo absolute-file");
		var options = new PackageOptions();

		Assert.Equal("echo absolute-file", TextSource.Read(Path.Combine(directory.Path, "another-source"), file.Replace('\\', '/'), options));
	}

	[Fact]
	public void Read_ExplicitText_PreservesShellExpressionsAndWhitespace()
	{
		using var directory = new MigrationTestDirectory();
		const string CONTENT = "  echo $(name) %name% ${HOME}\n echo /opt/zongsoft/web  ";
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["name"] = "zongsoft.web" }));

		Assert.Equal(CONTENT, TextSource.Read(directory.Path, "text:" + CONTENT, options));
	}

	[Fact]
	public void Read_SingleLineShellCommand_IsLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		var options = new PackageOptions();

		Assert.Equal("echo /opt/zongsoft/web", TextSource.Read(directory.Path, "echo /opt/zongsoft/web", options));
	}

	[Theory]
	[InlineData("$(name) %name% ${HOME}")]
	[InlineData("scripts/second.sh")]
	public void Read_FileContent_IsNeitherExpandedNorReadAgain(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/first.sh", content);
		directory.Write("scripts/second.sh", "echo must-not-be-read");
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["name"] = "zongsoft.web" }));

		Assert.Equal(content, TextSource.Read(directory.Path, "scripts/first.sh", options));
	}

	[Theory]
	[InlineData("file:scripts/missing.sh")]
	[InlineData("scripts/missing.sh")]
	public void Read_MissingFile_ReportsResolvedPath(string value)
	{
		using var directory = new MigrationTestDirectory();
		var options = new PackageOptions();

		var error = Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value, options));

		Assert.Equal(Path.Combine(directory.Path, "scripts", "missing.sh"), error.FileName);
	}

	[Fact]
	public void Read_FileOnly_DoesNotAcceptLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		var options = new PackageOptions();

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, "echo installed", options, true));
	}

	[Fact]
	public void Read_FileOnly_ReadsExtensionlessFile()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/installed", "echo installed");
		var options = new PackageOptions();

		Assert.Equal("echo installed", TextSource.Read(directory.Path, "scripts/installed", options, true));
	}
	#endregion
}
