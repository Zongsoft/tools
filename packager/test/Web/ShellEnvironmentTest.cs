using System.IO;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class ShellEnvironmentTest
{
	[Theory]
	[InlineData("")]
	[InlineData("cmd")]
	[InlineData("bin")]
	[InlineData("usr/bin")]
	public void FindsToolsInNonstandardInstallationDirectories(string relative)
	{
		using var files = new MigrationTestDirectory();
		var bash = files.Write("portable git/bin/bash.exe", "placeholder");
		var cygpath = files.Write("portable git/usr/bin/cygpath.exe", "placeholder");
		var root = Path.Combine(files.Path, "portable git");
		var directory = Directory.CreateDirectory(Path.Combine(root, relative)).FullName;
		var tools = ShellEnvironment.FindWindows([directory]);

		Assert.NotNull(tools);
		Assert.Equal(bash, tools.Bash);
		Assert.Equal(cygpath, tools.Cygpath);
	}

	[Fact]
	public void DoesNotCombineToolsFromIncompleteInstallations()
	{
		using var files = new MigrationTestDirectory();
		files.Write("first/bin/bash.exe", "placeholder");
		files.Write("second/usr/bin/cygpath.exe", "placeholder");

		Assert.Null(ShellEnvironment.FindWindows([Path.Combine(files.Path, "first"), Path.Combine(files.Path, "second")]));
	}

	[Fact]
	public void MissingToolsAreUnavailable()
	{
		Assert.NotNull(ShellEnvironment.CheckSupport(null));
	}

	[Fact]
	public void NonExecutableToolsAreUnavailable()
	{
		using var files = new MigrationTestDirectory();
		var bash = files.Write("bash.exe", "placeholder");
		var cygpath = files.Write("cygpath.exe", "placeholder");

		Assert.NotNull(ShellEnvironment.CheckSupport(new(bash, cygpath)));
	}
}
