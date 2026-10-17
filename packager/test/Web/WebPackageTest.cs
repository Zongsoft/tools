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
	[Fact]
	public async Task ApplicationRoutesAndTemplatesShareRootReferencesAcrossFormatsAsync()
	{
		using var files = new MigrationTestDirectory();
		files.Write(".version", "example@1.0.0");
		files.Write("web.profile", "[api]\nhost=api.example.test\nbind!public=http://*");
		files.Write("web.option", "<?zongsoft.tools.binding name=\"web\" path=\"Server\" template=\"${this.Url}\" protocols=\"${this.Protocols}\" required=\"true\"?>\n<configuration><option><server url=\"http://*:8069\" protocols=\"Http1\" /></option></configuration>");
		files.Write("metrics.option", "<?zongsoft.tools.binding name=\"metrics\" path=\"Metrics\" template=\"${this.Url}\" protocols=\"${this.Protocols}\" required=\"true\"?>\n<configuration><option><metrics url=\"http://*:9464/metrics\" protocols=\"Http1\" /></option></configuration>");
		files.Write("telemetry.option", "<?zongsoft.tools.binding name=\"telemetry\" path=\"Telemetry\" template=\"${this.Url}\" protocols=\"${this.Protocols}\" required=\"true\"?>\n<configuration><option><telemetry url=\"http://*:4317/opentelemetry\" protocols=\"grpc+http2\" /></option></configuration>");
		var stale = files.Write(".web/nginx/.bindings", "[legacy]\nbind=http://*:9");
		var originalLegacy = File.ReadAllBytes(stale);
		byte[] baseline = null;
		string baselineTemplate = null;

		foreach(var format in new[] { "tar", "deb", "rpm" })
		{
			var archive = await ExecuteAsync(format, files, ["--web:nginx", "--version:1.0.0", "--exclude:out"]);
			var entries = PackageArtifactTest.ReadArchive(archive, format);
			var prefix = format == "tar" ? "" : "opt/example/";
			var entry = Assert.Single(entries, item => item.Name == prefix + ".bindings");
			var document = BindingDocument.Deserialize(entry.Content);
			var site = Assert.Single(document.Sites);
			var applications = document.Bindings.Where(binding => binding.Owner == "application").ToArray();
			var configuration = Encoding.UTF8.GetString(Assert.Single(entries, item => item.Name == prefix + ".web/nginx/example.conf").Content);
			var template = Encoding.UTF8.GetString(Assert.Single(entries, item => item.Name == prefix + ".web/nginx/example.conf.template").Content);

			Assert.Equal(Utility.Unix.Mode644, entry.Mode);
			Assert.Equal(3, applications.Length);
			Assert.Equal(4, site.Routes.Length);
			Assert.Equal(["api.example.test"], site.Hosts);
			Assert.DoesNotContain(entries, item => item.Name == prefix + ".web/nginx/.bindings");
			Assert.DoesNotContain("{{zongsoft:", configuration);
			Assert.DoesNotContain("{{zongsoft:application", template);

			foreach(var route in site.Routes)
			{
				var binding = Assert.Single(applications, binding => binding.Id == route.Target.Binding);
				var endpoint = Assert.Single(binding.Endpoints, endpoint => endpoint.Id == route.Target.Endpoint);
				Assert.Equal("0.0.0.0", endpoint.Host);
				Assert.Equal("127.0.0.1", route.Connection.Host);
				Assert.Equal(endpoint.Port, route.Connection.Port);
				Assert.Contains("{{zongsoft:binding:" + binding.Id + ":" + endpoint.Id + "}}", template);
			}

			Assert.Contains("grpc_pass grpc://127.0.0.1:4317;", configuration);
			Assert.Contains("proxy_pass http://127.0.0.1:9464;", configuration);
			Assert.Equal(originalLegacy, File.ReadAllBytes(stale));

			if(baseline == null)
			{
				baseline = entry.Content;
				baselineTemplate = template;
			}
			else
			{
				Assert.Equal(baseline, entry.Content);
				Assert.Equal(baselineTemplate, template);
			}

			PackageArtifactTest.AssertOrdinaryConfiguration(archive, format, "/opt/example/.bindings");
		}
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public async Task OrdinaryRootBindingsConflictBeforePublicationAsync(string format)
	{
		using var files = new MigrationTestDirectory();
		files.Write(".version", "example@1.0.0");
		files.Write("web.profile", "[api]\nbind!public=http://*\nserver=http://backend");
		var path = files.Write(".bindings", "[legacy]\nbind=http://*:10");
		var original = File.ReadAllBytes(path);

		await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(format, files, ["--web:nginx"]));

		Assert.Equal(original, File.ReadAllBytes(path));
		Assert.False(Directory.Exists(Path.Combine(files.Path, "out")));
		Assert.Equal("example@1.0.0", File.ReadAllText(Path.Combine(files.Path, ".version")));
	}

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

		var bindings = Assert.Single(entries, item => item.Name == prefix + ".bindings");
		Assert.Equal(Utility.Unix.Mode644, bindings.Mode);
		var document = BindingDocument.Deserialize(bindings.Content);
		Assert.Equal(["api.example.com"], Assert.Single(document.Sites).Hosts);
		Assert.Equal(2, document.Bindings.Length);
		Assert.DoesNotContain(entries, item => item.Name == prefix + ".web/nginx/.bindings");

		var template = Assert.Single(entries, item => item.Name == prefix + ".web/nginx/example.conf.template");
		Assert.Equal(configuration.Content, template.Content);
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

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public async Task UnsupportedExportCannotCarryStaleBindingsAsync(string format)
	{
		using var files = new MigrationTestDirectory();
		files.Write(".version", "example@1.0.0");
		files.Write("web.profile", "[api]\nbind!legacy=http://*\nserver=http://app\nnginx:listen=80 proxy_protocol");

		var stale = files.Write(".web/nginx/.bindings", "[old]\nbind=http://0.0.0.0:9");
		await Assert.ThrowsAsync<DefinitionException>(() => ExecuteAsync(format, files, ["--web:nginx"]));
		Assert.False(Directory.Exists(Path.Combine(files.Path, "out")));
		Assert.Equal("[old]\r\nbind=http://0.0.0.0:9", File.ReadAllText(stale));
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
	public void GeneratedServiceUsesItsConfigurationWithoutInjectingUrls()
	{
		using var files = new MigrationTestDirectory();
		files.Write("example.dll", "");
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["source"] = files.Path.Replace('\\', '/') }));
		var package = new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, options);
		package.Host = ApplicationHost.Resolve(package);
		package.Scriptor.Script();
		var host = package.Host;

		Assert.Equal(ApplicationHost.HostKind.Generated, host.Kind);
		using var reader = new StreamReader(Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(".service", StringComparison.Ordinal)).OpenRead());
		var content = reader.ReadToEnd();
		Assert.Contains("ExecStart=dotnet /opt/example/example.dll", content);
		Assert.DoesNotContain("--urls", content);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("NoNe:")]
	public void DisabledOptionDoesNotProbeTheInput(string value)
	{
		using var files = new MigrationTestDirectory();
		Assert.False(Configurator.Parse(value, files.Path, new PackageOptions()).Enabled);
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
		Assert.Throws<DefinitionException>(() => Configurator.Parse(value, files.Path, new PackageOptions()));
	}

	[Fact]
	public void OptionUsesSourceAndPreservesTheWindowsDriveColon()
	{
		using var files = new MigrationTestDirectory();
		var path = files.Write("nested input/web.profile", "");
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["input"] = path.Replace('\\', '/') }));

		Assert.Equal(path, Configurator.Parse("NGINX:${input}", files.Path, options).FilePath);
		Assert.Equal(path, Configurator.Parse("nginx:nested input/web.profile", files.Path, options).FilePath);
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void PayloadConflictIsDetectedWhenTheGeneratedFileWasAddedFirst(string format)
	{
		using var files = new MigrationTestDirectory();
		var source = files.Write("manual.conf", "manual");
		var options = new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["source"] = files.Path.Replace('\\', '/'), ["daemon"] = "none" }));

		Package package = format switch
		{
			"tar" => new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, options),
			"deb" => new Package.Deb("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, options),
			_ => new Package.Rpm("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, options),
		};
		package.InstallPath = "/opt/example";

		Installation.Attach(package, NginxConfiguratorTest.Configure(files, "[api]\nbind!legacy=http://*\nserver=http://app"));
		var error = Assert.Throws<DefinitionException>(() => package.Entries.AddEntry(files.Path, source, "/opt/example/.web/nginx/example.conf", package.EntryPrefix));
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
		var arguments = new[] { format, "--source:" + (source ?? files.Path).Replace('\\', '/'), "--output:out", "--platform:Linux", "--framework:net10.0", "--daemon:none", "--install-path:/opt/example" }.Concat(options).ToArray();
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);
		var field = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previous = (ITerminal)field.GetValue(null);

		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, MigrationPackageTest.RecordingTerminal>();
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
