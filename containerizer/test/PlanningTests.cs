using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class PlanningTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-planning-" + Guid.NewGuid().ToString("N"));
	public PlanningTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void PlanRetainsReferencesWarnsForMissingValuesAndNeverRunsEngine()
	{
		this.Write(".settings", "[redis]\ntag=8.4\nsettings=storage=temporary;password=$(cache_password)\n");
		var manifest = this.Create("redis", "rustfs");
		var path = new DeliveryBuilder(new RejectRunner()).Plan(manifest);
		var draft = ContainerManifest.Read(path);
		Assert.Equal("plan", draft["stage"]);
		Assert.False(draft.IsGenerated);
		Assert.Equal("$(cache_password)", draft.Components[0].Settings["password"]);
		Assert.Equal("temporary", draft.Components[0].Settings["storage"]);
		Assert.Equal("$(rustfs_access_key)", draft.Components[1].Settings["access-key"]);
		Assert.Null(draft.Components[0]["digest"]);
		Assert.Empty(Directory.GetFiles(_root, "*.tar.gz"));
		Assert.Equal("[redis]\ntag=8.4\nsettings=storage=temporary;password=$(cache_password)\n", File.ReadAllText(Path.Combine(_root, ".settings")));
	}

	[Theory]
	[InlineData(null, "offline")]
	[InlineData("online", "online")]
	[InlineData("offline", "offline")]
	public void ImagingOptionIsRecordedWithoutAnEngineAndPreservedForMake(string option, string expected)
	{
		var arguments = new List<string> { "redis", "--source:" + _root, "--name:example", "--version:1.0", "--distribution:debian" };
		if(option != null)
			arguments.Add("--imaging:" + option);
		var manifest = ContainerManifest.From(Context([.. arguments]), planning: true);
		var path = new DeliveryBuilder(new RejectRunner()).Plan(manifest);
		var make = ContainerManifest.From(Context(path), make: true);
		Assert.Equal(expected, make["imaging"]);
		Assert.Equal("offline", make["bootstrap"]);
		Assert.False(make.Root.ContainsKey("mode"));
	}

	[Fact]
	public void NacosModeIsAServiceSettingIndependentOfImaging()
	{
		var manifest = this.Create("nacos");
		manifest.Components[0]["settings"] = "mode=standalone";
		manifest.Components[0]["imaging"] = "online";
		var path = new DeliveryBuilder(new RejectRunner()).Plan(manifest);
		var component = Assert.Single(ContainerManifest.Read(path).Components);
		Assert.Equal("standalone", component.Settings["mode"]);
		Assert.Equal("online", component["imaging"]);
		Assert.False(component.Values.ContainsKey("mode"));
	}

	[Theory]
	[InlineData("x64")]
	[InlineData("arm64")]
	public void PlanRecordsTheTargetOnceAndMakeInheritsIt(string architecture)
	{
		var manifest = this.Create("redis");
		manifest["architecture"] = architecture;
		var path = new DeliveryBuilder(new RejectRunner()).Plan(manifest);
		var draft = ContainerManifest.Read(path);
		Assert.Equal(architecture, draft["architecture"]);
		Assert.Equal("debian@13", draft["distribution"]);
		var component = Assert.Single(draft.Components);
		Assert.False(component.Values.ContainsKey("platform"));
		Assert.False(component.Values.ContainsKey("architecture"));
		var make = ContainerManifest.From(Context(path), make: true);
		Assert.Equal(architecture, make["architecture"]);
		Assert.Single(DeliveryBuilder.PrepareSources(make));
	}

	[Theory]
	[InlineData(true, "architecture", "arm64", true)]
	[InlineData(false, "architecture", "arm64", true)]
	[InlineData(false, "distribution", "ubuntu@22.04", false)]
	public void ImageIdentityUsesTheRootTarget(bool planning, string key, string value, bool invalidated)
	{
		var manifest = this.Create("redis");
		DeliveryBuilder.PrepareSources(manifest);
		var component = manifest.Components[0];
		component["digest"] = "sha256:" + new string('a', 64);
		component["timestamp"] = "2026-09-15T05:30:05Z";
		component["size"] = "123456";
		var path = manifest.Prepare(_root, planning);
		Assert.Equal(component["digest"], Assert.Single(ContainerManifest.Read(path).Components)["digest"]);

		File.WriteAllText(path, File.ReadAllText(path).Replace(key + "=" + manifest[key], key + "=" + value, StringComparison.Ordinal));
		var make = ContainerManifest.From(Context(path), make: true);
		Assert.Equal(value, make[key]);
		var replay = Assert.Single(make.Components);

		foreach(var field in new[] { "digest", "timestamp", "size", "identity" })
			Assert.Equal(invalidated ? null : component[field], replay[field]);

		Assert.Single(DeliveryBuilder.PrepareSources(make));
		Assert.False(replay.Values.ContainsKey("platform"));
		Assert.False(replay.Values.ContainsKey("architecture"));
	}

	[Fact]
	public void MakeUsesEditedDraftAndResolvesValuesAfterSplittingSettings()
	{
		this.Write(".settings", "[redis]\ntag=8.4\nsettings=password=shared;storage=persistent\n");
		var path = new DeliveryBuilder(new RejectRunner()).Plan(this.Create("redis"));
		var text = File.ReadAllText(path);
		text = text.Replace("password=shared", "password=$(test_password)", StringComparison.Ordinal);
		File.WriteAllText(path, text);
		this.Write(".settings", "intentionally invalid and must not be read");
		var manifest = ContainerManifest.From(Context(path));
		manifest.Variables["test_password"] = "a;b=c\"d$(literal)";
		manifest.Variables["literal"] = "value";
		var source = Assert.Single(DeliveryBuilder.PrepareSources(manifest));
		Assert.Contains("a;b=c\"dvalue", source.Plan.Command);
		Assert.Contains("a;b=c\"dvalue", source.Plan.Health.Test);
		Assert.Equal("a;b=c\"dvalue", manifest.Components[0].Settings["password"]);
	}

	[Fact]
	public void EmptyCollectionUsesTemplateDefaultsAndEmptyPasswordSuppressesBinding()
	{
		var path = this.Write("draft.container", "name=example\nversion=1.0\ndistribution=debian\nstage=plan\n[redis]\nsettings=\n");
		this.Write(".settings", "[redis]\nsettings=password=shared;storage=temporary\n");
		var manifest = ContainerManifest.From(Context(path));
		var source = Assert.Single(DeliveryBuilder.PrepareSources(manifest));
		Assert.Equal("persistent", source.Settings["storage"]);
		Assert.Equal("both", source.Settings["persistence"]);
		Assert.Equal("", source.Settings["password"]);
		Assert.DoesNotContain("--requirepass", source.Plan.Command);

		var mysql = this.Create("mysql");
		mysql.Components[0]["settings"] = "root-password=";
		mysql.Variables["mysql_root_password"] = "must-not-be-used";
		Assert.Contains("root-password", new TemplateCatalog().Read(mysql.Components[0], mysql).MissingSettings);
	}

	[Theory]
	[InlineData("none", "temporary", "", "no")]
	[InlineData("rdb", "persistent", "3600 1 300 100 60 10000", "no")]
	[InlineData("aof", "temporary", "", "yes")]
	[InlineData("both", "persistent", "3600 1 300 100 60 10000", "yes")]
	public void CacheSettingsGenerateMatchingCommandsMountsAndPorts(string persistence, string storage, string snapshot, string append)
	{
		var manifest = this.Create("redis");
		manifest.Components[0]["settings"] = $"persistence={persistence};storage={storage};port=none";
		var source = Assert.Single(DeliveryBuilder.PrepareSources(manifest));
		var command = source.Plan.Command;
		Assert.Equal(snapshot, command[Array.IndexOf(command, "--save") + 1]);
		Assert.Equal(append, command[Array.IndexOf(command, "--appendonly") + 1]);
		Assert.Equal(storage == "temporary", Assert.Single(source.Plan.Mounts).Temporary);
		Assert.Equal(6379, Assert.Single(source.Plan.Ports).Container);
		source.Plan.Image.Tag = "example";
		source.Plan.Image.Platform = "linux/amd64";
		ComposeWriter.Write(new DeliveryPlan { Project = "example" }, [source], _root);
		using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "compose.yaml")));
		var service = json.RootElement.GetProperty("services").GetProperty("redis");
		Assert.False(service.TryGetProperty("ports", out _));
		var volume = service.GetProperty("volumes")[0];
		Assert.Equal(storage == "temporary" ? "volume" : "bind", volume.GetProperty("type").GetString());
		Assert.Equal(storage != "temporary", volume.TryGetProperty("source", out _));

		if(storage == "persistent")
			Assert.Equal("/var/lib/containerizer/data/example/redis", volume.GetProperty("source").GetString());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CustomDataMountsCollapseOnlyWhenThereIsOne(bool multiple)
	{
		var data = multiple ? "files=/files\nlogs=/logs\n" : "files=/files\n";
		this.Write("custom.template", $"version=1\nimage=docker.io/library/redis\n[data]\n{data}");
		var manifest = this.Create("redis");
		manifest.Components[0]["template"] = "custom.template";
		var source = new TemplateCatalog().Read(manifest.Components[0], manifest);

		if(multiple)
		{
			Assert.Equal(2, source.Plan.Mounts.Count);
			Assert.Contains(source.Plan.Mounts, mount => mount.Source == "/var/lib/containerizer/data/example/redis/files" && mount.Target == "/files");
			Assert.Contains(source.Plan.Mounts, mount => mount.Source == "/var/lib/containerizer/data/example/redis/logs" && mount.Target == "/logs");
		}
		else
		{
			var mount = Assert.Single(source.Plan.Mounts);
			Assert.Equal("/var/lib/containerizer/data/example/redis", mount.Source);
			Assert.Equal("/files", mount.Target);
		}
	}

	[Fact]
	public void ReplanningCompleteManifestKeepsDigestAndEscapesLiteralVariableShapes()
	{
		var manifest = this.Create("redis");
		DeliveryBuilder.PrepareSources(manifest);
		manifest.Components[0]["settings"] = ServiceSettings.Format(new Dictionary<string, string> { ["password"] = "$(absent);%absent%" });
		manifest.Components[0]["digest"] = "sha256:" + new string('a', 64);
		manifest.Prepare(_root);
		var original = File.ReadAllBytes(manifest.ManifestPath);
		var clone = ContainerManifest.From(Context(manifest.ManifestPath, "--version:2.0"), planning: true);
		var path = new DeliveryBuilder(new RejectRunner()).Plan(clone);
		var remake = ContainerManifest.From(Context(path));
		var source = Assert.Single(DeliveryBuilder.PrepareSources(remake));
		Assert.Contains("$(absent);%absent%", source.Plan.Command);
		Assert.Equal(manifest.Components[0]["digest"], remake.Components[0]["digest"]);
		Assert.Equal(original, File.ReadAllBytes(manifest.ManifestPath));
		File.WriteAllText(path, File.ReadAllText(path).Replace("tag=latest", "tag=new", StringComparison.Ordinal));
		Assert.Null(ContainerManifest.Read(path).Components[0]["digest"]);
	}

	[Theory]
	[InlineData("password=\"a;b=c\";storage=temporary", "a;b=c")]
	[InlineData("password='a;\"b';storage=temporary", "a;\"b")]
	[InlineData("password=;storage=temporary", "")]
	public void SettingsRoundTripEmptyAndQuotedValues(string input, string password)
	{
		var values = ServiceSettings.Parse(input);
		Assert.Equal(password, values["password"]);
		Assert.Equal(password, ServiceSettings.Parse(ServiceSettings.Format(values))["password"]);
		Assert.Equal("temporary", values["storage"]);
	}

	[Theory]
	[InlineData("127.0.0.1:6379", "password=127.0.0.1:6379")]
	[InlineData("persistent", "password=persistent")]
	[InlineData("$(redis_password)", "password=$(redis_password)")]
	[InlineData("a=b", "password=a=b")]
	[InlineData("=abc==", "password==abc==")]
	[InlineData("", "password=")]
	[InlineData("a;b", "password=\"a;b\"")]
	[InlineData("a\"b", "password=\"a\"\"b\"")]
	[InlineData("'abc'", "password=\"'abc'\"")]
	[InlineData(" padded ", "password=\" padded \"")]
	[InlineData(@"C:\data\cache", @"password=C:\data\cache")]
	public void SettingsOutputQuotesOnlyAmbiguousValuesAndPreservesTheirContents(string value, string expected)
	{
		var output = ServiceSettings.Format(new Dictionary<string, string> { ["password"] = value });
		Assert.Equal(expected, output);
		Assert.Equal(value, ServiceSettings.Parse(output)["password"]);
	}

	[Theory]
	[InlineData("127.0.0.1:6380", "127.0.0.1", 6380)]
	[InlineData("6380", "127.0.0.1", 6380)]
	[InlineData("[::1]:6380", "::1", 6380)]
	public void HostPortSupportsNumbersAndExplicitAddresses(string value, string address, int port)
	{
		var manifest = this.Create("redis");
		manifest.Components[0]["settings"] = "port=" + value;
		var mapped = Assert.Single(Assert.Single(DeliveryBuilder.PrepareSources(manifest)).Plan.Ports);
		Assert.Equal(address, mapped.Address);
		Assert.Equal(port, mapped.Host);
		Assert.Equal(6379, mapped.Container);
	}

	[Fact]
	public void InvalidValuesAndUnknownParametersFailDuringPlan()
	{
		foreach(var settings in new[] { "storage=memory", "persistence=unknown", "typo=true", "port=0" })
		{
			var manifest = this.Create("redis");
			manifest.Components[0]["settings"] = settings;
			Assert.Throws<ContainerizationException>(() => new DeliveryBuilder(new RejectRunner()).Plan(manifest));
		}
	}

	[Fact]
	public void RequiredBindingsWarnForEmptyVariablesAndAcceptExplicitEnvironment()
	{
		var manifest = this.Create("mysql");
		manifest.Variables["mysql_root_password"] = "";
		Assert.Contains("root-password", new TemplateCatalog().Read(manifest.Components[0], manifest).MissingSettings);
		manifest.Components[0]["environment!MYSQL_ROOT_PASSWORD"] = "explicit";
		var source = new TemplateCatalog().Read(manifest.Components[0], manifest);
		Assert.Empty(source.MissingSettings);
		Assert.Equal("explicit", source.Settings["root-password"]);
		manifest.Components[0]["settings"] = "root-password=different";
		Assert.Throws<ContainerizationException>(() => new TemplateCatalog().Read(manifest.Components[0], manifest));
	}

	[Fact]
	public void SettingsRejectMultilineValuesBeforeManifestPublication()
	{
		Assert.Empty(ServiceSettings.Parse("  "));
		Assert.Throws<ContainerizationException>(() => ServiceSettings.Parse("password=\"a\nb\""));
		Assert.Throws<ContainerizationException>(() => ServiceSettings.Format(new Dictionary<string, string> { ["password"] = "a\nb" }));
	}

	[Fact]
	public void PackagedNginxFragmentsAreTransferredUnchanged()
	{
		var manifest = this.Create("nginx");
		manifest.Components.Add(new ContainerManifest.Component { Name = "web", ["package"] = "app.tar.gz", ["dependences"] = "nginx" });
		var nginx = new ServiceBuildContext { Plan = new() { Id = "nginx" } };
		var web = new ServiceBuildContext { Plan = new() { Id = "web" } };
		var config = this.Write("app.conf", "server { listen 80; }\n");
		web.Ingress.Add("app.conf", config);
		DeliveryBuilder.ConfigureIngress(manifest, [nginx, web], _root);
		Assert.Equal(config, nginx.Configuration["/etc/nginx/conf.d/app.conf"]);
		Assert.Contains("web", nginx.Plan.Dependencies);
		Assert.Single(web.Ingress);
		Assert.Empty(web.Configuration);
		Assert.False(File.Exists(Path.Combine(_root, "nginx.generated.conf")));
	}

	[Fact]
	public void EnvironmentOverridesUnmanagedDefaultsAndTemplateNamesAreCaseInsensitive()
	{
		var source = new ServiceBuildContext();
		source.Environment["LANG"] = "old";
		TemplateCatalog.ApplyEnvironment(new ContainerManifest.Component { ["environment!LANG"] = "new" }, source);
		Assert.Equal("new", source.Environment["LANG"]);
		var manifest = this.Create("Redis");
		var cache = new TemplateCatalog().Read(manifest.Components[0], manifest);
		Assert.Equal("both", cache.Settings["persistence"]);
		Assert.Contains("--appendonly", cache.Plan.Command);
	}

	private ContainerManifest Create(params string[] components) => ContainerManifest.From(Context([.. components, "--source:" + _root, "--name:example", "--version:1.0", "--distribution:debian"]), planning: true);
	private static CommandContext Context(params string[] arguments) => new(new CommandExecutor(), CommandLine.Parse(Utility.FormatCommand("containerize", arguments))[0], new ContainerizeCommand(), null);
	private string Write(string name, string content)
	{
		var path = Path.Combine(_root, name);
		File.WriteAllText(path, content);
		return path;
	}
	private sealed class RejectRunner : IProcessRunner
	{
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900) => throw new InvalidOperationException("plan must not invoke an engine");
	}
}
