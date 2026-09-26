using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Components;
using Zongsoft.Terminals;
using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class WebPackageTest
{
	[Theory]
	[InlineData("tar", false)]
	[InlineData("tar", true)]
	[InlineData("deb", false)]
	[InlineData("deb", true)]
	[InlineData("rpm", false)]
	[InlineData("rpm", true)]
	public async Task CommandProducesTheSameConfigurationAndPreservesProfileSelectionAsync(string format, bool exclude)
	{
		using var files = new MigrationTestDirectory();
		var profile = files.Write("web.profile", "[api]\nhost=api.example.com\nbind!legacy=http://*,http://[::]\nserver=http://app:8069");
		files.Write(".version", "example@1.0.0");
		var args = new List<string> { "--web:nginx" };
		if(exclude)
			args.Add("--exclude:*.profile");

		var path = await ExecuteAsync(format, files, args);
		var entries = PackageArtifactTest.ReadArchive(path, format);
		var prefix = format == "tar" ? "" : "opt/example/";
		Assert.Equal(!exclude, entries.Any(item => item.Name == prefix + "web.profile"));
		var configuration = Assert.Single(entries, item => item.Name == prefix + ".web/nginx/example.conf");
		Assert.Equal(Utility.Unix.Mode644, configuration.Mode);
		PackageArtifactTest.AssertOrdinaryConfiguration(path, format, "/opt/example/.web/nginx/example.conf");
		Assert.Equal(NginxConfiguratorTest.Configure(files, File.ReadAllText(profile)).Files[0].Content.Render("/opt/example"), Encoding.UTF8.GetString(configuration.Content));
		Assert.DoesNotContain(entries, item => item.Name.Contains("etc/nginx", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, item => item.Name.EndsWith(".hoster", StringComparison.Ordinal));
		Assert.Equal("[api]\r\nhost=api.example.com\r\nbind!legacy=http://*,http://[::]\r\nserver=http://app:8069", File.ReadAllText(profile));
		if(format == "deb")
		{
			var postinst = PackageArtifactTest.ReadControl(path, "postinst");
			Assert.Contains("configure)", postinst);
			Assert.True(postinst.IndexOf("HOSTER_WEB_CHANGED=0", StringComparison.Ordinal) < postinst.IndexOf("hoster_web_check", StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task InvalidWebDoesNotPublishOrSaveSourceVersionAsync()
	{
		using var files = new MigrationTestDirectory();
		var version = files.Write(".version", "example@1.0.0");
		files.Write("web.profile", "#@import absent.profile\n[api]");
		var before = File.ReadAllBytes(version);
		await Assert.ThrowsAsync<DefinitionException>(() => ExecuteAsync("tar", files, ["--web:nginx"]));
		Assert.Equal(before, File.ReadAllBytes(version));
		Assert.False(Directory.Exists(Path.Combine(files.Path, "out")));
	}

	[Theory]
	[InlineData("tar", ".web/nginx/example.conf")]
	[InlineData("tar", "/opt/example/.web/nginx/example.conf")]
	[InlineData("deb", ".web/nginx/example.conf")]
	[InlineData("deb", "/opt/example/.web/nginx/example.conf")]
	[InlineData("rpm", ".web/nginx/example.conf")]
	public async Task OrdinaryAliasCannotReplaceGeneratedFileAsync(string format, string target)
	{
		using var files = new MigrationTestDirectory();
		files.Write(".version", "example@1.0.0");
		files.Write("web.profile", "[api]\nbind!legacy=http://*\nserver=http://app");
		files.Write("manual.conf", "manual");
		var error = await Assert.ThrowsAsync<DefinitionException>(() => ExecuteAsync(format, files, ["--web:nginx", "manual.conf:" + target]));
		Assert.Equal("Conflict", error.Diagnostic.Code);
	}

	[Fact]
	public void ApplicationReferenceUsesOnlyTheGeneratedServiceListen()
	{
		using var files = new MigrationTestDirectory();
		files.Write("example.dll", "");
		var variables = new Variables(new Dictionary<string, string> { ["source"] = files.Path, ["listen"] = "8069" });
		var package = new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, variables);
		package.Host = ApplicationHost.Resolve(package);
		package.Scriptor.Script();
		var host = package.Host;
		Assert.Equal(ApplicationHost.HostKind.Generated, host.Kind);
		Assert.Equal("http://127.0.0.1:8069", host.Listen);
		var definition = Definition.Load(files.Write("web.profile", "[api]\nbind!legacy=http://*"));
		var content = new Configurator.Nginx().Configure(definition, new("example", "/opt/example", variables.Raw, host.Listen)).Files[0].Content.Render("/opt/example");
		Assert.Contains("proxy_pass http://127.0.0.1:8069;", content);
		using var reader = new StreamReader(Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(".service", StringComparison.Ordinal)).OpenRead());
		Assert.Contains("--urls http://127.0.0.1:8069", reader.ReadToEnd());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("NoNe:")]
	public void DisabledOptionDoesNotProbeTheInput(string value)
	{
		using var files = new MigrationTestDirectory();
		Assert.False(Configurator.Parse(value, files.Path, new Variables()).Enabled);
	}

	[Theory]
	[InlineData(":web.profile")]
	[InlineData("none:web.profile")]
	[InlineData("iis")]
	[InlineData("ngnix")]
	[InlineData("nginx:*.profile")]
	[InlineData("nginx:.")]
	public void InvalidOptionsFailExplicitly(string value)
	{
		using var files = new MigrationTestDirectory();
		Assert.Throws<DefinitionException>(() => Configurator.Parse(value, files.Path, new Variables()));
	}

	[Fact]
	public void OptionUsesSourceAndPreservesTheWindowsDriveColon()
	{
		using var files = new MigrationTestDirectory();
		var path = files.Write("nested input/web.profile", "");
		var variables = new Variables(new Dictionary<string, string> { ["input"] = path });
		Assert.Equal(path, Configurator.Parse("NGINX:$(input)", files.Path, variables).FilePath);
		Assert.Equal(path, Configurator.Parse("nginx:nested input/web.profile", files.Path, variables).FilePath);
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void PayloadConflictIsDetectedWhenTheGeneratedFileWasAddedFirst(string format)
	{
		using var files = new MigrationTestDirectory();
		var source = files.Write("manual.conf", "manual");
		var variables = new Variables(new Dictionary<string, string> { ["source"] = files.Path, ["daemon"] = "none" });
		Package package = format switch
		{
			"tar" => new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, variables),
			"deb" => new Package.Deb("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, variables),
			_ => new Package.Rpm("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, variables),
		};
		package.InstallPath = "/opt/example";
		Installation.Attach(package, NginxConfiguratorTest.Configure(files, "[api]\nbind!legacy=http://*\nserver=http://app"));
		var error = Assert.Throws<DefinitionException>(() => package.Entries.Add(files.Path, source + ":/opt/example/.web/nginx/example.conf"));
		Assert.Equal("Conflict", error.Diagnostic.Code);
	}


	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public async Task ExcludedSourceCanImportOutsideThePayloadRootAsync(string format)
	{
		using var files = new MigrationTestDirectory();
		files.Write("shared.profile", "[api]\nbind!legacy=http://*\nserver=http://shared:8069");
		files.Write("source/.version", "example@1.0.0");
		files.Write("source/web.profile", "#@import ../shared.profile");
		var path = await ExecuteAsync(format, files, ["--web:nginx", "--exclude:*.profile"], Path.Combine(files.Path, "source"));
		var entries = PackageArtifactTest.ReadArchive(path, format);
		Assert.DoesNotContain(entries, entry => entry.Name.EndsWith(".profile", StringComparison.Ordinal));
		Assert.Contains("proxy_pass http://shared:8069;", Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name.EndsWith(".web/nginx/example.conf", StringComparison.Ordinal)).Content));
	}

	private static async Task<string> ExecuteAsync(string format, MigrationTestDirectory files, IReadOnlyList<string> options, string source = null)
	{
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new[] { format, "--source:" + (source ?? files.Path), "--output:out", "--platform:Linux", "--framework:net10.0", "--daemon:none", "--install-path:/opt/example" }.Concat(options).ToArray();
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);
		var field = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previous = (ITerminal)field.GetValue(null);

		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, MigratorPackageTest.RecordingTerminal>();
			return Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));
		}
		finally
		{
			if(previous == null)
				field.SetValue(null, null);
			else
				Terminal.Default = previous;
		}
	}
}
