using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Terminals;
using Zongsoft.Components;
using Zongsoft.Services;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageCommandTest
{
	#region 命令选项
	[Theory]
	[InlineData("tar", null)]
	[InlineData("deb", null)]
	[InlineData("rpm", null)]
	[InlineData("tar", "--framework")]
	[InlineData("deb", "--framework")]
	[InlineData("rpm", "--framework")]
	[InlineData("tar", "--framework=")]
	[InlineData("deb", "--framework=")]
	[InlineData("rpm", "--framework=")]
	public async Task Command_FrameworkPresenceControlsBuildHostResolutionAsync(string format, string option)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("hosting/.env", "framework=net9.0\n");
		directory.Write("hosting/daemon/bin/Release/net9.0/example.dll", "net9.0 application");
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new List<string>
		{
			format, "--name:example", "--version:1.0.0", "--source:" + Path.Combine(directory.Path, "hosting", "daemon").Replace('\\', '/'),
			"--output:out", "--platform:Linux", "--install-path:/opt/example", "--compilation:Release",
		};

		if(option != null)
			arguments.Add(option);
		arguments.Add("bin/${compilation}/${framework}:~");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);

		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, MigrationPackageTest.RecordingTerminal>();
			var executor = new CommandExecutor();
			executor.Root.Children.Add(command);

			var path = Assert.IsType<string>(await executor.ExecuteAsync(Utility.FormatCommand(format, arguments.Skip(1).ToArray()), cancellation: TestContext.Current.CancellationToken));
			var entries = PackageArtifactTest.ReadArchive(path, format);
			var application = Assert.Single(entries, entry => entry.Name.EndsWith("example.dll", StringComparison.Ordinal));
			Assert.Equal("net9.0 application", Encoding.UTF8.GetString(application.Content));
			Assert.DoesNotContain("bin/", application.Name);

			if(option == null)
			{
				Assert.DoesNotContain("net9.0/", application.Name);
				var service = Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("example.service", StringComparison.Ordinal)).Content);
				Assert.Contains("ExecStart=dotnet /opt/example/example.dll", service);
			}
			else
			{
				Assert.EndsWith("net9.0/example.dll", application.Name, StringComparison.Ordinal);
				Assert.DoesNotContain(entries, entry => entry.Name.EndsWith(".service", StringComparison.Ordinal));
			}
		}
		finally
		{
			Terminal.Default = terminal;
		}
	}

	[Theory]
	[InlineData("tar", "files")]
	[InlineData("deb", "files")]
	[InlineData("rpm", "files")]
	[InlineData("tar", "published")]
	[InlineData("deb", "published")]
	[InlineData("rpm", "published")]
	[InlineData("tar", "service")]
	[InlineData("deb", "service")]
	[InlineData("rpm", "service")]
	[InlineData("tar", "build")]
	[InlineData("deb", "build")]
	[InlineData("rpm", "build")]
	public async Task Command_OptionalDotNetOptions_PackFilesAndResolveHostsAsync(string format, string sourceKind)
	{
		using var directory = new MigrationTestDirectory();
		const string SERVICE = "[Service]\nExecStart=/usr/bin/example\n";

		if(sourceKind is "files" or "service")
		{
			directory.Write("index.html", "<html>frontend</html>");
			directory.Write("assets/site.css", "body { color: black; }");
			if(sourceKind == "service")
				directory.Write("example.service", SERVICE);
		}
		else
			directory.Write(sourceKind == "build" ? "bin/Release/net10.0/example.dll" : "example.dll", "published application");

		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new List<string> { format, "--name:example", "--version:1.0.0", "--source:" + directory.Path.Replace('\\', '/'), "--output:out", "--platform:Linux", "--install-path:/opt/example" };

		if(sourceKind == "files")
			arguments.Add("--daemon:none");
		else if(sourceKind == "service")
			arguments.Add("--daemon:example.service");
		else if(sourceKind == "build")
		{
			arguments.Add("--framework:net10.0");
			arguments.Add("bin/${compilation}/${framework}:~");
		}

		var framework = Environment.GetEnvironmentVariable("framework");
		var compilation = Environment.GetEnvironmentVariable("compilation");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);

		try
		{
			Environment.SetEnvironmentVariable("framework", null);
			Environment.SetEnvironmentVariable("compilation", null);
			Terminal.Default = DispatchProxy.Create<ITerminal, MigrationPackageTest.RecordingTerminal>();
			var executor = new CommandExecutor();
			executor.Root.Children.Add(command);
			Assert.False(CommandDescriptor.Describe(command.GetType()).Options["framework"].Required);
			var path = Assert.IsType<string>(await executor.ExecuteAsync(CommandLine.Get(arguments.ToArray()), cancellation: TestContext.Current.CancellationToken));
			var entries = PackageArtifactTest.ReadArchive(path, format);
			using var identifier = new MemoryStream(Assert.Single(entries, entry => entry.Name.EndsWith(".version", StringComparison.Ordinal)).Content);
			Assert.Equal(new ApplicationIdentifier("example", null, new Version(1, 0, 0)), ApplicationIdentifier.Load(identifier));

			if(sourceKind is "files" or "service")
			{
				Assert.Equal("<html>frontend</html>", Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("index.html", StringComparison.Ordinal)).Content));
				Assert.Equal("body { color: black; }", Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("assets/site.css", StringComparison.Ordinal)).Content));
			}
			else
				Assert.Equal("published application", Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("example.dll", StringComparison.Ordinal)).Content));

			if(sourceKind == "files")
				Assert.DoesNotContain(entries, entry => entry.Name.EndsWith(".service", StringComparison.Ordinal));
			else
			{
				var service = Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith("example.service", StringComparison.Ordinal)).Content);
				if(sourceKind == "service")
					Assert.Equal(SERVICE.Replace("\n", "\r\n"), service);
				else
					Assert.Contains("ExecStart=dotnet /opt/example/example.dll", service);
			}
		}
		finally
		{
			Terminal.Default = terminal;
			Environment.SetEnvironmentVariable("framework", framework);
			Environment.SetEnvironmentVariable("compilation", compilation);
		}
	}
	#endregion

	#region 源版本保存
	[Theory]
	[InlineData("tar", false)]
	[InlineData("tar", true)]
	[InlineData("deb", false)]
	[InlineData("deb", true)]
	[InlineData("rpm", false)]
	[InlineData("rpm", true)]
	public async Task Command_ManifestUpdatesBothSourceFilesAndPackagedIdentityAsync(string format, bool hasIdentifier)
	{
		using var directory = new MigrationTestDirectory();
		var manifest = directory.Write(".edition", "; application\nexample=Enterprise\n[Community]\n1.0.0\n[Enterprise]\n3.0.0\n");
		var identifier = Path.Combine(directory.Path, ".version");

		if(hasIdentifier)
			directory.Write(".version", "different-Legacy@8.0.0\n");
		directory.Write("application.txt", "application payload");
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new[]
		{
			"--edition:community", "--version:2.0.0", "--source:" + directory.Path.Replace('\\', '/'), "--output:out",
			"--platform:Linux", "--architecture:X64", "--install-path:/opt/example", "--daemon:none", "application.txt",
		};
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);

		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, MigrationPackageTest.RecordingTerminal>();
			var executor = new CommandExecutor();
			executor.Root.Children.Add(command);

			var path = Assert.IsType<string>(await executor.ExecuteAsync(Utility.FormatCommand(format, arguments), cancellation: TestContext.Current.CancellationToken));
			var expected = new ApplicationIdentifier("example", "Community", new Version(2, 0, 0));
			Assert.Equal(expected, ApplicationIdentifier.Load(identifier));
			var saved = ApplicationManifest.Load(manifest);
			Assert.Equal("Community", saved.Editions.Current.Name);
			Assert.Equal(expected.Version, saved.Editions.Current.Version);
			Assert.Equal(new Version(3, 0, 0), saved.Editions["Enterprise"].Version);
			Assert.Contains("application", File.ReadAllText(manifest));
			var entries = PackageArtifactTest.ReadArchive(path, format);
			using var packaged = new MemoryStream(Assert.Single(entries, entry => entry.Name.EndsWith(".version", StringComparison.Ordinal)).Content);
			Assert.Equal(expected, ApplicationIdentifier.Load(packaged));
			Assert.DoesNotContain(entries, entry => entry.Name.EndsWith(".edition", StringComparison.Ordinal));
		}
		finally
		{
			Terminal.Default = terminal;
		}
	}
	#endregion
}
