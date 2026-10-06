using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class WebIngressTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-web-" + Guid.NewGuid().ToString("N"));
	public WebIngressTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void PackageInfersHosterAndPublishesFrontendWithoutInternalListener()
	{
		var manifest = this.Manifest("[name]\nhost=api.example.test\nbind=http://0.0.0.0:80;http://[::]:80\n[port]\nbind=http://0.0.0.0:8080,http://[::]:8080\n");
		var sources = DeliveryBuilder.PrepareSources(manifest);
		var app = sources.Single(source => source.Plan.Id == "web");
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		Assert.Equal("nginx", manifest.Components[0]["dependences"]);
		Assert.Empty(app.Plan.Ports);
		Assert.Contains("8069", app.Plan.Health.Test[1]);
		Assert.Equal([80, 8080], nginx.Plan.Ports.Select(port => port.Container));
		Assert.Equal(4, nginx.Plan.Web.Sum(site => site.Bindings.Count));
		Assert.Contains("web", nginx.Plan.Dependencies);
		Assert.Equal("port", RunContext.Endpoint.Create(new() { Services = [app.Plan, nginx.Plan] })[0].Service.Web[1].Name);
		WebIngress.Render(manifest, sources, _root);
		Assert.Contains("proxy_pass http://web:8069", File.ReadAllText(nginx.Configuration["/etc/nginx/containerizer/web.conf"].Source));
		var main = File.ReadAllText(nginx.Configuration["/etc/nginx/nginx.conf"].Source);
		Assert.Contains("include /etc/nginx/containerizer/web.conf;", main);
		Assert.DoesNotContain("conf.d", main);
	}

	[Theory]
	[InlineData(null, "server {}")]
	[InlineData("[api]\nbind=http://0.0.0.0:80", null)]
	public void IncompleteDeclaredHosterNeverFallsBack(string bindings, string template)
	{
		var path = WebFixtures.WritePackage(Path.Combine(_root, "web.tar.gz"), "web", bindings, template);
		Assert.Throws<ContainerizationException>(() => PackageReader.Read(path));
	}

	[Theory]
	[InlineData("[api]\nbind=http://0.0.0.0:80,,http://[::]:80")]
	[InlineData("[api]\nbind=http://0.0.0.0:80/hello:80")]
	[InlineData("[api]\nbind=http://0.0.0.0")]
	[InlineData("[api]\nbind=http://example.test:80")]
	[InlineData("[api]\nbind=http://0.0.0.0:80\ndefault=http://0.0.0.0:81")]
	[InlineData("[api]\nbind=http://0.0.0.0:80\nbind=http://0.0.0.0:81")]
	[InlineData("state=ok\n[api]\nbind=http://0.0.0.0:80")]
	public void RejectsInvalidHandoff(string text) => Assert.Throws<ContainerizationException>(() => WebPackage.Parse(text, "test"));

	[Fact]
	public void ListsAcceptBothSeparatorsAndPreserveExplicitDefaults()
	{
		var site = Assert.Single(WebPackage.Parse("[api]\nhost=a.test;b.test, c.test\nbind=http://0.0.0.0:80;http://[::]:80\ndefault=http://0.0.0.0:80,http://[::]:80", "test"));
		Assert.Equal(["a.test", "b.test", "c.test"], site.Hosts);
		Assert.All(site.Bindings, binding => Assert.True(binding.ExplicitDefault));
	}

	[Theory]
	[InlineData("80:0")]
	[InlineData("80:65536")]
	[InlineData("90:8080")]
	[InlineData("80:8080,80:none")]
	[InlineData("80:443")]
	[InlineData("80:8080,443:8080")]
	public void RejectsInvalidOrConflictingPortOverrides(string text) => Assert.Throws<ContainerizationException>(() => WebIngress.Ports(text, [80, 443], "nginx"));

	[Fact]
	public void PortNoneRetainsNativeBindingsAndRemovesPublicationAndProbeRequirement()
	{
		var manifest = this.Manifest("[api]\nhost=*.example.test\nbind=https://127.0.0.1:443\n[local]\nbind=http://0.0.0.0:80", "port=80:18080,443:none");
		var nginx = DeliveryBuilder.PrepareSources(manifest).Single(source => source.Plan.Id == "nginx");
		Assert.Equal(18080, Assert.Single(nginx.Plan.Ports).Host);
		Assert.Null(nginx.Plan.Web[0].Bindings[0].Publication);
		Assert.Empty(nginx.Plan.Web[0].ProbeHosts);
		Assert.Equal("127.0.0.1", nginx.Plan.Web[0].Bindings[0].Address);
	}

	[Theory]
	[InlineData("http://127.0.0.1:80")]
	[InlineData("http://192.0.2.10:80")]
	[InlineData("http://[::]:80")]
	public void UnpublishableFrontendFailsMaking(string bindings)
	{
		var manifest = this.Manifest("[api]\nbind=" + bindings);
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(manifest));
	}

	[Fact]
	public void UnequalDualStackIsRejected()
	{
		var manifest = this.Manifest("[a]\nhost=a.test\nbind=http://0.0.0.0:80;http://[::]:80\n[b]\nhost=b.test\nbind=http://0.0.0.0:80");
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(manifest));
	}

	[Fact]
	public void WildcardProbeMustMatchItsSiteWithoutBeingShadowed()
	{
		var manifest = this.Manifest("[tenant]\nhost=*.example.test\nbind=http://0.0.0.0:80\n[admin]\nhost=admin.example.test\nbind=http://0.0.0.0:80");
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(manifest));
		manifest.Components[0]["probe-host!tenant"] = "admin.example.test";
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(manifest));
		manifest.Components[0]["probe-host!tenant"] = "demo.example.test";
		var nginx = DeliveryBuilder.PrepareSources(manifest).Single(source => source.Plan.Id == "nginx");
		Assert.Equal(["demo.example.test"], nginx.Plan.Web[0].ProbeHosts);
	}

	[Fact]
	public void HostlessSiteMustBeTheActualDefaultAndConflictingDefaultsFail()
	{
		var first = "[api]\nhost=api.test\nbind=http://0.0.0.0:80\n";
		var second = "[local]\nbind=http://0.0.0.0:80\n";
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(this.Manifest(first + second)));
		var nginx = DeliveryBuilder.PrepareSources(this.Manifest(first + second + "default=http://0.0.0.0:80")).Single(source => source.Plan.Id == "nginx");
		Assert.True(nginx.Plan.Web[1].Bindings[0].IsDefault);
		Assert.False(nginx.Plan.Web[0].Bindings[0].IsDefault);
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(this.Manifest(first + "default=http://0.0.0.0:80\n" + second + "default=http://0.0.0.0:80")));
	}

	[Fact]
	public void ResourcesAreCollectedOnceAndExternalInputsUseSourceAndReadonlyTargets()
	{
		var relative = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("./.certificates/site.pem")) + "}}";
		var external = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("/etc/tls/Trust.pem")) + "}}";
		var template = $"ssl_certificate \"{relative}\";\nssl_certificate_key \"{relative}\";\nproxy_ssl_trusted_certificate \"{external}\";";
		var manifest = this.Manifest("[api]\nbind=http://0.0.0.0:80", template: template, files: new() { [".certificates/site.pem"] = "private fixture" });
		File.WriteAllText(Path.Combine(_root, "trust.pem"), "trust fixture");
		manifest.Components.Single(component => component.Name == "nginx")["file!/etc/tls/Trust.pem"] = "./trust.pem";
		var sources = DeliveryBuilder.PrepareSources(manifest);
		WebIngress.Render(manifest, sources, _root);
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		var owned = Assert.Single(nginx.Configuration, pair => pair.Key.StartsWith("/etc/nginx/containerizer-files/", StringComparison.Ordinal));
		Assert.Equal("private fixture", File.ReadAllText(owned.Value.Source));
		Assert.Equal(Path.Combine(_root, "trust.pem"), nginx.Configuration["/etc/tls/Trust.pem"].Source);
		var text = File.ReadAllText(nginx.Configuration["/etc/nginx/containerizer/web.conf"].Source);
		Assert.Equal(2, text.Split(owned.Key).Length - 1);
		Assert.Contains("/etc/tls/Trust.pem", text);
		Assert.DoesNotContain("private fixture", text);
	}

	[Fact]
	public void MissingResourcesAndReservedTemplateMarkersFailBeforeEngineUse()
	{
		var marker = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("./missing.pem")) + "}}";
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(this.Manifest("[api]\nbind=http://0.0.0.0:80", template: marker)));
		Assert.Throws<ContainerizationException>(() => DeliveryBuilder.PrepareSources(this.Manifest("[api]\nbind=http://0.0.0.0:80", template: "{{zongsoft:unknown}}")));
		Assert.Throws<ContainerizationException>(() => ServiceSettings.Parse("port=80:8080;port=80:none"));
	}

	[Theory]
	[InlineData("reference")]
	[InlineData("site")]
	[InlineData("default")]
	[InlineData("binding")]
	[InlineData("port")]
	[InlineData("application")]
	public void DeliveryProtocolRejectsBrokenWebRelationships(string failure)
	{
		var sources = DeliveryBuilder.PrepareSources(this.Manifest("[api]\nbind=http://0.0.0.0:80"));
		var services = sources.Select(source => source.Plan).ToArray();
		var nginx = services.Single(service => service.Id == "nginx");
		DeliveryBundle.ValidateWeb(nginx, services);

		switch(failure)
		{
			case "reference":
				nginx.Web[0].Bindings[0].Publication = "unknown";
				break;
			case "site":
				nginx.Web.Add(nginx.Web[0]);
				break;
			case "default":
				nginx.Web[0].Bindings[0].IsDefault = false;
				break;
			case "binding":
				nginx.Web[0].Bindings.Add(nginx.Web[0].Bindings[0]);
				break;
			case "port":
				nginx.Ports.Add(nginx.Ports[0]);
				break;
			case "application":
				nginx.Web[0].Application = "unknown";
				break;
		}

		Assert.Throws<ContainerizationException>(() => DeliveryBundle.ValidateWeb(nginx, services));
	}

	private ContainerManifest Manifest(string bindings, string settings = null, string template = "server { listen 8080; location / { proxy_pass http://{{zongsoft:application}}:8069; } }", Dictionary<string, string> files = null)
	{
		var path = WebFixtures.WritePackage(Path.Combine(_root, "web.tar.gz"), "web", bindings, template, files);
		var manifest = ContainerManifest.From(Context(path, "--source:" + _root, "--name:example", "--version:1.0", "--distribution:debian"));
		if(settings != null)
			manifest.Components.Single(component => component.Name == "nginx")["settings"] = settings;
		return manifest;
	}

	private static CommandContext Context(params string[] arguments) => new(new CommandExecutor(), CommandLine.Parse(Utility.FormatCommand("containerize", arguments))[0], new ContainerizeCommand(), null);
}
