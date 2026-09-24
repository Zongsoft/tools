using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageInputTest
{
	#region 变量展开
	[Fact]
	public void GetVariables_ExplicitThenEnvironmentThenDefaults_PreservesPriority()
	{
		var architecture = Environment.GetEnvironmentVariable("architecture");
		var compilation = Environment.GetEnvironmentVariable("compilation");
		var url = Environment.GetEnvironmentVariable("url");
		try
		{
			Environment.SetEnvironmentVariable("architecture", "Arm64");
			Environment.SetEnvironmentVariable("compilation", "Debug");
			Environment.SetEnvironmentVariable("url", null);
			var command = new DebCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("deb --architecture:X64 --platform:Linux --framework:net10.0")[0], command, null);

			var values = PackCommand<Package.Deb>.GetVariables(context);
			var variables = new Variables(values);

			Assert.Equal("X64", values["architecture"]);
			Assert.Equal("Debug", values["compilation"]);
			Assert.Equal("https://github.com/Zongsoft", values["url"]);
			Assert.Equal(System.Runtime.InteropServices.Architecture.X64, variables.Architecture);
			Assert.Equal("Debug", variables.Compilation);
		}
		finally
		{
			Environment.SetEnvironmentVariable("architecture", architecture);
			Environment.SetEnvironmentVariable("compilation", compilation);
			Environment.SetEnvironmentVariable("url", url);
		}
	}

	[Fact]
	public void Variables_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "$(name)/bin",
			["unused"] = "$(missing)",
			["loop"] = "$(loop)",
		});

		Assert.Equal("zongsoft.daemon/bin", variables["payload"]);
		Assert.Equal("zongsoft.daemon", variables.Name);
		Assert.Throws<InvalidOperationException>(() => variables["unused"]);
		Assert.Throws<InvalidOperationException>(() => variables["loop"]);
	}

	[Fact]
	public void Normalize_NestedAndRepeatedReferences_ExpandsBothSyntaxes()
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["scheme"] = "default",
			["root"] = "$(scheme)/%NAME%",
		};

		var result = Normalizer.Normalize("$(root)/$(name)-%name%", variables);

		Assert.True(result.Succeed);
		Assert.Equal("default/zongsoft.daemon/zongsoft.daemon-zongsoft.daemon", result.Value);
		Assert.Equal("$(scheme)/%NAME%", variables["root"]);
	}

	[Fact]
	public void Normalize_OrdinaryDictionaryIgnoresVariableNameCase()
	{
		var variables = new Dictionary<string, string>
		{
			["Root"] = "$(service.name)",
			["Service.Name"] = "worker",
		};

		var result = Normalizer.Normalize("$(ROOT)/%SERVICE.NAME%", variables);
		Assert.True(result.Succeed);
		Assert.Equal("worker/worker", result.Value);
	}

	[Fact]
	public void Variables_WinPlatformAliasMapsToWindows()
	{
		var variables = new Variables(new Dictionary<string, string> { ["platform"] = "win" });
		Assert.Equal(Platform.Windows, variables.Platform);
	}

	[Theory]
	[InlineData("0", (Architecture)0)]
	[InlineData("999", (Architecture)999)]
	public void Variables_NumericArchitectureUsesCoreConversionAfterExpansion(string architecture, Architecture expected)
	{
		var variables = new Variables(new Dictionary<string, string>
		{
			["architecture"] = "$(target)",
			["target"] = architecture,
		});
		Assert.Equal(expected, variables.Architecture);
	}

	[Theory]
	[InlineData("$(missing)", "resolved", "missing")]
	[InlineData("$(first)", "resolved", "first")]
	[InlineData("$(second)", "%first%", "second")]
	public void Variables_InvalidReference_ThrowsWhenRead(string first, string second, string failedVariable)
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["first"] = first,
			["second"] = second,
			["valid"] = "unaffected",
		});

		var error = Assert.Throws<InvalidOperationException>(() => variables["first"]);

		Assert.Contains(failedVariable, error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("unaffected", variables["valid"]);
	}

	[Theory]
	[InlineData("$(missing)", "valid", "missing")]
	[InlineData("$(first)", "valid", "first")]
	[InlineData("$(second)", "%first%", "first")]
	public void Normalize_InvalidReference_ReturnsFailure(string first, string second, string failedVariable)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["first"] = first,
			["second"] = second,
		};

		var result = Normalizer.Normalize("$(first)", variables);

		Assert.False(result.Succeed);
		Assert.Equal(failedVariable, result.Value);
		Assert.Equal(first, variables["first"]);
	}

	[Fact]
	public void Variables_ChangedDependency_UpdatesResolvedValue()
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["version"] = "1.0.0",
			["migration"] = ".deploy/default/migration/$(version)/*.migration",
		});
		Assert.Equal(".deploy/default/migration/1.0.0/*.migration", variables["migration"]);

		variables["version"] = "1.1.0";

		Assert.Equal(".deploy/default/migration/1.1.0/*.migration", variables["migration"]);
		Assert.Equal(new Version(1, 1, 0), variables.Version);
	}

	[Fact]
	public void Normalize_DepthLimit_AllowsSixtyFourReferencesAndRejectsNext()
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for(var index = 0; index < 63; index++)
			variables["step" + index] = "$(step" + (index + 1) + ")";
		variables["step63"] = "resolved";

		var withinLimit = Normalizer.Normalize("$(step0)", variables);
		Assert.True(withinLimit.Succeed);
		Assert.Equal("resolved", withinLimit.Value);

		variables["step63"] = "$(step64)";
		variables["step64"] = "resolved";
		var beyondLimit = Normalizer.Normalize("$(step0)", variables);
		Assert.False(beyondLimit.Succeed);
		Assert.Equal("step64", beyondLimit.Value);
	}

	[Fact]
	public void Variables_StructuredNamesAndSameKeysRemainInstanceScoped()
	{
		var first = new Variables(new Dictionary<string, string>
		{
			["channel.name"] = "alpha",
			["settings[0]"] = "one",
			["profile-key"] = "primary",
			["route"] = "$(channel.name)/%settings[0]%/$(profile-key)",
		});
		var second = new Variables(new Dictionary<string, string>
		{
			["channel.name"] = "beta",
			["settings[0]"] = "two",
			["profile-key"] = "secondary",
			["route"] = "$(channel.name)/%settings[0]%/$(profile-key)",
		});

		Assert.Equal("alpha/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
		first["channel.name"] = "updated";
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
		var variables = new Variables();
		var previous = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = Path.Combine(directory.Path, "working");

			Assert.Equal("source-only-content", TextSource.Read(Path.Combine(directory.Path, "source"), FILE_NAME, variables));
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
		var variables = new Variables();

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value, variables));
	}

	[Theory]
	[InlineData("scripts/setup script.sh")]
	[InlineData("file:scripts/setup script.sh")]
	public void Read_RelativeFile_UsesSourceDirectory(string value)
	{
		using var directory = new MigrationTestDirectory();
		const string CONTENT = "echo source-specific-content";
		directory.Write("scripts/setup script.sh", CONTENT);
		var variables = new Variables();

		Assert.NotEqual(Environment.CurrentDirectory, directory.Path);
		Assert.Equal(CONTENT, TextSource.Read(directory.Path, value, variables));
	}

	[Fact]
	public void Read_AbsoluteFile_ReadsContents()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write("scripts/install.sh", "echo absolute-file");
		var variables = new Variables();

		Assert.Equal("echo absolute-file", TextSource.Read(Path.Combine(directory.Path, "another-source"), file, variables));
	}

	[Fact]
	public void Read_ExplicitText_PreservesShellExpressionsAndWhitespace()
	{
		using var directory = new MigrationTestDirectory();
		const string CONTENT = "  echo $(name) %name% ${HOME}\n echo /opt/zongsoft/web  ";
		var variables = new Variables(new Dictionary<string, string> { ["name"] = "zongsoft.web" });

		Assert.Equal(CONTENT, TextSource.Read(directory.Path, "text:" + CONTENT, variables));
	}

	[Fact]
	public void Read_SingleLineShellCommand_IsLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		var variables = new Variables();

		Assert.Equal("echo /opt/zongsoft/web", TextSource.Read(directory.Path, "echo /opt/zongsoft/web", variables));
	}

	[Theory]
	[InlineData("$(name) %name% ${HOME}")]
	[InlineData("scripts/second.sh")]
	public void Read_FileContent_IsNeitherExpandedNorReadAgain(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/first.sh", content);
		directory.Write("scripts/second.sh", "echo must-not-be-read");
		var variables = new Variables(new Dictionary<string, string> { ["name"] = "zongsoft.web" });

		Assert.Equal(content, TextSource.Read(directory.Path, "scripts/first.sh", variables));
	}

	[Theory]
	[InlineData("file:scripts/missing.sh")]
	[InlineData("scripts/missing.sh")]
	public void Read_MissingFile_ReportsResolvedPath(string value)
	{
		using var directory = new MigrationTestDirectory();
		var variables = new Variables();

		var error = Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value, variables));

		Assert.Equal(Path.Combine(directory.Path, "scripts", "missing.sh"), error.FileName);
	}

	[Fact]
	public void Read_FileOnly_DoesNotAcceptLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		var variables = new Variables();

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, "echo installed", variables, true));
	}

	[Fact]
	public void Read_FileOnly_ReadsExtensionlessFile()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/installed", "echo installed");
		var variables = new Variables();

		Assert.Equal("echo installed", TextSource.Read(directory.Path, "scripts/installed", variables, true));
	}
	#endregion
}
