using System;
using System.IO;
using System.Collections.Generic;

using Zongsoft.Components;
using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageInputTests
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

			var variables = PackCommand<Package.Deb>.GetVariables(context);
			Normalizer.Initialize(variables);

			Assert.Equal("X64", variables["architecture"]);
			Assert.Equal("Debug", variables["compilation"]);
			Assert.Equal("https://github.com/Zongsoft", variables["url"]);
			Assert.Equal(System.Runtime.InteropServices.Architecture.X64, Normalizer.Variables.Architecture);
			Assert.Equal("Debug", Normalizer.Variables.Compilation);
		}
		finally
		{
			Environment.SetEnvironmentVariable("architecture", architecture);
			Environment.SetEnvironmentVariable("compilation", compilation);
			Environment.SetEnvironmentVariable("url", url);
		}
	}

	[Fact]
	public void Initialize_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "$(name)/bin",
			["unused"] = "$(missing)",
			["loop"] = "$(loop)",
		});

		Assert.Equal("zongsoft.daemon/bin", Normalizer.Variables["payload"]);
		Assert.Equal("zongsoft.daemon", Normalizer.Variables.Name);
		Assert.Throws<InvalidOperationException>(() => Normalizer.Variables["unused"]);
		Assert.Throws<InvalidOperationException>(() => Normalizer.Variables["loop"]);
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
			["migration"] = ".deploy/default/migration/$(version)/*.ini",
		});
		Assert.Equal(".deploy/default/migration/1.0.0/*.ini", variables["migration"]);

		variables["version"] = "1.1.0";

		Assert.Equal(".deploy/default/migration/1.1.0/*.ini", variables["migration"]);
		Assert.Equal(new Version(1, 1, 0), variables.Version);
	}
	#endregion

	#region 文本来源
	[Fact]
	public void Read_SameNameAsWorkingDirectoryFile_UsesOnlySourceFile()
	{
		using var directory = new MigrationTestDirectory();
		const string fileName = "install.sh";
		directory.Write("working/" + fileName, "working-directory-content");
		directory.Write("source/" + fileName, "source-only-content");
		Normalizer.Initialize(new Dictionary<string, string>());
		var previous = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = Path.Combine(directory.Path, "working");

			Assert.Equal("source-only-content", TextSource.Read(Path.Combine(directory.Path, "source"), fileName));
			Assert.Equal("working-directory-content", File.ReadAllText(fileName));
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
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value));
	}

	[Theory]
	[InlineData("scripts/setup script.sh")]
	[InlineData("file:scripts/setup script.sh")]
	public void Read_RelativeFile_UsesSourceDirectory(string value)
	{
		using var directory = new MigrationTestDirectory();
		const string content = "echo source-specific-content";
		directory.Write("scripts/setup script.sh", content);
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.NotEqual(Environment.CurrentDirectory, directory.Path);
		Assert.Equal(content, TextSource.Read(directory.Path, value));
	}

	[Fact]
	public void Read_AbsoluteFile_ReadsContents()
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write("scripts/install.sh", "echo absolute-file");
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.Equal("echo absolute-file", TextSource.Read(Path.Combine(directory.Path, "another-source"), file));
	}

	[Fact]
	public void Read_ExplicitText_PreservesShellExpressionsAndWhitespace()
	{
		using var directory = new MigrationTestDirectory();
		const string content = "  echo $(name) %name% ${HOME}\n echo /opt/zongsoft/web  ";
		Normalizer.Initialize(new Dictionary<string, string> { ["name"] = "zongsoft.web" });

		Assert.Equal(content, TextSource.Read(directory.Path, "text:" + content));
	}

	[Fact]
	public void Read_SingleLineShellCommand_IsLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.Equal("echo /opt/zongsoft/web", TextSource.Read(directory.Path, "echo /opt/zongsoft/web"));
	}

	[Theory]
	[InlineData("$(name) %name% ${HOME}")]
	[InlineData("scripts/second.sh")]
	public void Read_FileContent_IsNeitherExpandedNorReadAgain(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/first.sh", content);
		directory.Write("scripts/second.sh", "echo must-not-be-read");
		Normalizer.Initialize(new Dictionary<string, string> { ["name"] = "zongsoft.web" });

		Assert.Equal(content, TextSource.Read(directory.Path, "scripts/first.sh"));
	}

	[Theory]
	[InlineData("file:scripts/missing.sh")]
	[InlineData("scripts/missing.sh")]
	public void Read_MissingFile_ReportsResolvedPath(string value)
	{
		using var directory = new MigrationTestDirectory();
		Normalizer.Initialize(new Dictionary<string, string>());

		var error = Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, value));

		Assert.Equal(Path.Combine(directory.Path, "scripts", "missing.sh"), error.FileName);
	}

	[Fact]
	public void Read_FileOnly_DoesNotAcceptLiteralText()
	{
		using var directory = new MigrationTestDirectory();
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.Throws<FileNotFoundException>(() => TextSource.Read(directory.Path, "echo installed", true));
	}

	[Fact]
	public void Read_FileOnly_ReadsExtensionlessFile()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("scripts/installed", "echo installed");
		Normalizer.Initialize(new Dictionary<string, string>());

		Assert.Equal("echo installed", TextSource.Read(directory.Path, "scripts/installed", true));
	}
	#endregion
}
