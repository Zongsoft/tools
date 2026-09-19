using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed partial class MigrateCommandTest
{
	#region 版本来源测试
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t ")]
	public async Task Execute_DefaultVersion_ReadsCurrentFileAndIgnoresEnvironmentAsync(string option)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "Different.Application@2.3.4\n");
		var content = File.ReadAllBytes(path);
		var timestamp = File.GetLastWriteTimeUtc(path);
		PrepareMigration(directory, "/data/hosting.db");
		var previous = Environment.GetEnvironmentVariable("version");
		try
		{
			Environment.SetEnvironmentVariable("version", "9.9.9");
			var result = await RunAsync(directory, VersionArguments(option));

			Assert.True(result.Code == 0, result.Output);
			AssertVersionArtifacts(directory, "2.3.4");
			Assert.Equal(content, File.ReadAllBytes(path));
			Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
		}
		finally { Environment.SetEnvironmentVariable("version", previous); }
	}

	[Theory]
	[InlineData("2.3")]
	[InlineData("2.3.4")]
	[InlineData("2.3.4.5")]
	public async Task Execute_LiteralVersion_DoesNotReadCorruptDefaultFileAsync(string version)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "not a valid application version");
		PrepareMigration(directory, "/data/hosting.db");

		var result = await RunAsync(directory, VersionArguments(version));

		Assert.True(result.Code == 0, result.Output);
		AssertVersionArtifacts(directory, version);
		Assert.Equal("not a valid application version", File.ReadAllText(path));
	}

	[Theory]
	[InlineData("0.0")]
	[InlineData("0.0.0")]
	[InlineData("0.0.0.0")]
	public async Task Execute_ZeroLiteralVersion_DoesNotFallBackToFileAsync(string version)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "Zongsoft.Hosting@2.3.4");

		var result = await RunAsync(directory, VersionArguments(version));

		Assert.NotEqual(0, result.Code);
		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
		Assert.Equal("Zongsoft.Hosting@2.3.4", File.ReadAllText(path));
	}

	[Theory]
	[InlineData("versions with spaces/application.release", false, false)]
	[InlineData("versions with spaces/application.release", true, false)]
	[InlineData("owner's files/application.release", true, false)]
	[InlineData("versions with spaces", false, true)]
	[InlineData("versions with spaces", true, true)]
	[InlineData("./1.0.0", false, false)]
	public async Task Execute_VersionFileOrDirectory_ResolvesPathWithoutChangingInputBaseAsync(string value, bool absolute, bool isDirectory)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(isDirectory ? value + "/.version" : value, "Other.Application@2.3.4\n");
		var original = File.ReadAllBytes(path);
		var timestamp = File.GetLastWriteTimeUtc(path);
		PrepareMigration(directory, "/data/hosting.db");
		File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
		try
		{
			var result = await RunAsync(directory, VersionArguments(absolute ? Path.Combine(directory.Path, value) : value));

			Assert.True(result.Code == 0, result.Output);
			AssertVersionArtifacts(directory, "2.3.4");
			Assert.Equal(original, File.ReadAllBytes(path));
			Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
			Assert.False(File.Exists(Path.Combine(directory.Path, ".version")));
		}
		finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
	}

	[Fact]
	public async Task Execute_NumericFileName_UsesLiteralUnlessExplicitRelativePathAsync()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write("1.0.0", "Other.Application@2.3.4");
		PrepareMigration(directory, "/data/hosting.db");

		var literal = await RunAsync(directory, VersionArguments("1.0.0"));
		var file = await RunAsync(directory, VersionArguments("./1.0.0"));

		Assert.True(literal.Code == 0, literal.Output);
		Assert.True(file.Code == 0, file.Output);
		AssertVersionArtifacts(directory, "1.0.0");
		AssertVersionArtifacts(directory, "2.3.4");
		Assert.Equal("Other.Application@2.3.4", File.ReadAllText(path));
	}
	#endregion

	#region 版本分支测试
	[Theory]
	[InlineData("Other.Application@2.3.4", null, null, "2.3.4")]
	[InlineData("Other.Application@2.3.4", " \t ", null, "2.3.4")]
	[InlineData("Other.Application\n[Enterprise]\n2.3.4", null, "Enterprise", "2.3.4")]
	[InlineData("Other.Application\n[Enterprise]\n2.3.4", "", "Enterprise", "2.3.4")]
	[InlineData("Other.Application\n[Enterprise]\n2.3.4", " \t ", "Enterprise", "2.3.4")]
	[InlineData("Other.Application\n[Community]\n1.0.0\n[Enterprise]\n2.3.4", "enterprise", "Enterprise", "2.3.4")]
	public async Task Execute_VersionEdition_SelectsExpectedVersionAndCanonicalSpellingAsync(string text, string edition, string selected, string version)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", text);
		var original = File.ReadAllBytes(path);
		PrepareMigration(directory, "/data/hosting.db");
		var arguments = VersionArguments(null);
		if(edition != null)
			arguments.Add("--edition:" + edition);

		var result = await RunAsync(directory, arguments);

		Assert.True(result.Code == 0, result.Output);
		AssertVersionArtifacts(directory, version, selected);
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Theory]
	[InlineData("Other.Application\n[Community]\n1.0.0\n[Enterprise]\n2.3.4", null)]
	[InlineData("Other.Application\n[Community]\n1.0.0\n[Enterprise]\n2.3.4", " \t ")]
	[InlineData("Other.Application\n[Enterprise]\n2.3.4", "Missing")]
	[InlineData("Other.Application@2.3.4", "Enterprise")]
	[InlineData("Other.Application@0.0.0", null)]
	public async Task Execute_InvalidVersionEdition_PreservesFileAndExistingOutputsAsync(string text, string edition)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", text);
		var original = File.ReadAllBytes(path);
		var archive = directory.Write("out/zongsoft.daemon-migrate@2.3.4_linux-x64.tar.gz", "previous archive");
		var launcher = directory.Write("out/zongsoft.daemon-migrate@2.3.4_linux-x64.sh", "previous launcher");
		var arguments = VersionArguments(null);
		arguments.Add("--overwrite");
		if(edition != null)
			arguments.Add("--edition:" + edition);

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.Contains(path, result.Error.Message);
		Assert.Equal(original, File.ReadAllBytes(path));
		Assert.Equal("previous archive", File.ReadAllText(archive));
		Assert.Equal("previous launcher", File.ReadAllText(launcher));
		Assert.Equal(2, Directory.GetFiles(Path.Combine(directory.Path, "out")).Length);
	}

	[Fact]
	public async Task Execute_ResolvedVersionAndEdition_ExpandInputOutputAndPlanConsistentlyAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("versions/app.release", "Other.Application\n[Enterprise]\n2.3.4");
		directory.Write("Enterprise/2.3.4/db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("Enterprise/2.3.4/db.env", "[sqlite]\nDatabase=/data/hosting.db\n");
		directory.Write("Enterprise/2.3.4/schema.sql", "SELECT 234;");
		var arguments = VersionArguments("versions/app.release");
		arguments.Remove("--output:out");
		arguments.Remove("db.migration");
		arguments.Add("--edition:enterprise");
		arguments.Add("--output:out/$(edition)/$(version)");
		arguments.Add("--title:$(name) $(edition) $(version)");
		arguments.Add("$(edition)/$(version)/db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.True(result.Code == 0, result.Output);
		AssertVersionArtifacts(directory, "2.3.4", "Enterprise", "out/Enterprise/2.3.4");
		var archive = Path.Combine(directory.Path, "out/Enterprise/2.3.4/zongsoft.daemon-migrate-Enterprise@2.3.4_linux-x64.tar.gz");
		var entries = ReadArchive(archive);
		using var plan = JsonDocument.Parse(Assert.Single(entries, entry => entry.Name == ".migration/migration.json").Content);
		Assert.Equal("zongsoft.daemon Enterprise 2.3.4", plan.RootElement.GetProperty("Title").GetString());
		Assert.Equal("SELECT 234;", System.Text.Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("0001.sql", StringComparison.Ordinal)).Content));
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "versions/out")));
	}
	#endregion

	#region 错误与变量测试
	[Fact]
	public async Task Execute_MissingDefaultVersion_DoesNotUseEnvironmentOrCreateFileAsync()
	{
		using var directory = new MigrationTestDirectory();
		var previous = Environment.GetEnvironmentVariable("version");
		try
		{
			Environment.SetEnvironmentVariable("version", "2.3.4");
			var result = await RunAsync(directory, VersionArguments(null));

			Assert.NotEqual(0, result.Code);
			Assert.Contains(Path.Combine(directory.Path, ".version"), Assert.IsType<InvalidDataException>(result.Error).Message);
			Assert.False(File.Exists(Path.Combine(directory.Path, ".version")));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
		}
		finally { Environment.SetEnvironmentVariable("version", previous); }
	}

	[Theory]
	[InlineData("missing.release", false)]
	[InlineData("versions with spaces", true)]
	public async Task Execute_MissingVersionFile_ReportsFullExpectedPathAsync(string option, bool directoryExists)
	{
		using var directory = new MigrationTestDirectory();
		if(directoryExists)
			Directory.CreateDirectory(Path.Combine(directory.Path, option));
		var expected = Path.Combine(directory.Path, option, directoryExists ? ".version" : "").TrimEnd(Path.DirectorySeparatorChar);

		var result = await RunAsync(directory, VersionArguments(option));

		Assert.NotEqual(0, result.Code);
		Assert.Contains(expected, Assert.IsType<InvalidDataException>(result.Error).Message);
		Assert.False(File.Exists(expected));
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Fact]
	public async Task Execute_DefaultVersion_DoesNotSearchParentsOrChildrenAsync()
	{
		using var directory = new MigrationTestDirectory();
		var parent = directory.Write(".version", "Zongsoft.Hosting@2.3.4");
		var child = directory.Write("work/child/.version", "Zongsoft.Hosting@3.4.5");
		var working = Path.Combine(directory.Path, "work");

		var result = await RunAsync(directory, VersionArguments(null), working);

		Assert.NotEqual(0, result.Code);
		Assert.Contains(Path.Combine(working, ".version"), Assert.IsType<InvalidDataException>(result.Error).Message);
		Assert.False(File.Exists(Path.Combine(working, ".version")));
		Assert.False(Directory.Exists(Path.Combine(working, "out")));
		Assert.Equal("Zongsoft.Hosting@2.3.4", File.ReadAllText(parent));
		Assert.Equal("Zongsoft.Hosting@3.4.5", File.ReadAllText(child));
	}

	[Theory]
	[InlineData("broken version contents")]
	[InlineData("")]
	[InlineData(" # comment only\n ")]
	public async Task Execute_MalformedVersionFile_PreservesExistingOutputsAsync(string content)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write("broken.release", content);
		var archive = directory.Write("out/zongsoft.daemon-migrate@2.3.4_linux-x64.tar.gz", "previous archive");
		var launcher = directory.Write("out/zongsoft.daemon-migrate@2.3.4_linux-x64.sh", "previous launcher");
		var arguments = VersionArguments("broken.release");
		arguments.Add("--overwrite");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.Contains(path, Assert.IsType<InvalidDataException>(result.Error).Message);
		Assert.Equal(content.Replace("\n", "\r\n"), File.ReadAllText(path));
		Assert.Equal("previous archive", File.ReadAllText(archive));
		Assert.Equal("previous launcher", File.ReadAllText(launcher));
		Assert.Equal(2, Directory.GetFiles(Path.Combine(directory.Path, "out")).Length);
	}

	[Fact(Skip = "Requires Windows file-sharing semantics.", SkipUnless = nameof(IsWindows))]
	public async Task Execute_UnreadableVersionFile_ReportsFullPathAndCreatesNoOutputAsync()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "Zongsoft.Hosting@2.3.4");
		using(var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			var result = await RunAsync(directory, VersionArguments(null));

			Assert.NotEqual(0, result.Code);
			Assert.Contains(path, Assert.IsType<InvalidDataException>(result.Error).Message);
			Assert.IsAssignableFrom<IOException>(result.Error.InnerException);
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
		}
		Assert.Equal("Zongsoft.Hosting@2.3.4", File.ReadAllText(path));
	}

	[Theory]
	[InlineData("versions/app.release", true)]
	[InlineData("$(zongsoft_version_path)", false)]
	[InlineData(null, false)]
	public async Task Execute_VersionPathVariable_ExpandsOrRejectsUnknownAndCyclesAsync(string value, bool succeeds)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write("versions/app.release", "Other.Application@2.3.4");
		PrepareMigration(directory, "/data/hosting.db");
		var previous = Environment.GetEnvironmentVariable("zongsoft_version_path");
		try
		{
			Environment.SetEnvironmentVariable("zongsoft_version_path", value);
			var result = await RunAsync(directory, VersionArguments("$(zongsoft_version_path)"));

			if(succeeds)
			{
				Assert.True(result.Code == 0, result.Output);
				AssertVersionArtifacts(directory, "2.3.4");
			}
			else
			{
				Assert.NotEqual(0, result.Code);
				Assert.Contains("zongsoft_version_path", Assert.IsType<InvalidOperationException>(result.Error).Message);
				Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
			}
			Assert.Equal("Other.Application@2.3.4", File.ReadAllText(path));
		}
		finally { Environment.SetEnvironmentVariable("zongsoft_version_path", previous); }
	}
	#endregion

	#region 版本测试辅助
	private static List<string> VersionArguments(string option)
	{
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.RemoveAll(argument => argument.StartsWith("--version:", StringComparison.Ordinal));
		if(option != null)
			arguments.Add("--version:" + option);
		arguments.Add("db.migration");
		return arguments;
	}

	private static void AssertVersionArtifacts(MigrationTestDirectory directory, string version, string edition = null, string output = "out")
	{
		var name = "zongsoft.daemon-migrate" + (edition == null ? "" : "-" + edition);
		var prefix = Path.Combine(directory.Path, output, name + "@" + version + "_linux-x64");
		Assert.True(File.Exists(prefix + ".tar.gz"), prefix);
		Assert.True(File.Exists(prefix + ".sh"), prefix);
		Assert.Contains(Path.GetFileName(prefix) + ".tar.gz", File.ReadAllText(prefix + ".sh"));
		using var plan = JsonDocument.Parse(Assert.Single(ReadArchive(prefix + ".tar.gz"), entry => entry.Name == ".migration/migration.json").Content);
		Assert.Equal(name, plan.RootElement.GetProperty("Package").GetString());
		Assert.Equal(version, plan.RootElement.GetProperty("Version").GetString());
	}
	#endregion
}
