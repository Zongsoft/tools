using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;

using Xunit;

using Zongsoft.Terminals;
using Zongsoft.Components;
using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Packager.Tests;

public sealed partial class MigratorPackageTest
{
	[Fact]
	public async Task Command_EnvironmentSourceIsFixedAndIndependentOfWorkingDirectoryAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("working/.env", "zongsoft_env_payload=wrong.txt\n");
		directory.Write("source/.version", "zongsoft.daemon@1.0.0");
		directory.Write("source/.env", "source=../wrong\nzongsoft_env_source=../wrong\nname=wrong\nversion=9.9.9\n#@import settings/defaults.ini\n");
		directory.Write("source/settings/defaults.ini", "zongsoft_env_payload=application.txt\nzongsoft_env_output=$(source)/out/$(name)/$(version)\n");
		directory.Write("source/application.txt", "selected source payload");
		directory.Write("wrong/.version", "wrong@9.9.9");
		directory.Write("wrong/application.txt", "wrong source payload");
		var previousDirectory = Environment.CurrentDirectory;
		var previousSource = Environment.GetEnvironmentVariable("zongsoft_env_source");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Environment.CurrentDirectory = Path.Combine(directory.Path, "working");
			Environment.SetEnvironmentVariable("zongsoft_env_source", "../source");
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var command = new TarCommand();
			var arguments = new[] { "tar", "--source:$(zongsoft_env_source)", "--output:$(zongsoft_env_output)", "--platform:Linux", "--architecture:X64", "--framework:net10.0", "--daemon:disabled", "$(zongsoft_env_payload)" };
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);

			var path = Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));

			Assert.Equal(Path.Combine(directory.Path, "source", "out", "zongsoft.daemon", "1.0.0", "zongsoft.daemon@1.0.0-x64.tar.gz"), path);
			Assert.Equal("selected source payload", Encoding.UTF8.GetString(Assert.Single(ReadPayload(Path.GetDirectoryName(path), "tar"), entry => entry.Key.EndsWith("application.txt", StringComparison.Ordinal)).Value));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "working", "out")));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "wrong", "out")));
			Assert.Equal("wrong@9.9.9", File.ReadAllText(Path.Combine(directory.Path, "wrong", ".version")));
		}
		finally
		{
			Terminal.Default = previousTerminal;
			Environment.CurrentDirectory = previousDirectory;
			Environment.SetEnvironmentVariable("zongsoft_env_source", previousSource);
		}
	}

	[Fact]
	public async Task Command_EnvironmentFileCannotLocateSourceFromWorkingDirectoryAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "zongsoft_env_only_source=source\n");
		directory.Write("source/.version", "zongsoft.daemon@1.0.0");
		directory.Write("source/application.txt", "payload");
		var previousDirectory = Environment.CurrentDirectory;
		var previousSource = Environment.GetEnvironmentVariable("zongsoft_env_only_source");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Environment.CurrentDirectory = directory.Path;
			Environment.SetEnvironmentVariable("zongsoft_env_only_source", null);
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var command = new TarCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse("tar --source:$(zongsoft_env_only_source) --output:out --platform:Linux --architecture:X64 --daemon:disabled application.txt")[0], command, null);

			var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));

			Assert.Contains("zongsoft_env_only_source", error.Message, StringComparison.Ordinal);
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "source", "out")));
			Assert.Equal("zongsoft.daemon@1.0.0", File.ReadAllText(Path.Combine(directory.Path, "source", ".version")));
		}
		finally
		{
			Terminal.Default = previousTerminal;
			Environment.CurrentDirectory = previousDirectory;
			Environment.SetEnvironmentVariable("zongsoft_env_only_source", previousSource);
		}
	}

	[Fact]
	public async Task Command_EnvironmentFileFailurePreservesArtifactsAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "zongsoft.daemon@1.0.0");
		directory.Write(".env", "#@import .env\n");
		directory.Write("application.txt", "payload");
		var artifact = directory.Write("out/zongsoft.daemon@1.0.0-x64.tar.gz", "previous archive");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var command = new TarCommand();
			var arguments = new[] { "tar", "--source:" + directory.Path, "--output:out", "--platform:Linux", "--architecture:X64", "--framework:net10.0", "--daemon:disabled", "--overwrite", "application.txt" };
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);

			await Assert.ThrowsAsync<ProfileException>(async () => await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));

			Assert.Equal("previous archive", File.ReadAllText(artifact));
			Assert.Equal("zongsoft.daemon@1.0.0", File.ReadAllText(Path.Combine(directory.Path, ".version")));
			Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "out")));
		}
		finally
		{
			Terminal.Default = previousTerminal;
		}
	}
}
