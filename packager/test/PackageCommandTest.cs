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

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageCommandTest
{
	#region 命令选项
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
		var arguments = new List<string> { format, "--name:example", "--version:1.0.0", "--source:" + directory.Path, "--output:out", "--platform:Linux", "--install-path:/opt/example" };
		if(sourceKind == "files")
			arguments.Add("--daemon:none");
		else if(sourceKind == "service")
			arguments.Add("--daemon:example.service");
		else if(sourceKind == "build")
		{
			arguments.Add("--framework:net10.0");
			arguments.Add("bin/$(compilation)/$(framework):~");
		}

		var framework = Environment.GetEnvironmentVariable("framework");
		var compilation = Environment.GetEnvironmentVariable("compilation");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Environment.SetEnvironmentVariable("framework", null);
			Environment.SetEnvironmentVariable("compilation", null);
			Terminal.Default = DispatchProxy.Create<ITerminal, MigratorPackageTest.RecordingTerminal>();
			var executor = new CommandExecutor();
			executor.Root.Children.Add(command);
			Assert.False(CommandDescriptor.Describe(command.GetType()).Options["framework"].Required);
			var path = Assert.IsType<string>(await executor.ExecuteAsync(CommandLine.Get(arguments.ToArray()), cancellation: TestContext.Current.CancellationToken));
			var entries = PackageArtifactTest.ReadArchive(path, format);
			Assert.Equal("example@1.0.0", Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith(".version", StringComparison.Ordinal)).Content));

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
}
